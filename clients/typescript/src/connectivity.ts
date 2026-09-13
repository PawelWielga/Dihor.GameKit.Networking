export const connectivityTransportIds = {
  lanWebSocket: "lan-websocket",
  webRtcDataChannel: "webrtc-datachannel",
  signalRRelay: "signalr-relay",
} as const;

export type ConnectivityMode = "auto" | "lan" | "webrtc" | "signalr";
export type ConnectivityAttemptOutcome = "selected" | "failed" | "timed-out" | "unavailable";

export interface ConnectivityAttemptDiagnostic {
  transportId: string;
  outcome: ConnectivityAttemptOutcome;
  durationMs: number;
  error?: string;
}

export interface ConnectivityDiagnostics {
  attempts: readonly ConnectivityAttemptDiagnostic[];
  selectedTransport?: string;
  preferredTransport?: string;
  preferredTransportReused: boolean;
}

export interface TransportCandidate<TContext, TConnection> {
  transportId: string;
  connect(context: TContext, signal: AbortSignal): Promise<TConnection>;
  attemptTimeoutMs?: number;
  disposeLateConnection?(connection: TConnection): void | Promise<void>;
}

export interface AutomaticTransportSelectorOptions {
  attemptTimeoutMs?: number;
  autoOrder?: readonly string[];
}

export interface SelectedTransportConnection<TConnection> {
  connection: TConnection;
  transportId: string;
  diagnostics: ConnectivityDiagnostics;
}

export class ConnectivitySelectionError extends Error {
  public constructor(
    message: string,
    public readonly diagnostics: ConnectivityDiagnostics,
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = "ConnectivitySelectionError";
  }
}

export class AutomaticTransportSelector<TContext, TConnection> {
  private readonly candidates = new Map<string, TransportCandidate<TContext, TConnection>>();
  private readonly attemptTimeoutMs: number;
  private readonly autoOrder: readonly string[];
  private lastSuccessfulTransport: string | undefined;

  public constructor(
    candidates: readonly TransportCandidate<TContext, TConnection>[],
    options: AutomaticTransportSelectorOptions = {},
  ) {
    if (candidates.length === 0) throw new Error("At least one transport candidate is required");

    this.attemptTimeoutMs = options.attemptTimeoutMs ?? 3_000;
    if (!Number.isFinite(this.attemptTimeoutMs) || this.attemptTimeoutMs <= 0) {
      throw new Error("attemptTimeoutMs must be positive");
    }

    this.autoOrder = options.autoOrder ?? [
      connectivityTransportIds.lanWebSocket,
      connectivityTransportIds.webRtcDataChannel,
      connectivityTransportIds.signalRRelay,
    ];
    if (this.autoOrder.length === 0) throw new Error("autoOrder cannot be empty");

    const seenOrder = new Set<string>();
    for (const value of this.autoOrder) {
      const normalized = requiredTransportId(value);
      if (seenOrder.has(normalized)) throw new Error(`Duplicate autoOrder transport '${value}'`);
      seenOrder.add(normalized);
    }

    for (const candidate of candidates) {
      const key = requiredTransportId(candidate.transportId);
      if (this.candidates.has(key)) throw new Error(`Duplicate transport candidate '${candidate.transportId}'`);
      if (candidate.attemptTimeoutMs !== undefined &&
        (!Number.isFinite(candidate.attemptTimeoutMs) || candidate.attemptTimeoutMs <= 0)) {
        throw new Error(`attemptTimeoutMs for '${candidate.transportId}' must be positive`);
      }
      this.candidates.set(key, candidate);
    }
  }

  public get previousSuccessfulTransport(): string | undefined {
    return this.lastSuccessfulTransport;
  }

  public connect(
    context: TContext,
    mode: ConnectivityMode = "auto",
    signal?: AbortSignal,
  ): Promise<SelectedTransportConnection<TConnection>> {
    return this.select(context, mode, false, signal);
  }

  public reconnect(
    context: TContext,
    mode: ConnectivityMode = "auto",
    signal?: AbortSignal,
  ): Promise<SelectedTransportConnection<TConnection>> {
    return this.select(context, mode, true, signal);
  }

  private async select(
    context: TContext,
    mode: ConnectivityMode,
    preferPrevious: boolean,
    signal?: AbortSignal,
  ): Promise<SelectedTransportConnection<TConnection>> {
    throwIfAborted(signal);
    const preferredTransport = mode === "auto" && preferPrevious
      ? this.lastSuccessfulTransport
      : undefined;
    const attemptOrder = this.buildAttemptOrder(mode, preferredTransport);
    const attempts: ConnectivityAttemptDiagnostic[] = [];
    let lastError: unknown;

    for (const transportId of attemptOrder) {
      throwIfAborted(signal);
      const candidate = this.candidates.get(normalizeTransportId(transportId));
      if (candidate === undefined) {
        attempts.push({
          transportId,
          outcome: "unavailable",
          durationMs: 0,
          error: "Transport candidate is not registered in this runtime.",
        });
        continue;
      }

      const startedAt = performanceNow();
      const controller = new AbortController();
      const removeAbortForwarder = forwardAbort(signal, controller);
      const timeoutMs = candidate.attemptTimeoutMs ?? this.attemptTimeoutMs;
      let abandoned = false;
      let timedOut = false;
      let timeoutHandle: ReturnType<typeof setTimeout> | undefined;
      let removeCancellationListener = () => {};

      try {
        const connectPromise = candidate.connect(context, controller.signal);
        const timeoutPromise = new Promise<never>((_, reject) => {
          timeoutHandle = setTimeout(() => {
            abandoned = true;
            timedOut = true;
            controller.abort();
            reject(new CandidateTimeoutError(timeoutMs));
          }, timeoutMs);
        });
        const cancellationPromise = signal === undefined
          ? new Promise<never>(() => {})
          : new Promise<never>((_, reject) => {
              const handler = () => {
                abandoned = true;
                controller.abort();
                reject(abortError());
              };
              if (signal.aborted) {
                handler();
                return;
              }
              signal.addEventListener("abort", handler, { once: true });
              removeCancellationListener = () => signal.removeEventListener("abort", handler);
            });

        connectPromise.then(
          (lateConnection) => {
            if (abandoned) void disposeLate(candidate, lateConnection);
          },
          () => {},
        );

        const connection = await Promise.race([connectPromise, timeoutPromise, cancellationPromise]);
        if (timeoutHandle !== undefined) clearTimeout(timeoutHandle);
        removeCancellationListener();
        removeAbortForwarder();
        const durationMs = performanceNow() - startedAt;
        attempts.push({ transportId: candidate.transportId, outcome: "selected", durationMs });
        this.lastSuccessfulTransport = candidate.transportId;
        return {
          connection,
          transportId: candidate.transportId,
          diagnostics: {
            attempts: [...attempts],
            selectedTransport: candidate.transportId,
            ...(preferredTransport === undefined ? {} : { preferredTransport }),
            preferredTransportReused: preferredTransport !== undefined &&
              normalizeTransportId(preferredTransport) === normalizeTransportId(candidate.transportId),
          },
        };
      } catch (error) {
        if (timeoutHandle !== undefined) clearTimeout(timeoutHandle);
        removeCancellationListener();
        removeAbortForwarder();
        if (signal?.aborted === true) {
          abandoned = true;
          controller.abort();
          throw abortError();
        }

        const durationMs = performanceNow() - startedAt;
        lastError = error;
        attempts.push({
          transportId: candidate.transportId,
          outcome: timedOut || error instanceof CandidateTimeoutError ? "timed-out" : "failed",
          durationMs,
          error: timedOut ? `Connection attempt exceeded ${timeoutMs} ms.` : error instanceof Error ? error.message : String(error),
        });
      }
    }

    const diagnostics: ConnectivityDiagnostics = {
      attempts,
      ...(preferredTransport === undefined ? {} : { preferredTransport }),
      preferredTransportReused: false,
    };
    throw new ConnectivitySelectionError(
      "No configured communication transport could establish a connection.",
      diagnostics,
      lastError === undefined ? undefined : { cause: lastError },
    );
  }

  private buildAttemptOrder(mode: ConnectivityMode, preferredTransport?: string): string[] {
    if (mode !== "auto") return [modeToTransportId(mode)];

    const result: string[] = [];
    if (preferredTransport !== undefined) result.push(preferredTransport);
    for (const transportId of this.autoOrder) {
      if (!result.some((value) => normalizeTransportId(value) === normalizeTransportId(transportId))) {
        result.push(transportId);
      }
    }
    return result;
  }
}

function modeToTransportId(mode: Exclude<ConnectivityMode, "auto">): string {
  switch (mode) {
    case "lan": return connectivityTransportIds.lanWebSocket;
    case "webrtc": return connectivityTransportIds.webRtcDataChannel;
    case "signalr": return connectivityTransportIds.signalRRelay;
  }
}

function requiredTransportId(value: string): string {
  const normalized = normalizeTransportId(value);
  if (normalized.length === 0) throw new Error("Transport id cannot be empty");
  return normalized;
}

function normalizeTransportId(value: string): string {
  return value.trim().toLowerCase();
}

function throwIfAborted(signal?: AbortSignal): void {
  if (signal?.aborted === true) throw abortError();
}

function forwardAbort(source: AbortSignal | undefined, target: AbortController): () => void {
  if (source === undefined) return () => {};
  if (source.aborted) {
    target.abort();
    return () => {};
  }
  const handler = () => target.abort();
  source.addEventListener("abort", handler, { once: true });
  return () => source.removeEventListener("abort", handler);
}

async function disposeLate<TContext, TConnection>(
  candidate: TransportCandidate<TContext, TConnection>,
  connection: TConnection,
): Promise<void> {
  try {
    await candidate.disposeLateConnection?.(connection);
  } catch {
    // An abandoned candidate no longer owns the selected path. Disposal errors
    // must not interfere with timeout/cancellation/fallback behavior.
  }
}

function abortError(): Error {
  return new DOMException("Connectivity selection was cancelled.", "AbortError");
}

function performanceNow(): number {
  return typeof performance === "undefined" ? Date.now() : performance.now();
}

class CandidateTimeoutError extends Error {
  public constructor(timeoutMs: number) {
    super(`Connection attempt exceeded ${timeoutMs} ms.`);
    this.name = "CandidateTimeoutError";
  }
}

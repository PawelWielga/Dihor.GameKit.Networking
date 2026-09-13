export type WebRtcDataChannelProfile = "reliable" | "low-latency";
export type WebRtcOverflowPolicy = "reject" | "drop-newest";
export type WebRtcPeerState = "new" | "connecting" | "open" | "closing" | "closed" | "failed";

export type WebRtcSignal =
  | {
      kind: "description";
      description: RTCSessionDescriptionInit;
    }
  | {
      kind: "candidate";
      candidate: RTCIceCandidateInit | null;
    };

export interface WebRtcSignalingChannel {
  send(signal: WebRtcSignal): void | Promise<void>;
  subscribe(handler: (signal: WebRtcSignal) => void | Promise<void>): () => void;
}

export interface WebRtcPeerOptions {
  initiator?: boolean;
  profile?: WebRtcDataChannelProfile;
  label?: string;
  rtcConfiguration?: RTCConfiguration;
  maxBufferedAmount?: number;
  bufferedAmountLowThreshold?: number;
  overflowPolicy?: WebRtcOverflowPolicy;
  maxPendingIceCandidates?: number;
  peerConnectionFactory?: (configuration?: RTCConfiguration) => RTCPeerConnection;
}

export interface WebRtcSendResult {
  sent: boolean;
  dropped: boolean;
  bufferedAmount: number;
  droppedMessages: number;
}

export interface WebRtcNetworkDiagnostics {
  state: WebRtcPeerState;
  bufferedAmount: number;
  droppedMessages: number;
  roundTripTimeMs?: number;
  rttJitterMs?: number;
}

export class WebRtcBackpressureError extends Error {
  constructor(
    public readonly bufferedAmount: number,
    public readonly maxBufferedAmount: number,
  ) {
    super(
      `WebRTC DataChannel buffered amount ${bufferedAmount} bytes exceeds the configured limit of ${maxBufferedAmount} bytes.`,
    );
    this.name = "WebRtcBackpressureError";
  }
}

export class WebRtcPeer {
  private readonly signaling: WebRtcSignalingChannel;
  private readonly options: Required<
    Pick<
      WebRtcPeerOptions,
      | "initiator"
      | "profile"
      | "label"
      | "maxBufferedAmount"
      | "bufferedAmountLowThreshold"
      | "overflowPolicy"
      | "maxPendingIceCandidates"
    >
  > &
    Pick<WebRtcPeerOptions, "rtcConfiguration" | "peerConnectionFactory">;
  private readonly connection: RTCPeerConnection;
  private readonly messageListeners = new Set<(payload: Uint8Array) => void>();
  private readonly stateListeners = new Set<(state: WebRtcPeerState) => void>();
  private readonly pendingRemoteCandidates: Array<RTCIceCandidateInit | null> = [];
  private readonly openPromise: Promise<void>;
  private readonly resolveOpen: () => void;
  private readonly rejectOpen: (reason?: unknown) => void;
  private unsubscribeSignal: (() => void) | null = null;
  private channel: RTCDataChannel | null = null;
  private state: WebRtcPeerState = "new";
  private droppedMessages = 0;
  private lastRoundTripTimeMs: number | undefined;
  private started = false;
  private disposed = false;

  constructor(signaling: WebRtcSignalingChannel, options: WebRtcPeerOptions = {}) {
    this.signaling = signaling;
    const profile = options.profile ?? "reliable";
    this.options = {
      initiator: options.initiator ?? false,
      profile,
      label: options.label ?? "partygamekit",
      maxBufferedAmount: options.maxBufferedAmount ?? 256 * 1024,
      bufferedAmountLowThreshold: options.bufferedAmountLowThreshold ?? 32 * 1024,
      overflowPolicy:
        options.overflowPolicy ?? (profile === "low-latency" ? "drop-newest" : "reject"),
      maxPendingIceCandidates: options.maxPendingIceCandidates ?? 64,
      rtcConfiguration: options.rtcConfiguration,
      peerConnectionFactory: options.peerConnectionFactory,
    };

    if (this.options.maxBufferedAmount <= 0) {
      throw new RangeError("maxBufferedAmount must be positive.");
    }
    if (this.options.bufferedAmountLowThreshold < 0) {
      throw new RangeError("bufferedAmountLowThreshold must not be negative.");
    }
    if (this.options.maxPendingIceCandidates <= 0) {
      throw new RangeError("maxPendingIceCandidates must be positive.");
    }

    const factory =
      this.options.peerConnectionFactory ??
      ((configuration?: RTCConfiguration) => new RTCPeerConnection(configuration));
    this.connection = factory(this.options.rtcConfiguration);

    let resolveOpen!: () => void;
    let rejectOpen!: (reason?: unknown) => void;
    this.openPromise = new Promise<void>((resolve, reject) => {
      resolveOpen = resolve;
      rejectOpen = reject;
    });
    this.resolveOpen = resolveOpen;
    this.rejectOpen = rejectOpen;

    this.connection.addEventListener("icecandidate", (event) => {
      void this.sendSignal({
        kind: "candidate",
        candidate: event.candidate?.toJSON() ?? null,
      });
    });
    this.connection.addEventListener("connectionstatechange", () => {
      this.handleConnectionState();
    });
    this.connection.addEventListener("datachannel", (event) => {
      if (this.options.initiator || this.channel !== null) {
        return;
      }
      this.attachChannel(event.channel);
    });
  }

  get currentState(): WebRtcPeerState {
    return this.state;
  }

  get currentBufferedAmount(): number {
    return this.channel?.bufferedAmount ?? 0;
  }

  get droppedMessageCount(): number {
    return this.droppedMessages;
  }

  async connect(): Promise<void> {
    if (this.disposed) {
      throw new Error("WebRTC peer is closed.");
    }
    if (this.started) {
      return this.openPromise;
    }

    this.started = true;
    this.setState("connecting");
    this.unsubscribeSignal = this.signaling.subscribe((signal) => this.applySignal(signal));

    if (this.options.initiator) {
      const channel = this.connection.createDataChannel(
        this.options.label,
        createWebRtcDataChannelInit(this.options.profile),
      );
      this.attachChannel(channel);
      const offer = await this.connection.createOffer();
      await this.connection.setLocalDescription(offer);
      await this.sendSignal({ kind: "description", description: offer });
    }

    return this.openPromise;
  }

  onMessage(listener: (payload: Uint8Array) => void): () => void {
    this.messageListeners.add(listener);
    return () => this.messageListeners.delete(listener);
  }

  onStateChange(listener: (state: WebRtcPeerState) => void): () => void {
    this.stateListeners.add(listener);
    return () => this.stateListeners.delete(listener);
  }

  send(payload: Uint8Array): WebRtcSendResult {
    if (this.disposed || this.state !== "open" || this.channel?.readyState !== "open") {
      throw new Error("WebRTC DataChannel is not open.");
    }

    const projectedBufferedAmount = this.channel.bufferedAmount + payload.byteLength;
    if (projectedBufferedAmount > this.options.maxBufferedAmount) {
      if (this.options.overflowPolicy === "drop-newest") {
        this.droppedMessages += 1;
        return {
          sent: false,
          dropped: true,
          bufferedAmount: this.channel.bufferedAmount,
          droppedMessages: this.droppedMessages,
        };
      }

      throw new WebRtcBackpressureError(
        projectedBufferedAmount,
        this.options.maxBufferedAmount,
      );
    }

    const copy = payload.slice();
    this.channel.send(copy.buffer);
    return {
      sent: true,
      dropped: false,
      bufferedAmount: this.channel.bufferedAmount,
      droppedMessages: this.droppedMessages,
    };
  }

  async sampleDiagnostics(): Promise<WebRtcNetworkDiagnostics> {
    let roundTripTimeMs: number | undefined;
    const reports = await this.connection.getStats();
    reports.forEach((report) => {
      if (
        roundTripTimeMs === undefined &&
        report.type === "candidate-pair" &&
        report.state === "succeeded" &&
        (report.nominated === true || report.selected === true) &&
        typeof report.currentRoundTripTime === "number"
      ) {
        roundTripTimeMs = report.currentRoundTripTime * 1000;
      }
    });

    const previous = this.lastRoundTripTimeMs;
    if (roundTripTimeMs !== undefined) {
      this.lastRoundTripTimeMs = roundTripTimeMs;
    }

    return {
      state: this.state,
      bufferedAmount: this.currentBufferedAmount,
      droppedMessages: this.droppedMessages,
      roundTripTimeMs,
      rttJitterMs:
        roundTripTimeMs !== undefined && previous !== undefined
          ? Math.abs(roundTripTimeMs - previous)
          : undefined,
    };
  }

  close(): void {
    if (this.disposed) {
      return;
    }

    this.disposed = true;
    this.setState("closing");
    this.unsubscribeSignal?.();
    this.unsubscribeSignal = null;
    this.channel?.close();
    this.connection.close();
    this.setState("closed");
  }

  private async applySignal(signal: WebRtcSignal): Promise<void> {
    if (this.disposed) {
      return;
    }

    try {
      if (signal.kind === "description") {
        await this.connection.setRemoteDescription(signal.description);
        await this.flushPendingRemoteCandidates();

        if (signal.description.type === "offer") {
          const answer = await this.connection.createAnswer();
          await this.connection.setLocalDescription(answer);
          await this.sendSignal({ kind: "description", description: answer });
        }
        return;
      }

      if (this.connection.remoteDescription === null) {
        if (this.pendingRemoteCandidates.length >= this.options.maxPendingIceCandidates) {
          throw new Error(
            `WebRTC pending ICE candidate limit of ${this.options.maxPendingIceCandidates} was exceeded.`,
          );
        }
        this.pendingRemoteCandidates.push(signal.candidate);
        return;
      }

      await this.connection.addIceCandidate(signal.candidate);
    } catch (error) {
      this.fail(error);
      throw error;
    }
  }

  private async flushPendingRemoteCandidates(): Promise<void> {
    while (this.pendingRemoteCandidates.length > 0) {
      const candidate = this.pendingRemoteCandidates.shift() ?? null;
      await this.connection.addIceCandidate(candidate);
    }
  }

  private attachChannel(channel: RTCDataChannel): void {
    if (this.channel !== null) {
      channel.close();
      return;
    }

    this.channel = channel;
    channel.binaryType = "arraybuffer";
    channel.bufferedAmountLowThreshold = Math.min(
      this.options.bufferedAmountLowThreshold,
      this.options.maxBufferedAmount,
    );
    channel.addEventListener("open", () => {
      this.setState("open");
      this.resolveOpen();
    });
    channel.addEventListener("close", () => {
      if (!this.disposed) {
        this.setState("closed");
      }
    });
    channel.addEventListener("error", () => {
      this.fail(new Error("WebRTC DataChannel reported an error."));
    });
    channel.addEventListener("message", (event) => {
      const payload = toUint8Array(event.data);
      if (payload === null) {
        this.fail(new Error("WebRTC DataChannel received a non-binary payload."));
        return;
      }
      for (const listener of this.messageListeners) {
        listener(payload);
      }
    });
  }

  private handleConnectionState(): void {
    switch (this.connection.connectionState) {
      case "connected":
        if (this.channel?.readyState === "open") {
          this.setState("open");
          this.resolveOpen();
        }
        break;
      case "failed":
        this.fail(new Error("WebRTC peer connection failed."));
        break;
      case "closed":
        if (!this.disposed) {
          this.setState("closed");
        }
        break;
      case "disconnected":
        if (this.state === "open") {
          this.setState("connecting");
        }
        break;
      default:
        break;
    }
  }

  private async sendSignal(signal: WebRtcSignal): Promise<void> {
    if (this.disposed) {
      return;
    }
    try {
      await this.signaling.send(signal);
    } catch (error) {
      this.fail(error);
      throw error;
    }
  }

  private fail(reason: unknown): void {
    if (this.state === "failed" || this.state === "closed") {
      return;
    }
    this.setState("failed");
    this.rejectOpen(reason);
  }

  private setState(state: WebRtcPeerState): void {
    if (this.state === state) {
      return;
    }
    this.state = state;
    for (const listener of this.stateListeners) {
      listener(state);
    }
  }
}

export function createWebRtcDataChannelInit(
  profile: WebRtcDataChannelProfile,
): RTCDataChannelInit {
  if (profile === "reliable") {
    return { ordered: true };
  }

  return {
    ordered: false,
    maxRetransmits: 0,
  };
}

function toUint8Array(value: unknown): Uint8Array | null {
  if (value instanceof ArrayBuffer) {
    return new Uint8Array(value.slice(0));
  }
  if (ArrayBuffer.isView(value)) {
    return new Uint8Array(value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength));
  }
  return null;
}

export interface MonotonicTimingOptions {
  historyCapacity?: number;
  pendingProbeCapacity?: number;
  maxRoundTripMs?: number;
  maxSynchronizationAgeMs?: number;
  maxEventAgeMs?: number;
  maxFutureLeadMs?: number;
}

export interface TimingProbe {
  probeId: string;
  referenceSendMs: number;
}

export interface TimingProbeReply {
  probeId: string;
  peerReceiveMs: number;
  peerSendMs: number;
}

export type TimingSampleStatus =
  | "accepted"
  | "unknown-probe"
  | "invalid-timestamp"
  | "invalid-peer-processing"
  | "round-trip-too-large";

export interface TimingSample {
  offsetMs: number;
  roundTripMs: number;
  peerProcessingMs: number;
  referenceReceiveMs: number;
}

export interface TimingModel {
  offsetMs: number;
  roundTripMs: number;
  jitterMs: number;
  uncertaintyMs: number;
  sampleCount: number;
  updatedAtReferenceMs: number;
  generation: number;
}

export interface TimingSampleObservation {
  status: TimingSampleStatus;
  sample?: TimingSample;
  model?: TimingModel;
  reason?: string;
}

export type TimestampNormalizationStatus =
  | "accepted"
  | "unsynchronized"
  | "synchronization-stale"
  | "invalid-timestamp"
  | "non-monotonic"
  | "too-old"
  | "too-far-in-future";

export interface TimestampNormalizationResult {
  status: TimestampNormalizationStatus;
  referenceTimestampMs?: number;
  uncertaintyMs?: number;
  model?: TimingModel;
  reason?: string;
}

export type TimingResetReason = "manual" | "reconnect" | "transport-changed";

export interface TimingDiagnostics {
  generation: number;
  historyCount: number;
  pendingProbeCount: number;
  acceptedSamples: number;
  rejectedSamples: number;
  lastRejectedSampleStatus?: TimingSampleStatus;
  lastResetReason?: TimingResetReason;
  model?: TimingModel;
}

interface ResolvedOptions {
  historyCapacity: number;
  pendingProbeCapacity: number;
  maxRoundTripMs: number;
  maxSynchronizationAgeMs: number;
  maxEventAgeMs: number;
  maxFutureLeadMs: number;
}

export function monotonicNowMs(): number {
  if (typeof globalThis.performance?.now !== "function") {
    throw new Error("A monotonic Performance.now() clock is required in this runtime");
  }
  return globalThis.performance.now();
}

export class MonotonicTimingSynchronizer {
  private readonly options: ResolvedOptions;
  private readonly pendingProbes = new Map<string, number>();
  private readonly pendingOrder: string[] = [];
  private readonly samples: TimingSample[] = [];
  private currentModel: TimingModel | undefined;
  private lastAcceptedPeerTimestampMs: number | undefined;
  private generation = 0;
  private acceptedSamples = 0;
  private rejectedSamples = 0;
  private lastRejectedSampleStatus: TimingSampleStatus | undefined;
  private lastResetReason: TimingResetReason | undefined;

  public constructor(options: MonotonicTimingOptions = {}) {
    this.options = {
      historyCapacity: positiveInteger(options.historyCapacity ?? 16, "historyCapacity"),
      pendingProbeCapacity: positiveInteger(options.pendingProbeCapacity ?? 32, "pendingProbeCapacity"),
      maxRoundTripMs: positiveFinite(options.maxRoundTripMs ?? 5_000, "maxRoundTripMs"),
      maxSynchronizationAgeMs: positiveFinite(options.maxSynchronizationAgeMs ?? 30_000, "maxSynchronizationAgeMs"),
      maxEventAgeMs: positiveFinite(options.maxEventAgeMs ?? 10_000, "maxEventAgeMs"),
      maxFutureLeadMs: nonNegativeFinite(options.maxFutureLeadMs ?? 250, "maxFutureLeadMs"),
    };
  }

  public get model(): TimingModel | undefined {
    return this.currentModel;
  }

  public get diagnostics(): TimingDiagnostics {
    return {
      generation: this.generation,
      historyCount: this.samples.length,
      pendingProbeCount: this.pendingProbes.size,
      acceptedSamples: this.acceptedSamples,
      rejectedSamples: this.rejectedSamples,
      ...(this.lastRejectedSampleStatus === undefined ? {} : { lastRejectedSampleStatus: this.lastRejectedSampleStatus }),
      ...(this.lastResetReason === undefined ? {} : { lastResetReason: this.lastResetReason }),
      ...(this.currentModel === undefined ? {} : { model: this.currentModel }),
    };
  }

  public createProbe(referenceNowMs: number, probeId = createProbeId()): TimingProbe {
    validTimestamp(referenceNowMs, "referenceNowMs");
    const id = requiredString(probeId, "probeId");
    if (this.pendingProbes.has(id)) throw new Error(`Timing probe '${id}' is already pending`);

    while (this.pendingProbes.size >= this.options.pendingProbeCapacity && this.pendingOrder.length > 0) {
      const oldest = this.pendingOrder.shift();
      if (oldest !== undefined) this.pendingProbes.delete(oldest);
    }
    this.pendingProbes.set(id, referenceNowMs);
    this.pendingOrder.push(id);
    return { probeId: id, referenceSendMs: referenceNowMs };
  }

  public observeReply(reply: TimingProbeReply, referenceReceiveMs: number): TimingSampleObservation {
    const probeId = requiredString(reply.probeId, "probeId");
    validTimestamp(referenceReceiveMs, "referenceReceiveMs");
    const referenceSendMs = this.pendingProbes.get(probeId);
    if (referenceSendMs === undefined) {
      return this.reject("unknown-probe", `Timing probe '${probeId}' is not pending.`);
    }
    this.pendingProbes.delete(probeId);

    if (!isValidTimestamp(reply.peerReceiveMs) ||
        !isValidTimestamp(reply.peerSendMs) ||
        referenceReceiveMs < referenceSendMs) {
      return this.reject("invalid-timestamp", "Timing reply contains invalid or non-monotonic clock values.");
    }

    const peerProcessingMs = reply.peerSendMs - reply.peerReceiveMs;
    if (peerProcessingMs < 0) {
      return this.reject("invalid-peer-processing", "Peer send time precedes peer receive time.");
    }

    const roundTripMs = (referenceReceiveMs - referenceSendMs) - peerProcessingMs;
    if (!Number.isFinite(roundTripMs) || roundTripMs < 0) {
      return this.reject("invalid-peer-processing", "Peer processing interval exceeds the observed reference round trip.");
    }
    if (roundTripMs > this.options.maxRoundTripMs) {
      return this.reject("round-trip-too-large", `Observed round trip ${roundTripMs.toFixed(3)} ms exceeds the configured maximum.`);
    }

    const offsetMs = ((reply.peerReceiveMs - referenceSendMs) + (reply.peerSendMs - referenceReceiveMs)) / 2;
    const sample: TimingSample = { offsetMs, roundTripMs, peerProcessingMs, referenceReceiveMs };
    this.samples.push(sample);
    while (this.samples.length > this.options.historyCapacity) this.samples.shift();
    this.acceptedSamples += 1;
    this.currentModel = this.buildModel(referenceReceiveMs);
    return { status: "accepted", sample, model: this.currentModel };
  }

  public normalizePeerTimestamp(peerTimestampMs: number, referenceNowMs: number): TimestampNormalizationResult {
    if (!isValidTimestamp(peerTimestampMs) || !isValidTimestamp(referenceNowMs)) {
      return { status: "invalid-timestamp", reason: "Timestamp evidence must be finite and non-negative.", ...(this.currentModel === undefined ? {} : { model: this.currentModel, uncertaintyMs: this.currentModel.uncertaintyMs }) };
    }

    const model = this.currentModel;
    if (model === undefined) return { status: "unsynchronized", reason: "No synchronization model is available." };

    const modelAgeMs = referenceNowMs - model.updatedAtReferenceMs;
    if (modelAgeMs < 0) {
      return { status: "invalid-timestamp", model, uncertaintyMs: model.uncertaintyMs, reason: "Reference clock moved backwards relative to the synchronization model." };
    }
    if (modelAgeMs > this.options.maxSynchronizationAgeMs) {
      return { status: "synchronization-stale", model, uncertaintyMs: model.uncertaintyMs, reason: "Synchronization model is too old and must be reacquired." };
    }
    if (this.lastAcceptedPeerTimestampMs !== undefined && peerTimestampMs <= this.lastAcceptedPeerTimestampMs) {
      return { status: "non-monotonic", model, uncertaintyMs: model.uncertaintyMs, reason: "Peer timestamp did not advance monotonically." };
    }

    const referenceTimestampMs = peerTimestampMs - model.offsetMs;
    if (!isValidTimestamp(referenceTimestampMs)) {
      return { status: "invalid-timestamp", model, uncertaintyMs: model.uncertaintyMs, reason: "Normalized reference timestamp is invalid." };
    }
    if (referenceTimestampMs - referenceNowMs > this.options.maxFutureLeadMs) {
      return { status: "too-far-in-future", referenceTimestampMs, model, uncertaintyMs: model.uncertaintyMs, reason: "Normalized timestamp is implausibly far in the future." };
    }
    if (referenceNowMs - referenceTimestampMs > this.options.maxEventAgeMs) {
      return { status: "too-old", referenceTimestampMs, model, uncertaintyMs: model.uncertaintyMs, reason: "Normalized timestamp is too old for the configured evidence window." };
    }

    this.lastAcceptedPeerTimestampMs = peerTimestampMs;
    return { status: "accepted", referenceTimestampMs, model, uncertaintyMs: model.uncertaintyMs };
  }

  public reset(reason: TimingResetReason = "manual"): void {
    this.pendingProbes.clear();
    this.pendingOrder.length = 0;
    this.samples.length = 0;
    this.currentModel = undefined;
    this.lastAcceptedPeerTimestampMs = undefined;
    this.acceptedSamples = 0;
    this.rejectedSamples = 0;
    this.lastRejectedSampleStatus = undefined;
    this.lastResetReason = reason;
    this.generation += 1;
  }

  private buildModel(updatedAtReferenceMs: number): TimingModel {
    const sortedByRtt = [...this.samples].sort((left, right) => left.roundTripMs - right.roundTripMs);
    const selectedCount = Math.max(1, Math.ceil(sortedByRtt.length / 2));
    const best = sortedByRtt.slice(0, selectedCount);
    const offsetMs = median(best.map((sample) => sample.offsetMs));
    const roundTripMs = median(best.map((sample) => sample.roundTripMs));
    const allRoundTripMedian = median(this.samples.map((sample) => sample.roundTripMs));
    const jitterMs = median(this.samples.map((sample) => Math.abs(sample.roundTripMs - allRoundTripMedian)));
    const offsetSpreadMs = median(best.map((sample) => Math.abs(sample.offsetMs - offsetMs)));
    const minimumRoundTripMs = Math.min(...best.map((sample) => sample.roundTripMs));
    const uncertaintyMs = (minimumRoundTripMs / 2) + offsetSpreadMs + (jitterMs / 2);
    return {
      offsetMs,
      roundTripMs,
      jitterMs,
      uncertaintyMs,
      sampleCount: this.samples.length,
      updatedAtReferenceMs,
      generation: this.generation,
    };
  }

  private reject(status: TimingSampleStatus, reason: string): TimingSampleObservation {
    this.rejectedSamples += 1;
    this.lastRejectedSampleStatus = status;
    return { status, reason, ...(this.currentModel === undefined ? {} : { model: this.currentModel }) };
  }
}

function median(values: number[]): number {
  if (values.length === 0) throw new Error("Median requires at least one value");
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0 ? ((sorted[middle - 1] ?? 0) + (sorted[middle] ?? 0)) / 2 : (sorted[middle] ?? 0);
}

function positiveInteger(value: number, name: string): number {
  if (!Number.isInteger(value) || value <= 0) throw new Error(`${name} must be a positive integer`);
  return value;
}

function positiveFinite(value: number, name: string): number {
  if (!Number.isFinite(value) || value <= 0) throw new Error(`${name} must be finite and positive`);
  return value;
}

function nonNegativeFinite(value: number, name: string): number {
  if (!Number.isFinite(value) || value < 0) throw new Error(`${name} must be finite and non-negative`);
  return value;
}

function validTimestamp(value: number, name: string): number {
  if (!isValidTimestamp(value)) throw new Error(`${name} must be finite and non-negative`);
  return value;
}

function isValidTimestamp(value: number): boolean {
  return Number.isFinite(value) && value >= 0;
}

function requiredString(value: string, name: string): string {
  const normalized = value.trim();
  if (normalized.length === 0) throw new Error(`${name} cannot be empty`);
  return normalized;
}

function createProbeId(): string {
  return typeof globalThis.crypto?.randomUUID === "function"
    ? globalThis.crypto.randomUUID()
    : `timing-${Math.random().toString(36).slice(2)}`;
}

import { requiredString } from "./protocol.js";

export interface MessageIdDeduplicatorOptions {
  /** Maximum number of distinct peer/message-id pairs retained globally. */
  capacity?: number;
  /** How long an accepted pair remains a duplicate. Defaults to five minutes. */
  retentionMs?: number;
  /** Injectable clock used by tests or specialized runtimes. Defaults to Date.now. */
  now?: () => number;
}

/**
 * Bounded receiver-side deduplication keyed by stable peer identity plus
 * protocol messageId.
 *
 * This utility does not provide acknowledgements, retries or exactly-once
 * delivery. Connection identifiers and transport identifiers are deliberately
 * not part of the key, so a replay after reconnect/fallback remains a duplicate
 * for the same stable peer.
 */
export class MessageIdDeduplicator {
  public static readonly defaultCapacity = 1024;
  public static readonly defaultRetentionMs = 5 * 60 * 1000;

  private readonly capacityValue: number;
  private readonly retentionMsValue: number;
  private readonly now: () => number;
  private readonly entries = new Map<string, SeenMessage>();

  public constructor(options: MessageIdDeduplicatorOptions = {}) {
    const capacity = options.capacity ?? MessageIdDeduplicator.defaultCapacity;
    const retentionMs =
      options.retentionMs ?? MessageIdDeduplicator.defaultRetentionMs;

    if (!Number.isInteger(capacity) || capacity <= 0) {
      throw new Error("capacity must be a positive integer");
    }

    if (!Number.isFinite(retentionMs) || retentionMs <= 0) {
      throw new Error("retentionMs must be positive");
    }

    this.capacityValue = capacity;
    this.retentionMsValue = retentionMs;
    this.now = options.now ?? Date.now;
  }

  public get capacity(): number {
    return this.capacityValue;
  }

  public get retentionMs(): number {
    return this.retentionMsValue;
  }

  public get count(): number {
    this.removeExpired(this.now());
    return this.entries.size;
  }

  /**
   * Returns true exactly when this stable peer/message-id pair has not already
   * been accepted inside the current bounded deduplication window.
   */
  public tryAccept(peerId: string, messageId: string): boolean {
    const normalizedPeerId = requiredString(peerId, "peerId");
    const normalizedMessageId = requiredString(messageId, "messageId");
    const now = this.now();

    this.removeExpired(now);

    const key = entryKey(normalizedPeerId, normalizedMessageId);
    if (this.entries.has(key)) return false;

    this.entries.set(key, {
      peerId: normalizedPeerId,
      messageId: normalizedMessageId,
      acceptedAt: now,
    });

    this.trimToCapacity();
    return true;
  }

  /**
   * Removes all remembered message ids for one stable peer.
   * Call this when continuity for that peer is deliberately forgotten or its
   * reconnect window expires.
   */
  public forgetPeer(peerId: string): number {
    const normalizedPeerId = requiredString(peerId, "peerId");
    this.removeExpired(this.now());

    let removed = 0;

    for (const [key, entry] of this.entries) {
      if (entry.peerId !== normalizedPeerId) continue;
      this.entries.delete(key);
      removed += 1;
    }

    return removed;
  }

  public clear(): void {
    this.entries.clear();
  }

  private removeExpired(now: number): void {
    for (const [key, entry] of this.entries) {
      if (now - entry.acceptedAt >= this.retentionMsValue) {
        this.entries.delete(key);
      }
    }
  }

  private trimToCapacity(): void {
    while (this.entries.size > this.capacityValue) {
      const oldest = this.entries.keys().next();
      if (oldest.done) return;
      this.entries.delete(oldest.value);
    }
  }
}

interface SeenMessage {
  peerId: string;
  messageId: string;
  acceptedAt: number;
}

function entryKey(peerId: string, messageId: string): string {
  return JSON.stringify([peerId, messageId]);
}

/**
 * Sender abstraction used by LatestValueReplayBuffer.
 *
 * The sender should forward the staged message through the ordinary application
 * data path. It must not route replay data through heartbeat/control traffic.
 */
export interface ReplaySender<TMessage> {
  send(message: TMessage, signal: AbortSignal): void | Promise<void>;
}

/**
 * Latest-value/coalescing replay for transient application data.
 *
 * A new logical value should be staged with a new protocol messageId. If that
 * staged value must be replayed after reconnect, the exact same message object
 * should be sent again so its messageId remains stable for receiver-side
 * deduplication.
 */
export class LatestValueReplayBuffer<TMessage> {
  private readonly latest = new Map<string, BufferedMessage<TMessage>>();
  private readonly activeFlushes = new Map<string, Promise<number>>();

  private binding: SenderBinding<TMessage> | undefined;
  private nextVersion = 0;
  private nextGeneration = 0;

  public get bufferedCount(): number {
    return this.latest.size;
  }

  public async stageLatest(key: string, scope: string, message: TMessage): Promise<void> {
    const normalizedKey = requiredToken(key, "key");
    const normalizedScope = requiredToken(scope, "scope");
    const version = ++this.nextVersion;

    this.latest.set(normalizedKey, {
      scope: normalizedScope,
      message,
      version,
    });

    const binding = this.binding;
    if (binding === undefined) return;

    while (this.isCurrentBinding(binding) && this.latest.has(normalizedKey)) {
      const sentVersion = await this.getOrStartFlush(normalizedKey, binding);
      if (sentVersion >= version) return;
    }
  }

  /**
   * Replaces the active sender and immediately replays all currently buffered
   * latest values. The previous sender is aborted but never disposed.
   */
  public async bindSender(sender: ReplaySender<TMessage>): Promise<void> {
    if (sender === null || sender === undefined) throw new Error("sender is required");

    const previous = this.binding;
    const binding: SenderBinding<TMessage> = {
      generation: ++this.nextGeneration,
      sender,
      abortController: new AbortController(),
      sendTail: Promise.resolve(),
    };
    this.binding = binding;
    previous?.abortController.abort();

    await Promise.all(
      [...this.latest.keys()].map((key) => this.getOrStartFlush(key, binding)),
    );
  }

  /**
   * Detaches the current sender. When sender is supplied, a stale sender cannot
   * accidentally unbind a newer replacement sender.
   */
  public unbindSender(sender?: ReplaySender<TMessage>): void {
    const current = this.binding;
    if (current === undefined) return;
    if (sender !== undefined && current.sender !== sender) return;

    this.binding = undefined;
    current.abortController.abort();
  }

  /**
   * Clears one replay key. When scope is supplied, the entry is removed only if
   * the currently buffered scope still matches.
   */
  public clearLatest(key: string, scope?: string): boolean {
    const normalizedKey = requiredToken(key, "key");
    const normalizedScope = scope === undefined ? undefined : requiredToken(scope, "scope");
    const current = this.latest.get(normalizedKey);
    if (current === undefined) return false;
    if (normalizedScope !== undefined && current.scope !== normalizedScope) return false;
    return this.latest.delete(normalizedKey);
  }

  /**
   * Invalidates every replay entry owned by the supplied caller-defined
   * scope/epoch token.
   */
  public invalidateScope(scope: string): number {
    const normalizedScope = requiredToken(scope, "scope");
    let removed = 0;

    for (const [key, value] of this.latest) {
      if (value.scope !== normalizedScope) continue;
      this.latest.delete(key);
      removed += 1;
    }

    return removed;
  }

  private getOrStartFlush(
    key: string,
    binding: SenderBinding<TMessage>,
  ): Promise<number> {
    const flushId = `${binding.generation}:\u0000:${key}`;
    const active = this.activeFlushes.get(flushId);
    if (active !== undefined) return active;

    const task = this.flushKey(key, binding).finally(() => {
      if (this.activeFlushes.get(flushId) === task) {
        this.activeFlushes.delete(flushId);
      }
    });

    this.activeFlushes.set(flushId, task);
    return task;
  }

  private async flushKey(
    key: string,
    binding: SenderBinding<TMessage>,
  ): Promise<number> {
    let lastSentVersion = 0;

    while (true) {
      let buffered: BufferedMessage<TMessage> | undefined;

      try {
        buffered = await this.withSenderGate(binding, async () => {
          if (!this.isCurrentBinding(binding)) return undefined;

          const current = this.latest.get(key);
          if (current === undefined) return undefined;

          // No await occurs between this recheck and invoking sender.send().
          // In JavaScript that makes clear/invalidate atomic with respect to
          // send start. A synchronous callback triggered by send() can clear
          // state, but at that point the send has already started.
          await binding.sender.send(
            current.message,
            binding.abortController.signal,
          );
          return current;
        });

        if (buffered === undefined) return lastSentVersion;
        lastSentVersion = buffered.version;
      } catch (error) {
        if (binding.abortController.signal.aborted) return lastSentVersion;
        throw error;
      }

      if (!this.isCurrentBinding(binding)) {
        // Delivery on a connection replaced while the send was in flight is
        // ambiguous. Keep the same staged message for replay on the replacement.
        return lastSentVersion;
      }

      const current = this.latest.get(key);
      if (current === undefined) return lastSentVersion;

      if (current.version === buffered.version) {
        // A locally completed send is not a receiver acknowledgement. Keep the
        // latest value staged so a replacement connection can replay the same
        // message (and therefore the same protocol messageId).
        return lastSentVersion;
      }

      // A newer value was staged while the previous send was in flight. Loop
      // and send only the newest currently buffered value.
    }
  }

  private async withSenderGate<T>(
    binding: SenderBinding<TMessage>,
    action: () => Promise<T>,
  ): Promise<T> {
    const previous = binding.sendTail;
    let release!: () => void;
    binding.sendTail = new Promise<void>((resolve) => {
      release = resolve;
    });

    await previous;
    try {
      return await action();
    } finally {
      release();
    }
  }

  private isCurrentBinding(binding: SenderBinding<TMessage>): boolean {
    return this.binding?.generation === binding.generation;
  }
}

interface BufferedMessage<TMessage> {
  scope: string;
  message: TMessage;
  version: number;
}

interface SenderBinding<TMessage> {
  generation: number;
  sender: ReplaySender<TMessage>;
  abortController: AbortController;
  sendTail: Promise<void>;
}

function requiredToken(value: string, name: string): string {
  if (typeof value !== "string") throw new Error(`${name} must be a string`);
  const normalized = value.trim();
  if (normalized.length === 0) throw new Error(`${name} cannot be empty`);
  return normalized;
}

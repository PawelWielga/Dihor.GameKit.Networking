import test from "node:test";
import assert from "node:assert/strict";
import {
  AutomaticTransportSelector,
  LatestValueReplayBuffer,
  connectivityTransportIds,
  type ReplaySender,
  type TransportCandidate,
} from "../src/index.js";

interface ReplayMessage {
  messageId: string;
  value: number;
}

interface FakeConnection {
  transportId: string;
  sender: FakeSender;
}

test("latest-value replay coalesces disconnected updates per key", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();

  await replay.stageLatest("draft", "round-1", { messageId: "m1", value: 1 });
  await replay.stageLatest("draft", "round-1", { messageId: "m2", value: 2 });
  await replay.stageLatest("draft", "round-1", { messageId: "m3", value: 3 });

  const sender = new FakeSender();
  await replay.bindSender(sender);

  assert.deepEqual(sender.sent, [{ messageId: "m3", value: 3 }]);
  assert.equal(replay.bufferedCount, 1);

  replay.unbindSender(sender);
  const replacement = new FakeSender();
  await replay.bindSender(replacement);

  assert.deepEqual(replacement.sent, [{ messageId: "m3", value: 3 }]);
  assert.equal(replay.bufferedCount, 1);
  assert.equal(replay.clearLatest("draft", "round-1"), true);
  assert.equal(replay.bufferedCount, 0);
});

test("scope replacement prevents stale values from replaying", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();

  await replay.stageLatest("draft", "round-old", { messageId: "old", value: 1 });
  await replay.stageLatest("draft", "round-new", { messageId: "new", value: 2 });

  const sender = new FakeSender();
  await replay.bindSender(sender);

  assert.deepEqual(sender.sent, [{ messageId: "new", value: 2 }]);
});

test("failed send retains the exact staged message for a replacement sender", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();
  const staged = { messageId: "stable-replay-id", value: 42 };

  await replay.stageLatest("draft", "round-1", staged);

  const failing = new FakeSender(new Error("connection lost"));
  await assert.rejects(() => replay.bindSender(failing), /connection lost/);
  assert.equal(replay.bufferedCount, 1);

  replay.unbindSender(failing);

  const replacement = new FakeSender();
  await replay.bindSender(replacement);

  assert.equal(replacement.sent.length, 1);
  assert.equal(replacement.sent[0], staged);
  assert.equal(replacement.sent[0]?.messageId, "stable-replay-id");
  assert.equal(replay.bufferedCount, 1);
});

test("newer value staged during an in-flight send is delivered before stage completes", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();
  const sender = new BlockingSender();

  await replay.bindSender(sender);

  const first = replay.stageLatest(
    "draft",
    "round-1",
    { messageId: "m1", value: 1 },
  );

  await sender.sendStarted;

  const second = replay.stageLatest(
    "draft",
    "round-1",
    { messageId: "m2", value: 2 },
  );

  sender.release();
  await Promise.all([first, second]);

  assert.deepEqual(sender.sent, [
    { messageId: "m1", value: 1 },
    { messageId: "m2", value: 2 },
  ]);
  assert.equal(replay.bufferedCount, 1);
});

test("clearLatest and invalidateScope remove only matching replay state", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();

  await replay.stageLatest("a", "round-1", { messageId: "a", value: 1 });
  await replay.stageLatest("b", "round-1", { messageId: "b", value: 2 });
  await replay.stageLatest("c", "round-2", { messageId: "c", value: 3 });

  assert.equal(replay.clearLatest("a", "round-2"), false);
  assert.equal(replay.clearLatest("a", "round-1"), true);
  assert.equal(replay.invalidateScope("round-1"), 1);
  assert.equal(replay.bufferedCount, 1);

  const sender = new FakeSender();
  await replay.bindSender(sender);

  assert.deepEqual(sender.sent, [{ messageId: "c", value: 3 }]);
});

test("automatic reconnect fallback replays the newest buffered value on the replacement transport", async () => {
  const replay = new LatestValueReplayBuffer<ReplayMessage>();
  const webRtcSender = new FakeSender();
  const signalRSender = new FakeSender();
  let webRtcAvailable = true;

  const selector = createSelector([
    failure(connectivityTransportIds.lanWebSocket),
    {
      transportId: connectivityTransportIds.webRtcDataChannel,
      connect: async () => {
        if (!webRtcAvailable) throw new Error("WebRTC unavailable");
        return {
          transportId: connectivityTransportIds.webRtcDataChannel,
          sender: webRtcSender,
        };
      },
    },
    {
      transportId: connectivityTransportIds.signalRRelay,
      connect: async () => ({
        transportId: connectivityTransportIds.signalRRelay,
        sender: signalRSender,
      }),
    },
  ]);

  const initial = await selector.connect("connect");
  assert.equal(initial.transportId, connectivityTransportIds.webRtcDataChannel);

  await replay.bindSender(initial.connection.sender);
  await replay.stageLatest("draft", "round-1", { messageId: "m1", value: 1 });
  replay.unbindSender(initial.connection.sender);

  await replay.stageLatest("draft", "round-1", { messageId: "m2", value: 2 });
  webRtcAvailable = false;

  const replacement = await selector.reconnect("resume");
  assert.equal(replacement.transportId, connectivityTransportIds.signalRRelay);

  await replay.bindSender(replacement.connection.sender);

  assert.deepEqual(webRtcSender.sent, [{ messageId: "m1", value: 1 }]);
  assert.deepEqual(signalRSender.sent, [{ messageId: "m2", value: 2 }]);
});

class BlockingSender implements ReplaySender<ReplayMessage> {
  public readonly sent: ReplayMessage[] = [];
  private resolveSendStarted!: () => void;
  private resolveRelease!: () => void;
  public readonly sendStarted = new Promise<void>((resolve) => {
    this.resolveSendStarted = resolve;
  });
  private readonly released = new Promise<void>((resolve) => {
    this.resolveRelease = resolve;
  });

  public async send(message: ReplayMessage): Promise<void> {
    this.sent.push(message);
    this.resolveSendStarted();
    await this.released;
  }

  public release(): void {
    this.resolveRelease();
  }
}

class FakeSender implements ReplaySender<ReplayMessage> {
  public readonly sent: ReplayMessage[] = [];

  public constructor(private readonly failure?: Error) {}

  public send(message: ReplayMessage, signal: AbortSignal): void {
    if (signal.aborted) throw new DOMException("aborted", "AbortError");
    if (this.failure !== undefined) throw this.failure;
    this.sent.push(message);
  }
}

function createSelector(
  candidates: readonly TransportCandidate<string, FakeConnection>[],
): AutomaticTransportSelector<string, FakeConnection> {
  return new AutomaticTransportSelector(candidates, {
    attemptTimeoutMs: 100,
    autoOrder: [
      connectivityTransportIds.lanWebSocket,
      connectivityTransportIds.webRtcDataChannel,
      connectivityTransportIds.signalRRelay,
    ],
  });
}

function failure(
  transportId: string,
): TransportCandidate<string, FakeConnection> {
  return {
    transportId,
    connect: async () => {
      throw new Error(`${transportId} unavailable`);
    },
  };
}

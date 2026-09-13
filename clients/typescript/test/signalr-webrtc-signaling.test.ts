import test from "node:test";
import assert from "node:assert/strict";
import {
  SignalRWebRtcSignalingClient,
  type SignalRHubConnectionLike,
  type WebRtcSignal,
} from "../src/index.js";

const signalMethod = "PartyGameKit.WebRtcSignal";

test("SignalR signaling buffers an early offer until the peer channel subscribes", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  const offer: WebRtcSignal = {
    kind: "description",
    description: { type: "offer", sdp: "early-offer" },
  };
  connection.emit(signalMethod, "remote", JSON.stringify(offer));

  const received: WebRtcSignal[] = [];
  client.createChannel("remote").subscribe((signal) => {
    received.push(signal);
  });
  await Promise.resolve();
  await Promise.resolve();

  assert.deepEqual(received, [offer]);
});

test("SignalR signaling fails deterministically when the early-signal bound is exceeded", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    maxPendingSignalsPerPeer: 1,
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  connection.emit(
    signalMethod,
    "remote",
    JSON.stringify({
      kind: "description",
      description: { type: "offer", sdp: "offer" },
    }),
  );
  connection.emit(
    signalMethod,
    "remote",
    JSON.stringify({
      kind: "candidate",
      candidate: { candidate: "candidate:1" },
    }),
  );

  let caught: unknown;
  try {
    client.createChannel("remote").subscribe(() => {});
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof Error);
  assert.ok(caught.message.includes("pending signal limit"));
});

test("SignalR signaling sends only targeted serialized negotiation data", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  const signal: WebRtcSignal = {
    kind: "candidate",
    candidate: { candidate: "candidate:1" },
  };
  await client.createChannel("remote").send(signal);

  assert.deepEqual(connection.invocations.at(-1), {
    methodName: "SendSignal",
    args: ["remote", JSON.stringify(signal)],
  });
});

class FakeHubConnection implements SignalRHubConnectionLike {
  readonly invocations: Array<{ methodName: string; args: unknown[] }> = [];
  private readonly handlers = new Map<string, (...args: unknown[]) => void>();
  private started = false;

  constructor(public readonly connectionId: string | null) {}

  async start(): Promise<void> {
    this.started = true;
  }

  async stop(): Promise<void> {
    this.started = false;
  }

  async invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T> {
    if (!this.started) {
      throw new Error("Fake SignalR connection is not started.");
    }
    this.invocations.push({ methodName, args });
    if (methodName === "JoinChannel") {
      return [] as T;
    }
    return undefined as T;
  }

  on(methodName: string, newMethod: (...args: unknown[]) => void): void {
    this.handlers.set(methodName, newMethod);
  }

  off(methodName: string): void {
    this.handlers.delete(methodName);
  }

  emit(methodName: string, ...args: unknown[]): void {
    this.handlers.get(methodName)?.(...args);
  }
}

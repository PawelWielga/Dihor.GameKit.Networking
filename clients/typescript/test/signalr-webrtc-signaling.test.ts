import test from "node:test";
import assert from "node:assert/strict";
import {
  SignalRWebRtcSignalingClient,
  type SignalRHubConnectionLike,
  type WebRtcSignal,
} from "../src/index.js";

const peerLeftMethod = "PartyGameKit.WebRtcPeerLeft";
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

test("SignalR closure propagates a terminal failure to subscribed negotiating channels", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  const failures: unknown[] = [];
  client.createChannel("remote").subscribe(
    () => {},
    (reason) => failures.push(reason),
  );

  const expected = new Error("transport disconnected");
  connection.closeUnexpectedly(expected);

  assert.deepEqual(failures, [expected]);
});

test("peer departure fails an active negotiation channel", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  const failures: unknown[] = [];
  client.createChannel("remote").subscribe(
    () => {},
    (reason) => failures.push(reason),
  );

  connection.emit(peerLeftMethod, "remote");

  assert.equal(failures.length, 1);
  assert.ok(failures[0] instanceof Error);
  assert.ok(failures[0].message.includes("left before negotiation completed"));
});

test("peer departure before subscription fails the late negotiation channel", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  connection.emit(peerLeftMethod, "remote");

  let caught: unknown;
  try {
    client.createChannel("remote").subscribe(() => {});
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof Error);
  assert.ok(caught.message.includes("left before negotiation completed"));
});

test("disposing signaling fails active negotiation channels before listeners are cleared", async () => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });

  await client.connect();
  const failures: unknown[] = [];
  client.createChannel("remote").subscribe(
    () => {},
    (reason) => failures.push(reason),
  );

  await client.dispose();

  assert.equal(failures.length, 1);
  assert.ok(failures[0] instanceof Error);
  assert.equal(failures[0].message, "SignalR WebRTC signaling client was disposed.");
});

test("malformed signaling payload fails the affected subscribed peer channel", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  const failures: unknown[] = [];
  client.createChannel("remote").subscribe(
    () => {},
    (reason) => failures.push(reason),
  );

  connection.emit(signalMethod, "remote", "{not-json");

  assert.equal(failures.length, 1);
  assert.ok(failures[0] instanceof Error);
});

test("malformed early signaling payload fails when the peer channel subscribes", async (t) => {
  const connection = new FakeHubConnection("local");
  const client = new SignalRWebRtcSignalingClient({
    endpoint: "https://example.test/signaling",
    channelId: "channel-a",
    hubConnectionFactory: () => connection,
  });
  t.after(() => client.dispose());

  await client.connect();
  connection.emit(signalMethod, "remote", JSON.stringify({ kind: "unknown" }));

  let caught: unknown;
  try {
    client.createChannel("remote").subscribe(() => {});
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof Error);
  assert.equal(caught.message, "Unsupported WebRTC signal kind.");
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
  private readonly closeHandlers = new Set<(error?: Error) => void>();
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

  onclose(callback: (error?: Error) => void): void {
    this.closeHandlers.add(callback);
  }

  emit(methodName: string, ...args: unknown[]): void {
    this.handlers.get(methodName)?.(...args);
  }

  closeUnexpectedly(error?: Error): void {
    this.started = false;
    for (const handler of this.closeHandlers) {
      handler(error);
    }
  }
}

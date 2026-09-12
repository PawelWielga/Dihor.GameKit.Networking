import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  MemoryIdentityStore,
  PartyGameClient,
  SnapshotSequenceGate,
  createMessage,
  messageTypes,
  parseJoinDescriptorJson,
  parseJoinDescriptorUri,
  parseMessage,
  serializeJoinDescriptorJson,
  serializeJoinDescriptorUri,
  serializeMessage,
  type ProtocolEnvelope,
  type StateSnapshotPayload,
  type WebSocketLike,
} from "../src/index.js";

const fixturesDir = resolve(process.cwd(), "../../protocol/fixtures");
const fixture = (name: string): string => readFileSync(resolve(fixturesDir, name), "utf8").trim();

const canonicalMessages: Record<string, string> = {
  "heartbeat.json": messageTypes.heartbeat,
  "join-rejected.json": messageTypes.joinRejected,
  "join-success.json": messageTypes.joinAccepted,
  "rejoin-rejected.json": messageTypes.rejoinRejected,
  "rejoin.json": messageTypes.rejoinRequest,
  "snapshot-player.json": messageTypes.stateSnapshot,
  "snapshot-public.json": messageTypes.stateSnapshot,
};

test("TypeScript protocol consumes the canonical C#/Dart message fixtures", () => {
  for (const [name, type] of Object.entries(canonicalMessages)) {
    const message = parseMessage(fixture(name), type);
    assert.equal(message.protocolVersion, 1, name);
    assert.equal(message.type, type, name);
    assert.ok(message.messageId.length > 0, name);
  }
});

test("join descriptor JSON and URI stay canonical across languages", () => {
  const canonical = fixture("join-descriptor.json");
  const descriptor = parseJoinDescriptorJson(canonical);
  assert.equal(serializeJoinDescriptorJson(descriptor), canonical);

  const uri = serializeJoinDescriptorUri(descriptor);
  assert.equal(
    uri,
    "partygamekit://join?protocolVersion=1&roomId=room-001&joinCode=ROOM42&transport=lan-websocket&endpoint=ws%3A%2F%2F192.168.1.20%3A5042%2Fpartygamekit",
  );
  assert.deepEqual(parseJoinDescriptorUri(uri), descriptor);
});

test("snapshot ordering is independent per public/private projection", () => {
  const gate = new SnapshotSequenceGate();
  const public42 = parseMessage<StateSnapshotPayload>(fixture("snapshot-public.json"), messageTypes.stateSnapshot).payload;
  const player42 = parseMessage<StateSnapshotPayload>(fixture("snapshot-player.json"), messageTypes.stateSnapshot).payload;

  assert.equal(gate.accept(public42), true);
  assert.equal(gate.accept(player42), true);
  assert.equal(gate.accept(public42), false);
  assert.equal(gate.lastSeenSequence, 42);
});

test("player joins over WebSocket and reconnects with stable player identity", async () => {
  const sockets: FakeWebSocket[] = [];
  const identityStore = new MemoryIdentityStore();
  const ids = ["join-1", "rejoin-1", "other-1"];
  const client = new PartyGameClient({
    role: "player",
    identityStore,
    autoReconnect: false,
    heartbeatIntervalMs: 60_000,
    messageIdFactory: () => ids.shift() ?? "fallback-id",
    webSocketFactory: (url) => {
      const socket = new FakeWebSocket(url);
      sockets.push(socket);
      return socket;
    },
  });

  const joinPromise = client.join(parseJoinDescriptorJson(fixture("join-descriptor.json")));
  sockets[0].open();
  const joinRequest = parseMessage<{ playerId?: string }>(sockets[0].sent[0], messageTypes.joinRequest);
  const playerId = joinRequest.payload.playerId;
  assert.ok(playerId?.startsWith("player-"));

  sockets[0].receive(serializeMessage(createMessage(
    messageTypes.joinAccepted,
    "accepted-1",
    {
      roomId: "room-001",
      connectionId: "connection-1",
      role: "player",
      playerId,
      authorityId: "authority-001",
      reconnectToken: "resume-001",
    },
    joinRequest.messageId,
  )));
  const joined = await joinPromise;
  assert.equal(joined.playerId, playerId);
  assert.equal(client.stablePlayerId, playerId);

  sockets[0].remoteClose(1006, "network-lost");
  const reconnectPromise = client.reconnect();
  sockets[1].open();
  const rejoin = parseMessage<{
    playerId: string;
    reconnectToken: string;
    lastSeenSnapshotSequence: number;
  }>(sockets[1].sent[0], messageTypes.rejoinRequest);
  assert.equal(rejoin.payload.playerId, playerId);
  assert.equal(rejoin.payload.reconnectToken, "resume-001");
  assert.equal(rejoin.payload.lastSeenSnapshotSequence, 0);

  sockets[1].receive(serializeMessage(createMessage(messageTypes.rejoinAccepted, "reaccepted-1", {
    roomId: "room-001",
    playerId,
    connectionId: "connection-2",
    authorityId: "authority-001",
  }, rejoin.messageId)));
  const rejoined = await reconnectPromise;
  assert.equal(rejoined.playerId, playerId);
  assert.equal(rejoined.connectionId, "connection-2");
  client.leave();
});

test("shared-screen joins without player identity and only receives public projection", async () => {
  const socket = new FakeWebSocket("ws://unused");
  const client = new PartyGameClient({
    role: "shared-screen",
    autoReconnect: false,
    heartbeatIntervalMs: 60_000,
    messageIdFactory: () => "screen-msg",
    webSocketFactory: () => socket,
  });
  const received: StateSnapshotPayload[] = [];
  client.on("snapshot", (snapshot) => received.push(snapshot));

  const promise = client.join(parseJoinDescriptorJson(fixture("join-descriptor.json")));
  socket.open();
  const request = parseMessage<Record<string, unknown>>(socket.sent[0], messageTypes.joinRequest);
  assert.equal(request.payload.role, "shared-screen");
  assert.equal("playerId" in request.payload, false);

  socket.receive(serializeMessage(createMessage(messageTypes.joinAccepted, "accepted-screen", {
    roomId: "room-001",
    connectionId: "screen-connection",
    role: "shared-screen",
    authorityId: "authority-001",
  }, request.messageId)));
  await promise;

  socket.receive(fixture("snapshot-player.json"));
  socket.receive(fixture("snapshot-public.json"));
  assert.equal(received.length, 1);
  assert.equal(received[0].target.kind, "public");
  client.leave();
});

test("heartbeat reports newest accepted snapshot sequence", async () => {
  const socket = new FakeWebSocket("ws://unused");
  let id = 0;
  const client = new PartyGameClient({
    role: "shared-screen",
    autoReconnect: false,
    heartbeatIntervalMs: 5,
    messageIdFactory: () => `msg-${++id}`,
    webSocketFactory: () => socket,
  });

  const promise = client.join(parseJoinDescriptorJson(fixture("join-descriptor.json")));
  socket.open();
  const request = parseMessage(socket.sent[0], messageTypes.joinRequest);
  socket.receive(serializeMessage(createMessage(messageTypes.joinAccepted, "accepted", {
    roomId: "room-001",
    connectionId: "screen-connection",
    role: "shared-screen",
    authorityId: "authority-001",
  }, request.messageId)));
  await promise;
  socket.receive(fixture("snapshot-public.json"));

  await new Promise((resolveWait) => setTimeout(resolveWait, 20));
  const heartbeats = socket.sent
    .slice(1)
    .map((json) => parseMessage(json))
    .filter((message) => message.type === messageTypes.heartbeat) as ProtocolEnvelope<{ lastSeenSnapshotSequence: number }>[];
  assert.ok(heartbeats.length > 0);
  assert.equal(heartbeats.at(-1)?.payload.lastSeenSnapshotSequence, 42);
  client.leave();
});

class FakeWebSocket implements WebSocketLike {
  public readyState = 0;
  public binaryType: BinaryType = "blob";
  public onopen: ((event: Event) => void) | null = null;
  public onmessage: ((event: MessageEvent) => void) | null = null;
  public onclose: ((event: CloseEvent) => void) | null = null;
  public onerror: ((event: Event) => void) | null = null;
  public readonly sent: string[] = [];

  public constructor(public readonly url: string) {}

  public send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void {
    assert.equal(this.readyState, 1, "socket must be open before send");
    assert.equal(typeof data, "string", "protocol tests send JSON text frames");
    this.sent.push(data as string);
  }

  public close(code = 1000, reason = ""): void {
    this.readyState = 3;
    this.onclose?.({ code, reason } as CloseEvent);
  }

  public open(): void {
    this.readyState = 1;
    this.onopen?.(new Event("open"));
  }

  public receive(data: string): void {
    assert.equal(this.readyState, 1);
    this.onmessage?.({ data } as MessageEvent);
  }

  public remoteClose(code: number, reason: string): void {
    this.readyState = 3;
    this.onclose?.({ code, reason } as CloseEvent);
  }
}

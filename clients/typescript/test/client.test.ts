import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  MemoryIdentityStore,
  PartyGameClient,
  createMessage,
  messageTypes,
  parseConnectionDescriptorJson,
  parseConnectionDescriptorUri,
  parseMessage,
  serializeConnectionDescriptorJson,
  serializeConnectionDescriptorUri,
  serializeMessage,
  type ApplicationMessagePayload,
  type ConnectRequestPayload,
  type ResumeRequestPayload,
  type WebSocketLike,
} from "../src/index.js";

const fixturesDir = resolve(process.cwd(), "../../protocol/fixtures");
const fixture = (name: string): string => readFileSync(resolve(fixturesDir, name), "utf8").trim();

const canonicalMessages: Record<string, string> = {
  "v2-connect-request.json": messageTypes.connectRequest,
  "v2-resume-request.json": messageTypes.resumeRequest,
  "v2-heartbeat.json": messageTypes.heartbeat,
  "v2-application-message.json": messageTypes.applicationMessage,
};

test("TypeScript protocol consumes the canonical neutral v2 fixtures", () => {
  for (const [name, type] of Object.entries(canonicalMessages)) {
    const message = parseMessage(fixture(name), type);
    assert.equal(message.protocolVersion, 2, name);
    assert.equal(message.type, type, name);
    assert.ok(message.messageId.length > 0, name);
  }
});

test("connection descriptor JSON and URI stay canonical across languages", () => {
  const canonical = fixture("v2-connection-descriptor.json");
  const descriptor = parseConnectionDescriptorJson(canonical);
  assert.equal(serializeConnectionDescriptorJson(descriptor), canonical);

  const uri = serializeConnectionDescriptorUri(descriptor);
  assert.equal(
    uri,
    "dihor-gamekit-networking://connect?protocolVersion=2&transport=lan-websocket&endpoint=ws%3A%2F%2F192.168.1.10%3A45678%2Fdihor-gamekit-networking&channelId=channel-a",
  );
  assert.deepEqual(parseConnectionDescriptorUri(uri), descriptor);
});

test("generic peer connects, exchanges application messages and resumes without product roles", async (t) => {
  const sockets: FakeWebSocket[] = [];
  const identityStore = new MemoryIdentityStore();
  const ids = ["connect-1", "app-1", "resume-1", "app-2", "disconnect-1"];
  const client = new PartyGameClient({
    peerId: "peer-a",
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
  t.after(() => client.disconnect("test-cleanup"));

  const descriptor = parseConnectionDescriptorJson(fixture("v2-connection-descriptor.json"));
  const connectPromise = client.connect(descriptor);
  sockets[0].open();

  const connect = parseMessage<ConnectRequestPayload>(sockets[0].sent[0], messageTypes.connectRequest);
  assert.equal(connect.payload.peerId, "peer-a");
  assert.equal("role" in (connect.payload as object), false);
  assert.equal("playerId" in (connect.payload as object), false);

  sockets[0].receive(serializeMessage(createMessage(
    messageTypes.connectAccepted,
    "accepted-1",
    { connectionId: "connection-1", peerId: "peer-a", resumeToken: "resume-001" },
    connect.messageId,
  )));
  const connected = await connectPromise;
  assert.equal(connected.connectionId, "connection-1");
  assert.equal(connected.peerId, "peer-a");
  assert.equal(client.stablePeerId, "peer-a");

  client.send("sample.echo", { value: 42 });
  const outgoing = parseMessage<ApplicationMessagePayload<{ value: number }>>(
    sockets[0].sent[1],
    messageTypes.applicationMessage,
  );
  assert.equal(outgoing.payload.applicationType, "sample.echo");
  assert.deepEqual(outgoing.payload.data, { value: 42 });

  const incomingPromise = new Promise<ApplicationMessagePayload>((resolveMessage) => {
    const unsubscribe = client.on("applicationMessage", (message) => {
      unsubscribe();
      resolveMessage(message);
    });
  });
  sockets[0].receive(serializeMessage(createMessage(
    messageTypes.applicationMessage,
    "server-app-1",
    { applicationType: "sample.reply", data: { ok: true } },
  )));
  const incoming = await incomingPromise;
  assert.equal(incoming.applicationType, "sample.reply");
  assert.deepEqual(incoming.data, { ok: true });

  sockets[0].remoteClose(1006, "network-lost");
  const resumePromise = client.reconnect();
  sockets[1].open();
  const resume = parseMessage<ResumeRequestPayload>(sockets[1].sent[0], messageTypes.resumeRequest);
  assert.equal(resume.payload.peerId, "peer-a");
  assert.equal(resume.payload.resumeToken, "resume-001");

  sockets[1].receive(serializeMessage(createMessage(
    messageTypes.resumeAccepted,
    "resumed-1",
    { connectionId: "connection-2", peerId: "peer-a", resumeToken: "resume-002" },
    resume.messageId,
  )));
  const resumed = await resumePromise;
  assert.equal(resumed.connectionId, "connection-2");
  assert.equal(resumed.peerId, "peer-a");
  assert.equal(resumed.resumeToken, "resume-002");

  client.disconnect("test-complete");
});

test("anonymous connection does not acquire player or role semantics", async (t) => {
  const socket = new FakeWebSocket("ws://unused");
  const client = new PartyGameClient({
    peerId: null,
    autoReconnect: false,
    heartbeatIntervalMs: 60_000,
    messageIdFactory: () => "connect-anonymous",
    webSocketFactory: () => socket,
  });
  t.after(() => client.disconnect("test-cleanup"));

  const descriptor = parseConnectionDescriptorJson(fixture("v2-connection-descriptor.json"));
  const promise = client.connect(descriptor);
  socket.open();
  const request = parseMessage<ConnectRequestPayload>(socket.sent[0], messageTypes.connectRequest);
  assert.deepEqual(request.payload, {});

  socket.receive(serializeMessage(createMessage(
    messageTypes.connectAccepted,
    "accepted-anonymous",
    { connectionId: "connection-anonymous" },
    request.messageId,
  )));
  const connection = await promise;
  assert.equal(connection.connectionId, "connection-anonymous");
  assert.equal(connection.peerId, undefined);
  assert.equal(client.stablePeerId, null);
  client.disconnect();
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

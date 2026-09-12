import assert from "node:assert/strict";
import test from "node:test";
import { createSignalRSocket } from "../src/signalr.js";

class FakeSignalRConnection {
  public started = false;
  public stopped = false;
  public readonly sent: Array<{ method: string; args: unknown[] }> = [];
  private readonly handlers = new Map<string, (...args: unknown[]) => void>();
  private closeHandler: ((error?: Error) => void) | null = null;

  public async start(): Promise<void> {
    this.started = true;
  }

  public async stop(): Promise<void> {
    this.stopped = true;
  }

  public async send(methodName: string, ...args: unknown[]): Promise<void> {
    this.sent.push({ method: methodName, args });
  }

  public on(methodName: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(methodName, handler);
  }

  public onclose(callback: (error?: Error) => void): void {
    this.closeHandler = callback;
  }

  public emit(methodName: string, ...args: unknown[]): void {
    this.handlers.get(methodName)?.(...args);
  }

  public closeFromTransport(): void {
    this.closeHandler?.();
  }
}

test("SignalR adapter preserves PartyGameKit byte payloads and close semantics", async () => {
  const connection = new FakeSignalRConnection();
  const socket = createSignalRSocket("https://example.test/partygamekit", () => connection);

  await new Promise<void>((resolve) => {
    socket.onopen = () => resolve();
  });
  assert.equal(connection.started, true);
  assert.equal(socket.readyState, 1);

  socket.send("hello");
  await tick();
  assert.equal(connection.sent.length, 1);
  assert.equal(connection.sent[0]?.method, "Send");
  assert.equal(connection.sent[0]?.args[0], "aGVsbG8=");

  const received = new Promise<ArrayBuffer>((resolve) => {
    socket.onmessage = (event) => resolve(event.data as ArrayBuffer);
  });
  connection.emit("Receive", "AQID");
  assert.deepEqual(Array.from(new Uint8Array(await received)), [1, 2, 3]);

  const closed = new Promise<string>((resolve) => {
    socket.onclose = (event) => resolve(event.reason);
  });
  connection.emit("Disconnect", "server-stop");
  assert.equal(await closed, "server-stop");
  assert.equal(connection.stopped, true);
  assert.equal(socket.readyState, 3);
});

async function tick(): Promise<void> {
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
}

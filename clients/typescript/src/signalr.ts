import type { WebSocketLike } from "./client.js";

interface SignalRConnectionLike {
  start(): Promise<void>;
  stop(): Promise<void>;
  send(methodName: string, ...args: unknown[]): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

interface SignalRBrowserGlobal {
  HubConnectionBuilder: new () => {
    withUrl(url: string): { build(): SignalRConnectionLike };
  };
}

export type SignalRConnectionFactory = (url: string) => SignalRConnectionLike;

export function createSignalRSocket(
  url: string,
  connectionFactory: SignalRConnectionFactory = defaultConnectionFactory,
): WebSocketLike {
  return new SignalRSocket(url, connectionFactory);
}

class SignalRSocket implements WebSocketLike {
  public binaryType: BinaryType = "arraybuffer";
  public onopen: ((event: Event) => void) | null = null;
  public onmessage: ((event: MessageEvent) => void) | null = null;
  public onclose: ((event: CloseEvent) => void) | null = null;
  public onerror: ((event: Event) => void) | null = null;
  public readyState = 0;

  private readonly connection: SignalRConnectionLike;
  private closeRaised = false;

  public constructor(url: string, connectionFactory: SignalRConnectionFactory) {
    this.connection = connectionFactory(url);
    this.connection.on("Receive", (payloadBase64: unknown) => {
      if (typeof payloadBase64 !== "string") {
        this.raiseError();
        return;
      }
      try {
        const data = decodeBase64(payloadBase64);
        this.onmessage?.({ data } as MessageEvent);
      } catch {
        this.raiseError();
      }
    });
    this.connection.on("Disconnect", (reason: unknown) => {
      void this.stop(typeof reason === "string" ? reason : "server-disconnect");
    });
    this.connection.onclose((error) => {
      this.readyState = 3;
      this.raiseClose(error?.message ?? "signalr-connection-closed");
    });

    queueMicrotask(() => void this.start());
  }

  public send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void {
    if (this.readyState !== 1) {
      throw new Error("SignalR socket is not connected");
    }
    void this.sendAsync(data);
  }

  public close(_code?: number, reason?: string): void {
    if (this.readyState === 2 || this.readyState === 3) return;
    void this.stop(reason ?? "client-close");
  }

  private async start(): Promise<void> {
    try {
      await this.connection.start();
      if (this.readyState !== 0) return;
      this.readyState = 1;
      this.onopen?.(new Event("open"));
    } catch {
      this.readyState = 3;
      this.raiseError();
      this.raiseClose("signalr-start-failed");
    }
  }

  private async sendAsync(data: string | ArrayBufferLike | Blob | ArrayBufferView): Promise<void> {
    try {
      const bytes = await toBytes(data);
      await this.connection.send("Send", encodeBase64(bytes));
    } catch {
      this.raiseError();
    }
  }

  private async stop(reason: string): Promise<void> {
    if (this.readyState === 3) {
      this.raiseClose(reason);
      return;
    }

    this.readyState = 2;
    try {
      await this.connection.stop();
    } catch {
      this.raiseError();
    } finally {
      this.readyState = 3;
      this.raiseClose(reason);
    }
  }

  private raiseError(): void {
    this.onerror?.(new Event("error"));
  }

  private raiseClose(reason: string): void {
    if (this.closeRaised) return;
    this.closeRaised = true;
    this.onclose?.({ code: 1000, reason, wasClean: true } as CloseEvent);
  }
}

function defaultConnectionFactory(url: string): SignalRConnectionLike {
  const signalR = (globalThis as typeof globalThis & { signalR?: SignalRBrowserGlobal }).signalR;
  if (signalR === undefined) {
    throw new Error(
      "SignalR browser runtime is unavailable. Load @microsoft/signalr/dist/browser/signalr.min.js or provide a SignalRConnectionFactory.",
    );
  }
  return new signalR.HubConnectionBuilder().withUrl(url).build();
}

async function toBytes(data: string | ArrayBufferLike | Blob | ArrayBufferView): Promise<Uint8Array> {
  if (typeof data === "string") return new TextEncoder().encode(data);
  if (data instanceof Blob) return new Uint8Array(await data.arrayBuffer());
  if (ArrayBuffer.isView(data)) {
    return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
  }
  return new Uint8Array(data as ArrayBuffer);
}

function encodeBase64(bytes: Uint8Array): string {
  let binary = "";
  const chunkSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
  }
  return btoa(binary);
}

function decodeBase64(value: string): ArrayBuffer {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes.buffer;
}

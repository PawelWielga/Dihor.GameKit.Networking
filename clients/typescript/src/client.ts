import {
  createMessage,
  isRecord,
  isStateSnapshotPayload,
  messageTypes,
  parseMessage,
  requiredString,
  serializeMessage,
  type BrowserClientRole,
  type HeartbeatPayload,
  type JoinAcceptedPayload,
  type JoinRejectedPayload,
  type JoinRequestPayload,
  type LeavePayload,
  type ProtocolEnvelope,
  type RejoinAcceptedPayload,
  type RejoinRejectedPayload,
  type RejoinRequestPayload,
  type StateSnapshotPayload,
} from "./protocol.js";
import {
  assertCompatibleJoinDescriptor,
  createJoinDescriptor,
  type JoinDescriptor,
} from "./join-descriptor.js";
import {
  createDefaultIdentityStore,
  type ClientIdentityStore,
} from "./identity.js";
import { SnapshotSequenceGate } from "./snapshot.js";

export type ConnectionState = "idle" | "connecting" | "connected" | "reconnecting" | "closing" | "closed";

export interface SessionInfo {
  roomId: string;
  role: BrowserClientRole;
  connectionId: string;
  authorityId: string;
  playerId?: string;
}

export interface PartyGameClientEvents {
  state: ConnectionState;
  snapshot: StateSnapshotPayload;
  message: MessageEventData;
  error: Error;
}

export type MessageEventData = string | ArrayBuffer | Blob;
export type ClientEventName = keyof PartyGameClientEvents;
export type ClientEventListener<K extends ClientEventName> = (value: PartyGameClientEvents[K]) => void;

export interface WebSocketLike {
  readonly readyState: number;
  binaryType: BinaryType;
  onopen: ((event: Event) => void) | null;
  onmessage: ((event: MessageEvent) => void) | null;
  onclose: ((event: CloseEvent) => void) | null;
  onerror: ((event: Event) => void) | null;
  send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void;
  close(code?: number, reason?: string): void;
}

export interface PartyGameClientOptions {
  role: BrowserClientRole;
  identityStore?: ClientIdentityStore;
  webSocketFactory?: (url: string) => WebSocketLike;
  messageIdFactory?: () => string;
  heartbeatIntervalMs?: number;
  reconnectDelayMs?: number;
  autoReconnect?: boolean;
}

export class JoinRejectedError extends Error {
  public constructor(public readonly rejection: JoinRejectedPayload | RejoinRejectedPayload) {
    super(rejection.reason ?? rejection.code);
    this.name = "JoinRejectedError";
  }
}

export class PartyGameClient {
  private readonly identityStore: ClientIdentityStore;
  private readonly webSocketFactory: (url: string) => WebSocketLike;
  private readonly messageIdFactory: () => string;
  private readonly heartbeatIntervalMs: number;
  private readonly reconnectDelayMs: number;
  private readonly autoReconnect: boolean;
  private readonly snapshotGate = new SnapshotSequenceGate();
  private readonly listeners = new Map<ClientEventName, Set<(value: unknown) => void>>();

  private socket: WebSocketLike | null = null;
  private descriptor: JoinDescriptor | null = null;
  private session: SessionInfo | null = null;
  private currentState: ConnectionState = "idle";
  private heartbeatHandle: ReturnType<typeof setInterval> | null = null;
  private reconnectHandle: ReturnType<typeof setTimeout> | null = null;
  private pendingJoin: {
    resolve: (session: SessionInfo) => void;
    reject: (reason: unknown) => void;
  } | null = null;
  private intentionallyClosing = false;
  private playerId: string | null = null;

  public constructor(private readonly options: PartyGameClientOptions) {
    if (options.role !== "player" && options.role !== "shared-screen") {
      throw new Error("Browser client role must be 'player' or 'shared-screen'");
    }
    this.identityStore = options.identityStore ?? createDefaultIdentityStore();
    this.webSocketFactory = options.webSocketFactory ?? defaultWebSocketFactory;
    this.messageIdFactory = options.messageIdFactory ?? defaultMessageIdFactory;
    this.heartbeatIntervalMs = options.heartbeatIntervalMs ?? 10_000;
    this.reconnectDelayMs = options.reconnectDelayMs ?? 1_000;
    this.autoReconnect = options.autoReconnect ?? true;
    if (this.heartbeatIntervalMs <= 0) throw new Error("heartbeatIntervalMs must be positive");
    if (this.reconnectDelayMs < 0) throw new Error("reconnectDelayMs cannot be negative");
  }

  public get state(): ConnectionState {
    return this.currentState;
  }

  public get activeSession(): SessionInfo | null {
    return this.session;
  }

  public get stablePlayerId(): string | null {
    return this.playerId;
  }

  public get lastSeenSnapshotSequence(): number {
    return this.snapshotGate.lastSeenSequence;
  }

  public on<K extends ClientEventName>(event: K, listener: ClientEventListener<K>): () => void {
    let listeners = this.listeners.get(event);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(event, listeners);
    }
    listeners.add(listener as (value: unknown) => void);
    return () => listeners?.delete(listener as (value: unknown) => void);
  }

  public async join(descriptor: JoinDescriptor): Promise<SessionInfo> {
    if (this.pendingJoin !== null || this.socket !== null) {
      throw new Error("Client is already connecting or connected");
    }
    const normalized = createJoinDescriptor(descriptor);
    assertCompatibleJoinDescriptor(normalized);
    this.descriptor = normalized;
    this.intentionallyClosing = false;
    this.snapshotGate.reset();
    this.session = null;
    this.playerId = this.options.role === "player" ? this.ensureStablePlayerId() : null;
    return this.openSocket(false);
  }

  public async reconnect(): Promise<SessionInfo> {
    if (this.descriptor === null) throw new Error("Cannot reconnect before join()");
    if (this.socket !== null) throw new Error("Client is already connected");
    this.intentionallyClosing = false;
    return this.openSocket(true);
  }

  public leave(): void {
    this.intentionallyClosing = true;
    this.cancelReconnect();
    this.stopHeartbeat();
    if (this.socket !== null && this.session !== null && this.socket.readyState === 1) {
      const payload: LeavePayload = {
        roomId: this.session.roomId,
        connectionId: this.session.connectionId,
        ...(this.session.playerId === undefined ? {} : { playerId: this.session.playerId }),
      };
      this.socket.send(serializeMessage(createMessage(messageTypes.leave, this.messageIdFactory(), payload)));
      if (this.options.role === "player") {
        this.identityStore.clearReconnectCredential(this.session.roomId);
      }
    }
    this.setState("closing");
    this.socket?.close(1000, "client-leave");
    this.socket = null;
    this.session = null;
    this.setState("closed");
  }

  public send(data: string | ArrayBufferLike | Blob | ArrayBufferView): void {
    if (this.socket === null || this.socket.readyState !== 1 || this.session === null) {
      throw new Error("Client is not connected");
    }
    this.socket.send(data);
  }

  private openSocket(forceReconnect: boolean): Promise<SessionInfo> {
    const descriptor = this.descriptor;
    if (descriptor === null) throw new Error("Join descriptor is unavailable");
    const reconnectCredential = this.options.role === "player"
      ? this.identityStore.getReconnectCredential(descriptor.roomId)
      : null;
    const shouldRejoin = this.options.role === "player" && reconnectCredential !== null &&
      (forceReconnect || reconnectCredential.playerId === this.playerId);

    this.setState(forceReconnect || shouldRejoin ? "reconnecting" : "connecting");
    const socket = this.webSocketFactory(descriptor.endpoint);
    socket.binaryType = "arraybuffer";
    this.socket = socket;

    const result = new Promise<SessionInfo>((resolve, reject) => {
      this.pendingJoin = { resolve, reject };
    });

    socket.onopen = () => {
      if (shouldRejoin && reconnectCredential !== null) {
        const payload: RejoinRequestPayload = {
          roomId: descriptor.roomId,
          playerId: reconnectCredential.playerId,
          reconnectToken: reconnectCredential.reconnectToken,
          lastSeenSnapshotSequence: this.snapshotGate.lastSeenSequence,
        };
        socket.send(serializeMessage(createMessage(messageTypes.rejoinRequest, this.messageIdFactory(), payload)));
        return;
      }

      const payload: JoinRequestPayload = {
        roomId: descriptor.roomId,
        joinCode: descriptor.joinCode,
        role: this.options.role,
        ...(this.playerId === null ? {} : { playerId: this.playerId }),
      };
      socket.send(serializeMessage(createMessage(messageTypes.joinRequest, this.messageIdFactory(), payload)));
    };

    socket.onmessage = (event) => void this.handleIncoming(event.data);
    socket.onerror = () => this.emit("error", new Error("WebSocket transport error"));
    socket.onclose = (event) => this.handleClose(event);
    return result;
  }

  private async handleIncoming(data: unknown): Promise<void> {
    let messageData: MessageEventData;
    if (typeof data === "string" || data instanceof ArrayBuffer || data instanceof Blob) {
      messageData = data;
    } else if (ArrayBuffer.isView(data)) {
      messageData = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength) as ArrayBuffer;
    } else {
      return;
    }

    let protocolInput: string | ArrayBuffer;
    if (messageData instanceof Blob) {
      protocolInput = await messageData.arrayBuffer();
    } else {
      protocolInput = messageData;
    }

    let envelope: ProtocolEnvelope;
    try {
      envelope = parseMessage(protocolInput);
    } catch {
      this.emit("message", messageData);
      return;
    }

    try {
      switch (envelope.type) {
        case messageTypes.joinAccepted:
          this.acceptJoin(envelope as ProtocolEnvelope<JoinAcceptedPayload>);
          break;
        case messageTypes.rejoinAccepted:
          this.acceptRejoin(envelope as ProtocolEnvelope<RejoinAcceptedPayload>);
          break;
        case messageTypes.joinRejected:
          this.rejectJoin(envelope.payload as JoinRejectedPayload);
          break;
        case messageTypes.rejoinRejected:
          this.rejectJoin(envelope.payload as RejoinRejectedPayload);
          break;
        case messageTypes.stateSnapshot:
          this.acceptSnapshot(envelope.payload);
          break;
        default:
          this.emit("message", messageData);
          break;
      }
    } catch (error) {
      this.emit("error", error instanceof Error ? error : new Error(String(error)));
    }
  }

  private acceptJoin(envelope: ProtocolEnvelope<JoinAcceptedPayload>): void {
    const payload = envelope.payload;
    if (!isJoinAcceptedPayload(payload)) throw new Error("Invalid join accepted payload");
    if (payload.role !== this.options.role) throw new Error("Host accepted a different client role");
    if (this.options.role === "player") {
      if (payload.playerId === undefined || payload.playerId !== this.playerId) {
        throw new Error("Host accepted a different player identity");
      }
      if (payload.reconnectToken !== undefined) {
        this.identityStore.setReconnectCredential({
          roomId: payload.roomId,
          playerId: payload.playerId,
          reconnectToken: payload.reconnectToken,
        });
      }
    }

    this.session = {
      roomId: payload.roomId,
      role: this.options.role,
      connectionId: payload.connectionId,
      authorityId: payload.authorityId,
      ...(payload.playerId === undefined ? {} : { playerId: payload.playerId }),
    };
    this.completeJoin();
  }

  private acceptRejoin(envelope: ProtocolEnvelope<RejoinAcceptedPayload>): void {
    const payload = envelope.payload;
    if (!isRejoinAcceptedPayload(payload)) throw new Error("Invalid rejoin accepted payload");
    if (this.options.role !== "player" || payload.playerId !== this.playerId) {
      throw new Error("Rejoin accepted for an unexpected player identity");
    }
    this.session = {
      roomId: payload.roomId,
      role: "player",
      connectionId: payload.connectionId,
      authorityId: payload.authorityId,
      playerId: payload.playerId,
    };
    this.completeJoin();
  }

  private completeJoin(): void {
    const session = this.session;
    if (session === null) return;
    this.cancelReconnect();
    this.setState("connected");
    this.startHeartbeat();
    this.pendingJoin?.resolve(session);
    this.pendingJoin = null;
  }

  private rejectJoin(payload: JoinRejectedPayload | RejoinRejectedPayload): void {
    const error = new JoinRejectedError(payload);
    if (this.descriptor !== null && "roomId" in payload && this.options.role === "player") {
      this.identityStore.clearReconnectCredential(this.descriptor.roomId);
    }
    this.pendingJoin?.reject(error);
    this.pendingJoin = null;
    this.emit("error", error);
    this.intentionallyClosing = true;
    this.socket?.close(1008, "join-rejected");
    this.socket = null;
    this.setState("closed");
  }

  private acceptSnapshot(payload: unknown): void {
    if (!isStateSnapshotPayload(payload)) throw new Error("Invalid state snapshot payload");
    if (this.session === null || payload.roomId !== this.session.roomId) return;
    if (payload.target.kind === "player") {
      if (this.options.role !== "player" || payload.target.playerId !== this.playerId) return;
    }
    if (!this.snapshotGate.accept(payload)) return;
    this.emit("snapshot", payload);
  }

  private handleClose(event: CloseEvent): void {
    this.stopHeartbeat();
    this.socket = null;
    this.session = null;
    if (this.pendingJoin !== null) {
      this.pendingJoin.reject(new Error(`WebSocket closed before join completed (${event.code})`));
      this.pendingJoin = null;
    }
    if (this.intentionallyClosing || !this.autoReconnect || this.descriptor === null) {
      this.setState("closed");
      return;
    }
    this.setState("reconnecting");
    this.cancelReconnect();
    this.reconnectHandle = setTimeout(() => {
      this.reconnectHandle = null;
      void this.openSocket(true).catch((error) => this.emit("error", asError(error)));
    }, this.reconnectDelayMs);
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    this.heartbeatHandle = setInterval(() => {
      const socket = this.socket;
      const session = this.session;
      if (socket === null || session === null || socket.readyState !== 1) return;
      const payload: HeartbeatPayload = {
        roomId: session.roomId,
        lastSeenSnapshotSequence: this.snapshotGate.lastSeenSequence,
      };
      socket.send(serializeMessage(createMessage(messageTypes.heartbeat, this.messageIdFactory(), payload)));
    }, this.heartbeatIntervalMs);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatHandle !== null) {
      clearInterval(this.heartbeatHandle);
      this.heartbeatHandle = null;
    }
  }

  private cancelReconnect(): void {
    if (this.reconnectHandle !== null) {
      clearTimeout(this.reconnectHandle);
      this.reconnectHandle = null;
    }
  }

  private ensureStablePlayerId(): string {
    const existing = this.identityStore.getPlayerId();
    if (existing !== null && existing.trim().length > 0) return existing;
    const generated = `player-${randomId()}`;
    this.identityStore.setPlayerId(generated);
    return generated;
  }

  private setState(state: ConnectionState): void {
    if (this.currentState === state) return;
    this.currentState = state;
    this.emit("state", state);
  }

  private emit<K extends ClientEventName>(event: K, value: PartyGameClientEvents[K]): void {
    for (const listener of this.listeners.get(event) ?? []) {
      listener(value);
    }
  }
}

function isJoinAcceptedPayload(value: unknown): value is JoinAcceptedPayload {
  return isRecord(value) && typeof value.roomId === "string" &&
    typeof value.connectionId === "string" &&
    (value.role === "player" || value.role === "shared-screen" || value.role === "host") &&
    typeof value.authorityId === "string" &&
    (value.playerId === undefined || typeof value.playerId === "string") &&
    (value.reconnectToken === undefined || typeof value.reconnectToken === "string");
}

function isRejoinAcceptedPayload(value: unknown): value is RejoinAcceptedPayload {
  return isRecord(value) && typeof value.roomId === "string" && typeof value.playerId === "string" &&
    typeof value.connectionId === "string" && typeof value.authorityId === "string";
}

function defaultWebSocketFactory(url: string): WebSocketLike {
  if (typeof globalThis.WebSocket === "undefined") {
    throw new Error("WebSocket is not available in this runtime");
  }
  return new globalThis.WebSocket(url);
}

function defaultMessageIdFactory(): string {
  return `msg-${randomId()}`;
}

function randomId(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") return globalThis.crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

function asError(value: unknown): Error {
  return value instanceof Error ? value : new Error(String(value));
}

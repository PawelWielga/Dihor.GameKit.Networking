export const protocolVersion = 1 as const;

export const messageTypes = {
  joinRequest: "session.join.request",
  joinAccepted: "session.join.accepted",
  joinRejected: "session.join.rejected",
  leave: "session.leave",
  disconnected: "session.disconnected",
  heartbeat: "session.heartbeat",
  rejoinRequest: "session.rejoin.request",
  rejoinAccepted: "session.rejoin.accepted",
  rejoinRejected: "session.rejoin.rejected",
  stateSnapshot: "state.snapshot",
} as const;

export type ProtocolMessageType = (typeof messageTypes)[keyof typeof messageTypes];
export type ClientRole = "host" | "player" | "shared-screen";
export type BrowserClientRole = Exclude<ClientRole, "host">;

export interface ProtocolEnvelope<TPayload = unknown> {
  type: string;
  protocolVersion: number;
  messageId: string;
  correlationId?: string;
  payload: TPayload;
}

export interface JoinRequestPayload {
  roomId: string;
  joinCode: string;
  role: ClientRole;
  playerId?: string;
}

export interface JoinAcceptedPayload {
  roomId: string;
  connectionId: string;
  role: ClientRole;
  playerId?: string;
  authorityId: string;
  reconnectToken?: string;
}

export type JoinRejectionCode =
  | "room-not-found"
  | "room-closed"
  | "room-full"
  | "protocol-mismatch"
  | "resume-rejected";

export interface JoinRejectedPayload {
  joinCode: string;
  code: JoinRejectionCode;
  reason?: string;
}

export interface LeavePayload {
  roomId: string;
  connectionId: string;
  playerId?: string;
}

export interface DisconnectedPayload {
  roomId: string;
  connectionId: string;
  playerId?: string;
  reason?: string;
}

export interface HeartbeatPayload {
  roomId: string;
  lastSeenSnapshotSequence: number;
}

export interface RejoinRequestPayload {
  roomId: string;
  playerId: string;
  reconnectToken: string;
  lastSeenSnapshotSequence: number;
}

export interface RejoinAcceptedPayload {
  roomId: string;
  playerId: string;
  connectionId: string;
  authorityId: string;
}

export interface RejoinRejectedPayload {
  roomId: string;
  code: string;
  reason?: string;
}

export type SnapshotTarget =
  | { kind: "public" }
  | { kind: "player"; playerId: string };

export interface StateSnapshotPayload<TState = unknown> {
  roomId: string;
  authorityId: string;
  sequence: number;
  target: SnapshotTarget;
  state: TState;
}

export function createMessage<TPayload>(
  type: string,
  messageId: string,
  payload: TPayload,
  correlationId?: string,
): ProtocolEnvelope<TPayload> {
  const normalizedType = requiredString(type, "type");
  const normalizedMessageId = requiredString(messageId, "messageId");
  if (correlationId !== undefined && correlationId.trim().length === 0) {
    throw new Error("correlationId cannot be blank when present");
  }

  return {
    type: normalizedType,
    protocolVersion,
    messageId: normalizedMessageId,
    ...(correlationId === undefined ? {} : { correlationId }),
    payload,
  };
}

export function serializeMessage(message: ProtocolEnvelope): string {
  validateEnvelope(message);
  return JSON.stringify(message);
}

export function parseMessage<TPayload = unknown>(
  input: string | ArrayBuffer | ArrayBufferView,
  expectedType?: string,
): ProtocolEnvelope<TPayload> {
  const json = typeof input === "string" ? input : new TextDecoder().decode(toUint8Array(input));
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch (error) {
    throw new ProtocolError("invalid-json", "Invalid PartyGameKit JSON", error);
  }

  if (!isRecord(value)) {
    throw new ProtocolError("invalid-contract", "Protocol envelope must be an object");
  }
  if (typeof value.type !== "string" || value.type.trim().length === 0) {
    throw new ProtocolError("missing-message-type", "Protocol message type is required");
  }
  if (expectedType !== undefined && value.type !== expectedType) {
    throw new ProtocolError("message-type-mismatch", `Expected ${expectedType}, received ${value.type}`);
  }
  if (typeof value.protocolVersion !== "number" || !Number.isInteger(value.protocolVersion)) {
    throw new ProtocolError("missing-protocol-version", "Protocol version is required");
  }
  if (value.protocolVersion !== protocolVersion) {
    throw new ProtocolError(
      "protocol-version-mismatch",
      `Unsupported protocol version ${String(value.protocolVersion)}`,
    );
  }
  if (typeof value.messageId !== "string" || value.messageId.trim().length === 0) {
    throw new ProtocolError("invalid-contract", "messageId is required");
  }
  if (value.correlationId !== undefined &&
      (typeof value.correlationId !== "string" || value.correlationId.trim().length === 0)) {
    throw new ProtocolError("invalid-contract", "correlationId cannot be blank");
  }
  if (!("payload" in value)) {
    throw new ProtocolError("invalid-contract", "payload is required");
  }

  return value as unknown as ProtocolEnvelope<TPayload>;
}

export class ProtocolError extends Error {
  public constructor(
    public readonly code:
      | "invalid-json"
      | "invalid-contract"
      | "missing-message-type"
      | "message-type-mismatch"
      | "missing-protocol-version"
      | "protocol-version-mismatch",
    message: string,
    options?: unknown,
  ) {
    super(message, options instanceof Error ? { cause: options } : undefined);
    this.name = "ProtocolError";
  }
}

export function isStateSnapshotPayload(value: unknown): value is StateSnapshotPayload {
  if (!isRecord(value) ||
      typeof value.roomId !== "string" ||
      typeof value.authorityId !== "string" ||
      typeof value.sequence !== "number" ||
      !Number.isSafeInteger(value.sequence) ||
      value.sequence <= 0 ||
      !isRecord(value.target) ||
      typeof value.target.kind !== "string") {
    return false;
  }

  if (value.target.kind === "public") {
    return value.target.playerId === undefined;
  }

  return value.target.kind === "player" &&
    typeof value.target.playerId === "string" &&
    value.target.playerId.trim().length > 0;
}

function validateEnvelope(message: ProtocolEnvelope): void {
  requiredString(message.type, "type");
  requiredString(message.messageId, "messageId");
  if (message.protocolVersion !== protocolVersion) {
    throw new ProtocolError("protocol-version-mismatch", "Only protocol v1 can be serialized");
  }
  if (message.correlationId !== undefined && message.correlationId.trim().length === 0) {
    throw new ProtocolError("invalid-contract", "correlationId cannot be blank");
  }
}

export function requiredString(value: string, name: string): string {
  const normalized = value.trim();
  if (normalized.length === 0) {
    throw new Error(`${name} cannot be empty`);
  }
  return normalized;
}

function toUint8Array(value: ArrayBuffer | ArrayBufferView): Uint8Array {
  return value instanceof ArrayBuffer
    ? new Uint8Array(value)
    : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

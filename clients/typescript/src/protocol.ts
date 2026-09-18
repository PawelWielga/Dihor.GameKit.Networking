export const protocolVersion = 2 as const;

export const messageTypes = {
  connectRequest: "connection.connect.request",
  connectAccepted: "connection.connect.accepted",
  connectRejected: "connection.connect.rejected",
  resumeRequest: "connection.resume.request",
  resumeAccepted: "connection.resume.accepted",
  resumeRejected: "connection.resume.rejected",
  heartbeat: "connection.heartbeat",
  disconnect: "connection.disconnect",
  applicationMessage: "application.message",
} as const;

export type ProtocolMessageType = (typeof messageTypes)[keyof typeof messageTypes];

export interface ProtocolEnvelope<TPayload = unknown> {
  type: string;
  protocolVersion: number;
  messageId: string;
  correlationId?: string;
  payload: TPayload;
}

export interface ConnectRequestPayload { peerId?: string; }
export interface ConnectAcceptedPayload { connectionId: string; peerId?: string; resumeToken?: string; }
export type ConnectionRejectionCode =
  | "protocol-mismatch"
  | "invalid-request"
  | "unknown-peer"
  | "invalid-resume-credential"
  | "reconnect-window-expired"
  | "peer-already-connected"
  | "connection-already-bound";
export interface ConnectRejectedPayload { code: ConnectionRejectionCode | string; reason?: string; }
export interface ResumeRequestPayload { peerId: string; resumeToken: string; }
export interface ResumeAcceptedPayload { connectionId: string; peerId: string; resumeToken?: string; }
export interface ResumeRejectedPayload { peerId: string; code: ConnectionRejectionCode | string; reason?: string; }
export interface HeartbeatPayload { peerId?: string; }
export interface DisconnectPayload { reason?: string; }
export interface ApplicationMessagePayload<TData = unknown> { applicationType: string; data: TData; }

export function createMessage<TPayload>(type: string, messageId: string, payload: TPayload, correlationId?: string): ProtocolEnvelope<TPayload> {
  const normalizedType = requiredString(type, "type");
  const normalizedMessageId = requiredString(messageId, "messageId");
  if (correlationId !== undefined && correlationId.trim().length === 0) throw new Error("correlationId cannot be blank when present");
  return { type: normalizedType, protocolVersion, messageId: normalizedMessageId, ...(correlationId === undefined ? {} : { correlationId }), payload };
}

export function serializeMessage(message: ProtocolEnvelope): string {
  validateEnvelope(message);
  return JSON.stringify(message);
}

export function parseMessage<TPayload = unknown>(input: string | ArrayBuffer | ArrayBufferView, expectedType?: string): ProtocolEnvelope<TPayload> {
  const json = typeof input === "string" ? input : new TextDecoder().decode(toUint8Array(input));
  let value: unknown;
  try { value = JSON.parse(json); } catch (error) { throw new ProtocolError("invalid-json", "Invalid Dihor.GameKit.Networking JSON", error); }
  if (!isRecord(value)) throw new ProtocolError("invalid-contract", "Protocol envelope must be an object");
  if (typeof value.type !== "string" || value.type.trim().length === 0) throw new ProtocolError("missing-message-type", "Protocol message type is required");
  if (expectedType !== undefined && value.type !== expectedType) throw new ProtocolError("message-type-mismatch", `Expected ${expectedType}, received ${value.type}`);
  if (typeof value.protocolVersion !== "number" || !Number.isInteger(value.protocolVersion)) throw new ProtocolError("missing-protocol-version", "Protocol version is required");
  if (value.protocolVersion !== protocolVersion) throw new ProtocolError("protocol-version-mismatch", `Unsupported protocol version ${String(value.protocolVersion)}`);
  if (typeof value.messageId !== "string" || value.messageId.trim().length === 0) throw new ProtocolError("invalid-contract", "messageId is required");
  if (value.correlationId !== undefined && (typeof value.correlationId !== "string" || value.correlationId.trim().length === 0)) throw new ProtocolError("invalid-contract", "correlationId cannot be blank");
  if (!("payload" in value)) throw new ProtocolError("invalid-contract", "payload is required");
  return value as unknown as ProtocolEnvelope<TPayload>;
}

export class ProtocolError extends Error {
  public constructor(
    public readonly code: "invalid-json" | "invalid-contract" | "missing-message-type" | "message-type-mismatch" | "missing-protocol-version" | "protocol-version-mismatch",
    message: string,
    options?: unknown,
  ) {
    super(message, options instanceof Error ? { cause: options } : undefined);
    this.name = "ProtocolError";
  }
}

export function isApplicationMessagePayload(value: unknown): value is ApplicationMessagePayload {
  return isRecord(value) && typeof value.applicationType === "string" && value.applicationType.trim().length > 0 && "data" in value;
}

export function requiredString(value: string, name: string): string {
  const normalized = value.trim();
  if (normalized.length === 0) throw new Error(`${name} cannot be empty`);
  return normalized;
}

function validateEnvelope(message: ProtocolEnvelope): void {
  requiredString(message.type, "type");
  requiredString(message.messageId, "messageId");
  if (message.protocolVersion !== protocolVersion) throw new ProtocolError("protocol-version-mismatch", "Only protocol v2 can be serialized");
  if (message.correlationId !== undefined && message.correlationId.trim().length === 0) throw new ProtocolError("invalid-contract", "correlationId cannot be blank");
}

function toUint8Array(value: ArrayBuffer | ArrayBufferView): Uint8Array {
  return value instanceof ArrayBuffer ? new Uint8Array(value) : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

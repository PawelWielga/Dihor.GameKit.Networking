import { protocolVersion, requiredString } from "./protocol.js";

export interface JoinDescriptor {
  protocolVersion: number;
  roomId: string;
  joinCode: string;
  transport: string;
  endpoint: string;
}

export function createJoinDescriptor(value: JoinDescriptor): JoinDescriptor {
  if (!Number.isInteger(value.protocolVersion) || value.protocolVersion <= 0) {
    throw new Error("protocolVersion must be a positive integer");
  }

  const endpoint = canonicalEndpoint(requiredString(value.endpoint, "endpoint"));
  return {
    protocolVersion: value.protocolVersion,
    roomId: requiredString(value.roomId, "roomId"),
    joinCode: requiredString(value.joinCode, "joinCode").toUpperCase(),
    transport: requiredString(value.transport, "transport").toLowerCase(),
    endpoint,
  };
}

export function parseJoinDescriptorJson(json: string): JoinDescriptor {
  let value: unknown;
  try {
    value = JSON.parse(requiredString(json, "join descriptor JSON"));
  } catch (error) {
    throw new Error("Invalid join descriptor JSON", { cause: error });
  }
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("Join descriptor JSON must be an object");
  }

  const candidate = value as Partial<JoinDescriptor>;
  if (typeof candidate.protocolVersion !== "number" ||
      typeof candidate.roomId !== "string" ||
      typeof candidate.joinCode !== "string" ||
      typeof candidate.transport !== "string" ||
      typeof candidate.endpoint !== "string") {
    throw new Error("Join descriptor is missing required fields");
  }

  return createJoinDescriptor(candidate as JoinDescriptor);
}

export function serializeJoinDescriptorJson(descriptor: JoinDescriptor): string {
  const normalized = createJoinDescriptor(descriptor);
  return JSON.stringify({
    protocolVersion: normalized.protocolVersion,
    roomId: normalized.roomId,
    joinCode: normalized.joinCode,
    transport: normalized.transport,
    endpoint: normalized.endpoint,
  });
}

export function parseJoinDescriptorUri(input: string): JoinDescriptor {
  const value = requiredString(input, "join descriptor");
  const match = /^partygamekit:\/\/join\?(.+)$/i.exec(value);
  if (match === null) {
    throw new Error("Invalid PartyGameKit join URI");
  }

  const fields = new Map<string, string>();
  for (const segment of match[1].split("&")) {
    const separator = segment.indexOf("=");
    if (separator <= 0) {
      throw new Error("Invalid join URI query");
    }
    const key = decodeURIComponent(segment.slice(0, separator));
    const fieldValue = decodeURIComponent(segment.slice(separator + 1));
    if (fields.has(key)) {
      throw new Error(`Duplicate join URI field '${key}'`);
    }
    fields.set(key, fieldValue);
  }

  const versionText = fields.get("protocolVersion");
  const roomId = fields.get("roomId");
  const joinCode = fields.get("joinCode");
  const transport = fields.get("transport");
  const endpoint = fields.get("endpoint");
  if (versionText === undefined || roomId === undefined || joinCode === undefined ||
      transport === undefined || endpoint === undefined || !/^\d+$/.test(versionText)) {
    throw new Error("Join URI is missing required fields");
  }

  return createJoinDescriptor({
    protocolVersion: Number(versionText),
    roomId,
    joinCode,
    transport,
    endpoint,
  });
}

export function serializeJoinDescriptorUri(descriptor: JoinDescriptor): string {
  const normalized = createJoinDescriptor(descriptor);
  return "partygamekit://join" +
    `?protocolVersion=${normalized.protocolVersion}` +
    `&roomId=${encodeURIComponent(normalized.roomId)}` +
    `&joinCode=${encodeURIComponent(normalized.joinCode)}` +
    `&transport=${encodeURIComponent(normalized.transport)}` +
    `&endpoint=${encodeURIComponent(normalized.endpoint)}`;
}

export function parseJoinDescriptor(input: string): JoinDescriptor {
  const value = requiredString(input, "join descriptor");
  return value.startsWith("{") ? parseJoinDescriptorJson(value) : parseJoinDescriptorUri(value);
}

export function assertCompatibleJoinDescriptor(descriptor: JoinDescriptor): void {
  if (descriptor.protocolVersion !== protocolVersion) {
    throw new Error(`Unsupported protocol version ${descriptor.protocolVersion}`);
  }

  const endpoint = new URL(descriptor.endpoint);
  if (descriptor.transport === "lan-websocket") {
    if (endpoint.protocol !== "ws:" && endpoint.protocol !== "wss:") {
      throw new Error("Browser LAN transport requires ws:// or wss:// endpoint");
    }
    return;
  }

  if (descriptor.transport === "signalr") {
    if (endpoint.protocol !== "http:" && endpoint.protocol !== "https:") {
      throw new Error("Browser SignalR transport requires http:// or https:// endpoint");
    }
    return;
  }

  throw new Error(`Unsupported browser transport '${descriptor.transport}'`);
}

function canonicalEndpoint(value: string): string {
  try {
    return new URL(value).href;
  } catch (error) {
    throw new Error("endpoint must be an absolute URI", { cause: error });
  }
}

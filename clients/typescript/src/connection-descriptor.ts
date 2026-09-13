import { protocolVersion, requiredString } from "./protocol.js";

export interface ConnectionDescriptor {
  protocolVersion: number;
  transport: string;
  endpoint: string;
  channelId?: string;
}

export function createConnectionDescriptor(value: ConnectionDescriptor): ConnectionDescriptor {
  if (!Number.isInteger(value.protocolVersion) || value.protocolVersion <= 0) {
    throw new Error("protocolVersion must be a positive integer");
  }

  const descriptor: ConnectionDescriptor = {
    protocolVersion: value.protocolVersion,
    transport: requiredString(value.transport, "transport"),
    endpoint: canonicalEndpoint(requiredString(value.endpoint, "endpoint")),
  };

  if (value.channelId !== undefined) {
    descriptor.channelId = requiredString(value.channelId, "channelId");
  }
  return descriptor;
}

export function parseConnectionDescriptorJson(json: string): ConnectionDescriptor {
  let value: unknown;
  try {
    value = JSON.parse(requiredString(json, "connection descriptor JSON"));
  } catch (error) {
    throw new Error("Invalid connection descriptor JSON", { cause: error });
  }
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error("Connection descriptor JSON must be an object");
  }

  const candidate = value as Partial<ConnectionDescriptor>;
  if (typeof candidate.protocolVersion !== "number" ||
      typeof candidate.transport !== "string" ||
      typeof candidate.endpoint !== "string" ||
      (candidate.channelId !== undefined && typeof candidate.channelId !== "string")) {
    throw new Error("Connection descriptor is missing required fields");
  }

  return createConnectionDescriptor(candidate as ConnectionDescriptor);
}

export function serializeConnectionDescriptorJson(descriptor: ConnectionDescriptor): string {
  const normalized = createConnectionDescriptor(descriptor);
  return JSON.stringify({
    protocolVersion: normalized.protocolVersion,
    transport: normalized.transport,
    endpoint: normalized.endpoint,
    ...(normalized.channelId === undefined ? {} : { channelId: normalized.channelId }),
  });
}

export function parseConnectionDescriptorUri(input: string): ConnectionDescriptor {
  const value = requiredString(input, "connection URI");
  const match = /^partygamekit:\/\/connect\?(.+)$/i.exec(value);
  if (match === null) {
    throw new Error("Invalid PartyGameKit connection URI");
  }

  const fields = new Map<string, string>();
  for (const segment of match[1].split("&")) {
    const separator = segment.indexOf("=");
    if (separator <= 0) throw new Error("Invalid connection URI query");
    const key = decodeURIComponent(segment.slice(0, separator));
    const fieldValue = decodeURIComponent(segment.slice(separator + 1));
    if (fields.has(key)) throw new Error(`Duplicate connection URI field '${key}'`);
    fields.set(key, fieldValue);
  }

  const versionText = fields.get("protocolVersion");
  const transport = fields.get("transport");
  const endpoint = fields.get("endpoint");
  const channelId = fields.get("channelId");
  if (versionText === undefined || transport === undefined || endpoint === undefined || !/^\d+$/.test(versionText)) {
    throw new Error("Connection URI is missing required fields");
  }

  return createConnectionDescriptor({
    protocolVersion: Number(versionText),
    transport,
    endpoint,
    ...(channelId === undefined ? {} : { channelId }),
  });
}

export function serializeConnectionDescriptorUri(descriptor: ConnectionDescriptor): string {
  const normalized = createConnectionDescriptor(descriptor);
  return "partygamekit://connect" +
    `?protocolVersion=${normalized.protocolVersion}` +
    `&transport=${encodeURIComponent(normalized.transport)}` +
    `&endpoint=${encodeURIComponent(normalized.endpoint)}` +
    (normalized.channelId === undefined ? "" : `&channelId=${encodeURIComponent(normalized.channelId)}`);
}

export function parseConnectionDescriptor(input: string): ConnectionDescriptor {
  const value = requiredString(input, "connection descriptor");
  return value.startsWith("{") ? parseConnectionDescriptorJson(value) : parseConnectionDescriptorUri(value);
}

export function assertCompatibleConnectionDescriptor(descriptor: ConnectionDescriptor): void {
  if (descriptor.protocolVersion !== protocolVersion) {
    throw new Error(`Unsupported protocol version ${descriptor.protocolVersion}`);
  }
  if (descriptor.transport !== "lan-websocket") {
    throw new Error(`Unsupported browser transport '${descriptor.transport}'`);
  }
  const endpoint = new URL(descriptor.endpoint);
  if (endpoint.protocol !== "ws:" && endpoint.protocol !== "wss:") {
    throw new Error("Browser LAN transport requires ws:// or wss:// endpoint");
  }
}

function canonicalEndpoint(value: string): string {
  try {
    const url = new URL(value);
    if (url.protocol === "file:") throw new Error("file URI is not a network endpoint");
    return url.href;
  } catch (error) {
    throw new Error("endpoint must be an absolute non-file URI", { cause: error });
  }
}

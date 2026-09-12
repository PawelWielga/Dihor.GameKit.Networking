import { requiredString } from "./protocol.js";

export interface ReconnectCredential {
  roomId: string;
  playerId: string;
  reconnectToken: string;
}

export interface ClientIdentityStore {
  getPlayerId(): string | null;
  setPlayerId(playerId: string): void;
  getReconnectCredential(roomId: string): ReconnectCredential | null;
  setReconnectCredential(credential: ReconnectCredential): void;
  clearReconnectCredential(roomId: string): void;
}

export class MemoryIdentityStore implements ClientIdentityStore {
  private playerId: string | null = null;
  private readonly reconnect = new Map<string, ReconnectCredential>();

  public getPlayerId(): string | null {
    return this.playerId;
  }

  public setPlayerId(playerId: string): void {
    this.playerId = requiredString(playerId, "playerId");
  }

  public getReconnectCredential(roomId: string): ReconnectCredential | null {
    return this.reconnect.get(requiredString(roomId, "roomId")) ?? null;
  }

  public setReconnectCredential(credential: ReconnectCredential): void {
    const roomId = requiredString(credential.roomId, "roomId");
    this.reconnect.set(roomId, {
      roomId,
      playerId: requiredString(credential.playerId, "playerId"),
      reconnectToken: requiredString(credential.reconnectToken, "reconnectToken"),
    });
  }

  public clearReconnectCredential(roomId: string): void {
    this.reconnect.delete(requiredString(roomId, "roomId"));
  }
}

export class LocalStorageIdentityStore implements ClientIdentityStore {
  public constructor(
    private readonly storage: Pick<Storage, "getItem" | "setItem" | "removeItem">,
    private readonly prefix = "partygamekit.client",
  ) {}

  public getPlayerId(): string | null {
    return this.storage.getItem(`${this.prefix}.playerId`);
  }

  public setPlayerId(playerId: string): void {
    this.storage.setItem(`${this.prefix}.playerId`, requiredString(playerId, "playerId"));
  }

  public getReconnectCredential(roomId: string): ReconnectCredential | null {
    const normalizedRoomId = requiredString(roomId, "roomId");
    const raw = this.storage.getItem(`${this.prefix}.reconnect.${normalizedRoomId}`);
    if (raw === null) return null;
    try {
      const value = JSON.parse(raw) as ReconnectCredential;
      if (value.roomId !== normalizedRoomId ||
          typeof value.playerId !== "string" || value.playerId.trim().length === 0 ||
          typeof value.reconnectToken !== "string" || value.reconnectToken.trim().length === 0) {
        return null;
      }
      return value;
    } catch {
      return null;
    }
  }

  public setReconnectCredential(credential: ReconnectCredential): void {
    const normalized: ReconnectCredential = {
      roomId: requiredString(credential.roomId, "roomId"),
      playerId: requiredString(credential.playerId, "playerId"),
      reconnectToken: requiredString(credential.reconnectToken, "reconnectToken"),
    };
    this.storage.setItem(`${this.prefix}.reconnect.${normalized.roomId}`, JSON.stringify(normalized));
  }

  public clearReconnectCredential(roomId: string): void {
    this.storage.removeItem(`${this.prefix}.reconnect.${requiredString(roomId, "roomId")}`);
  }
}

export function createDefaultIdentityStore(): ClientIdentityStore {
  try {
    if (typeof globalThis.localStorage !== "undefined") {
      return new LocalStorageIdentityStore(globalThis.localStorage);
    }
  } catch {
    // Access to localStorage can be denied by browser privacy/security settings.
  }
  return new MemoryIdentityStore();
}

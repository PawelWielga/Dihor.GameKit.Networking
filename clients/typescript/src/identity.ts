import { requiredString } from "./protocol.js";

export interface ResumeCredential {
  scope: string;
  peerId: string;
  resumeToken: string;
}

export interface PeerIdentityStore {
  getPeerId(): string | null;
  setPeerId(peerId: string): void;
  getResumeCredential(scope: string): ResumeCredential | null;
  setResumeCredential(credential: ResumeCredential): void;
  clearResumeCredential(scope: string): void;
}

export class MemoryIdentityStore implements PeerIdentityStore {
  private peerId: string | null = null;
  private readonly resume = new Map<string, ResumeCredential>();

  public getPeerId(): string | null {
    return this.peerId;
  }

  public setPeerId(peerId: string): void {
    this.peerId = requiredString(peerId, "peerId");
  }

  public getResumeCredential(scope: string): ResumeCredential | null {
    return this.resume.get(requiredString(scope, "scope")) ?? null;
  }

  public setResumeCredential(credential: ResumeCredential): void {
    const scope = requiredString(credential.scope, "scope");
    this.resume.set(scope, {
      scope,
      peerId: requiredString(credential.peerId, "peerId"),
      resumeToken: requiredString(credential.resumeToken, "resumeToken"),
    });
  }

  public clearResumeCredential(scope: string): void {
    this.resume.delete(requiredString(scope, "scope"));
  }
}

export class LocalStorageIdentityStore implements PeerIdentityStore {
  public constructor(
    private readonly storage: Pick<Storage, "getItem" | "setItem" | "removeItem">,
    private readonly prefix = "partygamekit.client",
  ) {}

  public getPeerId(): string | null {
    return this.storage.getItem(`${this.prefix}.peerId`);
  }

  public setPeerId(peerId: string): void {
    this.storage.setItem(`${this.prefix}.peerId`, requiredString(peerId, "peerId"));
  }

  public getResumeCredential(scope: string): ResumeCredential | null {
    const normalizedScope = requiredString(scope, "scope");
    const raw = this.storage.getItem(`${this.prefix}.resume.${encodeURIComponent(normalizedScope)}`);
    if (raw === null) return null;
    try {
      const value = JSON.parse(raw) as ResumeCredential;
      if (value.scope !== normalizedScope ||
          typeof value.peerId !== "string" || value.peerId.trim().length === 0 ||
          typeof value.resumeToken !== "string" || value.resumeToken.trim().length === 0) {
        return null;
      }
      return value;
    } catch {
      return null;
    }
  }

  public setResumeCredential(credential: ResumeCredential): void {
    const normalized: ResumeCredential = {
      scope: requiredString(credential.scope, "scope"),
      peerId: requiredString(credential.peerId, "peerId"),
      resumeToken: requiredString(credential.resumeToken, "resumeToken"),
    };
    this.storage.setItem(
      `${this.prefix}.resume.${encodeURIComponent(normalized.scope)}`,
      JSON.stringify(normalized),
    );
  }

  public clearResumeCredential(scope: string): void {
    const normalizedScope = requiredString(scope, "scope");
    this.storage.removeItem(`${this.prefix}.resume.${encodeURIComponent(normalizedScope)}`);
  }
}

export function createDefaultIdentityStore(): PeerIdentityStore {
  try {
    if (typeof globalThis.localStorage !== "undefined") {
      return new LocalStorageIdentityStore(globalThis.localStorage);
    }
  } catch {
    // Access to localStorage can be denied by browser privacy/security settings.
  }
  return new MemoryIdentityStore();
}

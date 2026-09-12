import type { StateSnapshotPayload } from "./protocol.js";

export class SnapshotSequenceGate {
  private readonly latestByTarget = new Map<string, number>();

  public accept(snapshot: StateSnapshotPayload): boolean {
    const key = snapshot.target.kind === "public"
      ? "public"
      : `player:${snapshot.target.playerId}`;
    const previous = this.latestByTarget.get(key) ?? 0;
    if (snapshot.sequence <= previous) {
      return false;
    }
    this.latestByTarget.set(key, snapshot.sequence);
    return true;
  }

  public get lastSeenSequence(): number {
    let latest = 0;
    for (const value of this.latestByTarget.values()) {
      latest = Math.max(latest, value);
    }
    return latest;
  }

  public reset(): void {
    this.latestByTarget.clear();
  }
}

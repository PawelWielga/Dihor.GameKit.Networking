import test from "node:test";
import assert from "node:assert/strict";
import {
  WebRtcPeer,
  type WebRtcSignal,
  type WebRtcSignalingChannel,
} from "../src/index.js";

test("WebRTC connection failure rejects connect and exposes failed state", async (t) => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  connection.fail();

  let caught: unknown;
  try {
    await connecting;
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof Error);
  assert.equal(caught.message, "WebRTC peer connection failed.");
  assert.equal(peer.currentState, "failed");
});

test("closing before DataChannel open rejects connect instead of leaving it pending", async () => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });

  const connecting = peer.connect();
  peer.close();

  let caught: unknown;
  try {
    await connecting;
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof Error);
  assert.equal(caught.message, "WebRTC peer was closed before the DataChannel opened.");
  assert.equal(peer.currentState, "closed");
});

class IdleSignalingChannel implements WebRtcSignalingChannel {
  send(_signal: WebRtcSignal): void {}

  subscribe(_handler: (signal: WebRtcSignal) => void | Promise<void>): () => void {
    return () => {};
  }
}

class LifecyclePeerConnection {
  connectionState: RTCPeerConnectionState = "new";
  private readonly listeners = new Map<string, Set<() => void>>();

  asRtcPeerConnection(): RTCPeerConnection {
    return this as unknown as RTCPeerConnection;
  }

  addEventListener(type: string, listener: () => void): void {
    let listeners = this.listeners.get(type);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(type, listeners);
    }
    listeners.add(listener);
  }

  close(): void {
    this.connectionState = "closed";
    this.emit("connectionstatechange");
  }

  fail(): void {
    this.connectionState = "failed";
    this.emit("connectionstatechange");
  }

  private emit(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener();
    }
  }
}

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

  const caught = await captureRejection(connecting);
  assert.ok(caught instanceof Error);
  assert.equal(caught.message, "WebRTC peer connection failed.");
  assert.equal(peer.currentState, "failed");
  assert.ok(connection.closeCount >= 1);

  const reconnect = await captureRejection(peer.connect());
  assert.equal(reconnect, caught);
  assert.equal(peer.currentState, "failed");
});

test("closing before DataChannel open rejects connect instead of leaving it pending", async () => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });

  const connecting = peer.connect();
  peer.close();

  const caught = await captureRejection(connecting);
  assert.ok(caught instanceof Error);
  assert.equal(caught.message, "WebRTC peer was closed before the DataChannel opened.");
  assert.equal(peer.currentState, "closed");
});

test("closing a peer that was never connected is cleanup-safe", async () => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });

  peer.close();
  await Promise.resolve();

  assert.equal(peer.currentState, "closed");
  assert.equal(connection.closeCount, 1);
});

test("synchronous signaling subscription failure becomes terminal and cleans RTC resources", async (t) => {
  const connection = new LifecyclePeerConnection();
  const expected = new Error("signaling backlog overflow");
  const peer = new WebRtcPeer(new ThrowingSignalingChannel(expected), {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const first = await captureRejection(peer.connect());
  assert.equal(first, expected);
  assert.equal(peer.currentState, "failed");
  assert.equal(connection.closeCount, 1);

  const second = await captureRejection(peer.connect());
  assert.equal(second, expected);
  assert.equal(peer.currentState, "failed");
});

test("asynchronous signaling failure rejects a pending negotiation and cleans RTC resources", async (t) => {
  const connection = new LifecyclePeerConnection();
  const signaling = new FailableSignalingChannel();
  const peer = new WebRtcPeer(signaling, {
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  const expected = new Error("SignalR signaling connection closed");
  signaling.fail(expected);

  const caught = await captureRejection(connecting);
  assert.equal(caught, expected);
  assert.equal(peer.currentState, "failed");
  assert.equal(connection.closeCount, 1);
});

test("signaling failure after DataChannel open does not tear down direct peer traffic", async (t) => {
  const connection = new LifecyclePeerConnection();
  const signaling = new FailableSignalingChannel();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  connection.channel.openEvenIfClosed();
  connection.connected();
  await connecting;

  signaling.fail(new Error("signaling backend unavailable"));

  assert.equal(peer.currentState, "open");
  assert.equal(connection.closeCount, 0);
  assert.equal(peer.send(new Uint8Array([1])).sent, true);
});

test("transient disconnected state keeps an established DataChannel open", async (t) => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  connection.channel.openEvenIfClosed();
  connection.connected();
  await connecting;

  connection.disconnected();

  assert.equal(peer.currentState, "open");
  assert.equal(connection.closeCount, 0);
  assert.equal(peer.send(new Uint8Array([1])).sent, true);
});

test("late ICE signaling send failure after DataChannel open preserves direct peer traffic", async (t) => {
  const connection = new LifecyclePeerConnection();
  const signaling = new FailableSignalingChannel();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  connection.channel.openEvenIfClosed();
  connection.connected();
  await connecting;

  signaling.rejectSends(new Error("signaling backend unavailable"));
  connection.emitIceCandidate();
  await Promise.resolve();
  await Promise.resolve();

  assert.equal(peer.currentState, "open");
  assert.equal(connection.closeCount, 0);
  assert.equal(peer.send(new Uint8Array([1])).sent, true);
});

test("remote DataChannel close releases RTC and signaling resources", async () => {
  const connection = new LifecyclePeerConnection();
  const signaling = new FailableSignalingChannel();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });

  const connecting = peer.connect();
  connection.channel.openEvenIfClosed();
  connection.connected();
  await connecting;

  connection.channel.close();

  assert.equal(peer.currentState, "closed");
  assert.equal(connection.closeCount, 1);
  assert.equal(signaling.unsubscribeCount, 1);
});

test("a late DataChannel open cannot revive a failed peer", async (t) => {
  const connection = new LifecyclePeerConnection();
  const peer = new WebRtcPeer(new IdleSignalingChannel(), {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connecting = peer.connect();
  void connecting.catch(() => {});
  connection.fail();

  const caught = await captureRejection(connecting);
  assert.ok(caught instanceof Error);
  assert.equal(peer.currentState, "failed");

  connection.channel.openEvenIfClosed();
  connection.connected();
  assert.equal(peer.currentState, "failed");

  let sendError: unknown;
  try {
    peer.send(new Uint8Array([1]));
  } catch (error) {
    sendError = error;
  }
  assert.ok(sendError instanceof Error);
  assert.equal(sendError.message, "WebRTC DataChannel is not open.");
});

async function captureRejection(promise: Promise<unknown>): Promise<unknown> {
  try {
    await promise;
    return undefined;
  } catch (error) {
    return error;
  }
}

class IdleSignalingChannel implements WebRtcSignalingChannel {
  send(_signal: WebRtcSignal): void {}

  subscribe(_handler: (signal: WebRtcSignal) => void | Promise<void>): () => void {
    return () => {};
  }
}

class ThrowingSignalingChannel implements WebRtcSignalingChannel {
  constructor(private readonly error: Error) {}

  send(_signal: WebRtcSignal): void {}

  subscribe(_handler: (signal: WebRtcSignal) => void | Promise<void>): () => void {
    throw this.error;
  }
}

class FailableSignalingChannel implements WebRtcSignalingChannel {
  private failureHandler: ((reason: unknown) => void) | undefined;
  private sendError: Error | undefined;
  unsubscribeCount = 0;

  send(_signal: WebRtcSignal): void {
    if (this.sendError !== undefined) {
      throw this.sendError;
    }
  }

  subscribe(
    _handler: (signal: WebRtcSignal) => void | Promise<void>,
    failureHandler?: (reason: unknown) => void,
  ): () => void {
    this.failureHandler = failureHandler;
    return () => {
      this.unsubscribeCount += 1;
      if (this.failureHandler === failureHandler) {
        this.failureHandler = undefined;
      }
    };
  }

  fail(reason: unknown): void {
    this.failureHandler?.(reason);
  }

  rejectSends(error: Error): void {
    this.sendError = error;
  }
}

class LifecyclePeerConnection {
  readonly channel = new LifecycleDataChannel();
  connectionState: RTCPeerConnectionState = "new";
  closeCount = 0;
  private readonly listeners = new Map<string, Set<(event?: unknown) => void>>();

  asRtcPeerConnection(): RTCPeerConnection {
    return this as unknown as RTCPeerConnection;
  }

  addEventListener(type: string, listener: (event?: unknown) => void): void {
    let listeners = this.listeners.get(type);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(type, listeners);
    }
    listeners.add(listener);
  }

  createDataChannel(_label: string, _init?: RTCDataChannelInit): RTCDataChannel {
    return this.channel.asRtcDataChannel();
  }

  async createOffer(): Promise<RTCSessionDescriptionInit> {
    return { type: "offer", sdp: "lifecycle-offer" };
  }

  async setLocalDescription(_description?: RTCLocalSessionDescriptionInit): Promise<void> {}

  close(): void {
    this.closeCount += 1;
    this.connectionState = "closed";
    this.emit("connectionstatechange");
  }

  fail(): void {
    this.connectionState = "failed";
    this.emit("connectionstatechange");
  }

  connected(): void {
    this.connectionState = "connected";
    this.emit("connectionstatechange");
  }

  disconnected(): void {
    this.connectionState = "disconnected";
    this.emit("connectionstatechange");
  }

  emitIceCandidate(): void {
    this.emit("icecandidate", {
      candidate: {
        toJSON: () => ({ candidate: "candidate:late" }),
      },
    });
  }

  private emit(type: string, event?: unknown): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class LifecycleDataChannel {
  readyState: RTCDataChannelState = "connecting";
  bufferedAmount = 0;
  bufferedAmountLowThreshold = 0;
  binaryType: BinaryType = "blob";
  private readonly listeners = new Map<string, Set<() => void>>();

  asRtcDataChannel(): RTCDataChannel {
    return this as unknown as RTCDataChannel;
  }

  addEventListener(type: string, listener: () => void): void {
    let listeners = this.listeners.get(type);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(type, listeners);
    }
    listeners.add(listener);
  }

  send(_data: string | Blob | ArrayBuffer | ArrayBufferView): void {}

  close(): void {
    this.readyState = "closed";
    this.emit("close");
  }

  openEvenIfClosed(): void {
    this.readyState = "open";
    this.emit("open");
  }

  private emit(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener();
    }
  }
}

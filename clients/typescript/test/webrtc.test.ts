import test from "node:test";
import assert from "node:assert/strict";
import {
  WebRtcBackpressureError,
  WebRtcPeer,
  createWebRtcDataChannelInit,
  type WebRtcSignal,
  type WebRtcSignalingChannel,
} from "../src/index.js";

test("WebRTC profiles map to explicit reliability semantics", () => {
  assert.deepEqual(createWebRtcDataChannelInit("reliable"), { ordered: true });
  assert.deepEqual(createWebRtcDataChannelInit("low-latency"), {
    ordered: false,
    maxRetransmits: 0,
  });
});

test("reliable WebRTC peer rejects sends above the configured buffer limit", async (t) => {
  const signaling = new FakeSignalingChannel();
  const connection = new FakePeerConnection();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    profile: "reliable",
    maxBufferedAmount: 100,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connected = peer.connect();
  connection.channel.open();
  await connected;
  connection.channel.bufferedAmount = 90;

  let caught: unknown;
  try {
    peer.send(new Uint8Array(20));
  } catch (error) {
    caught = error;
  }

  assert.ok(caught instanceof WebRtcBackpressureError);
  assert.equal(caught.bufferedAmount, 110);
  assert.equal(caught.maxBufferedAmount, 100);
  assert.equal(connection.channel.sent.length, 0);
  assert.equal(peer.droppedMessageCount, 0);
});

test("low-latency WebRTC peer drops newest payload instead of growing a queue", async (t) => {
  const signaling = new FakeSignalingChannel();
  const connection = new FakePeerConnection();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    profile: "low-latency",
    maxBufferedAmount: 100,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connected = peer.connect();
  connection.channel.open();
  await connected;
  connection.channel.bufferedAmount = 90;

  const result = peer.send(new Uint8Array(20));
  assert.deepEqual(result, {
    sent: false,
    dropped: true,
    bufferedAmount: 90,
    droppedMessages: 1,
  });
  assert.equal(connection.channel.sent.length, 0);
  assert.equal(peer.droppedMessageCount, 1);
});

test("WebRTC peer sends offer and trickle ICE only through signaling", async (t) => {
  const signaling = new FakeSignalingChannel();
  const connection = new FakePeerConnection();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connected = peer.connect();
  await Promise.resolve();
  assert.deepEqual(signaling.sent[0], {
    kind: "description",
    description: { type: "offer", sdp: "fake-offer" },
  });

  connection.emitIceCandidate({ candidate: "candidate:1" });
  await Promise.resolve();
  assert.deepEqual(signaling.sent[1], {
    kind: "candidate",
    candidate: { candidate: "candidate:1" },
  });

  connection.channel.open();
  await connected;
});

test("WebRTC diagnostics expose RTT variation without inspecting application payloads", async (t) => {
  const signaling = new FakeSignalingChannel();
  const connection = new FakePeerConnection();
  const peer = new WebRtcPeer(signaling, {
    initiator: true,
    peerConnectionFactory: () => connection.asRtcPeerConnection(),
  });
  t.after(() => peer.close());

  const connected = peer.connect();
  connection.channel.open();
  await connected;

  connection.roundTripTimeSeconds = 0.012;
  const first = await peer.sampleDiagnostics();
  assert.equal(first.roundTripTimeMs, 12);
  assert.equal(first.rttJitterMs, undefined);

  connection.roundTripTimeSeconds = 0.018;
  const second = await peer.sampleDiagnostics();
  assert.equal(second.roundTripTimeMs, 18);
  assert.equal(second.rttJitterMs, 6);
});

class FakeSignalingChannel implements WebRtcSignalingChannel {
  readonly sent: WebRtcSignal[] = [];
  private handler: ((signal: WebRtcSignal) => void | Promise<void>) | null = null;

  send(signal: WebRtcSignal): void {
    this.sent.push(signal);
  }

  subscribe(handler: (signal: WebRtcSignal) => void | Promise<void>): () => void {
    this.handler = handler;
    return () => {
      if (this.handler === handler) {
        this.handler = null;
      }
    };
  }

  async deliver(signal: WebRtcSignal): Promise<void> {
    await this.handler?.(signal);
  }
}

class FakePeerConnection {
  readonly channel = new FakeDataChannel();
  connectionState: RTCPeerConnectionState = "new";
  remoteDescription: RTCSessionDescription | null = null;
  roundTripTimeSeconds = 0;
  private readonly listeners = new Map<string, Set<(event: unknown) => void>>();

  asRtcPeerConnection(): RTCPeerConnection {
    return this as unknown as RTCPeerConnection;
  }

  addEventListener(type: string, listener: (event: unknown) => void): void {
    let listeners = this.listeners.get(type);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(type, listeners);
    }
    listeners.add(listener);
  }

  createDataChannel(_label: string, init?: RTCDataChannelInit): RTCDataChannel {
    this.channel.init = init;
    return this.channel.asRtcDataChannel();
  }

  async createOffer(): Promise<RTCSessionDescriptionInit> {
    return { type: "offer", sdp: "fake-offer" };
  }

  async createAnswer(): Promise<RTCSessionDescriptionInit> {
    return { type: "answer", sdp: "fake-answer" };
  }

  async setLocalDescription(_description?: RTCLocalSessionDescriptionInit): Promise<void> {}

  async setRemoteDescription(description: RTCSessionDescriptionInit): Promise<void> {
    this.remoteDescription = description as RTCSessionDescription;
  }

  async addIceCandidate(_candidate?: RTCIceCandidateInit | null): Promise<void> {}

  async getStats(): Promise<RTCStatsReport> {
    const report: RTCStats = {
      id: "candidate-pair",
      timestamp: 0,
      type: "candidate-pair",
      state: "succeeded",
      nominated: true,
      currentRoundTripTime: this.roundTripTimeSeconds,
    };
    return {
      forEach(callback: (value: RTCStats, key: string, parent: RTCStatsReport) => void) {
        callback(report, "candidate-pair", this as unknown as RTCStatsReport);
      },
    } as RTCStatsReport;
  }

  close(): void {
    this.connectionState = "closed";
    this.emit("connectionstatechange", {});
  }

  emitIceCandidate(candidate: RTCIceCandidateInit | null): void {
    this.emit("icecandidate", {
      candidate:
        candidate === null
          ? null
          : {
              toJSON: () => candidate,
            },
    });
  }

  private emit(type: string, event: unknown): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

class FakeDataChannel {
  readyState: RTCDataChannelState = "connecting";
  bufferedAmount = 0;
  bufferedAmountLowThreshold = 0;
  binaryType: BinaryType = "blob";
  init: RTCDataChannelInit | undefined;
  readonly sent: ArrayBuffer[] = [];
  private readonly listeners = new Map<string, Set<(event: unknown) => void>>();

  asRtcDataChannel(): RTCDataChannel {
    return this as unknown as RTCDataChannel;
  }

  addEventListener(type: string, listener: (event: unknown) => void): void {
    let listeners = this.listeners.get(type);
    if (listeners === undefined) {
      listeners = new Set();
      this.listeners.set(type, listeners);
    }
    listeners.add(listener);
  }

  send(data: string | Blob | ArrayBuffer | ArrayBufferView): void {
    assert.ok(data instanceof ArrayBuffer);
    this.sent.push(data);
  }

  open(): void {
    this.readyState = "open";
    this.emit("open", {});
  }

  close(): void {
    if (this.readyState === "closed") {
      return;
    }
    this.readyState = "closed";
    this.emit("close", {});
  }

  private emit(type: string, event: unknown): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

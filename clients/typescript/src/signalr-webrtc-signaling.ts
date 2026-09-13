import {
  HubConnectionBuilder,
  type HubConnection,
  type IHttpConnectionOptions,
} from "@microsoft/signalr";
import type { WebRtcSignal, WebRtcSignalingChannel } from "./webrtc.js";

const peerJoinedMethod = "PartyGameKit.WebRtcPeerJoined";
const peerLeftMethod = "PartyGameKit.WebRtcPeerLeft";
const signalMethod = "PartyGameKit.WebRtcSignal";

export interface SignalRWebRtcSignalingClientOptions {
  endpoint: string;
  channelId: string;
  connectionOptions?: IHttpConnectionOptions;
  maxPendingSignalsPerPeer?: number;
  hubConnectionFactory?: () => SignalRHubConnectionLike;
}

export interface SignalRWebRtcSignalingRegistration {
  connectionId: string;
  existingConnectionIds: readonly string[];
}

export interface SignalRHubConnectionLike {
  readonly connectionId: string | null;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T>;
  on(methodName: string, newMethod: (...args: unknown[]) => void): void;
  off(methodName: string): void;
}

export class SignalRWebRtcSignalingClient {
  private readonly connection: SignalRHubConnectionLike;
  private readonly channelId: string;
  private readonly maxPendingSignalsPerPeer: number;
  private readonly peerJoinedListeners = new Set<(connectionId: string) => void>();
  private readonly peerLeftListeners = new Set<(connectionId: string) => void>();
  private readonly signalListeners = new Map<
    string,
    Set<(signal: WebRtcSignal) => void | Promise<void>>
  >();
  private readonly pendingSignals = new Map<string, WebRtcSignal[]>();
  private readonly pendingSignalOverflowPeers = new Set<string>();
  private started = false;
  private disposed = false;

  constructor(options: SignalRWebRtcSignalingClientOptions) {
    if (options.endpoint.trim().length === 0) {
      throw new Error("SignalR WebRTC signaling endpoint is required.");
    }
    if (options.channelId.trim().length === 0) {
      throw new Error("SignalR WebRTC signaling channelId is required.");
    }

    this.maxPendingSignalsPerPeer = options.maxPendingSignalsPerPeer ?? 64;
    if (this.maxPendingSignalsPerPeer <= 0) {
      throw new RangeError("maxPendingSignalsPerPeer must be positive.");
    }

    this.channelId = options.channelId.trim();
    const suppliedConnection = options.hubConnectionFactory?.();
    if (suppliedConnection !== undefined) {
      this.connection = suppliedConnection;
    } else {
      const builder = new HubConnectionBuilder();
      this.connection = (
        options.connectionOptions === undefined
          ? builder.withUrl(options.endpoint)
          : builder.withUrl(options.endpoint, options.connectionOptions)
      ).build() as HubConnection;
    }

    this.connection.on(peerJoinedMethod, (...args: unknown[]) => {
      const connectionId = requiredString(args[0], "peer connection id");
      for (const listener of this.peerJoinedListeners) {
        listener(connectionId);
      }
    });
    this.connection.on(peerLeftMethod, (...args: unknown[]) => {
      const connectionId = requiredString(args[0], "peer connection id");
      this.pendingSignals.delete(connectionId);
      this.pendingSignalOverflowPeers.delete(connectionId);
      for (const listener of this.peerLeftListeners) {
        listener(connectionId);
      }
    });
    this.connection.on(signalMethod, (...args: unknown[]) => {
      const sourceConnectionId = requiredString(args[0], "signal source connection id");
      const signalJson = requiredString(args[1], "WebRTC signal payload");
      const signal = parseWebRtcSignal(signalJson);
      const listeners = this.signalListeners.get(sourceConnectionId);
      if (listeners === undefined || listeners.size === 0) {
        this.bufferEarlySignal(sourceConnectionId, signal);
        return;
      }

      for (const listener of listeners) {
        void Promise.resolve(listener(signal)).catch(() => {
          // A WebRtcPeer listener transitions itself to failed before rejecting.
          // Consume the callback rejection at this event-dispatch boundary.
        });
      }
    });
  }

  get connectionId(): string | null {
    return this.connection.connectionId;
  }

  async connect(): Promise<SignalRWebRtcSignalingRegistration> {
    if (this.disposed) {
      throw new Error("SignalR WebRTC signaling client is disposed.");
    }
    if (this.started) {
      const connectionId = this.connection.connectionId;
      if (connectionId === null) {
        throw new Error("SignalR WebRTC signaling connection has no connection id.");
      }
      return {
        connectionId,
        existingConnectionIds: [],
      };
    }

    await this.connection.start();
    try {
      const existingConnectionIds = await this.connection.invoke<string[]>(
        "JoinChannel",
        this.channelId,
      );
      const connectionId = this.connection.connectionId;
      if (connectionId === null) {
        throw new Error("SignalR WebRTC signaling connection has no connection id.");
      }
      this.started = true;
      return {
        connectionId,
        existingConnectionIds: [...existingConnectionIds],
      };
    } catch (error) {
      await this.connection.stop();
      throw error;
    }
  }

  createChannel(targetConnectionId: string): WebRtcSignalingChannel {
    const target = targetConnectionId.trim();
    if (target.length === 0) {
      throw new Error("WebRTC signaling target connection id is required.");
    }
    if (!this.started || this.connection.connectionId === null) {
      throw new Error("SignalR WebRTC signaling client is not connected.");
    }
    if (target === this.connection.connectionId) {
      throw new Error("A WebRTC signaling connection cannot target itself.");
    }

    return {
      send: async (signal) => {
        if (this.disposed || !this.started) {
          throw new Error("SignalR WebRTC signaling client is not connected.");
        }
        await this.connection.invoke(
          "SendSignal",
          target,
          JSON.stringify(signal),
        );
      },
      subscribe: (listener) => {
        if (this.pendingSignalOverflowPeers.delete(target)) {
          this.pendingSignals.delete(target);
          throw new Error(
            `WebRTC signaling pending signal limit of ${this.maxPendingSignalsPerPeer} was exceeded for peer ${target}.`,
          );
        }

        let listeners = this.signalListeners.get(target);
        if (listeners === undefined) {
          listeners = new Set();
          this.signalListeners.set(target, listeners);
        }
        listeners.add(listener);

        const pending = this.pendingSignals.get(target);
        this.pendingSignals.delete(target);
        if (pending !== undefined && pending.length > 0) {
          let delivery = Promise.resolve();
          for (const signal of pending) {
            delivery = delivery.then(() => listener(signal));
          }
          void delivery.catch(() => {
            // WebRtcPeer owns the failure state; avoid an unhandled rejection
            // from this synchronous subscription boundary.
          });
        }

        return () => {
          const current = this.signalListeners.get(target);
          current?.delete(listener);
          if (current?.size === 0) {
            this.signalListeners.delete(target);
          }
        };
      },
    };
  }

  onPeerJoined(listener: (connectionId: string) => void): () => void {
    this.peerJoinedListeners.add(listener);
    return () => this.peerJoinedListeners.delete(listener);
  }

  onPeerLeft(listener: (connectionId: string) => void): () => void {
    this.peerLeftListeners.add(listener);
    return () => this.peerLeftListeners.delete(listener);
  }

  async dispose(): Promise<void> {
    if (this.disposed) {
      return;
    }
    this.disposed = true;

    if (this.started) {
      try {
        await this.connection.invoke("LeaveChannel");
      } catch {
        // The physical SignalR connection may already be gone. The server also
        // removes signaling membership from OnDisconnectedAsync.
      }
    }

    this.started = false;
    this.signalListeners.clear();
    this.pendingSignals.clear();
    this.pendingSignalOverflowPeers.clear();
    this.peerJoinedListeners.clear();
    this.peerLeftListeners.clear();
    this.connection.off(peerJoinedMethod);
    this.connection.off(peerLeftMethod);
    this.connection.off(signalMethod);
    await this.connection.stop();
  }

  private bufferEarlySignal(sourceConnectionId: string, signal: WebRtcSignal): void {
    if (this.pendingSignalOverflowPeers.has(sourceConnectionId)) {
      return;
    }

    let pending = this.pendingSignals.get(sourceConnectionId);
    if (pending === undefined) {
      pending = [];
      this.pendingSignals.set(sourceConnectionId, pending);
    }

    if (pending.length >= this.maxPendingSignalsPerPeer) {
      pending.length = 0;
      this.pendingSignals.delete(sourceConnectionId);
      this.pendingSignalOverflowPeers.add(sourceConnectionId);
      return;
    }

    pending.push(signal);
  }
}

export function parseWebRtcSignal(json: string): WebRtcSignal {
  const value: unknown = JSON.parse(json);
  if (!isRecord(value)) {
    throw new Error("WebRTC signal must be a JSON object.");
  }

  if (value.kind === "description") {
    if (!isRecord(value.description)) {
      throw new Error("WebRTC description signal requires a description object.");
    }
    const type = value.description.type;
    if (type !== "offer" && type !== "answer" && type !== "pranswer" && type !== "rollback") {
      throw new Error("WebRTC description signal contains an unsupported SDP type.");
    }
    const sdp = value.description.sdp;
    if (sdp !== undefined && typeof sdp !== "string") {
      throw new Error("WebRTC SDP must be a string when present.");
    }
    return {
      kind: "description",
      description: {
        type,
        ...(sdp === undefined ? {} : { sdp }),
      },
    };
  }

  if (value.kind === "candidate") {
    if (value.candidate === null) {
      return { kind: "candidate", candidate: null };
    }
    if (!isRecord(value.candidate)) {
      throw new Error("WebRTC candidate signal requires an ICE candidate object or null.");
    }
    const candidate = value.candidate.candidate;
    if (candidate !== undefined && typeof candidate !== "string") {
      throw new Error("WebRTC ICE candidate must be a string when present.");
    }
    const sdpMid = value.candidate.sdpMid;
    if (sdpMid !== undefined && sdpMid !== null && typeof sdpMid !== "string") {
      throw new Error("WebRTC ICE sdpMid must be a string or null when present.");
    }
    const sdpMLineIndex = value.candidate.sdpMLineIndex;
    if (
      sdpMLineIndex !== undefined &&
      sdpMLineIndex !== null &&
      typeof sdpMLineIndex !== "number"
    ) {
      throw new Error("WebRTC ICE sdpMLineIndex must be a number or null when present.");
    }
    const usernameFragment = value.candidate.usernameFragment;
    if (
      usernameFragment !== undefined &&
      usernameFragment !== null &&
      typeof usernameFragment !== "string"
    ) {
      throw new Error("WebRTC ICE usernameFragment must be a string or null when present.");
    }

    return {
      kind: "candidate",
      candidate: {
        ...(candidate === undefined ? {} : { candidate }),
        ...(sdpMid === undefined ? {} : { sdpMid }),
        ...(sdpMLineIndex === undefined ? {} : { sdpMLineIndex }),
        ...(usernameFragment === undefined ? {} : { usernameFragment }),
      },
    };
  }

  throw new Error("Unsupported WebRTC signal kind.");
}

function requiredString(value: unknown, name: string): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error(`${name} must be a non-empty string.`);
  }
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

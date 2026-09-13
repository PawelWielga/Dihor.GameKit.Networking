import test from "node:test";
import assert from "node:assert/strict";
import {
  AutomaticTransportSelector,
  ConnectivitySelectionError,
  connectivityTransportIds,
  type TransportCandidate,
} from "../src/index.js";

interface FakeConnection {
  transportId: string;
  context: string;
  disposed: boolean;
}

test("auto selection stops after LAN succeeds", async () => {
  const attempts: string[] = [];
  const selector = createSelector([
    success(connectivityTransportIds.lanWebSocket, attempts),
    success(connectivityTransportIds.webRtcDataChannel, attempts),
    success(connectivityTransportIds.signalRRelay, attempts),
  ]);

  const selected = await selector.connect("opaque-connect");

  assert.equal(selected.transportId, connectivityTransportIds.lanWebSocket);
  assert.deepEqual(attempts, [connectivityTransportIds.lanWebSocket]);
  assert.equal(selected.diagnostics.attempts.length, 1);
});

test("auto selection falls back LAN -> WebRTC -> SignalR deterministically", async () => {
  const attempts: string[] = [];
  const selector = createSelector([
    failure(connectivityTransportIds.lanWebSocket, attempts),
    failure(connectivityTransportIds.webRtcDataChannel, attempts),
    success(connectivityTransportIds.signalRRelay, attempts),
  ]);

  const selected = await selector.connect("opaque-connect");

  assert.equal(selected.transportId, connectivityTransportIds.signalRRelay);
  assert.deepEqual(attempts, [
    connectivityTransportIds.lanWebSocket,
    connectivityTransportIds.webRtcDataChannel,
    connectivityTransportIds.signalRRelay,
  ]);
  assert.deepEqual(
    selected.diagnostics.attempts.map((attempt) => attempt.outcome),
    ["failed", "failed", "selected"],
  );
});

test("all path failures expose structured diagnostics", async () => {
  const selector = createSelector([
    failure(connectivityTransportIds.lanWebSocket),
    failure(connectivityTransportIds.webRtcDataChannel),
    failure(connectivityTransportIds.signalRRelay),
  ]);

  const caught = await captureRejection(selector.connect("opaque-connect"));

  assert.ok(caught instanceof ConnectivitySelectionError);
  assert.equal(caught.diagnostics.selectedTransport, undefined);
  assert.deepEqual(
    caught.diagnostics.attempts.map((attempt) => attempt.transportId),
    [
      connectivityTransportIds.lanWebSocket,
      connectivityTransportIds.webRtcDataChannel,
      connectivityTransportIds.signalRRelay,
    ],
  );
});

test("timed-out candidate is bounded and late success is disposed", async () => {
  let resolveLate: ((connection: FakeConnection) => void) | undefined;
  const lateConnection: FakeConnection = {
    transportId: connectivityTransportIds.lanWebSocket,
    context: "late",
    disposed: false,
  };
  const selector = createSelector([
    {
      transportId: connectivityTransportIds.lanWebSocket,
      attemptTimeoutMs: 10,
      connect: () => new Promise<FakeConnection>((resolve) => {
        resolveLate = resolve;
      }),
      disposeLateConnection: (connection) => {
        connection.disposed = true;
      },
    },
    success(connectivityTransportIds.webRtcDataChannel),
  ]);

  const selected = await selector.connect("opaque-connect");
  resolveLate?.(lateConnection);
  await eventually(() => lateConnection.disposed);

  assert.equal(selected.transportId, connectivityTransportIds.webRtcDataChannel);
  assert.equal(selected.diagnostics.attempts[0]?.outcome, "timed-out");
  assert.equal(lateConnection.disposed, true);
});

test("external cancellation stops selection without trying fallback", async () => {
  let secondAttempted = false;
  const selector = createSelector([
    {
      transportId: connectivityTransportIds.lanWebSocket,
      connect: (_context, signal) => new Promise<FakeConnection>((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new DOMException("cancelled", "AbortError")), { once: true });
      }),
    },
    {
      transportId: connectivityTransportIds.webRtcDataChannel,
      connect: async (context) => {
        secondAttempted = true;
        return connection(connectivityTransportIds.webRtcDataChannel, context);
      },
    },
  ]);
  const cancellation = new AbortController();
  const pending = selector.connect("opaque-connect", "auto", cancellation.signal);
  cancellation.abort();

  const caught = await captureRejection(pending);

  assert.ok(caught instanceof DOMException);
  assert.equal(caught.name, "AbortError");
  assert.equal(secondAttempted, false);
});

test("external cancellation disposes a late success from a candidate that ignores abort", async () => {
  let resolveLate: ((connection: FakeConnection) => void) | undefined;
  const lateConnection = connection(connectivityTransportIds.lanWebSocket, "opaque-connect");
  const selector = createSelector([
    {
      transportId: connectivityTransportIds.lanWebSocket,
      connect: () => new Promise<FakeConnection>((resolve) => {
        resolveLate = resolve;
      }),
      disposeLateConnection: (candidateConnection) => {
        candidateConnection.disposed = true;
      },
    },
  ]);
  const cancellation = new AbortController();
  const pending = selector.connect("opaque-connect", "auto", cancellation.signal);
  cancellation.abort();

  const caught = await captureRejection(pending);
  assert.ok(caught instanceof DOMException);
  assert.equal(caught.name, "AbortError");

  resolveLate?.(lateConnection);
  await eventually(() => lateConnection.disposed);

  assert.equal(lateConnection.disposed, true);
});

test("reconnect prefers the previously successful path", async () => {
  const attempts: string[] = [];
  let lanAvailable = false;
  const selector = createSelector([
    conditional(connectivityTransportIds.lanWebSocket, attempts, () => lanAvailable),
    success(connectivityTransportIds.webRtcDataChannel, attempts),
    success(connectivityTransportIds.signalRRelay, attempts),
  ]);

  const first = await selector.connect("peer=stable;phase=connect");
  assert.equal(first.transportId, connectivityTransportIds.webRtcDataChannel);

  attempts.length = 0;
  lanAvailable = true;
  const resumed = await selector.reconnect("peer=stable;phase=resume");

  assert.equal(resumed.transportId, connectivityTransportIds.webRtcDataChannel);
  assert.deepEqual(attempts, [connectivityTransportIds.webRtcDataChannel]);
  assert.equal(resumed.diagnostics.preferredTransportReused, true);
});

test("reconnect falls back deterministically when the previous path fails", async () => {
  const attempts: string[] = [];
  let webRtcAvailable = true;
  const selector = createSelector([
    failure(connectivityTransportIds.lanWebSocket, attempts),
    conditional(connectivityTransportIds.webRtcDataChannel, attempts, () => webRtcAvailable),
    success(connectivityTransportIds.signalRRelay, attempts),
  ]);

  const first = await selector.connect("peer=stable;phase=connect");
  assert.equal(first.transportId, connectivityTransportIds.webRtcDataChannel);

  attempts.length = 0;
  webRtcAvailable = false;
  const resumed = await selector.reconnect("peer=stable;phase=resume");

  assert.equal(resumed.transportId, connectivityTransportIds.signalRRelay);
  assert.deepEqual(attempts, [
    connectivityTransportIds.webRtcDataChannel,
    connectivityTransportIds.lanWebSocket,
    connectivityTransportIds.signalRRelay,
  ]);
  assert.equal(resumed.diagnostics.preferredTransportReused, false);
});

test("forced modes attempt only their requested transport", async () => {
  for (const [mode, expected] of [
    ["lan", connectivityTransportIds.lanWebSocket],
    ["webrtc", connectivityTransportIds.webRtcDataChannel],
    ["signalr", connectivityTransportIds.signalRRelay],
  ] as const) {
    const attempts: string[] = [];
    const selector = createSelector([
      success(connectivityTransportIds.lanWebSocket, attempts),
      success(connectivityTransportIds.webRtcDataChannel, attempts),
      success(connectivityTransportIds.signalRRelay, attempts),
    ]);

    const selected = await selector.connect("opaque-connect", mode);

    assert.equal(selected.transportId, expected);
    assert.deepEqual(attempts, [expected]);
  }
});

test("opaque context remains unchanged when reconnect migrates transports", async () => {
  const contexts: Array<[string, string]> = [];
  let webRtcAvailable = true;
  const selector = createSelector([
    failure(connectivityTransportIds.lanWebSocket),
    {
      transportId: connectivityTransportIds.webRtcDataChannel,
      connect: async (context) => {
        contexts.push([connectivityTransportIds.webRtcDataChannel, context]);
        if (!webRtcAvailable) throw new Error("WebRTC unavailable");
        return connection(connectivityTransportIds.webRtcDataChannel, context);
      },
    },
    {
      transportId: connectivityTransportIds.signalRRelay,
      connect: async (context) => {
        contexts.push([connectivityTransportIds.signalRRelay, context]);
        return connection(connectivityTransportIds.signalRRelay, context);
      },
    },
  ]);

  await selector.connect("peer=stable;phase=connect");
  webRtcAvailable = false;
  await selector.reconnect("peer=stable;phase=resume");

  assert.deepEqual(contexts, [
    [connectivityTransportIds.webRtcDataChannel, "peer=stable;phase=connect"],
    [connectivityTransportIds.webRtcDataChannel, "peer=stable;phase=resume"],
    [connectivityTransportIds.signalRRelay, "peer=stable;phase=resume"],
  ]);
});

function createSelector(candidates: readonly TransportCandidate<string, FakeConnection>[]) {
  return new AutomaticTransportSelector(candidates, {
    attemptTimeoutMs: 100,
    autoOrder: [
      connectivityTransportIds.lanWebSocket,
      connectivityTransportIds.webRtcDataChannel,
      connectivityTransportIds.signalRRelay,
    ],
  });
}

function success(
  transportId: string,
  attempts?: string[],
): TransportCandidate<string, FakeConnection> {
  return {
    transportId,
    connect: async (context) => {
      attempts?.push(transportId);
      return connection(transportId, context);
    },
  };
}

function failure(
  transportId: string,
  attempts?: string[],
): TransportCandidate<string, FakeConnection> {
  return {
    transportId,
    connect: async () => {
      attempts?.push(transportId);
      throw new Error(`${transportId} unavailable`);
    },
  };
}

function conditional(
  transportId: string,
  attempts: string[],
  available: () => boolean,
): TransportCandidate<string, FakeConnection> {
  return {
    transportId,
    connect: async (context) => {
      attempts.push(transportId);
      if (!available()) throw new Error(`${transportId} unavailable`);
      return connection(transportId, context);
    },
  };
}

function connection(transportId: string, context: string): FakeConnection {
  return { transportId, context, disposed: false };
}

async function captureRejection(promise: Promise<unknown>): Promise<unknown> {
  try {
    await promise;
    return undefined;
  } catch (error) {
    return error;
  }
}

async function eventually(condition: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (condition()) return;
    await new Promise((resolve) => setTimeout(resolve, 1));
  }
  assert.fail("Condition did not become true in time");
}

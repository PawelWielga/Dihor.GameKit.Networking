import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import { resolve } from "node:path";
import { chromium } from "playwright";

const distDirectory = resolve(process.cwd(), "dist");
const server = createServer(async (request, response) => {
  if (request.url === "/webrtc.js") {
    response.writeHead(200, { "content-type": "application/javascript; charset=utf-8" });
    response.end(await readFile(resolve(distDirectory, "webrtc.js"), "utf8"));
    return;
  }

  response.writeHead(200, { "content-type": "text/html; charset=utf-8" });
  response.end("<!doctype html><meta charset=\"utf-8\"><title>PartyGameKit WebRTC test</title>");
});

await new Promise((resolveListen, reject) => {
  server.once("error", reject);
  server.listen(0, "127.0.0.1", resolveListen);
});

const address = server.address();
if (address === null || typeof address === "string") {
  server.close();
  throw new Error("Browser WebRTC test server did not expose a TCP address.");
}

const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  await page.goto(`http://127.0.0.1:${address.port}/`);

  const result = await page.evaluate(async () => {
    const { WebRtcPeer } = await import("/webrtc.js");
    const encoder = new TextEncoder();
    const decoder = new TextDecoder();

    function createSignalingPair() {
      let handlerA = null;
      let handlerB = null;
      let count = 0;
      let aIceComplete = false;
      let bIceComplete = false;

      const deliver = (handler, signal) => {
        const copy = structuredClone(signal);
        queueMicrotask(() => {
          void handler?.(copy);
        });
      };

      return {
        a: {
          send(signal) {
            count += 1;
            if (signal.kind === "candidate" && signal.candidate === null) {
              aIceComplete = true;
            }
            deliver(handlerB, signal);
          },
          subscribe(handler) {
            handlerA = handler;
            return () => {
              if (handlerA === handler) {
                handlerA = null;
              }
            };
          },
        },
        b: {
          send(signal) {
            count += 1;
            if (signal.kind === "candidate" && signal.candidate === null) {
              bIceComplete = true;
            }
            deliver(handlerA, signal);
          },
          subscribe(handler) {
            handlerB = handler;
            return () => {
              if (handlerB === handler) {
                handlerB = null;
              }
            };
          },
        },
        count: () => count,
        iceComplete: () => aIceComplete && bIceComplete,
      };
    }

    async function withTimeout(promise, timeoutMs, message) {
      let timeoutId;
      const timeout = new Promise((_, reject) => {
        timeoutId = setTimeout(() => reject(new Error(message)), timeoutMs);
      });
      try {
        return await Promise.race([promise, timeout]);
      } finally {
        clearTimeout(timeoutId);
      }
    }

    async function waitUntil(condition, timeoutMs, message) {
      const deadline = performance.now() + timeoutMs;
      while (!condition()) {
        if (performance.now() >= deadline) {
          throw new Error(message);
        }
        await new Promise((resolveDelay) => setTimeout(resolveDelay, 10));
      }
    }

    const reliableSignals = createSignalingPair();
    const reliableA = new WebRtcPeer(reliableSignals.a, {
      initiator: true,
      profile: "reliable",
      rtcConfiguration: { iceServers: [] },
    });
    const reliableB = new WebRtcPeer(reliableSignals.b, {
      profile: "reliable",
      rtcConfiguration: { iceServers: [] },
    });
    const receivedByA = [];
    const receivedByB = [];
    reliableA.onMessage((payload) => receivedByA.push(decoder.decode(payload)));
    reliableB.onMessage((payload) => receivedByB.push(decoder.decode(payload)));

    const reliableBConnect = reliableB.connect();
    const reliableAConnect = reliableA.connect();
    await withTimeout(
      Promise.all([reliableAConnect, reliableBConnect]),
      10_000,
      "Reliable WebRTC peers did not open a DataChannel.",
    );
    await waitUntil(
      () => reliableSignals.iceComplete(),
      5_000,
      "Reliable WebRTC ICE gathering did not complete before signaling baseline.",
    );

    const reliableSignalCountAfterConnect = reliableSignals.count();
    reliableA.send(encoder.encode("a-to-b"));
    reliableB.send(encoder.encode("b-to-a"));
    await waitUntil(
      () => receivedByA.length === 1 && receivedByB.length === 1,
      2_000,
      "Bidirectional reliable WebRTC messages were not delivered.",
    );
    const reliableSignalCountAfterMessages = reliableSignals.count();
    const reliableDiagnostics = await reliableA.sampleDiagnostics();
    reliableA.close();
    reliableB.close();

    const lowLatencySignals = createSignalingPair();
    const lowLatencyA = new WebRtcPeer(lowLatencySignals.a, {
      initiator: true,
      profile: "low-latency",
      rtcConfiguration: { iceServers: [] },
      maxBufferedAmount: 64 * 1024,
    });
    const lowLatencyB = new WebRtcPeer(lowLatencySignals.b, {
      profile: "low-latency",
      rtcConfiguration: { iceServers: [] },
      maxBufferedAmount: 64 * 1024,
    });

    const streamLatencies = [];
    const receivedSequences = [];
    lowLatencyB.onMessage((payload) => {
      const message = JSON.parse(decoder.decode(payload));
      receivedSequences.push(message.sequence);
      streamLatencies.push(performance.now() - message.sentAt);
    });

    const lowLatencyBConnect = lowLatencyB.connect();
    const lowLatencyAConnect = lowLatencyA.connect();
    await withTimeout(
      Promise.all([lowLatencyAConnect, lowLatencyBConnect]),
      10_000,
      "Low-latency WebRTC peers did not open a DataChannel.",
    );
    await waitUntil(
      () => lowLatencySignals.iceComplete(),
      5_000,
      "Low-latency WebRTC ICE gathering did not complete before signaling baseline.",
    );

    const lowLatencySignalCountAfterConnect = lowLatencySignals.count();
    const streamStart = performance.now();
    const sentCount = 60;
    for (let sequence = 0; sequence < sentCount; sequence += 1) {
      lowLatencyA.send(
        encoder.encode(
          JSON.stringify({
            sequence,
            sentAt: performance.now(),
          }),
        ),
      );
      await new Promise((resolveDelay) => setTimeout(resolveDelay, 16));
    }
    const streamElapsedMs = performance.now() - streamStart;

    await waitUntil(
      () => receivedSequences.length >= Math.floor(sentCount * 0.8),
      2_000,
      "Low-latency WebRTC stream delivered fewer than 80% of local-LAN test messages.",
    );

    const lowLatencySignalCountAfterStream = lowLatencySignals.count();
    const lowLatencyDiagnostics = await lowLatencyA.sampleDiagnostics();
    const sendRateHz = sentCount / (streamElapsedMs / 1000);
    const averageLatencyMs =
      streamLatencies.reduce((sum, value) => sum + value, 0) / streamLatencies.length;
    const jitterMs =
      streamLatencies.length < 2
        ? 0
        : streamLatencies
            .slice(1)
            .reduce(
              (sum, value, index) => sum + Math.abs(value - streamLatencies[index]),
              0,
            ) /
          (streamLatencies.length - 1);

    lowLatencyA.close();
    lowLatencyB.close();

    return {
      reliable: {
        receivedByA,
        receivedByB,
        signalingStable:
          reliableSignalCountAfterMessages === reliableSignalCountAfterConnect,
        diagnostics: reliableDiagnostics,
      },
      lowLatency: {
        sentCount,
        receivedCount: receivedSequences.length,
        sendRateHz,
        averageLatencyMs,
        jitterMs,
        signalingStable:
          lowLatencySignalCountAfterStream === lowLatencySignalCountAfterConnect,
        diagnostics: lowLatencyDiagnostics,
      },
    };
  });

  assert.deepEqual(result.reliable.receivedByA, ["b-to-a"]);
  assert.deepEqual(result.reliable.receivedByB, ["a-to-b"]);
  assert.equal(result.reliable.signalingStable, true);

  assert.equal(result.lowLatency.sentCount, 60);
  assert.ok(result.lowLatency.receivedCount >= 48, JSON.stringify(result.lowLatency));
  assert.ok(result.lowLatency.sendRateHz >= 30, JSON.stringify(result.lowLatency));
  assert.ok(result.lowLatency.sendRateHz <= 75, JSON.stringify(result.lowLatency));
  assert.equal(result.lowLatency.signalingStable, true);

  console.log(
    `WebRTC browser validation: ${result.lowLatency.receivedCount}/${result.lowLatency.sentCount} messages, ` +
      `${result.lowLatency.sendRateHz.toFixed(1)} Hz, ` +
      `${result.lowLatency.averageLatencyMs.toFixed(2)} ms average one-way observation, ` +
      `${result.lowLatency.jitterMs.toFixed(2)} ms inter-sample variation.`,
  );
} finally {
  await browser.close();
  await new Promise((resolveClose) => server.close(resolveClose));
}

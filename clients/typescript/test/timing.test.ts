import test from "node:test";
import assert from "node:assert/strict";
import { MonotonicTimingSynchronizer } from "../src/index.js";

test("known offset maps peer timestamp into reference clock domain", () => {
  const timing = new MonotonicTimingSynchronizer();
  const probe = timing.createProbe(1_000, "probe-1");
  const observation = timing.observeReply({ probeId: probe.probeId, peerReceiveMs: 1_110, peerSendMs: 1_112 }, 1_022);
  const normalized = timing.normalizePeerTimestamp(1_150, 1_060);

  assert.equal(observation.status, "accepted");
  assert.equal(observation.model?.offsetMs, 100);
  assert.equal(observation.model?.roundTripMs, 20);
  assert.equal(observation.model?.uncertaintyMs, 10);
  assert.equal(normalized.status, "accepted");
  assert.equal(normalized.referenceTimestampMs, 1_050);
});

test("RTT variation increases jitter and uncertainty", () => {
  const timing = new MonotonicTimingSynchronizer();
  addSample(timing, "p1", 1_000, 1_110, 1_110, 1_020);
  const firstUncertainty = timing.model?.uncertaintyMs ?? 0;
  addSample(timing, "p2", 2_000, 2_120, 2_120, 2_040);
  addSample(timing, "p3", 3_000, 3_130, 3_130, 3_060);

  assert.ok((timing.model?.jitterMs ?? 0) > 0);
  assert.ok((timing.model?.uncertaintyMs ?? 0) > firstUncertainty);
});

test("stale, non-monotonic and future timestamp evidence is rejected", () => {
  const timing = new MonotonicTimingSynchronizer({
    maxSynchronizationAgeMs: 100,
    maxEventAgeMs: 50,
    maxFutureLeadMs: 10,
  });
  addSample(timing, "p1", 1_000, 1_110, 1_110, 1_020);

  assert.equal(timing.normalizePeerTimestamp(1_130, 1_040).status, "accepted");
  assert.equal(timing.normalizePeerTimestamp(1_130, 1_041).status, "non-monotonic");
  assert.equal(timing.normalizePeerTimestamp(1_200, 1_080).status, "too-far-in-future");
  assert.equal(timing.normalizePeerTimestamp(1_140, 1_121).status, "synchronization-stale");
});

test("old timestamp is rejected when otherwise monotonic", () => {
  const timing = new MonotonicTimingSynchronizer({ maxEventAgeMs: 50 });
  addSample(timing, "p1", 1_000, 1_110, 1_110, 1_020);

  assert.equal(timing.normalizePeerTimestamp(1_120, 1_080).status, "too-old");
});

test("reconnect reset invalidates the old timing model", () => {
  const timing = new MonotonicTimingSynchronizer();
  addSample(timing, "p1", 1_000, 1_110, 1_110, 1_020);
  const generation = timing.model?.generation ?? -1;

  timing.reset("reconnect");
  assert.equal(timing.normalizePeerTimestamp(1_200, 1_100).status, "unsynchronized");
  addSample(timing, "p2", 2_000, 2_090, 2_090, 2_020);

  assert.equal(timing.diagnostics.lastResetReason, "reconnect");
  assert.equal(timing.model?.generation, generation + 1);
});

test("normalized ordering can differ from packet arrival ordering", () => {
  const peerA = new MonotonicTimingSynchronizer();
  const peerB = new MonotonicTimingSynchronizer();
  addSample(peerA, "a", 1_000, 1_110, 1_110, 1_020);
  addSample(peerB, "b", 1_000, 960, 960, 1_020);

  const bArrivesFirst = peerB.normalizePeerTimestamp(1_070, 1_130);
  const aArrivesLater = peerA.normalizePeerTimestamp(1_200, 1_140);

  assert.equal(bArrivesFirst.status, "accepted");
  assert.equal(aArrivesLater.status, "accepted");
  assert.ok((aArrivesLater.referenceTimestampMs ?? Number.POSITIVE_INFINITY) < (bArrivesFirst.referenceTimestampMs ?? Number.NEGATIVE_INFINITY));
});

test("sample and pending-probe storage stays bounded", () => {
  const timing = new MonotonicTimingSynchronizer({ historyCapacity: 3, pendingProbeCapacity: 2 });
  timing.createProbe(1, "stale-1");
  timing.createProbe(2, "stale-2");
  timing.createProbe(3, "stale-3");

  for (let index = 0; index < 10; index += 1) {
    const referenceSend = 1_000 + (index * 100);
    addSample(timing, `sample-${index}`, referenceSend, referenceSend + 110, referenceSend + 110, referenceSend + 20);
  }

  assert.equal(timing.diagnostics.historyCount, 3);
  assert.ok(timing.diagnostics.pendingProbeCount <= 2);
  assert.equal(timing.model?.sampleCount, 3);
});

test("unknown probe cannot produce a synchronization model", () => {
  const timing = new MonotonicTimingSynchronizer();
  const result = timing.observeReply({ probeId: "missing", peerReceiveMs: 10, peerSendMs: 11 }, 20);

  assert.equal(result.status, "unknown-probe");
  assert.equal(timing.model, undefined);
});

function addSample(
  timing: MonotonicTimingSynchronizer,
  probeId: string,
  referenceSendMs: number,
  peerReceiveMs: number,
  peerSendMs: number,
  referenceReceiveMs: number,
): void {
  timing.createProbe(referenceSendMs, probeId);
  const result = timing.observeReply({ probeId, peerReceiveMs, peerSendMs }, referenceReceiveMs);
  assert.equal(result.status, "accepted", result.reason);
}

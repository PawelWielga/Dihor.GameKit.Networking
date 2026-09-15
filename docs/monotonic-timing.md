# Synchronized monotonic timing

PartyGameKit `0.2.0-preview.5` adds a transport-neutral timing primitive for consumers that need to compare peer-local event timestamps without trusting raw packet-arrival order.

The timing layer reports communication facts only. It does **not** decide player roles, winners, reaction order, scoring, game pauses or whether a timing estimate is good enough for a particular game.

## Clock model

Use monotonic clocks only:

- .NET can use `MonotonicClock.TimestampMilliseconds`, backed by `Stopwatch.GetTimestamp()`;
- TypeScript/browser can use `monotonicNowMs()`, backed by `performance.now()`.

Do not use `DateTime`, Unix time, wall-clock timestamps or device timezone values for synchronization evidence.

A synchronization probe uses four monotonic timestamps:

- `t1`: reference side sends a probe;
- `t2`: peer receives the probe;
- `t3`: peer sends the reply;
- `t4`: reference side receives the reply.

PartyGameKit calculates peer-minus-reference offset and network round trip using the NTP-style formulas:

```text
offset = ((t2 - t1) + (t3 - t4)) / 2
rtt    = (t4 - t1) - (t3 - t2)
```

A positive offset means the peer monotonic clock is ahead of the reference clock. A peer event timestamp is mapped to the reference domain as:

```text
referenceEvent = peerEvent - offset
```

## Filtering and uncertainty

`MonotonicTimingSynchronizer` stores a bounded rolling sample history. By default it keeps 16 accepted samples and at most 32 outstanding probes.

The current model:

1. sorts samples by RTT;
2. uses the best half of the bounded history for the offset estimate;
3. takes the median offset and median RTT from that best half;
4. reports RTT jitter as median absolute deviation across the bounded history;
5. reports uncertainty as:

```text
minimumRTT / 2 + medianOffsetSpread + RTTJitter / 2
```

`UncertaintyMilliseconds` / `uncertaintyMs` is a conservative communication estimate, not a guarantee that the clocks are truly within that bound. Consumers must choose their own quality threshold based on their product/game requirements.

## .NET example

```csharp
var timing = new MonotonicTimingSynchronizer();

var probe = timing.CreateProbe(MonotonicClock.TimestampMilliseconds);
// Send probe.ProbeId to the peer using whichever PartyGameKit transport is active.

// The peer records its own monotonic receive/send timestamps and returns them.
var observation = timing.ObserveReply(
    new TimingProbeReply(probe.ProbeId, peerReceiveMs, peerSendMs),
    MonotonicClock.TimestampMilliseconds);

if (observation.IsAccepted)
{
    var normalized = timing.NormalizePeerTimestamp(
        peerEventTimestampMs,
        MonotonicClock.TimestampMilliseconds);

    if (normalized.IsAccepted)
    {
        Console.WriteLine(normalized.ReferenceTimestampMilliseconds);
        Console.WriteLine(normalized.UncertaintyMilliseconds);
    }
}
```

## TypeScript/browser example

```ts
const timing = new MonotonicTimingSynchronizer();

const probe = timing.createProbe(monotonicNowMs());
// Carry probe.probeId through LAN/WebRTC/SignalR/application-owned messaging.

const observation = timing.observeReply({
  probeId: probe.probeId,
  peerReceiveMs,
  peerSendMs,
}, monotonicNowMs());

if (observation.status === "accepted") {
  const normalized = timing.normalizePeerTimestamp(peerEventTimestampMs, monotonicNowMs());
  if (normalized.status === "accepted") {
    console.log(normalized.referenceTimestampMs, normalized.uncertaintyMs);
  }
}
```

## Transport and protocol boundary

Timing probes/replies are plain transport-neutral data. PartyGameKit does not add a new protocol-v2 control message and does not require one concrete transport.

A consumer may carry the probe ID and peer timestamps inside its existing opaque `application.message` payload, a WebRTC payload, or another adapter-owned envelope. LAN, SignalR, WebRTC and Auto selection do not inspect or reinterpret the timing data.

This keeps protocol version `2` unchanged.

## Evidence validation

A synchronization reply is rejected when:

- the probe ID is unknown or no longer pending;
- timestamps are non-finite/negative;
- peer send time precedes peer receive time;
- claimed peer processing makes the inferred network RTT negative;
- RTT exceeds the configured maximum.

A peer event timestamp is not normalized into authoritative/reference time when:

- no synchronization model exists;
- the model is stale;
- the timestamp is non-finite/negative;
- the timestamp does not advance monotonically relative to previously accepted evidence;
- the normalized event is too old;
- the normalized event is implausibly far in the future.

Raw peer timestamps are evidence only. A consumer should use `TimestampNormalizationResult.Status` and the uncertainty value instead of treating client numbers as authoritative.

## Reconnect and transport replacement

A reconnect or transport replacement may change path delay enough to invalidate an old estimate. The timing model is therefore not silently reused across communication-path replacement.

Call:

```csharp
timing.Reset(TimingResetReason.Reconnect);
```

or in TypeScript:

```ts
timing.reset("reconnect");
```

For an explicit path migration use `TransportChanged` / `"transport-changed"`. Reset clears outstanding probes, accepted history, last peer event evidence and the current model, then increments the timing generation. New timestamp evidence remains `Unsynchronized` until new samples are acquired.

## Diagnostics and bounds

`TimingDiagnostics` exposes:

- synchronization generation;
- accepted history count;
- outstanding probe count;
- accepted/rejected sample counters;
- last rejected sample status;
- last reset reason;
- current timing model.

Both sample history and probe bookkeeping are bounded. There is no background retry loop and no transport-owned synchronization traffic. The consumer controls probe cadence and decides when enough samples/quality are available for its use case.

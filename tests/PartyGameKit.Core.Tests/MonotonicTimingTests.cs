using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class MonotonicTimingTests
{
    [Fact]
    public void KnownOffsetProducesReferenceDomainTimestampAndUncertainty()
    {
        var synchronizer = new MonotonicTimingSynchronizer();
        var probe = synchronizer.CreateProbe(1_000, "probe-1");

        var observation = synchronizer.ObserveReply(
            new TimingProbeReply(probe.ProbeId, 1_110, 1_112),
            1_022);
        var normalized = synchronizer.NormalizePeerTimestamp(1_150, 1_060);

        Assert.True(observation.IsAccepted);
        Assert.Equal(100d, observation.Model?.OffsetMilliseconds);
        Assert.Equal(20d, observation.Model?.RoundTripMilliseconds);
        Assert.Equal(10d, observation.Model?.UncertaintyMilliseconds);
        Assert.True(normalized.IsAccepted);
        Assert.Equal(1_050d, normalized.ReferenceTimestampMilliseconds);
        Assert.Equal(10d, normalized.UncertaintyMilliseconds);
    }

    [Fact]
    public void RttVariationIncreasesJitterAndUncertainty()
    {
        var synchronizer = new MonotonicTimingSynchronizer();

        AddSample(synchronizer, "p1", 1_000, 1_110, 1_110, 1_020);
        var firstUncertainty = synchronizer.CurrentModel!.UncertaintyMilliseconds;
        AddSample(synchronizer, "p2", 2_000, 2_120, 2_120, 2_040);
        AddSample(synchronizer, "p3", 3_000, 3_130, 3_130, 3_060);

        Assert.True(synchronizer.CurrentModel!.JitterMilliseconds > 0);
        Assert.True(synchronizer.CurrentModel.UncertaintyMilliseconds > firstUncertainty);
    }

    [Fact]
    public void InvalidAndUnknownProbeEvidenceIsRejected()
    {
        var synchronizer = new MonotonicTimingSynchronizer();
        synchronizer.CreateProbe(1_000, "known");

        var unknown = synchronizer.ObserveReply(
            new TimingProbeReply("unknown", 1_100, 1_101),
            1_020);
        var invalidProcessing = synchronizer.ObserveReply(
            new TimingProbeReply("known", 1_110, 1_100),
            1_020);

        Assert.Equal(TimingSampleStatus.UnknownProbe, unknown.Status);
        Assert.Equal(TimingSampleStatus.InvalidPeerProcessing, invalidProcessing.Status);
        Assert.Equal(2, synchronizer.Diagnostics.RejectedSamples);
        Assert.Null(synchronizer.CurrentModel);
    }

    [Fact]
    public void TimestampEvidenceRejectsStaleNonMonotonicAndFutureValues()
    {
        var options = new MonotonicTimingOptions(
            maxSynchronizationAgeMilliseconds: 100,
            maxEventAgeMilliseconds: 50,
            maxFutureLeadMilliseconds: 10);
        var synchronizer = new MonotonicTimingSynchronizer(options);
        AddSample(synchronizer, "p1", 1_000, 1_110, 1_110, 1_020);

        var accepted = synchronizer.NormalizePeerTimestamp(1_130, 1_040);
        var nonMonotonic = synchronizer.NormalizePeerTimestamp(1_130, 1_041);
        var tooOld = synchronizer.NormalizePeerTimestamp(1_120, 1_080);
        var tooFuture = synchronizer.NormalizePeerTimestamp(1_200, 1_080);
        var staleModel = synchronizer.NormalizePeerTimestamp(1_140, 1_121);

        Assert.Equal(TimestampNormalizationStatus.Accepted, accepted.Status);
        Assert.Equal(TimestampNormalizationStatus.NonMonotonic, nonMonotonic.Status);
        Assert.Equal(TimestampNormalizationStatus.NonMonotonic, tooOld.Status);
        Assert.Equal(TimestampNormalizationStatus.TooFarInFuture, tooFuture.Status);
        Assert.Equal(TimestampNormalizationStatus.SynchronizationStale, staleModel.Status);
    }

    [Fact]
    public void OldTimestampIsRejectedWhenItIsOtherwiseMonotonic()
    {
        var options = new MonotonicTimingOptions(maxEventAgeMilliseconds: 50);
        var synchronizer = new MonotonicTimingSynchronizer(options);
        AddSample(synchronizer, "p1", 1_000, 1_110, 1_110, 1_020);

        var tooOld = synchronizer.NormalizePeerTimestamp(1_120, 1_080);

        Assert.Equal(TimestampNormalizationStatus.TooOld, tooOld.Status);
    }

    [Fact]
    public void ResetForReconnectInvalidatesOldModelAndRequiresNewSamples()
    {
        var synchronizer = new MonotonicTimingSynchronizer();
        AddSample(synchronizer, "p1", 1_000, 1_110, 1_110, 1_020);
        var previousGeneration = synchronizer.CurrentModel!.Generation;

        synchronizer.Reset(TimingResetReason.Reconnect);
        var beforeResync = synchronizer.NormalizePeerTimestamp(1_200, 1_100);
        AddSample(synchronizer, "p2", 2_000, 2_090, 2_090, 2_020);

        Assert.Equal(TimestampNormalizationStatus.Unsynchronized, beforeResync.Status);
        Assert.Equal(TimingResetReason.Reconnect, synchronizer.Diagnostics.LastResetReason);
        Assert.Equal(previousGeneration + 1, synchronizer.CurrentModel!.Generation);
    }

    [Fact]
    public void NormalizedOrderingCanDifferFromPacketArrivalOrdering()
    {
        var peerA = new MonotonicTimingSynchronizer();
        var peerB = new MonotonicTimingSynchronizer();
        AddSample(peerA, "a", 1_000, 1_110, 1_110, 1_020); // peer A is +100 ms
        AddSample(peerB, "b", 1_000, 960, 960, 1_020); // peer B is -50 ms

        var bArrivesFirst = peerB.NormalizePeerTimestamp(1_070, 1_130);
        var aArrivesLater = peerA.NormalizePeerTimestamp(1_200, 1_140);

        Assert.True(bArrivesFirst.IsAccepted);
        Assert.True(aArrivesLater.IsAccepted);
        Assert.True(aArrivesLater.ReferenceTimestampMilliseconds < bArrivesFirst.ReferenceTimestampMilliseconds);
    }

    [Fact]
    public void SampleAndProbeStorageRemainBounded()
    {
        var synchronizer = new MonotonicTimingSynchronizer(new MonotonicTimingOptions(
            historyCapacity: 3,
            pendingProbeCapacity: 2));

        synchronizer.CreateProbe(1, "stale-1");
        synchronizer.CreateProbe(2, "stale-2");
        synchronizer.CreateProbe(3, "stale-3");

        for (var index = 0; index < 10; index++)
        {
            var referenceSend = 1_000d + (index * 100d);
            AddSample(
                synchronizer,
                $"sample-{index}",
                referenceSend,
                referenceSend + 110,
                referenceSend + 110,
                referenceSend + 20);
        }

        Assert.Equal(3, synchronizer.Diagnostics.HistoryCount);
        Assert.True(synchronizer.Diagnostics.PendingProbeCount <= 2);
        Assert.Equal(3, synchronizer.CurrentModel?.SampleCount);
    }

    [Fact]
    public void UnsynchronizedEvidenceCannotBecomeAuthoritativeTime()
    {
        var synchronizer = new MonotonicTimingSynchronizer();

        var result = synchronizer.NormalizePeerTimestamp(100, 100);

        Assert.False(result.IsAccepted);
        Assert.Equal(TimestampNormalizationStatus.Unsynchronized, result.Status);
        Assert.Null(result.ReferenceTimestampMilliseconds);
    }

    private static void AddSample(
        MonotonicTimingSynchronizer synchronizer,
        string probeId,
        double referenceSend,
        double peerReceive,
        double peerSend,
        double referenceReceive)
    {
        synchronizer.CreateProbe(referenceSend, probeId);
        var observation = synchronizer.ObserveReply(
            new TimingProbeReply(probeId, peerReceive, peerSend),
            referenceReceive);
        Assert.True(observation.IsAccepted, observation.Reason);
    }
}

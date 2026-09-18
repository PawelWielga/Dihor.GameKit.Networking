using System.Diagnostics;

namespace Dihor.GameKit.Networking.Core;

public static class MonotonicClock
{
    public static double TimestampMilliseconds =>
        Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
}

public sealed class MonotonicTimingOptions
{
    public MonotonicTimingOptions(
        int historyCapacity = 16,
        int pendingProbeCapacity = 32,
        double maxRoundTripMilliseconds = 5_000,
        double maxSynchronizationAgeMilliseconds = 30_000,
        double maxEventAgeMilliseconds = 10_000,
        double maxFutureLeadMilliseconds = 250)
    {
        if (historyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(historyCapacity), historyCapacity, "History capacity must be positive.");
        }

        if (pendingProbeCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingProbeCapacity), pendingProbeCapacity, "Pending probe capacity must be positive.");
        }

        ValidatePositiveFinite(maxRoundTripMilliseconds, nameof(maxRoundTripMilliseconds));
        ValidatePositiveFinite(maxSynchronizationAgeMilliseconds, nameof(maxSynchronizationAgeMilliseconds));
        ValidatePositiveFinite(maxEventAgeMilliseconds, nameof(maxEventAgeMilliseconds));
        ValidateNonNegativeFinite(maxFutureLeadMilliseconds, nameof(maxFutureLeadMilliseconds));

        HistoryCapacity = historyCapacity;
        PendingProbeCapacity = pendingProbeCapacity;
        MaxRoundTripMilliseconds = maxRoundTripMilliseconds;
        MaxSynchronizationAgeMilliseconds = maxSynchronizationAgeMilliseconds;
        MaxEventAgeMilliseconds = maxEventAgeMilliseconds;
        MaxFutureLeadMilliseconds = maxFutureLeadMilliseconds;
    }

    public int HistoryCapacity { get; }

    public int PendingProbeCapacity { get; }

    public double MaxRoundTripMilliseconds { get; }

    public double MaxSynchronizationAgeMilliseconds { get; }

    public double MaxEventAgeMilliseconds { get; }

    public double MaxFutureLeadMilliseconds { get; }

    private static void ValidatePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and positive.");
        }
    }

    private static void ValidateNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and non-negative.");
        }
    }
}

public sealed record TimingProbe(
    string ProbeId,
    double ReferenceSendMilliseconds);

public sealed record TimingProbeReply(
    string ProbeId,
    double PeerReceiveMilliseconds,
    double PeerSendMilliseconds);

public enum TimingSampleStatus
{
    Accepted,
    UnknownProbe,
    InvalidTimestamp,
    InvalidPeerProcessing,
    RoundTripTooLarge,
}

public sealed record TimingSample(
    double OffsetMilliseconds,
    double RoundTripMilliseconds,
    double PeerProcessingMilliseconds,
    double ReferenceReceiveMilliseconds);

public sealed record TimingModel(
    double OffsetMilliseconds,
    double RoundTripMilliseconds,
    double JitterMilliseconds,
    double UncertaintyMilliseconds,
    int SampleCount,
    double UpdatedAtReferenceMilliseconds,
    long Generation);

public sealed record TimingSampleObservation(
    TimingSampleStatus Status,
    TimingSample? Sample,
    TimingModel? Model,
    string? Reason = null)
{
    public bool IsAccepted => Status == TimingSampleStatus.Accepted;
}

public enum TimestampNormalizationStatus
{
    Accepted,
    Unsynchronized,
    SynchronizationStale,
    InvalidTimestamp,
    NonMonotonic,
    TooOld,
    TooFarInFuture,
}

public sealed record TimestampNormalizationResult(
    TimestampNormalizationStatus Status,
    double? ReferenceTimestampMilliseconds,
    double? UncertaintyMilliseconds,
    TimingModel? Model,
    string? Reason = null)
{
    public bool IsAccepted => Status == TimestampNormalizationStatus.Accepted;
}

public enum TimingResetReason
{
    Manual,
    Reconnect,
    TransportChanged,
}

public sealed record TimingDiagnostics(
    long Generation,
    int HistoryCount,
    int PendingProbeCount,
    int AcceptedSamples,
    int RejectedSamples,
    TimingSampleStatus? LastRejectedSampleStatus,
    TimingResetReason? LastResetReason,
    TimingModel? Model);

public sealed class MonotonicTimingSynchronizer
{
    private readonly MonotonicTimingOptions _options;
    private readonly Dictionary<string, double> _pendingProbes = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingProbeOrder = new();
    private readonly Queue<TimingSample> _samples = new();
    private TimingModel? _currentModel;
    private double? _lastAcceptedPeerTimestampMilliseconds;
    private long _generation;
    private int _acceptedSamples;
    private int _rejectedSamples;
    private TimingSampleStatus? _lastRejectedSampleStatus;
    private TimingResetReason? _lastResetReason;

    public MonotonicTimingSynchronizer(MonotonicTimingOptions? options = null)
    {
        _options = options ?? new MonotonicTimingOptions();
    }

    public TimingModel? CurrentModel => _currentModel;

    public TimingDiagnostics Diagnostics => new(
        _generation,
        _samples.Count,
        _pendingProbes.Count,
        _acceptedSamples,
        _rejectedSamples,
        _lastRejectedSampleStatus,
        _lastResetReason,
        _currentModel);

    public TimingProbe CreateProbe(
        double referenceNowMilliseconds,
        string? probeId = null)
    {
        ValidateTimestamp(referenceNowMilliseconds, nameof(referenceNowMilliseconds));
        var resolvedProbeId = probeId is null
            ? Guid.NewGuid().ToString("N")
            : NormalizeProbeId(probeId);

        if (_pendingProbes.ContainsKey(resolvedProbeId))
        {
            throw new InvalidOperationException($"Timing probe '{resolvedProbeId}' is already pending.");
        }

        TrimPendingProbesForNewEntry();
        _pendingProbes.Add(resolvedProbeId, referenceNowMilliseconds);
        _pendingProbeOrder.Enqueue(resolvedProbeId);
        return new TimingProbe(resolvedProbeId, referenceNowMilliseconds);
    }

    public TimingSampleObservation ObserveReply(
        TimingProbeReply reply,
        double referenceReceiveMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var probeId = NormalizeProbeId(reply.ProbeId);
        ValidateTimestamp(referenceReceiveMilliseconds, nameof(referenceReceiveMilliseconds));

        if (!_pendingProbes.Remove(probeId, out var referenceSendMilliseconds))
        {
            return RejectSample(
                TimingSampleStatus.UnknownProbe,
                $"Timing probe '{probeId}' is not pending.");
        }
        CompactPendingProbeOrderIfNeeded();

        if (!IsValidTimestamp(reply.PeerReceiveMilliseconds) ||
            !IsValidTimestamp(reply.PeerSendMilliseconds) ||
            referenceReceiveMilliseconds < referenceSendMilliseconds)
        {
            return RejectSample(
                TimingSampleStatus.InvalidTimestamp,
                "Timing reply contains invalid or non-monotonic clock values.");
        }

        var peerProcessingMilliseconds = reply.PeerSendMilliseconds - reply.PeerReceiveMilliseconds;
        if (peerProcessingMilliseconds < 0)
        {
            return RejectSample(
                TimingSampleStatus.InvalidPeerProcessing,
                "Peer send time precedes peer receive time.");
        }

        var roundTripMilliseconds =
            (referenceReceiveMilliseconds - referenceSendMilliseconds) - peerProcessingMilliseconds;
        if (!double.IsFinite(roundTripMilliseconds) || roundTripMilliseconds < 0)
        {
            return RejectSample(
                TimingSampleStatus.InvalidPeerProcessing,
                "Peer processing interval exceeds the observed reference round trip.");
        }

        if (roundTripMilliseconds > _options.MaxRoundTripMilliseconds)
        {
            return RejectSample(
                TimingSampleStatus.RoundTripTooLarge,
                $"Observed round trip {roundTripMilliseconds:F3} ms exceeds the configured maximum.");
        }

        var offsetMilliseconds =
            ((reply.PeerReceiveMilliseconds - referenceSendMilliseconds) +
             (reply.PeerSendMilliseconds - referenceReceiveMilliseconds)) / 2d;
        var sample = new TimingSample(
            offsetMilliseconds,
            roundTripMilliseconds,
            peerProcessingMilliseconds,
            referenceReceiveMilliseconds);

        _samples.Enqueue(sample);
        while (_samples.Count > _options.HistoryCapacity)
        {
            _samples.Dequeue();
        }

        _acceptedSamples++;
        _currentModel = BuildModel(referenceReceiveMilliseconds);
        return new TimingSampleObservation(
            TimingSampleStatus.Accepted,
            sample,
            _currentModel);
    }

    public TimestampNormalizationResult NormalizePeerTimestamp(
        double peerTimestampMilliseconds,
        double referenceNowMilliseconds)
    {
        if (!IsValidTimestamp(peerTimestampMilliseconds) || !IsValidTimestamp(referenceNowMilliseconds))
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.InvalidTimestamp,
                null,
                null,
                _currentModel,
                "Timestamp evidence must be finite and non-negative.");
        }

        var model = _currentModel;
        if (model is null)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.Unsynchronized,
                null,
                null,
                null,
                "No synchronization model is available.");
        }

        var modelAgeMilliseconds = referenceNowMilliseconds - model.UpdatedAtReferenceMilliseconds;
        if (modelAgeMilliseconds < 0)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.InvalidTimestamp,
                null,
                model.UncertaintyMilliseconds,
                model,
                "Reference clock moved backwards relative to the synchronization model.");
        }

        if (modelAgeMilliseconds > _options.MaxSynchronizationAgeMilliseconds)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.SynchronizationStale,
                null,
                model.UncertaintyMilliseconds,
                model,
                "Synchronization model is too old and must be reacquired.");
        }

        if (_lastAcceptedPeerTimestampMilliseconds is { } lastPeerTimestamp &&
            peerTimestampMilliseconds <= lastPeerTimestamp)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.NonMonotonic,
                null,
                model.UncertaintyMilliseconds,
                model,
                "Peer timestamp did not advance monotonically.");
        }

        var referenceTimestampMilliseconds = peerTimestampMilliseconds - model.OffsetMilliseconds;
        if (!double.IsFinite(referenceTimestampMilliseconds) || referenceTimestampMilliseconds < 0)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.InvalidTimestamp,
                null,
                model.UncertaintyMilliseconds,
                model,
                "Normalized reference timestamp is invalid.");
        }

        if (referenceTimestampMilliseconds - referenceNowMilliseconds > _options.MaxFutureLeadMilliseconds)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.TooFarInFuture,
                referenceTimestampMilliseconds,
                model.UncertaintyMilliseconds,
                model,
                "Normalized timestamp is implausibly far in the future.");
        }

        if (referenceNowMilliseconds - referenceTimestampMilliseconds > _options.MaxEventAgeMilliseconds)
        {
            return new TimestampNormalizationResult(
                TimestampNormalizationStatus.TooOld,
                referenceTimestampMilliseconds,
                model.UncertaintyMilliseconds,
                model,
                "Normalized timestamp is too old for the configured evidence window.");
        }

        _lastAcceptedPeerTimestampMilliseconds = peerTimestampMilliseconds;
        return new TimestampNormalizationResult(
            TimestampNormalizationStatus.Accepted,
            referenceTimestampMilliseconds,
            model.UncertaintyMilliseconds,
            model);
    }

    public void Reset(TimingResetReason reason = TimingResetReason.Manual)
    {
        _pendingProbes.Clear();
        _pendingProbeOrder.Clear();
        _samples.Clear();
        _currentModel = null;
        _lastAcceptedPeerTimestampMilliseconds = null;
        _acceptedSamples = 0;
        _rejectedSamples = 0;
        _lastRejectedSampleStatus = null;
        _lastResetReason = reason;
        _generation++;
    }

    private TimingModel BuildModel(double updatedAtReferenceMilliseconds)
    {
        var allSamples = _samples.ToArray();
        var selectedCount = Math.Max(1, (allSamples.Length + 1) / 2);
        var bestSamples = allSamples
            .OrderBy(sample => sample.RoundTripMilliseconds)
            .Take(selectedCount)
            .ToArray();

        var offsetMilliseconds = Median(bestSamples
            .Select(sample => sample.OffsetMilliseconds)
            .ToArray());
        var roundTripMilliseconds = Median(bestSamples
            .Select(sample => sample.RoundTripMilliseconds)
            .ToArray());
        var allRoundTripMedian = Median(allSamples
            .Select(sample => sample.RoundTripMilliseconds)
            .ToArray());
        var jitterMilliseconds = Median(allSamples
            .Select(sample => Math.Abs(sample.RoundTripMilliseconds - allRoundTripMedian))
            .ToArray());
        var offsetSpreadMilliseconds = Median(bestSamples
            .Select(sample => Math.Abs(sample.OffsetMilliseconds - offsetMilliseconds))
            .ToArray());
        var minimumRoundTripMilliseconds = bestSamples.Min(sample => sample.RoundTripMilliseconds);
        var uncertaintyMilliseconds =
            (minimumRoundTripMilliseconds / 2d) +
            offsetSpreadMilliseconds +
            (jitterMilliseconds / 2d);

        return new TimingModel(
            offsetMilliseconds,
            roundTripMilliseconds,
            jitterMilliseconds,
            uncertaintyMilliseconds,
            allSamples.Length,
            updatedAtReferenceMilliseconds,
            _generation);
    }

    private TimingSampleObservation RejectSample(TimingSampleStatus status, string reason)
    {
        _rejectedSamples++;
        _lastRejectedSampleStatus = status;
        return new TimingSampleObservation(status, null, _currentModel, reason);
    }

    private void TrimPendingProbesForNewEntry()
    {
        while (_pendingProbes.Count >= _options.PendingProbeCapacity && _pendingProbeOrder.Count > 0)
        {
            var oldestProbeId = _pendingProbeOrder.Dequeue();
            _pendingProbes.Remove(oldestProbeId);
        }
    }

    private void CompactPendingProbeOrderIfNeeded()
    {
        if (_pendingProbeOrder.Count <= _options.PendingProbeCapacity)
        {
            return;
        }

        var pendingIds = _pendingProbeOrder
            .Where(_pendingProbes.ContainsKey)
            .ToArray();
        _pendingProbeOrder.Clear();
        foreach (var pendingId in pendingIds)
        {
            _pendingProbeOrder.Enqueue(pendingId);
        }
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            throw new ArgumentException("Median requires at least one value.", nameof(values));
        }

        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }

    private static string NormalizeProbeId(string probeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        return probeId.Trim();
    }

    private static void ValidateTimestamp(double value, string parameterName)
    {
        if (!IsValidTimestamp(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Timestamp must be finite and non-negative.");
        }
    }

    private static bool IsValidTimestamp(double value) =>
        double.IsFinite(value) && value >= 0;
}

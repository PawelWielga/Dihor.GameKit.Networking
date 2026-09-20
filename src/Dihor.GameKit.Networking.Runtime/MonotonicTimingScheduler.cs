using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Runtime;

public sealed class MonotonicTimingSchedulerOptions
{
    public MonotonicTimingSchedulerOptions(TimeSpan refreshInterval)
    {
        if (refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                refreshInterval,
                "Refresh interval must be positive.");
        }

        RefreshInterval = refreshInterval;
    }

    public TimeSpan RefreshInterval { get; }

    public static MonotonicTimingSchedulerOptions Default { get; } =
        new(TimeSpan.FromSeconds(5));
}

public sealed record TimingRefreshFailed(PeerId PeerId, Exception Exception);

/// <summary>
/// Owns periodic timing probes and per-peer synchronization state while the
/// application remains responsible for serializing and transporting probes.
/// </summary>
public sealed class MonotonicTimingScheduler : IAsyncDisposable
{
    private readonly Func<PeerId, TimingProbe, CancellationToken, ValueTask> _sendProbeAsync;
    private readonly MonotonicTimingSchedulerOptions _options;
    private readonly MonotonicTimingOptions _timingOptions;
    private readonly Func<double> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<PeerId, PeerTimingState> _peers = new();

    private CancellationTokenSource? _runSource;
    private Task? _runTask;
    private SchedulerState _state;

    public MonotonicTimingScheduler(
        Func<PeerId, TimingProbe, CancellationToken, ValueTask> sendProbeAsync,
        MonotonicTimingSchedulerOptions? options = null,
        MonotonicTimingOptions? timingOptions = null,
        Func<double>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(sendProbeAsync);
        _sendProbeAsync = sendProbeAsync;
        _options = options ?? MonotonicTimingSchedulerOptions.Default;
        _timingOptions = timingOptions ?? new MonotonicTimingOptions();
        _clock = clock ?? (() => MonotonicClock.TimestampMilliseconds);
    }

    public event EventHandler<TimingRefreshFailed>? RefreshFailed;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state != SchedulerState.Created)
            {
                throw new InvalidOperationException($"Timing scheduler cannot start while state is '{_state}'.");
            }

            _state = SchedulerState.Running;
            _runSource = new CancellationTokenSource();
            _runTask = RunAsync(_runSource.Token);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask ActivatePeerAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        PeerTimingState peerState;
        lock (_gate)
        {
            EnsureRunningLocked();
            if (!_peers.TryGetValue(peerId, out peerState!))
            {
                peerState = new PeerTimingState(new MonotonicTimingSynchronizer(_timingOptions));
                _peers.Add(peerId, peerState);
            }
        }

        await SendProbeAsync(peerId, peerState, cancellationToken).ConfigureAwait(false);
    }

    public bool DeactivatePeer(PeerId peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        lock (_gate)
        {
            return _peers.Remove(peerId);
        }
    }

    public async ValueTask ResetPeerAsync(
        PeerId peerId,
        TimingResetReason reason = TimingResetReason.Reconnect,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        PeerTimingState peerState;
        lock (_gate)
        {
            EnsureRunningLocked();
            if (!_peers.TryGetValue(peerId, out peerState!))
            {
                peerState = new PeerTimingState(new MonotonicTimingSynchronizer(_timingOptions));
                _peers.Add(peerId, peerState);
            }
        }

        lock (peerState.Gate)
        {
            peerState.Synchronizer.Reset(reason);
        }

        await SendProbeAsync(peerId, peerState, cancellationToken).ConfigureAwait(false);
    }

    public TimingSampleObservation ObserveReply(
        PeerId peerId,
        TimingProbeReply reply,
        double? referenceReceiveMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var peerState = GetPeerState(peerId);
        lock (peerState.Gate)
        {
            return peerState.Synchronizer.ObserveReply(
                reply,
                referenceReceiveMilliseconds ?? ReadClock());
        }
    }

    public TimestampNormalizationResult NormalizePeerTimestamp(
        PeerId peerId,
        double peerTimestampMilliseconds,
        double? referenceNowMilliseconds = null)
    {
        var peerState = GetPeerState(peerId);
        lock (peerState.Gate)
        {
            return peerState.Synchronizer.NormalizePeerTimestamp(
                peerTimestampMilliseconds,
                referenceNowMilliseconds ?? ReadClock());
        }
    }

    public TimingDiagnostics GetDiagnostics(PeerId peerId)
    {
        var peerState = GetPeerState(peerId);
        lock (peerState.Gate)
        {
            return peerState.Synchronizer.Diagnostics;
        }
    }

    public async ValueTask StopAsync()
    {
        CancellationTokenSource? source;
        Task? runTask;
        lock (_gate)
        {
            if (_state is SchedulerState.Stopped or SchedulerState.Disposed)
            {
                return;
            }

            if (_state == SchedulerState.Created)
            {
                _state = SchedulerState.Stopped;
                return;
            }

            _state = SchedulerState.Stopping;
            source = _runSource;
            runTask = _runTask;
        }

        source?.Cancel();
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (source?.IsCancellationRequested == true)
            {
            }
        }

        lock (_gate)
        {
            _state = SchedulerState.Stopped;
            _runSource?.Dispose();
            _runSource = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _peers.Clear();
            _state = SchedulerState.Disposed;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.RefreshInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            KeyValuePair<PeerId, PeerTimingState>[] peers;
            lock (_gate)
            {
                peers = _peers.ToArray();
            }

            foreach (var peer in peers)
            {
                try
                {
                    await SendProbeAsync(peer.Key, peer.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // One peer failure must not stop refreshes for other peers.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    RefreshFailed?.Invoke(this, new TimingRefreshFailed(peer.Key, exception));
                }
            }
        }
    }

    private async ValueTask SendProbeAsync(
        PeerId peerId,
        PeerTimingState peerState,
        CancellationToken cancellationToken)
    {
        TimingProbe probe;
        lock (peerState.Gate)
        {
            probe = peerState.Synchronizer.CreateProbe(ReadClock());
        }

        await _sendProbeAsync(peerId, probe, cancellationToken).ConfigureAwait(false);
    }

    private PeerTimingState GetPeerState(PeerId peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        lock (_gate)
        {
            return _peers.TryGetValue(peerId, out var peerState)
                ? peerState
                : throw new InvalidOperationException($"Peer '{peerId}' is not active in the timing scheduler.");
        }
    }

    private double ReadClock()
    {
        var value = _clock();
        if (!double.IsFinite(value) || value < 0)
        {
            throw new InvalidOperationException("Timing scheduler clock returned an invalid timestamp.");
        }

        return value;
    }

    private void EnsureRunningLocked()
    {
        if (_state != SchedulerState.Running)
        {
            throw new InvalidOperationException($"Timing scheduler is not running. Current state is '{_state}'.");
        }
    }

    private sealed class PeerTimingState(MonotonicTimingSynchronizer synchronizer)
    {
        public object Gate { get; } = new();

        public MonotonicTimingSynchronizer Synchronizer { get; } = synchronizer;
    }

    private enum SchedulerState
    {
        Created,
        Running,
        Stopping,
        Stopped,
        Disposed,
    }
}

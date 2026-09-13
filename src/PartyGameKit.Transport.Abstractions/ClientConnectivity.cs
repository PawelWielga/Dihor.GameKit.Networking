using System.Diagnostics;

namespace PartyGameKit.Transport.Abstractions;

public static class TransportIds
{
    public const string LanWebSocket = "lan-websocket";
    public const string WebRtcDataChannel = "webrtc-datachannel";
    public const string SignalRRelay = "signalr-relay";
}

public enum ConnectivityMode
{
    Auto,
    Lan,
    WebRtc,
    SignalR,
}

public sealed record ClientTransportMessage(
    ReadOnlyMemory<byte> Payload,
    bool IsClose = false,
    string? CloseDescription = null);

public interface IMessageTransportClient : IAsyncDisposable
{
    ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    ValueTask<ClientTransportMessage> ReceiveAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ConnectivitySelectionOptions
{
    public ConnectivitySelectionOptions(
        TimeSpan attemptTimeout,
        IReadOnlyList<string>? autoOrder = null)
    {
        if (attemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptTimeout),
                attemptTimeout,
                "Transport attempt timeout must be positive.");
        }

        var resolvedOrder = autoOrder ??
            [TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel, TransportIds.SignalRRelay];
        if (resolvedOrder.Count == 0)
        {
            throw new ArgumentException("Automatic transport order cannot be empty.", nameof(autoOrder));
        }

        if (resolvedOrder.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Automatic transport order cannot contain empty transport identifiers.", nameof(autoOrder));
        }

        if (resolvedOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count() != resolvedOrder.Count)
        {
            throw new ArgumentException("Automatic transport order cannot contain duplicate transport identifiers.", nameof(autoOrder));
        }

        AttemptTimeout = attemptTimeout;
        AutoOrder = resolvedOrder.Select(value => value.Trim()).ToArray();
    }

    public TimeSpan AttemptTimeout { get; }

    public IReadOnlyList<string> AutoOrder { get; }

    public static ConnectivitySelectionOptions Default { get; } =
        new(TimeSpan.FromSeconds(3));
}

public sealed record TransportClientCandidate
{
    public TransportClientCandidate(
        string transportId,
        Func<string, CancellationToken, Task<IMessageTransportClient>> connectAsync,
        TimeSpan? attemptTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportId);
        ArgumentNullException.ThrowIfNull(connectAsync);
        if (attemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptTimeout),
                attemptTimeout,
                "Candidate attempt timeout must be positive when specified.");
        }

        TransportId = transportId.Trim();
        ConnectAsync = connectAsync;
        AttemptTimeout = attemptTimeout;
    }

    public string TransportId { get; }

    public Func<string, CancellationToken, Task<IMessageTransportClient>> ConnectAsync { get; }

    public TimeSpan? AttemptTimeout { get; }
}

public enum ConnectivityAttemptOutcome
{
    Selected,
    Failed,
    TimedOut,
    Unavailable,
}

public sealed record ConnectivityAttemptDiagnostic(
    string TransportId,
    ConnectivityAttemptOutcome Outcome,
    TimeSpan Duration,
    string? Error = null);

public sealed record ConnectivityDiagnostics(
    IReadOnlyList<ConnectivityAttemptDiagnostic> Attempts,
    string? SelectedTransport,
    string? PreferredTransport,
    bool PreferredTransportReused);

public sealed class ConnectivitySelectionException : Exception
{
    public ConnectivitySelectionException(
        string message,
        ConnectivityDiagnostics diagnostics,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
    }

    public ConnectivityDiagnostics Diagnostics { get; }
}

public sealed class SelectedTransportClient : IMessageTransportClient
{
    private readonly IMessageTransportClient _inner;

    internal SelectedTransportClient(
        IMessageTransportClient inner,
        string transportId,
        ConnectivityDiagnostics diagnostics)
    {
        _inner = inner;
        TransportId = transportId;
        Diagnostics = diagnostics;
    }

    public string TransportId { get; }

    public ConnectivityDiagnostics Diagnostics { get; }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default) =>
        _inner.SendAsync(payload, cancellationToken);

    public ValueTask<ClientTransportMessage> ReceiveAsync(
        CancellationToken cancellationToken = default) =>
        _inner.ReceiveAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public sealed class AutomaticTransportSelector
{
    private readonly IReadOnlyDictionary<string, TransportClientCandidate> _candidates;
    private readonly ConnectivitySelectionOptions _options;
    private readonly object _gate = new();
    private string? _lastSuccessfulTransport;

    public AutomaticTransportSelector(
        IEnumerable<TransportClientCandidate> candidates,
        ConnectivitySelectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _options = options ?? ConnectivitySelectionOptions.Default;

        var candidateArray = candidates.ToArray();
        if (candidateArray.Length == 0)
        {
            throw new ArgumentException("At least one transport candidate is required.", nameof(candidates));
        }

        var duplicate = candidateArray
            .GroupBy(candidate => candidate.TransportId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Transport candidate '{duplicate.Key}' is registered more than once.",
                nameof(candidates));
        }

        _candidates = candidateArray.ToDictionary(
            candidate => candidate.TransportId,
            StringComparer.OrdinalIgnoreCase);
    }

    public string? LastSuccessfulTransport
    {
        get
        {
            lock (_gate)
            {
                return _lastSuccessfulTransport;
            }
        }
    }

    public Task<SelectedTransportClient> ConnectAsync(
        string handshake,
        ConnectivityMode mode = ConnectivityMode.Auto,
        CancellationToken cancellationToken = default) =>
        SelectAsync(handshake, mode, preferPrevious: false, cancellationToken);

    public Task<SelectedTransportClient> ReconnectAsync(
        string resumeHandshake,
        ConnectivityMode mode = ConnectivityMode.Auto,
        CancellationToken cancellationToken = default) =>
        SelectAsync(resumeHandshake, mode, preferPrevious: true, cancellationToken);

    private async Task<SelectedTransportClient> SelectAsync(
        string handshake,
        ConnectivityMode mode,
        bool preferPrevious,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handshake);
        cancellationToken.ThrowIfCancellationRequested();

        var preferredTransport = preferPrevious && mode == ConnectivityMode.Auto
            ? LastSuccessfulTransport
            : null;
        var transportIds = BuildAttemptOrder(mode, preferredTransport);
        var attempts = new List<ConnectivityAttemptDiagnostic>(transportIds.Count);
        Exception? lastError = null;

        foreach (var transportId in transportIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_candidates.TryGetValue(transportId, out var candidate))
            {
                attempts.Add(new ConnectivityAttemptDiagnostic(
                    transportId,
                    ConnectivityAttemptOutcome.Unavailable,
                    TimeSpan.Zero,
                    "Transport candidate is not registered in this runtime."));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var timeout = candidate.AttemptTimeout ?? _options.AttemptTimeout;
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<IMessageTransportClient>? connectTask = null;

            try
            {
                connectTask = candidate.ConnectAsync(handshake, attemptCancellation.Token);
                var client = await connectTask
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();

                attempts.Add(new ConnectivityAttemptDiagnostic(
                    candidate.TransportId,
                    ConnectivityAttemptOutcome.Selected,
                    stopwatch.Elapsed));
                SetLastSuccessfulTransport(candidate.TransportId);

                var diagnostics = new ConnectivityDiagnostics(
                    attempts.ToArray(),
                    candidate.TransportId,
                    preferredTransport,
                    preferredTransport is not null &&
                    string.Equals(preferredTransport, candidate.TransportId, StringComparison.OrdinalIgnoreCase));
                return new SelectedTransportClient(client, candidate.TransportId, diagnostics);
            }
            catch (TimeoutException exception)
            {
                stopwatch.Stop();
                attemptCancellation.Cancel();
                if (connectTask is not null)
                {
                    _ = DisposeLateConnectionAsync(connectTask);
                }

                lastError = exception;
                attempts.Add(new ConnectivityAttemptDiagnostic(
                    candidate.TransportId,
                    ConnectivityAttemptOutcome.TimedOut,
                    stopwatch.Elapsed,
                    $"Connection attempt exceeded {timeout}."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                attemptCancellation.Cancel();
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                lastError = exception;
                attempts.Add(new ConnectivityAttemptDiagnostic(
                    candidate.TransportId,
                    ConnectivityAttemptOutcome.Failed,
                    stopwatch.Elapsed,
                    exception.Message));
            }
        }

        var failedDiagnostics = new ConnectivityDiagnostics(
            attempts.ToArray(),
            null,
            preferredTransport,
            PreferredTransportReused: false);
        throw new ConnectivitySelectionException(
            "No configured communication transport could establish a connection.",
            failedDiagnostics,
            lastError);
    }

    private IReadOnlyList<string> BuildAttemptOrder(
        ConnectivityMode mode,
        string? preferredTransport)
    {
        if (mode != ConnectivityMode.Auto)
        {
            return [ModeToTransportId(mode)];
        }

        var result = new List<string>(_options.AutoOrder.Count + 1);
        if (!string.IsNullOrWhiteSpace(preferredTransport))
        {
            result.Add(preferredTransport);
        }

        foreach (var transportId in _options.AutoOrder)
        {
            if (!result.Contains(transportId, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(transportId);
            }
        }

        return result;
    }

    private static string ModeToTransportId(ConnectivityMode mode) => mode switch
    {
        ConnectivityMode.Lan => TransportIds.LanWebSocket,
        ConnectivityMode.WebRtc => TransportIds.WebRtcDataChannel,
        ConnectivityMode.SignalR => TransportIds.SignalRRelay,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported forced connectivity mode."),
    };

    private void SetLastSuccessfulTransport(string transportId)
    {
        lock (_gate)
        {
            _lastSuccessfulTransport = transportId;
        }
    }

    private static async Task DisposeLateConnectionAsync(Task<IMessageTransportClient> connectTask)
    {
        try
        {
            var client = await connectTask.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A timed-out candidate owns its eventual failure. If it succeeds after
            // the budget, dispose the late connection so fallback cannot leak it.
        }
    }
}

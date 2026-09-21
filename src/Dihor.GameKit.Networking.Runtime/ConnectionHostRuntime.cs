using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Runtime;

/// <summary>
/// Owns protocol-v2 connection continuity, heartbeats, reconnect windows and
/// application-message dispatch for a host transport.
/// </summary>
public sealed class ConnectionHostRuntime : IAsyncDisposable
{
    private readonly IMessageTransport _transport;
    private readonly ConnectionHostOptions _options;
    private readonly ConnectionContinuityCoordinator _continuity;
    private readonly MessageIdDeduplicator _deduplicator;
    private readonly Func<string> _messageIdFactory;
    private readonly object _lifecycleGate = new();
    private readonly object _messageIdGate = new();
    private readonly Channel<ConnectionHostEvent> _events = Channel.CreateUnbounded<ConnectionHostEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });

    private CancellationTokenSource? _runSource;
    private Task? _runTask;
    private HostState _state;
    private bool _transportDisposed;

    public ConnectionHostRuntime(
        IMessageTransport transport,
        ConnectionHostOptions? options = null,
        MessageIdDeduplicator? deduplicator = null,
        Func<string>? messageIdFactory = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _options = options ?? ConnectionHostOptions.Default;
        _deduplicator = deduplicator ?? new MessageIdDeduplicator();
        _messageIdFactory = messageIdFactory ?? (() => $"dihor-host-{Guid.NewGuid():N}");
        _continuity = new ConnectionContinuityCoordinator(
            new ConnectionContinuityOptions(_options.PeerTimeout, _options.ReconnectWindow));
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleGate)
        {
            if (_state != HostState.Created)
            {
                throw new InvalidOperationException($"Connection host cannot start while state is '{_state}'.");
            }

            _state = HostState.Running;
            _runSource = new CancellationTokenSource();
            _runTask = RunAsync(_runSource);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ConnectionHostEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_state == HostState.Created)
            {
                throw new InvalidOperationException("Connection host has not started.");
            }
        }

        await foreach (var hostEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return hostEvent;
        }
    }

    public async ValueTask<string> SendApplicationAsync(
        PeerId peerId,
        string applicationType,
        JsonElement data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationType);
        if (data.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Application data cannot be undefined.", nameof(data));
        }

        EnsureRunning();
        var presence = _continuity.GetPresence(peerId);
        if (presence is not { State: PeerConnectionState.Connected, ConnectionId: { } connectionId })
        {
            throw new InvalidOperationException($"Peer '{peerId}' is not currently connected.");
        }

        var messageId = NextMessageId();
        var message = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            messageId,
            new ApplicationMessagePayload(applicationType, data));
        await SendProtocolAsync(connectionId, message, cancellationToken).ConfigureAwait(false);
        return messageId;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? source;
        Task? runTask;
        lock (_lifecycleGate)
        {
            if (_state is HostState.Stopped or HostState.Disposed)
            {
                return;
            }

            if (_state == HostState.Created)
            {
                _state = HostState.Stopped;
                _events.Writer.TryComplete();
                return;
            }

            _state = HostState.Stopping;
            source = _runSource;
            runTask = _runTask;
        }

        source?.Cancel();
        await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        if (runTask is not null)
        {
            await runTask.ConfigureAwait(false);
        }

        lock (_lifecycleGate)
        {
            _state = HostState.Stopped;
            _runSource?.Dispose();
            _runSource = null;
        }

        _events.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (!_transportDisposed)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transportDisposed = true;
        }

        lock (_lifecycleGate)
        {
            _state = HostState.Disposed;
        }
    }

    private async Task RunAsync(CancellationTokenSource source)
    {
        Task? transportTask = null;
        Task? maintenanceTask = null;
        try
        {
            transportTask = PumpTransportAsync(source.Token);
            maintenanceTask = RunMaintenanceAsync(source.Token);
            var completedTask = await Task.WhenAny(transportTask, maintenanceTask).ConfigureAwait(false);
            await completedTask.ConfigureAwait(false);

            if (!source.IsCancellationRequested)
            {
                var component = completedTask == transportTask ? "Transport event stream" : "Maintenance loop";
                throw new InvalidOperationException(
                    $"{component} ended unexpectedly while the connection host was running.");
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Background failures are forwarded through the event channel.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            source.Cancel();
            lock (_lifecycleGate)
            {
                if (_state == HostState.Running)
                {
                    _state = HostState.Faulted;
                }
            }

            _events.Writer.TryComplete(exception);
        }
        finally
        {
            source.Cancel();
            await ObserveShutdownAsync(transportTask).ConfigureAwait(false);
            await ObserveShutdownAsync(maintenanceTask).ConfigureAwait(false);
        }
    }

    private static async Task ObserveShutdownAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The primary background failure was already forwarded through the event channel.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private async Task PumpTransportAsync(CancellationToken cancellationToken)
    {
        await foreach (var transportEvent in _transport
            .ReadEventsAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await HandleTransportEventAsync(transportEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.MaintenanceInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var timedOut in _continuity.SweepTimeouts())
            {
                _events.Writer.TryWrite(new ConnectionPeerDisconnected(timedOut.PeerId, CanResume: true));
                await TryDisconnectAsync(timedOut.ConnectionId, TransportCloseReason.Timeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var expiredPeer in _continuity.ExpireReconnectWindows())
            {
                _deduplicator.ForgetPeer(expiredPeer);
                _events.Writer.TryWrite(new ConnectionPeerDisconnected(expiredPeer, CanResume: false));
            }
        }
    }

    private async ValueTask HandleTransportEventAsync(
        TransportEvent transportEvent,
        CancellationToken cancellationToken)
    {
        switch (transportEvent)
        {
            case TransportConnectionOpened:
                return;
            case TransportConnectionClosed closed:
                MarkDisconnected(closed.ConnectionId);
                return;
            case TransportMessageReceived received:
                await HandleMessageAsync(received, cancellationToken).ConfigureAwait(false);
                return;
            case TransportFaulted faulted when faulted.Error.ConnectionId is { } connectionId:
                await TryDisconnectAsync(connectionId, TransportCloseReason.Faulted, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case TransportFaulted faulted:
                throw new TransportException(faulted.Error);
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(transportEvent),
                    transportEvent.GetType().FullName,
                    "Unknown transport event.");
        }
    }

    private async ValueTask HandleMessageAsync(
        TransportMessageReceived received,
        CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(received.Payload.Span);
        var type = TryReadMessageType(json);
        try
        {
            switch (type)
            {
                case ProtocolMessageTypes.ConnectRequest:
                    await HandleConnectAsync(received.ConnectionId, json, cancellationToken).ConfigureAwait(false);
                    return;
                case ProtocolMessageTypes.ResumeRequest:
                    await HandleResumeAsync(received.ConnectionId, json, cancellationToken).ConfigureAwait(false);
                    return;
                case ProtocolMessageTypes.Heartbeat:
                    await HandleHeartbeatAsync(received.ConnectionId, json, cancellationToken).ConfigureAwait(false);
                    return;
                case ProtocolMessageTypes.Disconnect:
                    await HandleDisconnectAsync(received.ConnectionId, json, cancellationToken).ConfigureAwait(false);
                    return;
                case ProtocolMessageTypes.ApplicationMessage:
                    await HandleApplicationAsync(received.ConnectionId, json, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    await TryDisconnectAsync(received.ConnectionId, TransportCloseReason.Faulted, cancellationToken)
                        .ConfigureAwait(false);
                    return;
            }
        }
        catch (TransportException)
        {
            await TryDisconnectAsync(received.ConnectionId, TransportCloseReason.Faulted, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask HandleConnectAsync(
        ConnectionId connectionId,
        string json,
        CancellationToken cancellationToken)
    {
        var parsed = ProtocolJson.Read<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest);
        if (!parsed.IsSuccess || parsed.Message?.Payload.PeerId is not { } peerId)
        {
            var correlationId = parsed.Message?.MessageId ?? NextMessageId();
            await RejectConnectAsync(
                connectionId,
                correlationId,
                ConnectionRejectionCode.InvalidRequest,
                "stable-peer-id-required",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var registration = _continuity.Register(peerId, connectionId);
        if (!registration.IsConnected)
        {
            var (code, reason) = registration.Status switch
            {
                RegisterPeerStatus.PeerAlreadyConnected =>
                    (ConnectionRejectionCode.PeerAlreadyConnected, "peer-already-connected"),
                RegisterPeerStatus.PeerRequiresResume =>
                    (ConnectionRejectionCode.InvalidRequest, "resume-required"),
                RegisterPeerStatus.ConnectionAlreadyBound =>
                    (ConnectionRejectionCode.ConnectionAlreadyBound, "connection-already-bound"),
                _ => throw new InvalidOperationException($"Unexpected register status '{registration.Status}'."),
            };
            await RejectConnectAsync(
                connectionId,
                parsed.Message.MessageId,
                code,
                reason,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var accepted = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ConnectAccepted,
            NextMessageId(),
            new ConnectAcceptedPayload(connectionId, peerId, registration.ResumeToken),
            parsed.Message.MessageId);
        await SendProtocolAsync(connectionId, accepted, cancellationToken).ConfigureAwait(false);
        _events.Writer.TryWrite(new ConnectionPeerConnected(peerId, IsResume: false));
    }

    private async ValueTask HandleResumeAsync(
        ConnectionId connectionId,
        string json,
        CancellationToken cancellationToken)
    {
        var parsed = ProtocolJson.Read<ResumeRequestPayload>(json, ProtocolMessageTypes.ResumeRequest);
        if (!parsed.IsSuccess || parsed.Message is null)
        {
            await TryDisconnectAsync(connectionId, TransportCloseReason.Faulted, cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = parsed.Message;
        var result = _continuity.Resume(
            request.Payload.PeerId,
            request.Payload.ResumeToken,
            connectionId);
        if (!result.IsResumed)
        {
            var code = result.Status switch
            {
                ResumePeerStatus.UnknownPeer => ConnectionRejectionCode.UnknownPeer,
                ResumePeerStatus.InvalidResumeCredential => ConnectionRejectionCode.InvalidResumeCredential,
                ResumePeerStatus.ReconnectWindowExpired => ConnectionRejectionCode.ReconnectWindowExpired,
                ResumePeerStatus.PeerAlreadyConnected => ConnectionRejectionCode.PeerAlreadyConnected,
                ResumePeerStatus.ConnectionAlreadyBound => ConnectionRejectionCode.ConnectionAlreadyBound,
                _ => throw new InvalidOperationException($"Unexpected resume status '{result.Status}'."),
            };
            var rejected = DihorGameKitNetworkingMessages.Create(
                ProtocolMessageTypes.ResumeRejected,
                NextMessageId(),
                new ResumeRejectedPayload(request.Payload.PeerId, code),
                request.MessageId);
            await SendProtocolAsync(connectionId, rejected, cancellationToken).ConfigureAwait(false);
            await TryDisconnectAsync(connectionId, TransportCloseReason.Normal, cancellationToken).ConfigureAwait(false);
            return;
        }

        var accepted = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ResumeAccepted,
            NextMessageId(),
            new ResumeAcceptedPayload(connectionId, request.Payload.PeerId, result.ResumeToken),
            request.MessageId);
        await SendProtocolAsync(connectionId, accepted, cancellationToken).ConfigureAwait(false);
        _events.Writer.TryWrite(new ConnectionPeerConnected(request.Payload.PeerId, IsResume: true));
    }

    private async ValueTask HandleHeartbeatAsync(
        ConnectionId connectionId,
        string json,
        CancellationToken cancellationToken)
    {
        var parsed = ProtocolJson.Read<HeartbeatPayload>(json, ProtocolMessageTypes.Heartbeat);
        if (!parsed.IsSuccess || !_continuity.RecordHeartbeat(connectionId))
        {
            await TryDisconnectAsync(connectionId, TransportCloseReason.Faulted, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleDisconnectAsync(
        ConnectionId connectionId,
        string json,
        CancellationToken cancellationToken)
    {
        var parsed = ProtocolJson.Read<DisconnectPayload>(json, ProtocolMessageTypes.Disconnect);
        if (!parsed.IsSuccess)
        {
            await TryDisconnectAsync(connectionId, TransportCloseReason.Faulted, cancellationToken).ConfigureAwait(false);
            return;
        }

        MarkDisconnected(connectionId);
        await TryDisconnectAsync(connectionId, TransportCloseReason.Normal, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleApplicationAsync(
        ConnectionId connectionId,
        string json,
        CancellationToken cancellationToken)
    {
        var peerId = _continuity.GetPeerId(connectionId);
        var parsed = ProtocolJson.Read<ApplicationMessagePayload>(json, ProtocolMessageTypes.ApplicationMessage);
        if (peerId is null || !parsed.IsSuccess || parsed.Message is null)
        {
            await TryDisconnectAsync(connectionId, TransportCloseReason.Faulted, cancellationToken).ConfigureAwait(false);
            return;
        }

        _continuity.RecordHeartbeat(connectionId);
        if (_deduplicator.TryAccept(peerId.Value, parsed.Message.MessageId))
        {
            _events.Writer.TryWrite(new ConnectionApplicationMessage(
                peerId.Value,
                parsed.Message.MessageId,
                parsed.Message.Payload.ApplicationType,
                parsed.Message.Payload.Data));
        }
    }

    private void MarkDisconnected(ConnectionId connectionId)
    {
        var result = _continuity.MarkDisconnected(connectionId);
        if (result.Status == DisconnectPeerStatus.Disconnected && result.PeerId is { } peerId)
        {
            _events.Writer.TryWrite(new ConnectionPeerDisconnected(peerId, CanResume: true));
        }
    }

    private async ValueTask RejectConnectAsync(
        ConnectionId connectionId,
        string correlationId,
        ConnectionRejectionCode code,
        string reason,
        CancellationToken cancellationToken)
    {
        var rejected = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ConnectRejected,
            NextMessageId(),
            new ConnectRejectedPayload(code, reason),
            correlationId);
        await SendProtocolAsync(connectionId, rejected, cancellationToken).ConfigureAwait(false);
        await TryDisconnectAsync(connectionId, TransportCloseReason.Normal, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask SendProtocolAsync<TPayload>(
        ConnectionId connectionId,
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            connectionId,
            Encoding.UTF8.GetBytes(ProtocolJson.Serialize(message)),
            cancellationToken);

    private async ValueTask TryDisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _transport.DisconnectAsync(connectionId, reason, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException exception) when (
            exception.Error.Code is TransportErrorCode.ConnectionNotFound or TransportErrorCode.TransportClosed)
        {
        }
    }

    private void EnsureRunning()
    {
        lock (_lifecycleGate)
        {
            if (_state != HostState.Running)
            {
                throw new InvalidOperationException($"Connection host is not running. Current state is '{_state}'.");
            }
        }
    }

    private string NextMessageId()
    {
        lock (_messageIdGate)
        {
            var value = _messageIdFactory()?.Trim();
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException("Message ID factory returned an empty identifier.")
                : value;
        }
    }

    private static string? TryReadMessageType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private enum HostState
    {
        Created,
        Running,
        Faulted,
        Stopping,
        Stopped,
        Disposed,
    }
}

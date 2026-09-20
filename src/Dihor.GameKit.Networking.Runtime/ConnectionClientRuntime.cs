using System.Text;
using System.Text.Json;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Runtime;

/// <summary>
/// Owns protocol-v2 connect, resume, heartbeat and reconnect behavior above a
/// transport-neutral client connector.
/// </summary>
public sealed class ConnectionClientRuntime : IAsyncDisposable
{
    private readonly IConnectionTransportConnector _connector;
    private readonly IConnectionResumeCredentialStore _credentialStore;
    private readonly ConnectionClientOptions _options;
    private readonly MessageIdDeduplicator _deduplicator;
    private readonly Func<string> _messageIdFactory;
    private readonly object _stateGate = new();
    private readonly object _messageIdGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private ConnectionClientState _state = ConnectionClientState.Idle;
    private PeerId? _peerId;
    private string? _credentialScope;
    private IMessageTransportClient? _client;
    private CancellationTokenSource? _runSource;
    private Task? _runTask;
    private bool _disposed;

    public ConnectionClientRuntime(
        IConnectionTransportConnector connector,
        IConnectionResumeCredentialStore credentialStore,
        ConnectionClientOptions? options = null,
        MessageIdDeduplicator? deduplicator = null,
        Func<string>? messageIdFactory = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(credentialStore);
        _connector = connector;
        _credentialStore = credentialStore;
        _options = options ?? ConnectionClientOptions.Default;
        _deduplicator = deduplicator ?? new MessageIdDeduplicator();
        _messageIdFactory = messageIdFactory ?? (() => $"dihor-client-{Guid.NewGuid():N}");
    }

    public event EventHandler<ConnectionClientState>? StateChanged;

    public event EventHandler<ConnectionClientApplicationMessage>? ApplicationMessageReceived;

    public event EventHandler<Exception>? ConnectionFailed;

    public ConnectionClientState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public async ValueTask ConnectAsync(
        PeerId peerId,
        string credentialScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialScope);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_runTask is not null)
            {
                throw new InvalidOperationException("Connection client is already running.");
            }

            var scope = credentialScope.Trim();
            SetState(ConnectionClientState.Connecting);
            IMessageTransportClient connectedClient;
            try
            {
                connectedClient = await OpenAsync(
                    peerId,
                    scope,
                    allowResume: true,
                    isReconnect: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SetState(ConnectionClientState.Faulted);
                throw;
            }

            var source = new CancellationTokenSource();
            lock (_stateGate)
            {
                _peerId = peerId;
                _credentialScope = scope;
                _client = connectedClient;
                _runSource = source;
                _state = ConnectionClientState.Connected;
                _runTask = RunAsync(peerId, scope, connectedClient, source.Token);
            }

            StateChanged?.Invoke(this, ConnectionClientState.Connected);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<string> SendApplicationAsync(
        string applicationType,
        JsonElement data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationType);
        if (data.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Application data cannot be undefined.", nameof(data));
        }

        var messageId = NextMessageId();
        var message = DihorGameKitNetworkingMessages.Create(
            ProtocolMessageTypes.ApplicationMessage,
            messageId,
            new ApplicationMessagePayload(applicationType, data));
        await SendProtocolAsync(GetConnectedClient(), message, cancellationToken).ConfigureAwait(false);
        return messageId;
    }

    public async ValueTask DisconnectAsync(
        bool clearResumeCredential = false,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runTask is null)
            {
                SetState(ConnectionClientState.Closed);
                return;
            }

            SetState(ConnectionClientState.Closing);
            var scope = _credentialScope;
            var activeClient = GetClientOrNull();
            if (activeClient is not null)
            {
                await TrySendDisconnectAsync(activeClient, cancellationToken).ConfigureAwait(false);
            }

            _runSource?.Cancel();
            await IgnoreExpectedCancellationAsync(_runTask).ConfigureAwait(false);

            if (clearResumeCredential && scope is not null)
            {
                await _credentialStore.ClearAsync(scope, cancellationToken).ConfigureAwait(false);
            }

            lock (_stateGate)
            {
                _peerId = null;
                _credentialScope = null;
                _client = null;
                _runTask = null;
                _runSource?.Dispose();
                _runSource = null;
                _state = ConnectionClientState.Closed;
            }

            StateChanged?.Invoke(this, ConnectionClientState.Closed);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        await DisconnectAsync(clearResumeCredential: false).ConfigureAwait(false);
    }

    private async Task RunAsync(
        PeerId peerId,
        string credentialScope,
        IMessageTransportClient initialClient,
        CancellationToken cancellationToken)
    {
        var currentClient = initialClient;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunConnectedSessionAsync(currentClient, peerId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // Session failures are reported and recovered by the reconnect loop.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    ConnectionFailed?.Invoke(this, exception);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await currentClient.DisposeAsync().ConfigureAwait(false);
                ClearClientIfCurrent(currentClient);
                SetState(ConnectionClientState.Reconnecting);
                currentClient = await ReconnectUntilAvailableAsync(
                    peerId,
                    credentialScope,
                    cancellationToken).ConfigureAwait(false);
                lock (_stateGate)
                {
                    _client = currentClient;
                }

                SetState(ConnectionClientState.Connected);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Terminal background failures are surfaced through ConnectionFailed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetState(ConnectionClientState.Faulted);
            ConnectionFailed?.Invoke(this, exception);
        }
        finally
        {
            await currentClient.DisposeAsync().ConfigureAwait(false);
            ClearClientIfCurrent(currentClient);
        }
    }

    private async Task RunConnectedSessionAsync(
        IMessageTransportClient activeClient,
        PeerId peerId,
        CancellationToken cancellationToken)
    {
        using var sessionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ReceiveLoopAsync(activeClient, peerId, sessionSource.Token);
        var heartbeatTask = HeartbeatLoopAsync(activeClient, peerId, sessionSource.Token);
        try
        {
            await receiveTask.ConfigureAwait(false);
        }
        finally
        {
            sessionSource.Cancel();
            await IgnoreExpectedCancellationAsync(heartbeatTask).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(
        IMessageTransportClient activeClient,
        PeerId peerId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await activeClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (received.IsClose)
            {
                return;
            }

            var json = Encoding.UTF8.GetString(received.Payload.Span);
            if (!string.Equals(TryReadMessageType(json), ProtocolMessageTypes.ApplicationMessage, StringComparison.Ordinal))
            {
                continue;
            }

            var parsed = ProtocolJson.Read<ApplicationMessagePayload>(json, ProtocolMessageTypes.ApplicationMessage);
            if (parsed.IsSuccess
                && parsed.Message is { } message
                && _deduplicator.TryAccept(peerId, message.MessageId))
            {
                ApplicationMessageReceived?.Invoke(
                    this,
                    new ConnectionClientApplicationMessage(
                        message.MessageId,
                        message.Payload.ApplicationType,
                        message.Payload.Data));
            }
        }
    }

    private async Task HeartbeatLoopAsync(
        IMessageTransportClient activeClient,
        PeerId peerId,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var message = DihorGameKitNetworkingMessages.Create(
                ProtocolMessageTypes.Heartbeat,
                NextMessageId(),
                new HeartbeatPayload(peerId));
            await SendProtocolAsync(activeClient, message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IMessageTransportClient> ReconnectUntilAvailableAsync(
        PeerId peerId,
        string credentialScope,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.ReconnectDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await OpenAsync(
                    peerId,
                    credentialScope,
                    allowResume: true,
                    isReconnect: true,
                    cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Every failed attempt is surfaced before retrying under cancellation.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                ConnectionFailed?.Invoke(this, exception);
            }
        }
    }

    private async Task<IMessageTransportClient> OpenAsync(
        PeerId peerId,
        string credentialScope,
        bool allowResume,
        bool isReconnect,
        CancellationToken cancellationToken)
    {
        var credential = allowResume
            ? await _credentialStore.ReadAsync(credentialScope, cancellationToken).ConfigureAwait(false)
            : null;
        var canResume = credential is not null
            && credential.PeerId == peerId
            && !string.IsNullOrWhiteSpace(credential.ResumeToken);
        if (credential is not null && !canResume)
        {
            await _credentialStore.ClearAsync(credentialScope, cancellationToken).ConfigureAwait(false);
        }

        var handshake = canResume
            ? ProtocolJson.Serialize(DihorGameKitNetworkingMessages.Create(
                ProtocolMessageTypes.ResumeRequest,
                NextMessageId(),
                new ResumeRequestPayload(peerId, credential!.ResumeToken)))
            : ProtocolJson.Serialize(DihorGameKitNetworkingMessages.Create(
                ProtocolMessageTypes.ConnectRequest,
                NextMessageId(),
                new ConnectRequestPayload(peerId)));
        var openedClient = await _connector
            .ConnectAsync(handshake, isReconnect, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var response = await openedClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsClose)
            {
                throw new TransportException(
                    $"Host closed the connection during handshake: {response.CloseDescription ?? "no reason"}.");
            }

            var json = Encoding.UTF8.GetString(response.Payload.Span);
            if (canResume)
            {
                var accepted = ProtocolJson.Read<ResumeAcceptedPayload>(json, ProtocolMessageTypes.ResumeAccepted);
                if (accepted.IsSuccess && accepted.Message is { } acceptedMessage)
                {
                    ValidateAcceptedPeer(peerId, acceptedMessage.Payload.PeerId);
                    await StoreResumeCredentialAsync(
                        credentialScope,
                        peerId,
                        acceptedMessage.Payload.ResumeToken,
                        cancellationToken).ConfigureAwait(false);
                    return openedClient;
                }

                var rejected = ProtocolJson.Read<ResumeRejectedPayload>(json, ProtocolMessageTypes.ResumeRejected);
                if (rejected.IsSuccess && rejected.Message is { } rejectedMessage)
                {
                    await _credentialStore.ClearAsync(credentialScope, cancellationToken).ConfigureAwait(false);
                    await openedClient.DisposeAsync().ConfigureAwait(false);
                    return await OpenAsync(
                        peerId,
                        credentialScope,
                        allowResume: false,
                        isReconnect,
                        cancellationToken).ConfigureAwait(false);
                }

                throw new InvalidOperationException("Host returned an invalid resume response.");
            }

            var connectAccepted = ProtocolJson.Read<ConnectAcceptedPayload>(json, ProtocolMessageTypes.ConnectAccepted);
            if (connectAccepted.IsSuccess && connectAccepted.Message is { } connectMessage)
            {
                if (connectMessage.Payload.PeerId is { } acceptedPeer)
                {
                    ValidateAcceptedPeer(peerId, acceptedPeer);
                }

                await StoreResumeCredentialAsync(
                    credentialScope,
                    peerId,
                    connectMessage.Payload.ResumeToken,
                    cancellationToken).ConfigureAwait(false);
                return openedClient;
            }

            var connectRejected = ProtocolJson.Read<ConnectRejectedPayload>(json, ProtocolMessageTypes.ConnectRejected);
            if (connectRejected.IsSuccess && connectRejected.Message is { } rejection)
            {
                throw new ConnectionRejectedException(rejection.Payload.Code, rejection.Payload.Reason);
            }

            throw new InvalidOperationException("Host returned an invalid connect response.");
        }
        catch
        {
            await openedClient.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask StoreResumeCredentialAsync(
        string scope,
        PeerId peerId,
        string? resumeToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resumeToken))
        {
            await _credentialStore.ClearAsync(scope, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _credentialStore.WriteAsync(
            new ConnectionResumeCredential(scope, peerId, resumeToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendProtocolAsync<TPayload>(
        IMessageTransportClient activeClient,
        ProtocolEnvelope<TPayload> message,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(message));
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await activeClient.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async ValueTask TrySendDisconnectAsync(
        IMessageTransportClient activeClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = DihorGameKitNetworkingMessages.Create(
                ProtocolMessageTypes.Disconnect,
                NextMessageId(),
                new DisconnectPayload());
            await SendProtocolAsync(activeClient, message, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Disconnect remains best effort while local shutdown continues.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private IMessageTransportClient GetConnectedClient()
    {
        lock (_stateGate)
        {
            if (_state != ConnectionClientState.Connected || _client is null)
            {
                throw new InvalidOperationException(
                    $"Connection client is not connected. Current state is '{_state}'.");
            }

            return _client;
        }
    }

    private IMessageTransportClient? GetClientOrNull()
    {
        lock (_stateGate)
        {
            return _client;
        }
    }

    private void ClearClientIfCurrent(IMessageTransportClient activeClient)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(_client, activeClient))
            {
                _client = null;
            }
        }
    }

    private void SetState(ConnectionClientState next)
    {
        bool changed;
        lock (_stateGate)
        {
            changed = _state != next;
            _state = next;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, next);
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

    private static void ValidateAcceptedPeer(PeerId requestedPeer, PeerId acceptedPeer)
    {
        if (requestedPeer != acceptedPeer)
        {
            throw new InvalidOperationException(
                $"Host accepted peer '{acceptedPeer}' while client requested '{requestedPeer}'.");
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

    private static async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

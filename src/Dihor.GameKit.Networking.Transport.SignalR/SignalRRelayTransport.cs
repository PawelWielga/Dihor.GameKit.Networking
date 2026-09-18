using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Transport.SignalR;

public sealed class SignalRRelayOptions
{
    public SignalRRelayOptions(
        Uri endpoint,
        ChannelId channelId,
        int maxMessageBytes = 256 * 1024)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "SignalR relay endpoint must be an absolute http or https URI.",
                nameof(endpoint));
        }

        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMessageBytes),
                maxMessageBytes,
                "Message limit must be positive.");
        }

        Endpoint = endpoint;
        ChannelId = channelId;
        MaxMessageBytes = maxMessageBytes;
    }

    public Uri Endpoint { get; }

    public ChannelId ChannelId { get; }

    public int MaxMessageBytes { get; }

    public Action<HttpConnectionOptions>? ConfigureConnection { get; init; }
}

public sealed class SignalRRelayTransport : IMessageTransport
{
    private readonly SignalRRelayOptions _options;
    private readonly HubConnection _connection;
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private readonly HashSet<ConnectionId> _connections = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly object _gate = new();
    private readonly object _stopGate = new();
    private Task? _stopTask;
    private int _unavailable;
    private int _stopping;
    private int _disposed;

    private SignalRRelayTransport(
        SignalRRelayOptions options,
        HubConnection connection)
    {
        _options = options;
        _connection = connection;
        _subscriptions.Add(_connection.On<string, byte[]>(
            SignalRRelayMethods.PeerConnected,
            HandlePeerConnected));
        _subscriptions.Add(_connection.On<string, byte[]>(
            SignalRRelayMethods.PeerMessage,
            HandlePeerMessage));
        _subscriptions.Add(_connection.On<string, string>(
            SignalRRelayMethods.PeerDisconnected,
            HandlePeerDisconnected));
        _connection.Closed += HandleRelayClosedAsync;
    }

    public ChannelId ChannelId => _options.ChannelId;

    public int ConnectionCount
    {
        get
        {
            lock (_gate)
            {
                return _connections.Count;
            }
        }
    }

    public static async Task<SignalRRelayTransport> StartAsync(
        SignalRRelayOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new HubConnectionBuilder().WithUrl(
            options.Endpoint,
            httpOptions => options.ConfigureConnection?.Invoke(httpOptions));
        var connection = builder.Build();
        var transport = new SignalRRelayTransport(options, connection);
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            await connection.InvokeAsync(
                    SignalRRelayMethods.RegisterListener,
                    options.ChannelId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            return transport;
        }
        catch
        {
            await transport.DisposeConnectionAfterFailedStartAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var transportEvent in _events.Reader
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return transportEvent;
        }
    }

    public async ValueTask SendAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        ThrowIfTooLarge(payload);
        EnsureConnectionExists(connectionId);

        try
        {
            await _connection.InvokeAsync(
                    SignalRRelayMethods.SendToClient,
                    connectionId.Value,
                    payload.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateDeliveryException(connectionId, exception);
        }
    }

    public async ValueTask BroadcastAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        ThrowIfTooLarge(payload);

        try
        {
            await _connection.InvokeAsync(
                    SignalRRelayMethods.Broadcast,
                    payload.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException("SignalR relay broadcast failed.", exception);
        }
    }

    public async ValueTask DisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason = TransportCloseReason.Normal,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        EnsureConnectionExists(connectionId);

        try
        {
            await _connection.InvokeAsync(
                    SignalRRelayMethods.DisconnectClient,
                    connectionId.Value,
                    CloseReasonCodec.Serialize(reason),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateDeliveryException(connectionId, exception);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task stopTask;
        lock (_stopGate)
        {
            stopTask = _stopTask ??= StopCoreAsync();
        }

        await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void HandlePeerConnected(string rawConnectionId, byte[] handshake)
    {
        if (Volatile.Read(ref _stopping) != 0 || Volatile.Read(ref _unavailable) != 0)
        {
            return;
        }

        ConnectionId connectionId;
        try
        {
            connectionId = new ConnectionId(rawConnectionId);
        }
        catch (ArgumentException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
            _ = RejectPeerAsync(rawConnectionId, "invalid-handshake");
            return;
        }

        if (handshake.Length > _options.MaxMessageBytes)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(
                    TransportErrorCode.DeliveryFailed,
                    $"SignalR handshake exceeds the configured limit of {_options.MaxMessageBytes} bytes.",
                    connectionId)));
            _ = RejectPeerAsync(rawConnectionId, "message-too-large");
            return;
        }

        var validation = ValidateHandshake(handshake);
        if (!validation.Accepted)
        {
            _ = RejectPeerAsync(rawConnectionId, validation.Reason);
            return;
        }

        lock (_gate)
        {
            if (!_connections.Add(connectionId))
            {
                _events.Writer.TryWrite(new TransportFaulted(
                    new TransportError(
                        TransportErrorCode.ConnectionAlreadyOpen,
                        $"Connection '{connectionId}' is already open.",
                        connectionId)));
                _ = RejectPeerAsync(rawConnectionId, "connection-already-open");
                return;
            }
        }

        _events.Writer.TryWrite(new TransportConnectionOpened(connectionId));
        _events.Writer.TryWrite(new TransportMessageReceived(
            connectionId,
            new ReadOnlyMemory<byte>(handshake.ToArray())));
    }

    private void HandlePeerMessage(string rawConnectionId, byte[] payload)
    {
        ConnectionId connectionId;
        try
        {
            connectionId = new ConnectionId(rawConnectionId);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!ContainsConnection(connectionId))
        {
            return;
        }

        if (payload.Length > _options.MaxMessageBytes)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(
                    TransportErrorCode.DeliveryFailed,
                    $"SignalR relay message exceeds the configured limit of {_options.MaxMessageBytes} bytes.",
                    connectionId)));
            _ = RejectPeerAsync(rawConnectionId, "message-too-large");
            return;
        }

        _events.Writer.TryWrite(new TransportMessageReceived(
            connectionId,
            new ReadOnlyMemory<byte>(payload.ToArray())));
    }

    private void HandlePeerDisconnected(string rawConnectionId, string reason)
    {
        ConnectionId connectionId;
        try
        {
            connectionId = new ConnectionId(rawConnectionId);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!RemoveConnection(connectionId))
        {
            return;
        }

        _events.Writer.TryWrite(new TransportConnectionClosed(
            connectionId,
            CloseReasonCodec.Parse(reason)));
    }

    private Task HandleRelayClosedAsync(Exception? exception)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref _unavailable, 1) != 0)
        {
            return Task.CompletedTask;
        }

        foreach (var connectionId in RemoveAllConnections())
        {
            _events.Writer.TryWrite(new TransportConnectionClosed(
                connectionId,
                TransportCloseReason.Faulted));
        }

        var message = exception is null
            ? "SignalR relay connection closed unexpectedly."
            : $"SignalR relay connection closed unexpectedly: {exception.Message}";
        _events.Writer.TryWrite(new TransportFaulted(
            new TransportError(TransportErrorCode.DeliveryFailed, message)));
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }

    private async Task RejectPeerAsync(string rawConnectionId, string reason)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _connection.InvokeAsync(
                    SignalRRelayMethods.DisconnectClient,
                    rawConnectionId,
                    reason,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _stopping) == 0 && Volatile.Read(ref _unavailable) == 0)
            {
                _events.Writer.TryWrite(new TransportFaulted(
                    new TransportError(
                        TransportErrorCode.DeliveryFailed,
                        $"Failed to reject SignalR relay connection '{rawConnectionId}': {exception.Message}")));
            }
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        try
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await _connection.InvokeAsync(
                            SignalRRelayMethods.UnregisterListener,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            foreach (var connectionId in RemoveAllConnections())
            {
                _events.Writer.TryWrite(new TransportConnectionClosed(
                    connectionId,
                    TransportCloseReason.TransportStopped));
            }

            if (_connection.State != HubConnectionState.Disconnected)
            {
                try
                {
                    await _connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _unavailable, 1);
            _events.Writer.TryComplete();
        }
    }

    private async Task DisposeConnectionAfterFailedStartAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        Interlocked.Exchange(ref _unavailable, 1);
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        try
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                await _connection.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EnsureConnectionExists(ConnectionId connectionId)
    {
        if (!ContainsConnection(connectionId))
        {
            throw new TransportException(new TransportError(
                TransportErrorCode.ConnectionNotFound,
                $"Connection '{connectionId}' is not open.",
                connectionId));
        }
    }

    private bool ContainsConnection(ConnectionId connectionId)
    {
        lock (_gate)
        {
            return _connections.Contains(connectionId);
        }
    }

    private bool RemoveConnection(ConnectionId connectionId)
    {
        lock (_gate)
        {
            return _connections.Remove(connectionId);
        }
    }

    private ConnectionId[] RemoveAllConnections()
    {
        lock (_gate)
        {
            var result = _connections.ToArray();
            _connections.Clear();
            return result;
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _stopping) != 0 ||
            Volatile.Read(ref _unavailable) != 0 ||
            _connection.State != HubConnectionState.Connected)
        {
            throw new TransportException(new TransportError(
                TransportErrorCode.TransportClosed,
                "SignalR relay transport is not connected."));
        }
    }

    private void ThrowIfTooLarge(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > _options.MaxMessageBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"Message exceeds the configured SignalR relay limit of {_options.MaxMessageBytes} bytes.");
        }
    }

    private static TransportException CreateDeliveryException(
        ConnectionId connectionId,
        Exception exception) =>
        new(
            new TransportError(
                TransportErrorCode.DeliveryFailed,
                $"SignalR relay delivery to '{connectionId}' failed: {exception.Message}",
                connectionId));

    private static HandshakeValidation ValidateHandshake(byte[] payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("type", out var typeProperty) ||
                typeProperty.ValueKind != JsonValueKind.String)
            {
                return new(false, "invalid-handshake");
            }

            var type = typeProperty.GetString();
            if (type == ProtocolMessageTypes.ConnectRequest)
            {
                var result = ProtocolJson.Read<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest);
                return ToHandshakeValidation(result.Error);
            }

            if (type == ProtocolMessageTypes.ResumeRequest)
            {
                var result = ProtocolJson.Read<ResumeRequestPayload>(json, ProtocolMessageTypes.ResumeRequest);
                return ToHandshakeValidation(result.Error);
            }

            return new(false, "invalid-handshake");
        }
        catch (JsonException)
        {
            return new(false, "invalid-handshake");
        }
    }

    private static HandshakeValidation ToHandshakeValidation(ProtocolReadError error) =>
        error switch
        {
            ProtocolReadError.None => new(true, string.Empty),
            ProtocolReadError.ProtocolVersionMismatch => new(false, "protocol-version-mismatch"),
            _ => new(false, "invalid-handshake"),
        };

    private readonly record struct HandshakeValidation(bool Accepted, string Reason);
}

internal static class SignalRRelayMethods
{
    public const string RegisterListener = "RegisterListener";
    public const string AttachClient = "AttachClient";
    public const string SendFromClient = "SendFromClient";
    public const string SendToClient = "SendToClient";
    public const string Broadcast = "Broadcast";
    public const string DisconnectClient = "DisconnectClient";
    public const string DetachClient = "DetachClient";
    public const string UnregisterListener = "UnregisterListener";
    public const string PeerConnected = "PartyGameKit.PeerConnected";
    public const string PeerMessage = "PartyGameKit.PeerMessage";
    public const string PeerDisconnected = "PartyGameKit.PeerDisconnected";
    public const string ClientMessage = "PartyGameKit.Message";
    public const string ClientDisconnected = "PartyGameKit.Disconnected";
}

internal static class CloseReasonCodec
{
    public static string Serialize(TransportCloseReason reason) =>
        reason switch
        {
            TransportCloseReason.Normal => "normal",
            TransportCloseReason.RemoteClosed => "remote-closed",
            TransportCloseReason.Timeout => "timeout",
            TransportCloseReason.Replaced => "replaced",
            TransportCloseReason.TransportStopped => "transport-stopped",
            TransportCloseReason.Faulted => "faulted",
            _ => "normal",
        };

    public static TransportCloseReason Parse(string? reason) =>
        reason switch
        {
            "normal" => TransportCloseReason.Normal,
            "timeout" => TransportCloseReason.Timeout,
            "replaced" => TransportCloseReason.Replaced,
            "transport-stopped" => TransportCloseReason.TransportStopped,
            "faulted" => TransportCloseReason.Faulted,
            _ => TransportCloseReason.RemoteClosed,
        };
}

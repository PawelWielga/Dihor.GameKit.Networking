using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.Lan;

public sealed class LanWebSocketTransport : IMessageTransport
{
    private const string ProtocolMismatchReason = "protocol-version-mismatch";
    private const string InvalidHandshakeReason = "invalid-handshake";
    private readonly LanWebSocketHostOptions _options;
    private readonly Func<string> _connectionIdFactory;
    private readonly ConcurrentDictionary<ConnectionId, LanConnection> _connections = new();
    private readonly ConcurrentQueue<LanConnection> _connectionOrder = new();
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _stopSource = new();
    private readonly object _stopGate = new();
    private WebApplication? _application;
    private Task? _stopTask;
    private int _stopped;
    private int _disposed;

    private LanWebSocketTransport(
        LanWebSocketHostOptions options,
        Func<string>? connectionIdFactory)
    {
        _options = options;
        _connectionIdFactory = connectionIdFactory ?? (() => $"lan-{Guid.NewGuid():N}");
    }

    public int BoundPort { get; private set; }

    public string Path => _options.Path;

    public int ConnectionCount => _connections.Count;

    public static async Task<LanWebSocketTransport> StartAsync(
        LanWebSocketHostOptions options,
        Func<string>? connectionIdFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var transport = new LanWebSocketTransport(options, connectionIdFactory);
        try
        {
            await transport.StartCoreAsync(cancellationToken).ConfigureAwait(false);
            return transport;
        }
        catch
        {
            transport._stopSource.Dispose();
            throw;
        }
    }

    public Uri CreateClientUri(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (BoundPort == 0)
        {
            throw new InvalidOperationException("LAN transport has not started.");
        }

        return new UriBuilder("ws", host.Trim(), BoundPort, _options.Path).Uri;
    }

    public async IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var transportEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return transportEvent;
        }
    }

    public async ValueTask SendAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        ThrowIfTooLarge(payload);
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw CreateException(
                TransportErrorCode.ConnectionNotFound,
                $"Connection '{connectionId}' is not open.",
                connectionId);
        }

        await SendCoreAsync(connection, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask BroadcastAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        ThrowIfTooLarge(payload);
        foreach (var connection in _connections.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCurrent(connection))
            {
                await SendCoreAsync(connection, payload, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason = TransportCloseReason.Normal,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw CreateException(
                TransportErrorCode.ConnectionNotFound,
                $"Connection '{connectionId}' is not open.",
                connectionId);
        }

        if (RemoveConnection(connection, reason))
        {
            await CloseSocketAsync(
                connection.Socket,
                CloseStatusFor(reason),
                CloseDescriptionFor(reason),
                cancellationToken).ConfigureAwait(false);
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
            _stopSource.Dispose();
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LanWebSocketTransport).Assembly.FullName,
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(_options.BindAddress, _options.Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
        });

        var application = builder.Build();
        application.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = _options.KeepAliveInterval,
        });
        application.Run(HandleRequestAsync);

        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            var server = application.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var boundAddress = addresses?.FirstOrDefault();
            if (boundAddress is null ||
                !Uri.TryCreate(boundAddress, UriKind.Absolute, out var uri) ||
                uri.Port <= 0)
            {
                throw new InvalidOperationException("Kestrel did not report a bound LAN endpoint.");
            }

            BoundPort = uri.Port;
            _application = application;
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        try
        {
            foreach (var connection in _connectionOrder.ToArray())
            {
                if (!RemoveConnection(connection, TransportCloseReason.TransportStopped))
                {
                    continue;
                }

                await CloseSocketAsync(
                    connection.Socket,
                    WebSocketCloseStatus.NormalClosure,
                    "transport-stopped",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _stopSource.Cancel();
            var application = _application;
            if (application is not null)
            {
                try
                {
                    await application.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                    _application = null;
                }
            }

            _events.Writer.TryComplete();
        }
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (context.Request.Path != _options.Path)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        try
        {
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted,
                _stopSource.Token);
            handshakeTimeout.CancelAfter(_options.HandshakeTimeout);

            LanWebSocketFrame handshake;
            try
            {
                handshake = await LanWebSocketMessageReader.ReadAsync(
                    socket,
                    _options.MaxMessageBytes,
                    handshakeTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!_stopSource.IsCancellationRequested)
            {
                await CloseSocketAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    "handshake-timeout",
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (LanMessageTooLargeException)
            {
                await CloseSocketAsync(
                    socket,
                    WebSocketCloseStatus.MessageTooBig,
                    "message-too-large",
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (handshake.IsClose() || handshake.MessageType != WebSocketMessageType.Text)
            {
                await CloseSocketAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    InvalidHandshakeReason,
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var validation = ValidateHandshake(handshake.Payload);
            if (!validation.Accepted)
            {
                await CloseSocketAsync(
                    socket,
                    WebSocketCloseStatus.PolicyViolation,
                    validation.Reason,
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var connection = AddConnection(socket);
            _events.Writer.TryWrite(new TransportConnectionOpened(connection.ConnectionId));
            _events.Writer.TryWrite(new TransportMessageReceived(
                connection.ConnectionId,
                new ReadOnlyMemory<byte>(handshake.Payload.ToArray())));

            await ReceiveLoopAsync(connection, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _stopSource.IsCancellationRequested || context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (LanMessageTooLargeException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
            await CloseSocketAsync(
                socket,
                WebSocketCloseStatus.MessageTooBig,
                "message-too-large",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
        }
    }

    private async Task ReceiveLoopAsync(LanConnection connection, CancellationToken requestAborted)
    {
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestAborted,
            _stopSource.Token);
        var closeReason = TransportCloseReason.RemoteClosed;
        try
        {
            while (IsCurrent(connection) &&
                   connection.Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var message = await LanWebSocketMessageReader.ReadAsync(
                    connection.Socket,
                    _options.MaxMessageBytes,
                    receiveCancellation.Token).ConfigureAwait(false);
                if (message.IsClose())
                {
                    await CloseSocketAsync(
                        connection.Socket,
                        WebSocketCloseStatus.NormalClosure,
                        "remote-closed",
                        CancellationToken.None).ConfigureAwait(false);
                    break;
                }

                if (!IsCurrent(connection))
                {
                    break;
                }

                _events.Writer.TryWrite(new TransportMessageReceived(
                    connection.ConnectionId,
                    new ReadOnlyMemory<byte>(message.Payload.ToArray())));
            }
        }
        catch (OperationCanceledException) when (
            _stopSource.IsCancellationRequested || requestAborted.IsCancellationRequested)
        {
            closeReason = Volatile.Read(ref _stopped) != 0
                ? TransportCloseReason.TransportStopped
                : TransportCloseReason.RemoteClosed;
        }
        catch (LanMessageTooLargeException exception)
        {
            closeReason = TransportCloseReason.Faulted;
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(
                    TransportErrorCode.DeliveryFailed,
                    exception.Message,
                    connection.ConnectionId)));
            await CloseSocketAsync(
                connection.Socket,
                WebSocketCloseStatus.MessageTooBig,
                "message-too-large",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException exception)
        {
            closeReason = TransportCloseReason.Faulted;
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(
                    TransportErrorCode.DeliveryFailed,
                    exception.Message,
                    connection.ConnectionId)));
        }
        finally
        {
            RemoveConnection(connection, closeReason);
        }
    }

    private LanConnection AddConnection(WebSocket socket)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var rawId = _connectionIdFactory()?.Trim();
            if (string.IsNullOrWhiteSpace(rawId))
            {
                throw new InvalidOperationException("Connection ID factory returned an empty identifier.");
            }

            var connection = new LanConnection(new ConnectionId(rawId), socket);
            if (_connections.TryAdd(connection.ConnectionId, connection))
            {
                _connectionOrder.Enqueue(connection);
                return connection;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique LAN connection identifier.");
    }

    private bool RemoveConnection(LanConnection connection, TransportCloseReason reason)
    {
        if (!_connections.TryGetValue(connection.ConnectionId, out var current) ||
            !ReferenceEquals(current, connection))
        {
            return false;
        }

        if (!_connections.TryRemove(
                new KeyValuePair<ConnectionId, LanConnection>(connection.ConnectionId, connection)))
        {
            return false;
        }

        _events.Writer.TryWrite(new TransportConnectionClosed(connection.ConnectionId, reason));
        return true;
    }

    private bool IsCurrent(LanConnection connection) =>
        _connections.TryGetValue(connection.ConnectionId, out var current) &&
        ReferenceEquals(current, connection);

    private async ValueTask SendCoreAsync(
        LanConnection connection,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        await connection.SendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(connection) || connection.Socket.State != WebSocketState.Open)
            {
                throw CreateException(
                    TransportErrorCode.ConnectionNotFound,
                    $"Connection '{connection.ConnectionId}' is not open.",
                    connection.ConnectionId);
            }

            await connection.Socket.SendAsync(
                payload,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is WebSocketException or InvalidOperationException)
        {
            throw new TransportException(
                $"Unable to deliver to connection '{connection.ConnectionId}'.",
                exception);
        }
        finally
        {
            connection.SendGate.Release();
        }
    }

    private void ThrowIfTooLarge(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > _options.MaxMessageBytes)
        {
            throw new TransportException(new TransportError(
                TransportErrorCode.DeliveryFailed,
                $"Message exceeds the configured limit of {_options.MaxMessageBytes} bytes."));
        }
    }

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw CreateException(TransportErrorCode.TransportClosed, "The LAN transport is stopped.");
        }
    }

    private static HandshakeValidation ValidateHandshake(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(payload.Span);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("protocolVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var protocolVersion))
            {
                return new(false, InvalidHandshakeReason);
            }

            if (protocolVersion != ProtocolVersions.Current)
            {
                return new(false, ProtocolMismatchReason);
            }

            return typeElement.GetString() switch
            {
                ProtocolMessageTypes.ConnectRequest =>
                    ProtocolJson.Read<ConnectRequestPayload>(json, ProtocolMessageTypes.ConnectRequest).IsSuccess
                        ? new(true, string.Empty)
                        : new(false, InvalidHandshakeReason),
                ProtocolMessageTypes.ResumeRequest => IsValidResumeRequest(json)
                    ? new(true, string.Empty)
                    : new(false, InvalidHandshakeReason),
                _ => new(false, InvalidHandshakeReason),
            };
        }
        catch (JsonException)
        {
            return new(false, InvalidHandshakeReason);
        }
    }

    private static bool IsValidResumeRequest(string json)
    {
        var result = ProtocolJson.Read<ResumeRequestPayload>(json, ProtocolMessageTypes.ResumeRequest);
        return result.IsSuccess &&
               result.Message?.Payload is { } resume &&
               !string.IsNullOrWhiteSpace(resume.PeerId.Value) &&
               !string.IsNullOrWhiteSpace(resume.ResumeToken);
    }

    private static TransportException CreateException(
        TransportErrorCode code,
        string message,
        ConnectionId? connectionId = null) =>
        new(new TransportError(code, message, connectionId));

    private static WebSocketCloseStatus CloseStatusFor(TransportCloseReason reason) => reason switch
    {
        TransportCloseReason.Faulted => WebSocketCloseStatus.InternalServerError,
        TransportCloseReason.Timeout => WebSocketCloseStatus.EndpointUnavailable,
        _ => WebSocketCloseStatus.NormalClosure,
    };

    private static string CloseDescriptionFor(TransportCloseReason reason) => reason switch
    {
        TransportCloseReason.Timeout => "timeout",
        TransportCloseReason.Replaced => "replaced",
        TransportCloseReason.Faulted => "faulted",
        TransportCloseReason.TransportStopped => "transport-stopped",
        _ => "closed",
    };

    private static async ValueTask CloseSocketAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(status, description, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class LanConnection(ConnectionId connectionId, WebSocket socket)
    {
        public ConnectionId ConnectionId { get; } = connectionId;

        public WebSocket Socket { get; } = socket;

        public SemaphoreSlim SendGate { get; } = new(1, 1);
    }

    private sealed record HandshakeValidation(bool Accepted, string Reason);
}

internal static class LanWebSocketFrameExtensions
{
    public static bool IsClose(this LanWebSocketFrame frame) =>
        frame.MessageType == WebSocketMessageType.Close;
}

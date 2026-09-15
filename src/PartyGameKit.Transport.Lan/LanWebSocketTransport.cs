using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.Lan;

public sealed class LanWebSocketTransport : IMessageTransport
{
    private const string ProtocolMismatchReason = "protocol-version-mismatch";
    private const string InvalidHandshakeReason = "invalid-handshake";
    private const int MaxHttpHeaderBytes = 16 * 1024;
    private const string WebSocketMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly LanWebSocketHostOptions _options;
    private readonly Func<string> _connectionIdFactory;
    private readonly ConcurrentDictionary<ConnectionId, LanConnection> _connections = new();
    private readonly ConcurrentQueue<LanConnection> _connectionOrder = new();
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _stopSource = new();
    private readonly object _stopGate = new();
    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private Task? _stopTask;
    private long _clientTaskSequence;
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
        EnsureStarted();
        return new UriBuilder("ws", host.Trim(), BoundPort, _options.Path).Uri;
    }

    public ConnectionDescriptor CreateConnectionDescriptor(
        string advertisedHost,
        ChannelId? channelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(advertisedHost);
        EnsureStarted();
        return LanConnectionDescriptor.Create(
            advertisedHost,
            BoundPort,
            channelId,
            _options.Path);
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

    private Task StartCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var listener = new TcpListener(_options.BindAddress, _options.Port);
        try
        {
            listener.Start();
            cancellationToken.ThrowIfCancellationRequested();

            if (listener.LocalEndpoint is not IPEndPoint endpoint || endpoint.Port <= 0)
            {
                throw new InvalidOperationException("TCP listener did not report a bound LAN endpoint.");
            }

            BoundPort = endpoint.Port;
            _listener = listener;
            _acceptLoopTask = AcceptLoopAsync(listener, _stopSource.Token);
            return Task.CompletedTask;
        }
        catch
        {
            listener.Stop();
            throw;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _stopped) != 0)
            {
                break;
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _stopped) != 0)
            {
                break;
            }
            catch (SocketException exception)
            {
                _events.Writer.TryWrite(new TransportFaulted(
                    new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
                break;
            }

            client.NoDelay = true;
            var sequence = Interlocked.Increment(ref _clientTaskSequence);
            var task = RunClientAsync(sequence, client, cancellationToken);
            _clientTasks.TryAdd(sequence, task);
        }
    }

    private async Task RunClientAsync(
        long sequence,
        TcpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
            _clientTasks.TryRemove(sequence, out _);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        try
        {
            using var httpTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopSource.Token);
            httpTimeout.CancelAfter(_options.HandshakeTimeout);

            var request = await ReadHttpUpgradeRequestAsync(stream, httpTimeout.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _stopped) != 0)
            {
                await WriteHttpErrorAsync(stream, 503, "Service Unavailable", CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            if (!string.Equals(request.Method, "GET", StringComparison.Ordinal))
            {
                await WriteHttpErrorAsync(stream, 405, "Method Not Allowed", httpTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            if (!string.Equals(request.Target, _options.Path, StringComparison.Ordinal))
            {
                await WriteHttpErrorAsync(stream, 404, "Not Found", httpTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            if (!IsValidWebSocketUpgrade(request))
            {
                await WriteHttpErrorAsync(stream, 400, "Bad Request", httpTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            var acceptKey = CreateWebSocketAcceptKey(request.Headers["Sec-WebSocket-Key"]);
            var upgradeResponse = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");
            await stream.WriteAsync(upgradeResponse, httpTimeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(httpTimeout.Token).ConfigureAwait(false);

            using var socket = WebSocket.CreateFromStream(
                stream,
                isServer: true,
                subProtocol: null,
                keepAliveInterval: _options.KeepAliveInterval);

            using var protocolHandshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopSource.Token);
            protocolHandshakeTimeout.CancelAfter(_options.HandshakeTimeout);

            LanWebSocketFrame handshake;
            try
            {
                handshake = await LanWebSocketMessageReader.ReadAsync(
                    socket,
                    _options.MaxMessageBytes,
                    protocolHandshakeTimeout.Token).ConfigureAwait(false);
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

            await ReceiveLoopAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            _stopSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpHeaderTooLargeException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
            await WriteHttpErrorAsync(stream, 431, "Request Header Fields Too Large", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            await WriteHttpErrorAsync(stream, 400, "Bad Request", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (LanMessageTooLargeException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
        }
        catch (WebSocketException) when (_stopSource.IsCancellationRequested)
        {
        }
        catch (WebSocketException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
        }
        catch (IOException) when (_stopSource.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
        }
        catch (SocketException) when (_stopSource.IsCancellationRequested)
        {
        }
        catch (SocketException exception)
        {
            _events.Writer.TryWrite(new TransportFaulted(
                new TransportError(TransportErrorCode.DeliveryFailed, exception.Message)));
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _stopSource.Cancel();

        var listener = Interlocked.Exchange(ref _listener, null);
        listener?.Stop();

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
                connection.Socket.Abort();
            }

            var acceptLoopTask = _acceptLoopTask;
            if (acceptLoopTask is not null)
            {
                await acceptLoopTask.ConfigureAwait(false);
            }

            var clientTasks = _clientTasks.Values.ToArray();
            if (clientTasks.Length > 0)
            {
                await Task.WhenAll(clientTasks).ConfigureAwait(false);
            }
        }
        finally
        {
            _events.Writer.TryComplete();
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
        catch (WebSocketException) when (_stopSource.IsCancellationRequested)
        {
            closeReason = TransportCloseReason.TransportStopped;
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

    private static async Task<HttpUpgradeRequest> ReadHttpUpgradeRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var singleByte = new byte[1];
        var delimiterState = 0;

        while (buffer.Length < MaxHttpHeaderBytes)
        {
            var read = await stream.ReadAsync(singleByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("Connection closed before the HTTP upgrade request completed.");
            }

            var value = singleByte[0];
            buffer.WriteByte(value);
            delimiterState = (delimiterState, value) switch
            {
                (0, (byte)'\r') => 1,
                (1, (byte)'\n') => 2,
                (2, (byte)'\r') => 3,
                (3, (byte)'\n') => 4,
                (_, (byte)'\r') => 1,
                _ => 0,
            };

            if (delimiterState == 4)
            {
                return ParseHttpUpgradeRequest(Encoding.ASCII.GetString(buffer.ToArray()));
            }
        }

        throw new HttpHeaderTooLargeException(MaxHttpHeaderBytes);
    }

    private static HttpUpgradeRequest ParseHttpUpgradeRequest(string rawRequest)
    {
        var lines = rawRequest.Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 2)
        {
            throw new InvalidDataException("Invalid HTTP upgrade request.");
        }

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid HTTP request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException("Invalid HTTP header.");
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (headers.TryGetValue(name, out var existing))
            {
                headers[name] = $"{existing},{value}";
            }
            else
            {
                headers.Add(name, value);
            }
        }

        return new HttpUpgradeRequest(requestLine[0], requestLine[1], headers);
    }

    private static bool IsValidWebSocketUpgrade(HttpUpgradeRequest request)
    {
        return request.Headers.TryGetValue("Upgrade", out var upgrade) &&
               string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase) &&
               request.Headers.TryGetValue("Connection", out var connection) &&
               HeaderContainsToken(connection, "Upgrade") &&
               request.Headers.TryGetValue("Sec-WebSocket-Version", out var version) &&
               string.Equals(version, "13", StringComparison.Ordinal) &&
               request.Headers.TryGetValue("Sec-WebSocket-Key", out var key) &&
               !string.IsNullOrWhiteSpace(key);
    }

    private static bool HeaderContainsToken(string headerValue, string token) =>
        headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => string.Equals(value, token, StringComparison.OrdinalIgnoreCase));

    private static string CreateWebSocketAcceptKey(string clientKey)
    {
#pragma warning disable CA5350 // RFC 6455 requires SHA-1 for Sec-WebSocket-Accept.
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(clientKey.Trim() + WebSocketMagic));
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
    }

    private static async Task WriteHttpErrorAsync(
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 0\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
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

    private void EnsureStarted()
    {
        if (BoundPort == 0)
        {
            throw new InvalidOperationException("LAN transport has not started.");
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

    private sealed record HttpUpgradeRequest(
        string Method,
        string Target,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class HttpHeaderTooLargeException(int maxHeaderBytes)
        : Exception($"HTTP WebSocket upgrade headers exceed the configured limit of {maxHeaderBytes} bytes.");
}

internal static class LanWebSocketFrameExtensions
{
    public static bool IsClose(this LanWebSocketFrame frame) =>
        frame.MessageType == WebSocketMessageType.Close;
}

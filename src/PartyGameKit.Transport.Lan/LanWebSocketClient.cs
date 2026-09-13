using System.Net.WebSockets;
using System.Text;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.Lan;

public sealed record LanWebSocketClientMessage(
    ReadOnlyMemory<byte> Payload,
    WebSocketMessageType MessageType,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseDescription = null)
{
    public bool IsClose => MessageType == WebSocketMessageType.Close;
}

public sealed class LanWebSocketClient : IMessageTransportClient
{
    private readonly ClientWebSocket _socket;
    private readonly int _maxMessageBytes;
    private int _disposed;

    private LanWebSocketClient(ClientWebSocket socket, int maxMessageBytes)
    {
        _socket = socket;
        _maxMessageBytes = maxMessageBytes;
    }

    public WebSocketState State => _socket.State;

    public static async Task<LanWebSocketClient> ConnectAsync(
        Uri endpoint,
        string handshakeJson,
        int maxMessageBytes = 256 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(handshakeJson);
        if (endpoint.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException("LAN WebSocket endpoint must use ws or wss.", nameof(endpoint));
        }

        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes), maxMessageBytes, "Message limit must be positive.");
        }

        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var handshakeBytes = Encoding.UTF8.GetBytes(handshakeJson);
            await socket.SendAsync(
                handshakeBytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
            return new LanWebSocketClient(socket, maxMessageBytes);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (payload.Length > _maxMessageBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, "Message exceeds the configured LAN message limit.");
        }

        return _socket.SendAsync(
            payload,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    public async ValueTask<LanWebSocketClientMessage> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return await LanWebSocketMessageReader.ReadClientMessageAsync(
            _socket,
            _maxMessageBytes,
            cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<ClientTransportMessage> IMessageTransportClient.ReceiveAsync(
        CancellationToken cancellationToken)
    {
        var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return new ClientTransportMessage(
            message.Payload,
            message.IsClose,
            message.CloseDescription);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "client-disposed",
                    closeTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _socket.Dispose();
        }
    }
}

internal static class LanWebSocketMessageReader
{
    public static async ValueTask<LanWebSocketClientMessage> ReadClientMessageAsync(
        WebSocket socket,
        int maxMessageBytes,
        CancellationToken cancellationToken)
    {
        var message = await ReadAsync(socket, maxMessageBytes, cancellationToken).ConfigureAwait(false);
        return new LanWebSocketClientMessage(
            message.Payload,
            message.MessageType,
            message.CloseStatus,
            message.CloseDescription);
    }

    public static async ValueTask<LanWebSocketFrame> ReadAsync(
        WebSocket socket,
        int maxMessageBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(8192, maxMessageBytes)];
        using var stream = new MemoryStream();
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new LanWebSocketFrame(
                    ReadOnlyMemory<byte>.Empty,
                    WebSocketMessageType.Close,
                    result.CloseStatus,
                    result.CloseStatusDescription);
            }

            messageType ??= result.MessageType;
            if (messageType != result.MessageType)
            {
                throw new WebSocketException("A fragmented WebSocket message changed message type.");
            }

            if (stream.Length + result.Count > maxMessageBytes)
            {
                throw new LanMessageTooLargeException(maxMessageBytes);
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            if (result.EndOfMessage)
            {
                return new LanWebSocketFrame(stream.ToArray(), messageType.Value);
            }
        }
    }
}

internal sealed record LanWebSocketFrame(
    ReadOnlyMemory<byte> Payload,
    WebSocketMessageType MessageType,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseDescription = null);

internal sealed class LanMessageTooLargeException(int maxMessageBytes)
    : Exception($"WebSocket message exceeds the configured limit of {maxMessageBytes} bytes.");

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.InMemory;

public sealed class InMemoryGameTransport : IGameTransport
{
    private readonly object _gate = new();
    private readonly Dictionary<ConnectionId, ConnectionState> _connections = new();
    private readonly List<ConnectionId> _connectionOrder = new();
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private bool _stopped;

    public ValueTask<InMemoryTransportPeer> OpenConnectionAsync(
        ConnectionId connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfStopped();
            if (_connections.ContainsKey(connectionId))
            {
                throw CreateException(
                    TransportErrorCode.ConnectionAlreadyOpen,
                    $"Connection '{connectionId}' is already open.",
                    connectionId);
            }

            var outbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    AllowSynchronousContinuations = false,
                    SingleReader = false,
                    SingleWriter = false,
                });
            _connections.Add(connectionId, new ConnectionState(outbound));
            _connectionOrder.Add(connectionId);
            _events.Writer.TryWrite(new TransportConnectionOpened(connectionId));
            return ValueTask.FromResult(new InMemoryTransportPeer(this, connectionId, outbound.Reader));
        }
    }

    public async IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var transportEvent in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return transportEvent;
        }
    }

    public ValueTask SendAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = CopyPayload(payload);

        lock (_gate)
        {
            ThrowIfStopped();
            if (!_connections.TryGetValue(connectionId, out var connection))
            {
                throw CreateException(
                    TransportErrorCode.ConnectionNotFound,
                    $"Connection '{connectionId}' is not open.",
                    connectionId);
            }

            if (!connection.Outbound.Writer.TryWrite(copy))
            {
                throw CreateException(
                    TransportErrorCode.DeliveryFailed,
                    $"Unable to deliver to connection '{connectionId}'.",
                    connectionId);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = payload.ToArray();

        lock (_gate)
        {
            ThrowIfStopped();
            foreach (var connectionId in _connectionOrder)
            {
                if (!_connections.TryGetValue(connectionId, out var connection))
                {
                    continue;
                }

                if (!connection.Outbound.Writer.TryWrite(new ReadOnlyMemory<byte>(bytes.ToArray())))
                {
                    throw CreateException(
                        TransportErrorCode.DeliveryFailed,
                        $"Unable to broadcast to connection '{connectionId}'.",
                        connectionId);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason = TransportCloseReason.Normal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseConnection(connectionId, reason, requireExisting: true);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_stopped)
            {
                return ValueTask.CompletedTask;
            }

            _stopped = true;
            foreach (var connectionId in _connectionOrder)
            {
                if (!_connections.Remove(connectionId, out var connection))
                {
                    continue;
                }

                connection.Outbound.Writer.TryComplete();
                _events.Writer.TryWrite(
                    new TransportConnectionClosed(connectionId, TransportCloseReason.TransportStopped));
            }

            _connectionOrder.Clear();
            _events.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync();

    internal ValueTask ReceiveFromPeerAsync(
        ConnectionId connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = CopyPayload(payload);

        lock (_gate)
        {
            ThrowIfStopped();
            if (!_connections.ContainsKey(connectionId))
            {
                throw CreateException(
                    TransportErrorCode.ConnectionNotFound,
                    $"Connection '{connectionId}' is not open.",
                    connectionId);
            }

            if (!_events.Writer.TryWrite(new TransportMessageReceived(connectionId, copy)))
            {
                throw CreateException(
                    TransportErrorCode.DeliveryFailed,
                    "Unable to publish an incoming transport message.",
                    connectionId);
            }
        }

        return ValueTask.CompletedTask;
    }

    internal ValueTask CloseFromPeerAsync(ConnectionId connectionId)
    {
        CloseConnection(connectionId, TransportCloseReason.RemoteClosed, requireExisting: false);
        return ValueTask.CompletedTask;
    }

    private void CloseConnection(
        ConnectionId connectionId,
        TransportCloseReason reason,
        bool requireExisting)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                if (requireExisting)
                {
                    ThrowIfStopped();
                }

                return;
            }

            if (!_connections.Remove(connectionId, out var connection))
            {
                if (requireExisting)
                {
                    throw CreateException(
                        TransportErrorCode.ConnectionNotFound,
                        $"Connection '{connectionId}' is not open.",
                        connectionId);
                }

                return;
            }

            _connectionOrder.Remove(connectionId);
            connection.Outbound.Writer.TryComplete();
            _events.Writer.TryWrite(new TransportConnectionClosed(connectionId, reason));
        }
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw CreateException(TransportErrorCode.TransportClosed, "The transport is stopped.");
        }
    }

    private static ReadOnlyMemory<byte> CopyPayload(ReadOnlyMemory<byte> payload) =>
        new(payload.ToArray());

    private static PartyGameTransportException CreateException(
        TransportErrorCode code,
        string message,
        ConnectionId? connectionId = null) =>
        new(new TransportError(code, message, connectionId));

    private sealed record ConnectionState(Channel<ReadOnlyMemory<byte>> Outbound);
}

public sealed class InMemoryTransportPeer : IAsyncDisposable
{
    private readonly InMemoryGameTransport _transport;
    private readonly ChannelReader<ReadOnlyMemory<byte>> _inbound;
    private int _disposed;

    internal InMemoryTransportPeer(
        InMemoryGameTransport transport,
        ConnectionId connectionId,
        ChannelReader<ReadOnlyMemory<byte>> inbound)
    {
        _transport = transport;
        ConnectionId = connectionId;
        _inbound = inbound;
    }

    public ConnectionId ConnectionId { get; }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.ReceiveFromPeerAsync(ConnectionId, payload, cancellationToken);
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var payload in _inbound.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return payload;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _transport.CloseFromPeerAsync(ConnectionId).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(InMemoryTransportPeer));
        }
    }
}

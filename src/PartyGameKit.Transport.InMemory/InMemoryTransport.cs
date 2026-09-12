using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.InMemory;

public sealed class InMemoryTransport : IMessageTransport
{
    private readonly object _gate = new();
    private readonly Dictionary<ConnectionId, InMemoryConnectionState> _connections = new();
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
            var state = new InMemoryConnectionState(outbound);
            _connections.Add(connectionId, state);
            _connectionOrder.Add(connectionId);
            _events.Writer.TryWrite(new TransportConnectionOpened(connectionId));
            return ValueTask.FromResult(new InMemoryTransportPeer(this, connectionId, state));
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
        InMemoryConnectionState state,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copy = CopyPayload(payload);

        lock (_gate)
        {
            ThrowIfStopped();
            if (!_connections.TryGetValue(connectionId, out var current) ||
                !ReferenceEquals(current, state))
            {
                throw CreateException(
                    TransportErrorCode.ConnectionNotFound,
                    $"Connection '{connectionId}' is no longer the active connection instance.",
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

    internal ValueTask CloseFromPeerAsync(
        ConnectionId connectionId,
        InMemoryConnectionState state)
    {
        CloseConnection(
            connectionId,
            TransportCloseReason.RemoteClosed,
            requireExisting: false,
            expectedState: state);
        return ValueTask.CompletedTask;
    }

    private void CloseConnection(
        ConnectionId connectionId,
        TransportCloseReason reason,
        bool requireExisting,
        InMemoryConnectionState? expectedState = null)
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

            if (!_connections.TryGetValue(connectionId, out var connection))
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

            if (expectedState is not null && !ReferenceEquals(connection, expectedState))
            {
                return;
            }

            _connections.Remove(connectionId);
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

    private static TransportException CreateException(
        TransportErrorCode code,
        string message,
        ConnectionId? connectionId = null) =>
        new(new TransportError(code, message, connectionId));
}

internal sealed class InMemoryConnectionState(
    Channel<ReadOnlyMemory<byte>> outbound)
{
    public Channel<ReadOnlyMemory<byte>> Outbound { get; } = outbound;
}

public sealed class InMemoryTransportPeer : IAsyncDisposable
{
    private readonly InMemoryTransport _transport;
    private readonly InMemoryConnectionState _state;
    private int _disposed;

    internal InMemoryTransportPeer(
        InMemoryTransport transport,
        ConnectionId connectionId,
        InMemoryConnectionState state)
    {
        _transport = transport;
        _state = state;
        ConnectionId = connectionId;
    }

    public ConnectionId ConnectionId { get; }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.ReceiveFromPeerAsync(ConnectionId, _state, payload, cancellationToken);
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var payload in _state.Outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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

        await _transport.CloseFromPeerAsync(ConnectionId, _state).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}

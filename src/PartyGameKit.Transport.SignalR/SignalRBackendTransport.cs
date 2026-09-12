using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;

namespace PartyGameKit.Transport.SignalR;

public interface IPartyGameKitSignalRClient
{
    Task Receive(string payloadBase64);

    Task Disconnect(string reason);
}

public sealed class PartyGameKitSignalRHub(SignalRRoomRegistry registry) : Hub<IPartyGameKitSignalRClient>
{
    public Task Send(string payloadBase64) =>
        registry.RouteMessageAsync(Context.ConnectionId, payloadBase64, Context.ConnectionAborted);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        registry.ConnectionClosed(Context.ConnectionId, exception);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
}

public static class PartyGameKitSignalRServiceCollectionExtensions
{
    public static IServiceCollection AddPartyGameKitSignalR(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSignalR();
        services.AddSingleton<SignalRRoomRegistry>();
        return services;
    }
}

public static class PartyGameKitSignalREndpointRouteBuilderExtensions
{
    public static HubEndpointConventionBuilder MapPartyGameKitSignalR(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/partygamekit")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapHub<PartyGameKitSignalRHub>(pattern);
    }
}

public sealed class SignalRRoomRegistry
{
    private readonly object _gate = new();
    private readonly IHubContext<PartyGameKitSignalRHub, IPartyGameKitSignalRClient> _hubContext;
    private readonly Dictionary<RoomId, SignalRRoomTransport> _roomsById = new();
    private readonly Dictionary<JoinCode, SignalRRoomTransport> _roomsByJoinCode = new();
    private readonly Dictionary<string, SignalRRoomTransport> _connectionRooms = new(StringComparer.Ordinal);

    public SignalRRoomRegistry(IHubContext<PartyGameKitSignalRHub, IPartyGameKitSignalRClient> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    public SignalRRoomTransport RegisterRoom(RoomId roomId, JoinCode joinCode)
    {
        lock (_gate)
        {
            if (_roomsById.ContainsKey(roomId))
            {
                throw new InvalidOperationException($"SignalR room '{roomId.Value}' is already registered.");
            }

            if (_roomsByJoinCode.ContainsKey(joinCode))
            {
                throw new InvalidOperationException($"SignalR join code '{joinCode.Value}' is already registered.");
            }

            var transport = new SignalRRoomTransport(this, _hubContext, roomId, joinCode);
            _roomsById.Add(roomId, transport);
            _roomsByJoinCode.Add(joinCode, transport);
            return transport;
        }
    }

    public bool TryGetRoom(RoomId roomId, out SignalRRoomTransport? transport)
    {
        lock (_gate)
        {
            return _roomsById.TryGetValue(roomId, out transport);
        }
    }

    internal async Task RouteMessageAsync(
        string hubConnectionId,
        string payloadBase64,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubConnectionId);
        ArgumentNullException.ThrowIfNull(payloadBase64);

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(payloadBase64);
        }
        catch (FormatException exception)
        {
            throw new HubException("PartyGameKit SignalR payload must be valid base64.", exception);
        }

        SignalRRoomTransport? transport;
        var newlyBound = false;
        lock (_gate)
        {
            _connectionRooms.TryGetValue(hubConnectionId, out transport);
        }

        if (transport is null)
        {
            transport = ResolveInitialRoom(payload)
                ?? throw new HubException("First PartyGameKit SignalR message must be a valid join or rejoin request for a registered room.");

            lock (_gate)
            {
                if (_connectionRooms.TryGetValue(hubConnectionId, out var existing))
                {
                    transport = existing;
                }
                else
                {
                    _connectionRooms.Add(hubConnectionId, transport);
                    newlyBound = true;
                }
            }
        }

        var connectionId = new ConnectionId(hubConnectionId);
        if (newlyBound)
        {
            transport.ConnectionOpened(connectionId);
        }

        transport.MessageReceived(connectionId, payload);
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void ConnectionClosed(string hubConnectionId, Exception? exception)
    {
        SignalRRoomTransport? transport;
        lock (_gate)
        {
            if (!_connectionRooms.Remove(hubConnectionId, out transport))
            {
                return;
            }
        }

        var connectionId = new ConnectionId(hubConnectionId);
        transport.ConnectionClosed(
            connectionId,
            exception is null ? TransportCloseReason.RemoteClosed : TransportCloseReason.Faulted);
    }

    internal bool IsBound(SignalRRoomTransport transport, ConnectionId connectionId)
    {
        lock (_gate)
        {
            return _connectionRooms.TryGetValue(connectionId.Value, out var bound) && ReferenceEquals(bound, transport);
        }
    }

    internal bool Unbind(SignalRRoomTransport transport, ConnectionId connectionId)
    {
        lock (_gate)
        {
            if (!_connectionRooms.TryGetValue(connectionId.Value, out var bound) || !ReferenceEquals(bound, transport))
            {
                return false;
            }

            return _connectionRooms.Remove(connectionId.Value);
        }
    }

    internal IReadOnlyList<ConnectionId> Unregister(SignalRRoomTransport transport)
    {
        lock (_gate)
        {
            if (!_roomsById.TryGetValue(transport.RoomId, out var registered) || !ReferenceEquals(registered, transport))
            {
                return Array.Empty<ConnectionId>();
            }

            _roomsById.Remove(transport.RoomId);
            _roomsByJoinCode.Remove(transport.JoinCode);

            var connectionIds = _connectionRooms
                .Where(pair => ReferenceEquals(pair.Value, transport))
                .Select(pair => new ConnectionId(pair.Key))
                .ToArray();
            foreach (var connectionId in connectionIds)
            {
                _connectionRooms.Remove(connectionId.Value);
            }

            return connectionIds;
        }
    }

    private SignalRRoomTransport? ResolveInitialRoom(ReadOnlySpan<byte> payload)
    {
        var json = Encoding.UTF8.GetString(payload);
        var join = ProtocolJson.Read<JoinRequestPayload>(json, ProtocolMessageTypes.JoinRequest);
        if (join.IsSuccess)
        {
            var request = join.Message!.Payload;
            lock (_gate)
            {
                return _roomsById.TryGetValue(request.RoomId, out var room) && room.JoinCode == request.JoinCode
                    ? room
                    : null;
            }
        }

        var rejoin = ProtocolJson.Read<RejoinRequestPayload>(json, ProtocolMessageTypes.RejoinRequest);
        if (rejoin.IsSuccess)
        {
            lock (_gate)
            {
                return _roomsById.GetValueOrDefault(rejoin.Message!.Payload.RoomId);
            }
        }

        return null;
    }
}

public sealed class SignalRRoomTransport : IGameTransport
{
    private readonly object _gate = new();
    private readonly SignalRRoomRegistry _registry;
    private readonly IHubContext<PartyGameKitSignalRHub, IPartyGameKitSignalRClient> _hubContext;
    private readonly Channel<TransportEvent> _events = Channel.CreateUnbounded<TransportEvent>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false,
        });
    private readonly HashSet<ConnectionId> _connections = new();
    private int _stopped;

    internal SignalRRoomTransport(
        SignalRRoomRegistry registry,
        IHubContext<PartyGameKitSignalRHub, IPartyGameKitSignalRClient> hubContext,
        RoomId roomId,
        JoinCode joinCode)
    {
        _registry = registry;
        _hubContext = hubContext;
        RoomId = roomId;
        JoinCode = joinCode;
    }

    public RoomId RoomId { get; }

    public JoinCode JoinCode { get; }

    public async IAsyncEnumerable<TransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
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
        if (!_registry.IsBound(this, connectionId))
        {
            throw CreateException(TransportErrorCode.ConnectionNotFound, connectionId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _hubContext.Clients.Client(connectionId.Value)
                .Receive(Convert.ToBase64String(payload.Span))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PartyGameTransportException(
                $"SignalR delivery to '{connectionId.Value}' failed.",
                exception);
        }
    }

    public async ValueTask BroadcastAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        ConnectionId[] connectionIds;
        lock (_gate)
        {
            connectionIds = _connections.ToArray();
        }

        if (connectionIds.Length == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _hubContext.Clients.Clients(connectionIds.Select(id => id.Value).ToArray())
                .Receive(Convert.ToBase64String(payload.Span))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PartyGameTransportException("SignalR broadcast failed.", exception);
        }
    }

    public async ValueTask DisconnectAsync(
        ConnectionId connectionId,
        TransportCloseReason reason = TransportCloseReason.Normal,
        CancellationToken cancellationToken = default)
    {
        ThrowIfStopped();
        if (!_registry.Unbind(this, connectionId))
        {
            throw CreateException(TransportErrorCode.ConnectionNotFound, connectionId);
        }

        RemoveConnection(connectionId);
        cancellationToken.ThrowIfCancellationRequested();
        await _hubContext.Clients.Client(connectionId.Value)
            .Disconnect(reason.ToString())
            .ConfigureAwait(false);
        _events.Writer.TryWrite(new TransportConnectionClosed(connectionId, reason));
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var connectionIds = _registry.Unregister(this);
        lock (_gate)
        {
            _connections.Clear();
        }

        foreach (var connectionId in connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _hubContext.Clients.Client(connectionId.Value)
                    .Disconnect(TransportCloseReason.TransportStopped.ToString())
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _events.Writer.TryWrite(new TransportFaulted(new TransportError(
                    TransportErrorCode.DeliveryFailed,
                    exception.Message,
                    connectionId)));
            }

            _events.Writer.TryWrite(new TransportConnectionClosed(connectionId, TransportCloseReason.TransportStopped));
        }

        _events.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal void ConnectionOpened(ConnectionId connectionId)
    {
        ThrowIfStopped();
        lock (_gate)
        {
            if (!_connections.Add(connectionId))
            {
                return;
            }
        }

        _events.Writer.TryWrite(new TransportConnectionOpened(connectionId));
    }

    internal void MessageReceived(ConnectionId connectionId, ReadOnlyMemory<byte> payload)
    {
        ThrowIfStopped();
        _events.Writer.TryWrite(new TransportMessageReceived(connectionId, payload.ToArray()));
    }

    internal void ConnectionClosed(ConnectionId connectionId, TransportCloseReason reason)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        if (RemoveConnection(connectionId))
        {
            _events.Writer.TryWrite(new TransportConnectionClosed(connectionId, reason));
        }
    }

    private bool RemoveConnection(ConnectionId connectionId)
    {
        lock (_gate)
        {
            return _connections.Remove(connectionId);
        }
    }

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new PartyGameTransportException(new TransportError(
                TransportErrorCode.TransportClosed,
                $"SignalR room '{RoomId.Value}' is stopped."));
        }
    }

    private static PartyGameTransportException CreateException(TransportErrorCode code, ConnectionId connectionId) =>
        new(new TransportError(code, $"SignalR connection '{connectionId.Value}' is not bound to this room.", connectionId));
}

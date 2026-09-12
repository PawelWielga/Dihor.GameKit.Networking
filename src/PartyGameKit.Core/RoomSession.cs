namespace PartyGameKit.Core;

public enum RoomSessionState
{
    Open,
    Closed,
}

public enum PlayerPresence
{
    Connected,
    Disconnected,
}

public enum SessionLifecycleEventKind
{
    SessionCreated,
    ClientConnected,
    ClientDisconnected,
    PlayerJoined,
    PlayerDisconnected,
    PlayerRejoined,
    PlayerLeft,
    SessionClosed,
}

public enum JoinPlayerStatus
{
    Accepted,
    RoomClosed,
    RoomFull,
    PlayerAlreadyExists,
    ConnectionAlreadyInUse,
}

public enum RejoinPlayerStatus
{
    Accepted,
    RoomClosed,
    UnknownPlayer,
    ConnectionAlreadyInUse,
}

public enum ConnectClientStatus
{
    Accepted,
    RoomClosed,
    PlayerRoleRequiresPlayerIdentity,
    ConnectionAlreadyInUse,
}

public enum LeavePlayerStatus
{
    Left,
    UnknownPlayer,
}

public enum DisconnectClientStatus
{
    Disconnected,
    UnknownConnection,
}

public sealed record PlayerMembership(
    PlayerId PlayerId,
    PlayerPresence Presence,
    ConnectionId? ConnectionId);

public sealed record ClientMembership(
    ConnectionId ConnectionId,
    ClientRole Role,
    PlayerId? PlayerId);

public sealed record SessionLifecycleEvent(
    long Sequence,
    SessionLifecycleEventKind Kind,
    ConnectionId? ConnectionId = null,
    PlayerId? PlayerId = null,
    ClientRole? Role = null);

public sealed record JoinPlayerResult(JoinPlayerStatus Status, PlayerMembership? Player = null)
{
    public bool IsAccepted => Status == JoinPlayerStatus.Accepted;
}

public sealed record RejoinPlayerResult(RejoinPlayerStatus Status, PlayerMembership? Player = null)
{
    public bool IsAccepted => Status == RejoinPlayerStatus.Accepted;
}

public sealed record ConnectClientResult(ConnectClientStatus Status, ClientMembership? Client = null)
{
    public bool IsAccepted => Status == ConnectClientStatus.Accepted;
}

public sealed class RoomSession
{
    private readonly Dictionary<PlayerId, PlayerMembership> _players = new();
    private readonly Dictionary<ConnectionId, ClientMembership> _clients = new();
    private readonly List<SessionLifecycleEvent> _events = new();
    private long _nextEventSequence = 1;

    public RoomSession(
        RoomId roomId,
        JoinCode joinCode,
        int playerCapacity,
        AuthorityId authorityId)
    {
        if (playerCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCapacity),
                playerCapacity,
                "Player capacity must be greater than zero.");
        }

        RoomId = roomId;
        JoinCode = joinCode;
        PlayerCapacity = playerCapacity;
        AuthorityId = authorityId;
        State = RoomSessionState.Open;
        AddEvent(SessionLifecycleEventKind.SessionCreated);
    }

    public RoomId RoomId { get; }

    public JoinCode JoinCode { get; }

    public int PlayerCapacity { get; }

    public AuthorityId AuthorityId { get; }

    public RoomSessionState State { get; private set; }

    public IReadOnlyCollection<PlayerMembership> Players => _players.Values.ToArray();

    public IReadOnlyCollection<ClientMembership> Clients => _clients.Values.ToArray();

    public IReadOnlyList<SessionLifecycleEvent> Events => _events.AsReadOnly();

    public JoinPlayerResult JoinPlayer(PlayerId playerId, ConnectionId connectionId)
    {
        if (State == RoomSessionState.Closed)
        {
            return new(JoinPlayerStatus.RoomClosed);
        }

        if (_players.ContainsKey(playerId))
        {
            return new(JoinPlayerStatus.PlayerAlreadyExists);
        }

        if (_clients.ContainsKey(connectionId))
        {
            return new(JoinPlayerStatus.ConnectionAlreadyInUse);
        }

        if (_players.Count >= PlayerCapacity)
        {
            return new(JoinPlayerStatus.RoomFull);
        }

        var player = new PlayerMembership(playerId, PlayerPresence.Connected, connectionId);
        var client = new ClientMembership(connectionId, ClientRole.Player, playerId);
        _players.Add(playerId, player);
        _clients.Add(connectionId, client);
        AddEvent(SessionLifecycleEventKind.PlayerJoined, connectionId, playerId, ClientRole.Player);
        return new(JoinPlayerStatus.Accepted, player);
    }

    public RejoinPlayerResult RejoinPlayer(PlayerId playerId, ConnectionId connectionId)
    {
        if (State == RoomSessionState.Closed)
        {
            return new(RejoinPlayerStatus.RoomClosed);
        }

        if (!_players.TryGetValue(playerId, out var existingPlayer))
        {
            return new(RejoinPlayerStatus.UnknownPlayer);
        }

        if (_clients.TryGetValue(connectionId, out var connectionOwner) && connectionOwner.PlayerId != playerId)
        {
            return new(RejoinPlayerStatus.ConnectionAlreadyInUse);
        }

        if (existingPlayer.ConnectionId == connectionId && existingPlayer.Presence == PlayerPresence.Connected)
        {
            return new(RejoinPlayerStatus.Accepted, existingPlayer);
        }

        if (existingPlayer.ConnectionId is { } previousConnectionId)
        {
            _clients.Remove(previousConnectionId);
        }

        var rejoinedPlayer = existingPlayer with
        {
            Presence = PlayerPresence.Connected,
            ConnectionId = connectionId,
        };
        _players[playerId] = rejoinedPlayer;
        _clients[connectionId] = new ClientMembership(connectionId, ClientRole.Player, playerId);
        AddEvent(SessionLifecycleEventKind.PlayerRejoined, connectionId, playerId, ClientRole.Player);
        return new(RejoinPlayerStatus.Accepted, rejoinedPlayer);
    }

    public ConnectClientResult ConnectClient(ConnectionId connectionId, ClientRole role)
    {
        if (State == RoomSessionState.Closed)
        {
            return new(ConnectClientStatus.RoomClosed);
        }

        if (role == ClientRole.Player)
        {
            return new(ConnectClientStatus.PlayerRoleRequiresPlayerIdentity);
        }

        if (_clients.ContainsKey(connectionId))
        {
            return new(ConnectClientStatus.ConnectionAlreadyInUse);
        }

        var client = new ClientMembership(connectionId, role, PlayerId: null);
        _clients.Add(connectionId, client);
        AddEvent(SessionLifecycleEventKind.ClientConnected, connectionId, role: role);
        return new(ConnectClientStatus.Accepted, client);
    }

    public LeavePlayerStatus LeavePlayer(PlayerId playerId)
    {
        if (!_players.Remove(playerId, out var player))
        {
            return LeavePlayerStatus.UnknownPlayer;
        }

        if (player.ConnectionId is { } connectionId)
        {
            _clients.Remove(connectionId);
        }

        AddEvent(SessionLifecycleEventKind.PlayerLeft, player.ConnectionId, playerId, ClientRole.Player);
        return LeavePlayerStatus.Left;
    }

    public DisconnectClientStatus Disconnect(ConnectionId connectionId)
    {
        if (!_clients.Remove(connectionId, out var client))
        {
            return DisconnectClientStatus.UnknownConnection;
        }

        if (client.PlayerId is { } playerId && _players.TryGetValue(playerId, out var player))
        {
            _players[playerId] = player with
            {
                Presence = PlayerPresence.Disconnected,
                ConnectionId = null,
            };
            AddEvent(SessionLifecycleEventKind.PlayerDisconnected, connectionId, playerId, ClientRole.Player);
            return DisconnectClientStatus.Disconnected;
        }

        AddEvent(SessionLifecycleEventKind.ClientDisconnected, connectionId, role: client.Role);
        return DisconnectClientStatus.Disconnected;
    }

    public bool Close()
    {
        if (State == RoomSessionState.Closed)
        {
            return false;
        }

        State = RoomSessionState.Closed;
        AddEvent(SessionLifecycleEventKind.SessionClosed);
        return true;
    }

    public PlayerMembership? FindPlayer(PlayerId playerId) =>
        _players.GetValueOrDefault(playerId);

    public ClientMembership? FindClient(ConnectionId connectionId) =>
        _clients.GetValueOrDefault(connectionId);

    private void AddEvent(
        SessionLifecycleEventKind kind,
        ConnectionId? connectionId = null,
        PlayerId? playerId = null,
        ClientRole? role = null)
    {
        _events.Add(new SessionLifecycleEvent(_nextEventSequence++, kind, connectionId, playerId, role));
    }
}

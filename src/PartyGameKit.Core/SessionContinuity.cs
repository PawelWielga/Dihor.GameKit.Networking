using System.Security.Cryptography;
using System.Text;

namespace PartyGameKit.Core;

public sealed record SessionContinuityOptions
{
    public SessionContinuityOptions(
        TimeSpan hostTimeout,
        TimeSpan clientTimeout,
        TimeSpan reconnectWindow)
    {
        if (hostTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hostTimeout), hostTimeout, "Host timeout must be positive.");
        }

        if (clientTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(clientTimeout), clientTimeout, "Client timeout must be positive.");
        }

        if (reconnectWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectWindow), reconnectWindow, "Reconnect window cannot be negative.");
        }

        HostTimeout = hostTimeout;
        ClientTimeout = clientTimeout;
        ReconnectWindow = reconnectWindow;
    }

    public TimeSpan HostTimeout { get; }

    public TimeSpan ClientTimeout { get; }

    public TimeSpan ReconnectWindow { get; }
}

public sealed record ConnectionPresence(
    ConnectionId ConnectionId,
    ClientRole Role,
    DateTimeOffset LastSeenAt);

public enum PresenceTimeoutKind
{
    ClientTimedOut,
    HostLost,
}

public sealed record PresenceTimeout(
    PresenceTimeoutKind Kind,
    ConnectionId ConnectionId,
    ClientRole Role,
    PlayerId? PlayerId,
    DateTimeOffset TimedOutAt,
    DateTimeOffset? ReconnectUntil);

public sealed record ContinuityJoinResult(
    JoinPlayerStatus Status,
    PlayerMembership? Player = null,
    string? ReconnectToken = null)
{
    public bool IsAccepted => Status == JoinPlayerStatus.Accepted;
}

public sealed record ContinuityDisconnectResult(
    DisconnectClientStatus Status,
    bool HostLost = false,
    PlayerId? PlayerId = null,
    DateTimeOffset? ReconnectUntil = null);

public enum ResumePlayerStatus
{
    Accepted,
    RoomClosed,
    InvalidResumeIdentity,
    ReconnectWindowExpired,
    ConnectionAlreadyInUse,
}

public sealed record ResumePlayerResult<TPublicState, TPrivateState>(
    ResumePlayerStatus Status,
    PlayerMembership? Player = null,
    StateSnapshot<PublicStateProjection<TPublicState>>? PublicSnapshot = null,
    StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>? PlayerSnapshot = null)
{
    public bool IsAccepted => Status == ResumePlayerStatus.Accepted;
}

public sealed class SessionContinuityCoordinator<TPublicState, TPrivateState>
{
    private readonly object _gate = new();
    private readonly RoomSession _session;
    private readonly AuthoritativeSnapshotPublisher<TPublicState, TPrivateState> _snapshotPublisher;
    private readonly SessionContinuityOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _reconnectTokenFactory;
    private readonly Dictionary<ConnectionId, ConnectionPresence> _presence = new();
    private readonly Dictionary<PlayerId, DateTimeOffset> _reconnectUntil = new();
    private readonly Dictionary<PlayerId, byte[]> _credentialFingerprints = new();

    public SessionContinuityCoordinator(
        RoomSession session,
        AuthoritativeSnapshotPublisher<TPublicState, TPrivateState> snapshotPublisher,
        SessionContinuityOptions options,
        Func<DateTimeOffset>? utcNow = null,
        Func<string>? reconnectTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(snapshotPublisher);
        ArgumentNullException.ThrowIfNull(options);

        _session = session;
        _snapshotPublisher = snapshotPublisher;
        _options = options;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _reconnectTokenFactory = reconnectTokenFactory ?? GenerateReconnectToken;
    }

    public IReadOnlyCollection<ConnectionPresence> Presence
    {
        get
        {
            lock (_gate)
            {
                return _presence.Values.ToArray();
            }
        }
    }

    public ContinuityJoinResult JoinPlayer(PlayerId playerId, ConnectionId connectionId)
    {
        lock (_gate)
        {
            var join = _session.JoinPlayer(playerId, connectionId);
            if (!join.IsAccepted)
            {
                return new ContinuityJoinResult(join.Status);
            }

            var token = CreateReconnectToken();
            _credentialFingerprints[playerId] = Fingerprint(token);
            _reconnectUntil.Remove(playerId);
            TrackPresence(connectionId, ClientRole.Player, _utcNow());
            return new ContinuityJoinResult(join.Status, join.Player, token);
        }
    }

    public ConnectClientResult ConnectClient(ConnectionId connectionId, ClientRole role)
    {
        lock (_gate)
        {
            var result = _session.ConnectClient(connectionId, role);
            if (result.IsAccepted)
            {
                TrackPresence(connectionId, role, _utcNow());
            }

            return result;
        }
    }

    public LeavePlayerStatus LeavePlayer(PlayerId playerId)
    {
        lock (_gate)
        {
            var existing = _session.FindPlayer(playerId);
            if (existing?.ConnectionId is { } connectionId)
            {
                _presence.Remove(connectionId);
            }

            var status = _session.LeavePlayer(playerId);
            if (status == LeavePlayerStatus.Left)
            {
                _credentialFingerprints.Remove(playerId);
                _reconnectUntil.Remove(playerId);
            }

            return status;
        }
    }

    public bool RecordHeartbeat(ConnectionId connectionId)
    {
        lock (_gate)
        {
            var client = _session.FindClient(connectionId);
            if (client is null)
            {
                return false;
            }

            TrackPresence(connectionId, client.Role, _utcNow());
            return true;
        }
    }

    public ContinuityDisconnectResult MarkDisconnected(ConnectionId connectionId)
    {
        lock (_gate)
        {
            return MarkDisconnectedCore(connectionId, _utcNow());
        }
    }

    public IReadOnlyList<PresenceTimeout> SweepTimeouts()
    {
        lock (_gate)
        {
            var now = _utcNow();
            var expired = _presence.Values
                .Where(item => now - item.LastSeenAt >= TimeoutFor(item.Role))
                .ToArray();
            if (expired.Length == 0)
            {
                return Array.Empty<PresenceTimeout>();
            }

            var results = new List<PresenceTimeout>(expired.Length);
            foreach (var presence in expired)
            {
                var client = _session.FindClient(presence.ConnectionId);
                if (client is null)
                {
                    _presence.Remove(presence.ConnectionId);
                    continue;
                }

                var disconnected = MarkDisconnectedCore(presence.ConnectionId, now);
                if (disconnected.Status != DisconnectClientStatus.Disconnected)
                {
                    continue;
                }

                results.Add(new PresenceTimeout(
                    disconnected.HostLost ? PresenceTimeoutKind.HostLost : PresenceTimeoutKind.ClientTimedOut,
                    presence.ConnectionId,
                    presence.Role,
                    disconnected.PlayerId,
                    now,
                    disconnected.ReconnectUntil));
            }

            return results;
        }
    }

    public DateTimeOffset? GetReconnectDeadline(PlayerId playerId)
    {
        lock (_gate)
        {
            return _reconnectUntil.GetValueOrDefault(playerId);
        }
    }

    public ResumePlayerResult<TPublicState, TPrivateState> RejoinPlayer(
        PlayerId playerId,
        ConnectionId connectionId,
        string reconnectToken)
    {
        lock (_gate)
        {
            if (_session.State == RoomSessionState.Closed)
            {
                return new(ResumePlayerStatus.RoomClosed);
            }

            var player = _session.FindPlayer(playerId);
            if (player is null || !CredentialMatches(playerId, reconnectToken))
            {
                return new(ResumePlayerStatus.InvalidResumeIdentity);
            }

            var now = _utcNow();
            if (player.Presence == PlayerPresence.Disconnected)
            {
                if (!_reconnectUntil.TryGetValue(playerId, out var reconnectUntil) || now >= reconnectUntil)
                {
                    return new(ResumePlayerStatus.ReconnectWindowExpired);
                }
            }

            var previousConnectionId = player.ConnectionId;
            var result = _session.RejoinPlayer(playerId, connectionId);
            if (!result.IsAccepted)
            {
                return result.Status switch
                {
                    RejoinPlayerStatus.RoomClosed => new(ResumePlayerStatus.RoomClosed),
                    RejoinPlayerStatus.ConnectionAlreadyInUse => new(ResumePlayerStatus.ConnectionAlreadyInUse),
                    _ => new(ResumePlayerStatus.InvalidResumeIdentity),
                };
            }

            if (previousConnectionId is { } previous && previous != connectionId)
            {
                _presence.Remove(previous);
            }

            _reconnectUntil.Remove(playerId);
            TrackPresence(connectionId, ClientRole.Player, now);

            _snapshotPublisher.TryGetLatestForPlayer(playerId, out var playerSnapshot);
            return new ResumePlayerResult<TPublicState, TPrivateState>(
                ResumePlayerStatus.Accepted,
                result.Player,
                _snapshotPublisher.Latest?.PublicSnapshot,
                playerSnapshot);
        }
    }

    private ContinuityDisconnectResult MarkDisconnectedCore(
        ConnectionId connectionId,
        DateTimeOffset now)
    {
        var client = _session.FindClient(connectionId);
        if (client is null)
        {
            _presence.Remove(connectionId);
            return new(DisconnectClientStatus.UnknownConnection);
        }

        var status = _session.Disconnect(connectionId);
        _presence.Remove(connectionId);
        if (status != DisconnectClientStatus.Disconnected)
        {
            return new(status);
        }

        DateTimeOffset? reconnectUntil = null;
        if (client.PlayerId is { } playerId)
        {
            reconnectUntil = now + _options.ReconnectWindow;
            _reconnectUntil[playerId] = reconnectUntil.Value;
        }

        return new ContinuityDisconnectResult(
            status,
            HostLost: client.Role == ClientRole.Host,
            PlayerId: client.PlayerId,
            ReconnectUntil: reconnectUntil);
    }

    private void TrackPresence(
        ConnectionId connectionId,
        ClientRole role,
        DateTimeOffset lastSeenAt)
    {
        _presence[connectionId] = new ConnectionPresence(connectionId, role, lastSeenAt);
    }

    private TimeSpan TimeoutFor(ClientRole role) =>
        role == ClientRole.Host ? _options.HostTimeout : _options.ClientTimeout;

    private string CreateReconnectToken()
    {
        var token = _reconnectTokenFactory()?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Reconnect token factory returned an empty token.");
        }

        return token;
    }

    private bool CredentialMatches(PlayerId playerId, string reconnectToken)
    {
        if (!_credentialFingerprints.TryGetValue(playerId, out var expected) ||
            string.IsNullOrWhiteSpace(reconnectToken))
        {
            return false;
        }

        var candidate = Fingerprint(reconnectToken.Trim());
        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    private static byte[] Fingerprint(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string GenerateReconnectToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}

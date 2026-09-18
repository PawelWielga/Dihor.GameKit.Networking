using System.Security.Cryptography;
using System.Text;

namespace Dihor.GameKit.Networking.Core;

public sealed class ConnectionContinuityOptions
{
    public ConnectionContinuityOptions(
        TimeSpan peerTimeout,
        TimeSpan reconnectWindow)
    {
        if (peerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(peerTimeout), peerTimeout, "Peer timeout must be positive.");
        }

        if (reconnectWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconnectWindow), reconnectWindow, "Reconnect window must be positive.");
        }

        PeerTimeout = peerTimeout;
        ReconnectWindow = reconnectWindow;
    }

    public TimeSpan PeerTimeout { get; }

    public TimeSpan ReconnectWindow { get; }
}

public enum PeerConnectionState
{
    Connected,
    AwaitingResume,
}

public sealed record PeerPresence(
    PeerId PeerId,
    ConnectionId? ConnectionId,
    PeerConnectionState State,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ResumeUntil);

public enum RegisterPeerStatus
{
    Connected,
    PeerAlreadyConnected,
    PeerRequiresResume,
    ConnectionAlreadyBound,
}

public sealed record RegisterPeerResult(
    RegisterPeerStatus Status,
    PeerId PeerId,
    ConnectionId ConnectionId,
    string? ResumeToken = null)
{
    public bool IsConnected => Status == RegisterPeerStatus.Connected;
}

public enum DisconnectPeerStatus
{
    Disconnected,
    ConnectionNotFound,
}

public sealed record DisconnectPeerResult(
    DisconnectPeerStatus Status,
    PeerId? PeerId = null,
    DateTimeOffset? ResumeUntil = null);

public enum ResumePeerStatus
{
    Resumed,
    UnknownPeer,
    InvalidResumeCredential,
    ReconnectWindowExpired,
    PeerAlreadyConnected,
    ConnectionAlreadyBound,
}

public sealed record ResumePeerResult(
    ResumePeerStatus Status,
    PeerId PeerId,
    ConnectionId ConnectionId,
    string? ResumeToken = null)
{
    public bool IsResumed => Status == ResumePeerStatus.Resumed;
}

public sealed record PeerTimedOut(
    PeerId PeerId,
    ConnectionId ConnectionId,
    DateTimeOffset ResumeUntil);

public sealed class ConnectionContinuityCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<PeerId, PeerState> _peers = new();
    private readonly Dictionary<ConnectionId, PeerId> _connections = new();
    private readonly ConnectionContinuityOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _resumeTokenFactory;

    public ConnectionContinuityCoordinator(
        ConnectionContinuityOptions options,
        Func<DateTimeOffset>? utcNow = null,
        Func<string>? resumeTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _resumeTokenFactory = resumeTokenFactory ?? CreateResumeToken;
    }

    public int PeerCount
    {
        get
        {
            lock (_gate)
            {
                return _peers.Count;
            }
        }
    }

    public int ConnectedPeerCount
    {
        get
        {
            lock (_gate)
            {
                return _connections.Count;
            }
        }
    }

    public RegisterPeerResult Register(PeerId peerId, ConnectionId connectionId)
    {
        lock (_gate)
        {
            var now = _utcNow();
            if (_connections.ContainsKey(connectionId))
            {
                return new(RegisterPeerStatus.ConnectionAlreadyBound, peerId, connectionId);
            }

            if (_peers.TryGetValue(peerId, out var existing))
            {
                if (existing.ConnectionId is not null)
                {
                    return new(RegisterPeerStatus.PeerAlreadyConnected, peerId, connectionId);
                }

                if (existing.ResumeUntil is { } resumeUntil && now <= resumeUntil)
                {
                    return new(RegisterPeerStatus.PeerRequiresResume, peerId, connectionId);
                }

                _peers.Remove(peerId);
            }

            var resumeToken = CreateValidatedResumeToken();
            _peers.Add(peerId, new PeerState(
                peerId,
                connectionId,
                HashToken(resumeToken),
                now));
            _connections.Add(connectionId, peerId);
            return new(RegisterPeerStatus.Connected, peerId, connectionId, resumeToken);
        }
    }

    public bool RecordHeartbeat(ConnectionId connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var peerId) ||
                !_peers.TryGetValue(peerId, out var state) ||
                state.ConnectionId != connectionId)
            {
                return false;
            }

            state.LastSeenAt = _utcNow();
            return true;
        }
    }

    public DisconnectPeerResult MarkDisconnected(ConnectionId connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var peerId) ||
                !_peers.TryGetValue(peerId, out var state) ||
                state.ConnectionId != connectionId)
            {
                return new(DisconnectPeerStatus.ConnectionNotFound);
            }

            var resumeUntil = _utcNow() + _options.ReconnectWindow;
            state.ConnectionId = null;
            state.ResumeUntil = resumeUntil;
            return new(DisconnectPeerStatus.Disconnected, peerId, resumeUntil);
        }
    }

    public ResumePeerResult Resume(
        PeerId peerId,
        string resumeToken,
        ConnectionId replacementConnectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeToken);

        lock (_gate)
        {
            if (_connections.ContainsKey(replacementConnectionId))
            {
                return new(ResumePeerStatus.ConnectionAlreadyBound, peerId, replacementConnectionId);
            }

            if (!_peers.TryGetValue(peerId, out var state))
            {
                return new(ResumePeerStatus.UnknownPeer, peerId, replacementConnectionId);
            }

            if (state.ConnectionId is not null)
            {
                return new(ResumePeerStatus.PeerAlreadyConnected, peerId, replacementConnectionId);
            }

            var now = _utcNow();
            if (state.ResumeUntil is not { } resumeUntil || now > resumeUntil)
            {
                _peers.Remove(peerId);
                return new(ResumePeerStatus.ReconnectWindowExpired, peerId, replacementConnectionId);
            }

            if (!TokenMatches(resumeToken, state.ResumeTokenHash))
            {
                return new(ResumePeerStatus.InvalidResumeCredential, peerId, replacementConnectionId);
            }

            var nextResumeToken = CreateValidatedResumeToken();
            state.ConnectionId = replacementConnectionId;
            state.LastSeenAt = now;
            state.ResumeUntil = null;
            state.ResumeTokenHash = HashToken(nextResumeToken);
            _connections.Add(replacementConnectionId, peerId);
            return new(ResumePeerStatus.Resumed, peerId, replacementConnectionId, nextResumeToken);
        }
    }

    public IReadOnlyList<PeerTimedOut> SweepTimeouts()
    {
        lock (_gate)
        {
            var now = _utcNow();
            var timedOutStates = _peers.Values
                .Where(state => state.ConnectionId is not null && now - state.LastSeenAt > _options.PeerTimeout)
                .ToArray();
            var result = new List<PeerTimedOut>(timedOutStates.Length);

            foreach (var state in timedOutStates)
            {
                var connectionId = state.ConnectionId!.Value;
                _connections.Remove(connectionId);
                state.ConnectionId = null;
                state.ResumeUntil = now + _options.ReconnectWindow;
                result.Add(new PeerTimedOut(state.PeerId, connectionId, state.ResumeUntil.Value));
            }

            return result;
        }
    }

    public IReadOnlyList<PeerId> ExpireReconnectWindows()
    {
        lock (_gate)
        {
            var now = _utcNow();
            var expired = _peers.Values
                .Where(state => state.ConnectionId is null && state.ResumeUntil is { } resumeUntil && now > resumeUntil)
                .Select(state => state.PeerId)
                .ToArray();

            foreach (var peerId in expired)
            {
                _peers.Remove(peerId);
            }

            return expired;
        }
    }

    public PeerPresence? GetPresence(PeerId peerId)
    {
        lock (_gate)
        {
            return _peers.TryGetValue(peerId, out var state)
                ? ToPresence(state)
                : null;
        }
    }

    public PeerId? GetPeerId(ConnectionId connectionId)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(connectionId, out var peerId)
                ? peerId
                : null;
        }
    }

    public bool Forget(PeerId peerId)
    {
        lock (_gate)
        {
            if (!_peers.Remove(peerId, out var state))
            {
                return false;
            }

            if (state.ConnectionId is { } connectionId)
            {
                _connections.Remove(connectionId);
            }

            return true;
        }
    }

    private string CreateValidatedResumeToken()
    {
        var token = _resumeTokenFactory();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Resume token factory returned an empty token.");
        }

        return token;
    }

    private static PeerPresence ToPresence(PeerState state) =>
        new(
            state.PeerId,
            state.ConnectionId,
            state.ConnectionId is null ? PeerConnectionState.AwaitingResume : PeerConnectionState.Connected,
            state.LastSeenAt,
            state.ResumeUntil);

    private static string CreateResumeToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool TokenMatches(string token, byte[] expectedHash)
    {
        var actualHash = HashToken(token);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private sealed class PeerState(
        PeerId peerId,
        ConnectionId connectionId,
        byte[] resumeTokenHash,
        DateTimeOffset lastSeenAt)
    {
        public PeerId PeerId { get; } = peerId;

        public ConnectionId? ConnectionId { get; set; } = connectionId;

        public byte[] ResumeTokenHash { get; set; } = resumeTokenHash;

        public DateTimeOffset LastSeenAt { get; set; } = lastSeenAt;

        public DateTimeOffset? ResumeUntil { get; set; }
    }
}

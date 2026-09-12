using System.Collections.ObjectModel;

namespace PartyGameKit.Core;

public enum SnapshotAudience
{
    Public,
    Player,
}

public readonly record struct SnapshotSequence
{
    public SnapshotSequence(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Snapshot sequence must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct SnapshotTarget
{
    private SnapshotTarget(SnapshotAudience audience, PlayerId? playerId)
    {
        Audience = audience;
        PlayerId = playerId;
    }

    public SnapshotAudience Audience { get; }

    public PlayerId? PlayerId { get; }

    public static SnapshotTarget Public { get; } = new(SnapshotAudience.Public, null);

    public static SnapshotTarget ForPlayer(PlayerId playerId) =>
        new(SnapshotAudience.Player, playerId);
}

public sealed record PublicStateProjection<TPublicState>(TPublicState State);

public sealed record PlayerStateProjection<TPublicState, TPrivateState>(
    TPublicState PublicState,
    TPrivateState PrivateState);

public sealed record StateSnapshot<TProjection>
{
    public StateSnapshot(
        RoomId roomId,
        AuthorityId authorityId,
        SnapshotSequence sequence,
        SnapshotTarget target,
        TProjection projection)
    {
        RoomId = roomId;
        AuthorityId = authorityId;
        Sequence = sequence;
        Target = target;
        Projection = projection;
    }

    public RoomId RoomId { get; }

    public AuthorityId AuthorityId { get; }

    public SnapshotSequence Sequence { get; }

    public SnapshotTarget Target { get; }

    public TProjection Projection { get; }
}

public sealed record PublishedSnapshotSet<TPublicState, TPrivateState>(
    StateSnapshot<PublicStateProjection<TPublicState>> PublicSnapshot,
    IReadOnlyDictionary<PlayerId, StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>> PlayerSnapshots);

public sealed class AuthoritativeSnapshotPublisher<TPublicState, TPrivateState>
{
    private readonly object _gate = new();
    private readonly RoomId _roomId;
    private long _sequence;
    private PublishedSnapshotSet<TPublicState, TPrivateState>? _latest;

    public AuthoritativeSnapshotPublisher(RoomId roomId)
    {
        _roomId = roomId;
    }

    public long CurrentSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    public PublishedSnapshotSet<TPublicState, TPrivateState>? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public PublishedSnapshotSet<TPublicState, TPrivateState> Publish(
        AuthorityId authorityId,
        TPublicState publicState,
        IReadOnlyDictionary<PlayerId, TPrivateState> privateStateByPlayer)
    {
        ArgumentNullException.ThrowIfNull(privateStateByPlayer);

        lock (_gate)
        {
            var sequenceValue = checked(_sequence + 1);
            var sequence = new SnapshotSequence(sequenceValue);
            var publicSnapshot = new StateSnapshot<PublicStateProjection<TPublicState>>(
                _roomId,
                authorityId,
                sequence,
                SnapshotTarget.Public,
                new PublicStateProjection<TPublicState>(publicState));
            var playerSnapshots = new Dictionary<PlayerId, StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>>(
                privateStateByPlayer.Count);

            foreach (var entry in privateStateByPlayer)
            {
                playerSnapshots.Add(
                    entry.Key,
                    new StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>(
                        _roomId,
                        authorityId,
                        sequence,
                        SnapshotTarget.ForPlayer(entry.Key),
                        new PlayerStateProjection<TPublicState, TPrivateState>(publicState, entry.Value)));
            }

            var published = new PublishedSnapshotSet<TPublicState, TPrivateState>(
                publicSnapshot,
                new ReadOnlyDictionary<PlayerId, StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>>(playerSnapshots));
            _sequence = sequenceValue;
            _latest = published;
            return published;
        }
    }

    public bool TryGetLatestForPlayer(
        PlayerId playerId,
        out StateSnapshot<PlayerStateProjection<TPublicState, TPrivateState>>? snapshot)
    {
        lock (_gate)
        {
            if (_latest is not null && _latest.PlayerSnapshots.TryGetValue(playerId, out var current))
            {
                snapshot = current;
                return true;
            }

            snapshot = null;
            return false;
        }
    }
}

public sealed class SnapshotSequenceGate
{
    private readonly object _gate = new();
    private long _lastAppliedSequence;

    public SnapshotSequenceGate(long lastAppliedSequence = 0)
    {
        if (lastAppliedSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastAppliedSequence),
                lastAppliedSequence,
                "Last applied snapshot sequence cannot be negative.");
        }

        _lastAppliedSequence = lastAppliedSequence;
    }

    public long LastAppliedSequence
    {
        get
        {
            lock (_gate)
            {
                return _lastAppliedSequence;
            }
        }
    }

    public bool TryAccept(SnapshotSequence sequence)
    {
        lock (_gate)
        {
            if (sequence.Value <= _lastAppliedSequence)
            {
                return false;
            }

            _lastAppliedSequence = sequence.Value;
            return true;
        }
    }
}

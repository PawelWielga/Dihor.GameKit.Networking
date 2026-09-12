using System.Net;
using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Discovery.Lan;

public sealed record DiscoveredSession(
    JoinDescriptor Descriptor,
    DateTimeOffset LastSeenAt,
    IPAddress? SourceAddress = null)
{
    public RoomId RoomId => Descriptor.RoomId;
}

public sealed class DiscoveredSessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<RoomId, DiscoveredSession> _sessions = new();
    private readonly Func<DateTimeOffset> _utcNow;

    public DiscoveredSessionRegistry(
        TimeSpan? timeToLive = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        TimeToLive = timeToLive ?? TimeSpan.FromSeconds(3);
        if (TimeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), TimeToLive, "Discovery TTL must be positive.");
        }

        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan TimeToLive { get; }

    public IReadOnlyList<DiscoveredSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Values
                    .OrderBy(item => item.RoomId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public bool Upsert(JoinDescriptor descriptor, IPAddress? sourceAddress = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            var now = _utcNow();
            var next = new DiscoveredSession(descriptor, now, sourceAddress);
            var changed = !_sessions.TryGetValue(descriptor.RoomId, out var previous) ||
                previous.Descriptor != descriptor ||
                !Equals(previous.SourceAddress, sourceAddress);
            _sessions[descriptor.RoomId] = next;
            return changed;
        }
    }

    public bool AddAnnouncement(string json, IPAddress? sourceAddress = null)
    {
        var announcement = DiscoveryAnnouncementCodec.Parse(json);
        return Upsert(announcement.Descriptor, sourceAddress);
    }

    public bool ExpireInactive()
    {
        lock (_gate)
        {
            var now = _utcNow();
            var expired = _sessions
                .Where(pair => now - pair.Value.LastSeenAt > TimeToLive)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var roomId in expired)
            {
                _sessions.Remove(roomId);
            }

            return expired.Length > 0;
        }
    }
}

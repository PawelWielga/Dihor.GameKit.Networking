using System.Net;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Discovery.Lan;

public sealed record DiscoveredEndpoint(
    ConnectionDescriptor Descriptor,
    DateTimeOffset LastSeenAt,
    IPAddress? SourceAddress = null);

public sealed class DiscoveredEndpointRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DiscoveredEndpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _utcNow;

    public DiscoveredEndpointRegistry(
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

    public IReadOnlyList<DiscoveredEndpoint> Endpoints
    {
        get
        {
            lock (_gate)
            {
                return _endpoints.Values
                    .OrderBy(item => GetKey(item.Descriptor), StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public bool Upsert(ConnectionDescriptor descriptor, IPAddress? sourceAddress = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            var key = GetKey(descriptor);
            var next = new DiscoveredEndpoint(descriptor, _utcNow(), sourceAddress);
            var changed = !_endpoints.TryGetValue(key, out var previous) ||
                previous.Descriptor != descriptor ||
                !Equals(previous.SourceAddress, sourceAddress);
            _endpoints[key] = next;
            return changed;
        }
    }

    public bool AddAnnouncement(string json, IPAddress? sourceAddress = null)
    {
        var announcement = DiscoveryEndpointAnnouncementCodec.Parse(json);
        return Upsert(announcement.Descriptor, sourceAddress);
    }

    public bool ExpireInactive()
    {
        lock (_gate)
        {
            var now = _utcNow();
            var expired = _endpoints
                .Where(pair => now - pair.Value.LastSeenAt > TimeToLive)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in expired)
            {
                _endpoints.Remove(key);
            }

            return expired.Length > 0;
        }
    }

    private static string GetKey(ConnectionDescriptor descriptor) =>
        descriptor.ChannelId is { } channelId
            ? $"channel:{channelId.Value}"
            : $"endpoint:{descriptor.Transport}:{descriptor.Endpoint}";
}

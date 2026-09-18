using System.Net;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Discovery.Lan;
using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Discovery.Tests;

public sealed class DiscoveryRegistryTests
{
    [Fact]
    public void UpsertWithChannelRefreshesSameTechnicalEndpointIdentity()
    {
        var now = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);
        var registry = new DiscoveredEndpointRegistry(
            TimeSpan.FromSeconds(3),
            () => now);
        var first = Descriptor("ws://192.168.1.10:45678/partygamekit", "channel-a");
        var second = Descriptor("ws://192.168.1.11:45678/partygamekit", "channel-a");

        Assert.True(registry.Upsert(first, IPAddress.Parse("192.168.1.10")));
        now = now.AddSeconds(1);
        Assert.True(registry.Upsert(second, IPAddress.Parse("192.168.1.11")));

        var discovered = Assert.Single(registry.Endpoints);
        Assert.Equal(second, discovered.Descriptor);
        Assert.Equal(now, discovered.LastSeenAt);
    }

    [Fact]
    public void RegistryWithoutChannelKeepsIndependentEndpoints()
    {
        var registry = new DiscoveredEndpointRegistry();

        registry.Upsert(Descriptor("ws://192.168.1.10:45678/partygamekit"));
        registry.Upsert(Descriptor("ws://192.168.1.11:45678/partygamekit"));

        Assert.Equal(2, registry.Endpoints.Count);
    }

    [Fact]
    public void ExpireInactiveRemovesStaleAdvertisement()
    {
        var now = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);
        var registry = new DiscoveredEndpointRegistry(
            TimeSpan.FromSeconds(3),
            () => now);
        registry.Upsert(Descriptor("ws://192.168.1.10:45678/partygamekit"));

        now = now.AddSeconds(4);

        Assert.True(registry.ExpireInactive());
        Assert.Empty(registry.Endpoints);
    }

    private static ConnectionDescriptor Descriptor(string endpoint, string? channelId = null) =>
        new(
            "lan-websocket",
            endpoint,
            ProtocolVersions.Current,
            channelId is null ? null : new ChannelId(channelId));
}

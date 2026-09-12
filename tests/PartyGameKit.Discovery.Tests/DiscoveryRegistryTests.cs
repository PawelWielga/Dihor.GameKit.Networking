using System.Net;
using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;

namespace PartyGameKit.Discovery.Tests;

public sealed class DiscoveryRegistryTests
{
    [Fact]
    public void RegistryDistinguishesRoomsRefreshesDuplicatesAndExpiresStaleEntries()
    {
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var registry = new DiscoveredSessionRegistry(TimeSpan.FromSeconds(3), () => now);
        var first = Descriptor("room-a", "ROOMA", 5001);
        var second = Descriptor("room-b", "ROOMB", 5002);

        Assert.True(registry.Upsert(first, IPAddress.Parse("192.168.1.10")));
        Assert.True(registry.Upsert(second, IPAddress.Parse("192.168.1.11")));
        Assert.Equal(2, registry.Sessions.Count);

        now += TimeSpan.FromSeconds(2);
        Assert.False(registry.Upsert(first, IPAddress.Parse("192.168.1.10")));
        Assert.Equal(2, registry.Sessions.Count);
        Assert.Equal(now, registry.Sessions.Single(item => item.RoomId == first.RoomId).LastSeenAt);

        now += TimeSpan.FromSeconds(2);
        Assert.True(registry.ExpireInactive());
        var remaining = Assert.Single(registry.Sessions);
        Assert.Equal(first.RoomId, remaining.RoomId);
    }

    [Fact]
    public void BroadcastAddressCalculationUsesActualSubnetMask()
    {
        var broadcast = LanDiscoveryBroadcastAddressResolver.Calculate(
            IPAddress.Parse("192.168.10.34"),
            IPAddress.Parse("255.255.254.0"));

        Assert.Equal(IPAddress.Parse("192.168.11.255"), broadcast);
    }

    private static JoinDescriptor Descriptor(string roomId, string joinCode, int port) =>
        new(
            new RoomId(roomId),
            new JoinCode(joinCode),
            "lan-websocket",
            $"ws://127.0.0.1:{port}/partygamekit",
            1);
}

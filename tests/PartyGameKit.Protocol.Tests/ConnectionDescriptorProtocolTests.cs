using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class ConnectionDescriptorProtocolTests
{
    [Fact]
    public void Descriptor_RoundTripsThroughJsonAndUri()
    {
        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://192.168.1.10:45678/partygamekit",
            ProtocolVersions.Current,
            new ChannelId("channel-a"));

        var json = ConnectionDescriptorCodec.SerializeJson(descriptor);
        var uri = ConnectionDescriptorCodec.SerializeUri(descriptor);

        Assert.Equal(descriptor, ConnectionDescriptorCodec.ParseJson(json));
        Assert.Equal(descriptor, ConnectionDescriptorCodec.ParseUri(uri));
        Assert.Contains("partygamekit://connect?", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("joinCode", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("player", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Descriptor_CanOmitRoutingScope()
    {
        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://127.0.0.1:45678/partygamekit",
            ProtocolVersions.Current);

        var json = ConnectionDescriptorCodec.SerializeJson(descriptor);
        var parsed = ConnectionDescriptorCodec.ParseJson(json);

        Assert.Null(parsed.ChannelId);
        Assert.DoesNotContain("channelId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryAnnouncement_RoundTripsTechnicalEndpoint()
    {
        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://127.0.0.1:45678/partygamekit",
            ProtocolVersions.Current,
            new ChannelId("channel-a"));
        var announcement = new DiscoveryEndpointAnnouncement(descriptor);

        var json = DiscoveryEndpointAnnouncementCodec.Serialize(announcement);
        var parsed = DiscoveryEndpointAnnouncementCodec.Parse(json);

        Assert.Equal(descriptor, parsed.Descriptor);
        Assert.Contains("connection.discovery.announce", json, StringComparison.Ordinal);
        Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
    }
}

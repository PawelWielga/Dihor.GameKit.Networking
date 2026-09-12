using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class ConnectionDescriptorTests
{
    [Fact]
    public void ConstructorStoresOnlyTechnicalConnectionData()
    {
        var descriptor = new ConnectionDescriptor(
            "lan-websocket",
            "ws://127.0.0.1:45678/partygamekit",
            protocolVersion: 2,
            new ChannelId("channel-a"));

        Assert.Equal("lan-websocket", descriptor.Transport);
        Assert.Equal("ws://127.0.0.1:45678/partygamekit", descriptor.Endpoint);
        Assert.Equal(2, descriptor.ProtocolVersion);
        Assert.Equal(new ChannelId("channel-a"), descriptor.ChannelId);
    }

    [Fact]
    public void ConstructorRejectsRelativeEndpoint()
    {
        Assert.Throws<ArgumentException>(() =>
            new ConnectionDescriptor("lan-websocket", "/partygamekit", 2));
    }

    [Fact]
    public void ConstructorRejectsNonPositiveProtocolVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConnectionDescriptor("lan-websocket", "ws://127.0.0.1:45678/", 0));
    }
}

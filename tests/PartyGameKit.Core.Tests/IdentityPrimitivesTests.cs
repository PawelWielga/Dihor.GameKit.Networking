using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class IdentityPrimitivesTests
{
    [Fact]
    public void CommunicationIdentifiers_TrimValues()
    {
        Assert.Equal("connection-1", new ConnectionId("  connection-1  ").Value);
        Assert.Equal("peer-1", new PeerId("  peer-1  ").Value);
        Assert.Equal("channel-1", new ChannelId("  channel-1  ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CommunicationIdentifiers_RejectBlankValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new ConnectionId(value));
        Assert.Throws<ArgumentException>(() => new PeerId(value));
        Assert.Throws<ArgumentException>(() => new ChannelId(value));
    }
}

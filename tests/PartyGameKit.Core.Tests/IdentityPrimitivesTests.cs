using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class IdentityPrimitivesTests
{
    [Fact]
    public void PlayerAndConnectionIdentityRemainDistinctTypes()
    {
        var playerId = new PlayerId("same-text");
        var connectionId = new ConnectionId("same-text");

        Assert.Equal("same-text", playerId.Value);
        Assert.Equal("same-text", connectionId.Value);
        Assert.NotEqual(typeof(PlayerId), typeof(ConnectionId));
    }

    [Fact]
    public void IdentifiersTrimInputAndJoinCodeNormalizesCase()
    {
        Assert.Equal("room-1", new RoomId("  room-1 ").Value);
        Assert.Equal("ROOM42", new JoinCode(" room42 ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IdentifiersRejectBlankValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new PlayerId(value));
    }
}

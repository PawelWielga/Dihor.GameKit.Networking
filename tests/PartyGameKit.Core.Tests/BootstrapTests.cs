using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void CoreAssembly_ExposesNeutralConnectionPrimitive()
    {
        var connectionId = new ConnectionId("connection-1");

        Assert.Equal("connection-1", connectionId.Value);
    }
}

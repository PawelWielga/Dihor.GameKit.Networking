using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void CoreAssemblyExposesNeutralConnectionPrimitive()
    {
        var connectionId = new ConnectionId("connection-1");

        Assert.Equal("connection-1", connectionId.Value);
    }
}

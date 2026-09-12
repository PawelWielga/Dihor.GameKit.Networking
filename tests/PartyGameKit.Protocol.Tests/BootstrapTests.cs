using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void ProtocolVersion_IsCommunicationProtocolV2()
    {
        Assert.Equal(2, ProtocolVersions.Current);
    }
}

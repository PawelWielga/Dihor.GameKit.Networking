using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Protocol.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void ProtocolVersionIsCommunicationProtocolV2()
    {
        Assert.Equal(2, ProtocolVersions.Current);
    }
}

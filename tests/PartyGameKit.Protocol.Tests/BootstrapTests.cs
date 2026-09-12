using System.Reflection;

namespace PartyGameKit.Protocol.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void ProtocolAssemblyBuildsAndLoads()
    {
        var assembly = Assembly.Load("PartyGameKit.Protocol");

        Assert.Equal("PartyGameKit.Protocol", assembly.GetName().Name);
    }
}

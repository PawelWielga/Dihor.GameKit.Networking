using System.Reflection;

namespace PartyGameKit.Transport.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void TransportAbstractionsAssemblyBuildsAndLoads()
    {
        var assembly = Assembly.Load("PartyGameKit.Transport.Abstractions");

        Assert.Equal("PartyGameKit.Transport.Abstractions", assembly.GetName().Name);
    }
}

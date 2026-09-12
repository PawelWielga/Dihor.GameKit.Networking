using System.Reflection;

namespace PartyGameKit.Core.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void CoreAssemblyBuildsAndLoads()
    {
        var assembly = Assembly.Load("PartyGameKit.Core");

        Assert.Equal("PartyGameKit.Core", assembly.GetName().Name);
    }
}

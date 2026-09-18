using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Transport.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void TransportAbstractionIsMessageOriented()
    {
        Assert.True(typeof(IMessageTransport).IsInterface);
        Assert.DoesNotContain("Game", typeof(IMessageTransport).Name, StringComparison.Ordinal);
    }
}

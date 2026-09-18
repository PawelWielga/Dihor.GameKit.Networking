using System.Reflection;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Discovery.Lan;
using Dihor.GameKit.Networking.Protocol;
using Dihor.GameKit.Networking.Transport.Abstractions;
using Dihor.GameKit.Networking.Transport.InMemory;
using Dihor.GameKit.Networking.Transport.Lan;

namespace Dihor.GameKit.Networking.Transport.Tests;

public sealed class PublicApiBoundaryTests
{
    private static readonly string[] ForbiddenLegacyFragments =
    [
        "PlayerId",
        "ClientRole",
        "AuthorityId",
        "RoomSession",
        "JoinCode",
        "JoinDescriptor",
        "StateSnapshot",
        "SnapshotAudience",
        "SnapshotTarget",
        "GameTransport",
        "DiscoveredSession",
    ];

    [Fact]
    public void ProductionAssembliesDoNotExposeLegacyProductSessionApi()
    {
        var assemblies = new[]
        {
            typeof(ConnectionId).Assembly,
            typeof(ProtocolVersions).Assembly,
            typeof(IMessageTransport).Assembly,
            typeof(InMemoryTransport).Assembly,
            typeof(LanWebSocketTransport).Assembly,
            typeof(UdpLanDiscoveryAdvertiser).Assembly,
        }.Distinct();

        var violations = assemblies
            .SelectMany(PublicSurface)
            .Where(name => ForbiddenLegacyFragments.Any(
                fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> PublicSurface(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            yield return $"{assembly.GetName().Name}:{type.FullName}";
            foreach (var member in type.GetMembers(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                yield return $"{assembly.GetName().Name}:{type.FullName}.{member.Name}";
            }
        }
    }
}

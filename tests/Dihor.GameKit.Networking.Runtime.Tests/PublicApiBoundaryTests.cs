using System.Reflection;
using Dihor.GameKit.Networking.Runtime;

namespace Dihor.GameKit.Networking.Runtime.Tests;

public sealed class PublicApiBoundaryTests
{
    private static readonly string[] ForbiddenProductFragments =
    [
        "Player",
        "Participant",
        "ClientRole",
        "Authority",
        "Lobby",
        "Party",
        "GameSession",
        "GameState",
        "Projection",
    ];

    [Fact]
    public void RuntimePublicApiRemainsCommunicationOnly()
    {
        var violations = typeof(ConnectionHostRuntime).Assembly
            .GetExportedTypes()
            .SelectMany(PublicSurface)
            .Where(name => ForbiddenProductFragments.Any(
                fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> PublicSurface(Type type)
    {
        yield return type.FullName ?? type.Name;
        foreach (var member in type.GetMembers(
                     BindingFlags.Public |
                     BindingFlags.Instance |
                     BindingFlags.Static |
                     BindingFlags.DeclaredOnly))
        {
            yield return $"{type.FullName}.{member.Name}";
        }
    }
}

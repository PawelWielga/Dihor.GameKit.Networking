using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Dihor.GameKit.Networking.Transport.SignalR.Server;

public static class SignalRRelayEndpointRouteBuilderExtensions
{
    public static HubEndpointConventionBuilder MapDihor.GameKit.NetworkingSignalRRelay(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/partygamekit-relay")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapHub<SignalRRelayHub>(pattern);
    }
}

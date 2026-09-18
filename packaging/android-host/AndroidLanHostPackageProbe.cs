using System.Net;
using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Discovery.Lan;
using Dihor.GameKit.Networking.Transport.Lan;

namespace Dihor.GameKit.Networking.Packaging.AndroidHost;

public static class AndroidLanHostPackageProbe
{
    public static Task<LanWebSocketTransport> StartAsync(
        IPAddress bindAddress,
        CancellationToken cancellationToken = default) =>
        LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(bindAddress),
            cancellationToken: cancellationToken);

    public static ConnectionDescriptor CreateDescriptor(
        LanWebSocketTransport host,
        string advertisedHost) =>
        host.CreateConnectionDescriptor(advertisedHost);

    public static Type DiscoveryAdvertiserType => typeof(UdpLanDiscoveryAdvertiser);

    public static Type DiscoveryListenerType => typeof(UdpLanDiscoveryListener);
}

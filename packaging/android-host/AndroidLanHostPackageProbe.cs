using System.Net;
using PartyGameKit.Core;
using PartyGameKit.Discovery.Lan;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Packaging.AndroidHost;

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

using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Protocol;

namespace Dihor.GameKit.Networking.Transport.Lan;

public static class LanConnectionDescriptor
{
    public const string TransportName = "lan-websocket";

    public static ConnectionDescriptor Create(
        string host,
        int port,
        ChannelId? channelId = null,
        string path = "/dihor-gamekit-networking",
        bool secure = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        if (path[0] != '/' || path.Contains('?'))
        {
            throw new ArgumentException("WebSocket path must start with '/' and cannot contain a query string.", nameof(path));
        }

        var endpoint = new UriBuilder(secure ? "wss" : "ws", host.Trim(), port, path).Uri;
        return new ConnectionDescriptor(
            TransportName,
            endpoint.AbsoluteUri,
            ProtocolVersions.Current,
            channelId);
    }

    public static Uri GetEndpointUri(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.Transport, TransportName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Connection descriptor is not a LAN WebSocket descriptor.", nameof(descriptor));
        }

        var uri = new Uri(descriptor.Endpoint, UriKind.Absolute);
        if (uri.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException("LAN WebSocket endpoint must use ws or wss.", nameof(descriptor));
        }

        return uri;
    }
}

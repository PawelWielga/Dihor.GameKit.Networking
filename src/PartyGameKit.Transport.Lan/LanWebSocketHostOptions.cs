using System.Net;

namespace PartyGameKit.Transport.Lan;

public sealed class LanWebSocketHostOptions
{
    public LanWebSocketHostOptions(
        IPAddress bindAddress,
        int port = 0,
        string path = "/partygamekit",
        int maxMessageBytes = 256 * 1024,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? keepAliveInterval = null)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");
        }

        if (path[0] != '/' || path.Contains('?'))
        {
            throw new ArgumentException("WebSocket path must start with '/' and cannot contain a query string.", nameof(path));
        }

        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes), maxMessageBytes, "Message limit must be positive.");
        }

        var resolvedHandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5);
        if (resolvedHandshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout), resolvedHandshakeTimeout, "Handshake timeout must be positive.");
        }

        var resolvedKeepAliveInterval = keepAliveInterval ?? TimeSpan.FromSeconds(15);
        if (resolvedKeepAliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(keepAliveInterval), resolvedKeepAliveInterval, "Keep-alive interval must be positive.");
        }

        BindAddress = bindAddress;
        Port = port;
        Path = path;
        MaxMessageBytes = maxMessageBytes;
        HandshakeTimeout = resolvedHandshakeTimeout;
        KeepAliveInterval = resolvedKeepAliveInterval;
    }

    public IPAddress BindAddress { get; }

    public int Port { get; }

    public string Path { get; }

    public int MaxMessageBytes { get; }

    public TimeSpan HandshakeTimeout { get; }

    public TimeSpan KeepAliveInterval { get; }
}

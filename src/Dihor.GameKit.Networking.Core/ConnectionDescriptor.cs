namespace Dihor.GameKit.Networking.Core;

public sealed record ConnectionDescriptor
{
    public ConnectionDescriptor(
        string transport,
        string endpoint,
        int protocolVersion,
        ChannelId? channelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (protocolVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                "Protocol version must be positive.");
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri) || endpointUri.IsFile)
        {
            throw new ArgumentException("Connection endpoint must be an absolute non-file URI.", nameof(endpoint));
        }

        Transport = transport.Trim();
        Endpoint = endpointUri.AbsoluteUri;
        ProtocolVersion = protocolVersion;
        ChannelId = channelId;
    }

    public string Transport { get; }

    public string Endpoint { get; }

    public int ProtocolVersion { get; }

    public ChannelId? ChannelId { get; }
}

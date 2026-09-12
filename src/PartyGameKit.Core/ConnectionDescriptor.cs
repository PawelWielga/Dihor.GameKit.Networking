namespace PartyGameKit.Core;

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

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri))
        {
            throw new ArgumentException("Connection endpoint must be an absolute URI.", nameof(endpoint));
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

namespace PartyGameKit.Core;

public sealed record JoinDescriptor
{
    public JoinDescriptor(
        RoomId roomId,
        JoinCode joinCode,
        string transport,
        string endpoint,
        int protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(roomId.Value))
        {
            throw new ArgumentException("Room ID cannot be empty.", nameof(roomId));
        }

        if (string.IsNullOrWhiteSpace(joinCode.Value))
        {
            throw new ArgumentException("Join code cannot be empty.", nameof(joinCode));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (protocolVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                "Protocol version must be positive.");
        }

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri) ||
            string.IsNullOrWhiteSpace(endpointUri.Scheme))
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        }

        RoomId = roomId;
        JoinCode = joinCode;
        Transport = transport.Trim().ToLowerInvariant();
        Endpoint = endpointUri.AbsoluteUri;
        ProtocolVersion = protocolVersion;
    }

    public RoomId RoomId { get; }

    public JoinCode JoinCode { get; }

    public string Transport { get; }

    public string Endpoint { get; }

    public int ProtocolVersion { get; }
}

namespace Dihor.GameKit.Networking.Core;

public readonly record struct ConnectionId
{
    public ConnectionId(string value)
    {
        Value = IdentifierValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct PeerId
{
    public PeerId(string value)
    {
        Value = IdentifierValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct ChannelId
{
    public ChannelId(string value)
    {
        Value = IdentifierValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

internal static class IdentifierValue
{
    public static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

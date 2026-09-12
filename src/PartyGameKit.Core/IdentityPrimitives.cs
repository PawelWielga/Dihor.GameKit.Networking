namespace PartyGameKit.Core;

public readonly record struct RoomId
{
    public RoomId(string value) => Value = IdentityValue.Normalize(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct PlayerId
{
    public PlayerId(string value) => Value = IdentityValue.Normalize(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct ConnectionId
{
    public ConnectionId(string value) => Value = IdentityValue.Normalize(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct AuthorityId
{
    public AuthorityId(string value) => Value = IdentityValue.Normalize(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct JoinCode
{
    public JoinCode(string value) => Value = IdentityValue.Normalize(value, nameof(value)).ToUpperInvariant();

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

internal static class IdentityValue
{
    public static string Normalize(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        }

        return normalized;
    }
}

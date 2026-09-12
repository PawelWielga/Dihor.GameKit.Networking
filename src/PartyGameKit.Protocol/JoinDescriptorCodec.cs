using System.Text.Json;
using System.Text.Json.Serialization;
using PartyGameKit.Core;

namespace PartyGameKit.Protocol;

public static class JoinDescriptorCodec
{
    public const string UriScheme = "partygamekit";
    public const string UriHost = "join";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public static string SerializeJson(JoinDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return JsonSerializer.Serialize(ToDto(descriptor), JsonOptions);
    }

    public static JoinDescriptor ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            var dto = JsonSerializer.Deserialize<JoinDescriptorDto>(json, JsonOptions)
                ?? throw new FormatException("Join descriptor JSON is empty.");
            return FromDto(dto);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid join descriptor JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("Invalid join descriptor values.", exception);
        }
    }

    public static string SerializeUri(JoinDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return string.Concat(
            UriScheme,
            "://",
            UriHost,
            "?protocolVersion=",
            descriptor.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "&roomId=",
            Escape(descriptor.RoomId.Value),
            "&joinCode=",
            Escape(descriptor.JoinCode.Value),
            "&transport=",
            Escape(descriptor.Transport),
            "&endpoint=",
            Escape(descriptor.Endpoint));
    }

    public static JoinDescriptor ParseUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, UriScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, UriHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Invalid PartyGameKit join URI.");
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("protocolVersion", out var protocolVersionText) ||
            !int.TryParse(
                protocolVersionText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var protocolVersion) ||
            !query.TryGetValue("roomId", out var roomId) ||
            !query.TryGetValue("joinCode", out var joinCode) ||
            !query.TryGetValue("transport", out var transport) ||
            !query.TryGetValue("endpoint", out var endpoint))
        {
            throw new FormatException("Join URI is missing required fields.");
        }

        try
        {
            return new JoinDescriptor(
                new RoomId(roomId),
                new JoinCode(joinCode),
                transport,
                endpoint,
                protocolVersion);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("Invalid join URI values.", exception);
        }
    }

    public static string SerializeText(JoinDescriptor descriptor) => SerializeUri(descriptor);

    public static JoinDescriptor ParseText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.TrimStart().StartsWith('{') ? ParseJson(value) : ParseUri(value);
    }

    private static JoinDescriptorDto ToDto(JoinDescriptor descriptor) =>
        new(
            descriptor.ProtocolVersion,
            descriptor.RoomId.Value,
            descriptor.JoinCode.Value,
            descriptor.Transport,
            descriptor.Endpoint);

    private static JoinDescriptor FromDto(JoinDescriptorDto dto)
    {
        if (dto.ProtocolVersion <= 0 ||
            string.IsNullOrWhiteSpace(dto.RoomId) ||
            string.IsNullOrWhiteSpace(dto.JoinCode) ||
            string.IsNullOrWhiteSpace(dto.Transport) ||
            string.IsNullOrWhiteSpace(dto.Endpoint))
        {
            throw new FormatException("Join descriptor is missing required fields.");
        }

        return new JoinDescriptor(
            new RoomId(dto.RoomId),
            new JoinCode(dto.JoinCode),
            dto.Transport,
            dto.Endpoint,
            dto.ProtocolVersion);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new FormatException("Invalid join URI query.");
            }

            var key = Uri.UnescapeDataString(segment[..separator]);
            var value = Uri.UnescapeDataString(segment[(separator + 1)..]);
            if (!result.TryAdd(key, value))
            {
                throw new FormatException($"Duplicate join URI field '{key}'.");
            }
        }

        return result;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record JoinDescriptorDto(
        [property: JsonPropertyName("protocolVersion"), JsonPropertyOrder(0)] int ProtocolVersion,
        [property: JsonPropertyName("roomId"), JsonPropertyOrder(1)] string RoomId,
        [property: JsonPropertyName("joinCode"), JsonPropertyOrder(2)] string JoinCode,
        [property: JsonPropertyName("transport"), JsonPropertyOrder(3)] string Transport,
        [property: JsonPropertyName("endpoint"), JsonPropertyOrder(4)] string Endpoint);
}

public sealed record DiscoveryAnnouncement(JoinDescriptor Descriptor);

public static class DiscoveryAnnouncementCodec
{
    public const string MessageType = "session.discovery.announce";

    public static string Serialize(DiscoveryAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(announcement.Descriptor);
        return string.Concat(
            "{\"type\":\"",
            MessageType,
            "\",\"protocolVersion\":",
            ProtocolVersions.Current.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ",\"descriptor\":",
            JoinDescriptorCodec.SerializeJson(announcement.Descriptor),
            "}");
    }

    public static DiscoveryAnnouncement Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                !string.Equals(type.GetString(), MessageType, StringComparison.Ordinal) ||
                !root.TryGetProperty("protocolVersion", out var version) ||
                !version.TryGetInt32(out var protocolVersion) ||
                protocolVersion != ProtocolVersions.Current ||
                !root.TryGetProperty("descriptor", out var descriptorElement) ||
                descriptorElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Invalid or incompatible discovery announcement.");
            }

            var descriptor = JoinDescriptorCodec.ParseJson(descriptorElement.GetRawText());
            if (descriptor.ProtocolVersion != ProtocolVersions.Current)
            {
                throw new FormatException("Join descriptor protocol version is incompatible.");
            }

            return new DiscoveryAnnouncement(descriptor);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid discovery announcement JSON.", exception);
        }
    }
}

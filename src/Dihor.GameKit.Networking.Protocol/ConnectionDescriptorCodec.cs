using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Protocol;

public static class ConnectionDescriptorCodec
{
    public const string UriScheme = "dihor-gamekit-networking";
    public const string UriHost = "connect";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public static string SerializeJson(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return JsonSerializer.Serialize(ToDto(descriptor), JsonOptions);
    }

    public static ConnectionDescriptor ParseJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            var dto = JsonSerializer.Deserialize<ConnectionDescriptorDto>(json, JsonOptions)
                ?? throw new FormatException("Connection descriptor JSON is empty.");
            return FromDto(dto);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid connection descriptor JSON.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("Invalid connection descriptor values.", exception);
        }
    }

    public static string SerializeUri(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var value = string.Concat(
            UriScheme,
            "://",
            UriHost,
            "?protocolVersion=",
            descriptor.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            "&transport=",
            Escape(descriptor.Transport),
            "&endpoint=",
            Escape(descriptor.Endpoint));

        return descriptor.ChannelId is { } channelId
            ? string.Concat(value, "&channelId=", Escape(channelId.Value))
            : value;
    }

    public static ConnectionDescriptor ParseUri(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, UriScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, UriHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Invalid Dihor.GameKit.Networking connection URI.");
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("protocolVersion", out var protocolVersionText) ||
            !int.TryParse(protocolVersionText, NumberStyles.None, CultureInfo.InvariantCulture, out var protocolVersion) ||
            !query.TryGetValue("transport", out var transport) ||
            !query.TryGetValue("endpoint", out var endpoint))
        {
            throw new FormatException("Connection URI is missing required fields.");
        }

        try
        {
            var channelId = query.TryGetValue("channelId", out var channelIdText)
                ? new ChannelId(channelIdText)
                : (ChannelId?)null;
            return new ConnectionDescriptor(transport, endpoint, protocolVersion, channelId);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("Invalid connection URI values.", exception);
        }
    }

    public static string SerializeText(ConnectionDescriptor descriptor) => SerializeUri(descriptor);

    public static ConnectionDescriptor ParseText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.TrimStart().StartsWith('{') ? ParseJson(value) : ParseUri(value);
    }

    private static ConnectionDescriptorDto ToDto(ConnectionDescriptor descriptor) =>
        new(
            descriptor.ProtocolVersion,
            descriptor.Transport,
            descriptor.Endpoint,
            descriptor.ChannelId?.Value);

    private static ConnectionDescriptor FromDto(ConnectionDescriptorDto dto)
    {
        if (dto.ProtocolVersion <= 0 ||
            string.IsNullOrWhiteSpace(dto.Transport) ||
            string.IsNullOrWhiteSpace(dto.Endpoint))
        {
            throw new FormatException("Connection descriptor is missing required fields.");
        }

        return new ConnectionDescriptor(
            dto.Transport,
            dto.Endpoint,
            dto.ProtocolVersion,
            string.IsNullOrWhiteSpace(dto.ChannelId) ? null : new ChannelId(dto.ChannelId));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException("Invalid connection URI query.");
            }

            var key = Uri.UnescapeDataString(segment[..separator]);
            var fieldValue = Uri.UnescapeDataString(segment[(separator + 1)..]);
            if (!result.TryAdd(key, fieldValue))
            {
                throw new FormatException($"Duplicate connection URI field '{key}'.");
            }
        }

        return result;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record ConnectionDescriptorDto(
        [property: JsonPropertyName("protocolVersion"), JsonPropertyOrder(0)] int ProtocolVersion,
        [property: JsonPropertyName("transport"), JsonPropertyOrder(1)] string Transport,
        [property: JsonPropertyName("endpoint"), JsonPropertyOrder(2)] string Endpoint,
        [property: JsonPropertyName("channelId"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChannelId);
}

public sealed record DiscoveryEndpointAnnouncement(ConnectionDescriptor Descriptor);

public static class DiscoveryEndpointAnnouncementCodec
{
    public const string MessageType = "connection.discovery.announce";

    public static string Serialize(DiscoveryEndpointAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(announcement.Descriptor);
        return string.Concat(
            "{\"type\":\"",
            MessageType,
            "\",\"protocolVersion\":",
            ProtocolVersions.Current.ToString(CultureInfo.InvariantCulture),
            ",\"descriptor\":",
            ConnectionDescriptorCodec.SerializeJson(announcement.Descriptor),
            "}");
    }

    public static DiscoveryEndpointAnnouncement Parse(string json)
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

            var descriptor = ConnectionDescriptorCodec.ParseJson(descriptorElement.GetRawText());
            if (descriptor.ProtocolVersion != ProtocolVersions.Current)
            {
                throw new FormatException("Connection descriptor protocol version is incompatible.");
            }

            return new DiscoveryEndpointAnnouncement(descriptor);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid discovery announcement JSON.", exception);
        }
    }
}

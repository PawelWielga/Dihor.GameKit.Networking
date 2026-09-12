using System.Text.Json;
using System.Text.Json.Serialization;
using PartyGameKit.Core;

namespace PartyGameKit.Protocol;

public enum ProtocolReadError
{
    None,
    InvalidJson,
    MissingMessageType,
    MessageTypeMismatch,
    MissingProtocolVersion,
    ProtocolVersionMismatch,
    InvalidContract,
}

public sealed record ProtocolReadResult<TPayload>(
    ProtocolEnvelope<TPayload>? Message,
    ProtocolReadError Error,
    int? ReceivedProtocolVersion = null)
{
    public bool IsSuccess => Error == ProtocolReadError.None && Message is not null;
}

public static class ProtocolJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<TPayload>(ProtocolEnvelope<TPayload> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateEnvelope(message.Type, message.ProtocolVersion, message.MessageId, message.CorrelationId);
        return JsonSerializer.Serialize(message, Options);
    }

    public static ProtocolReadResult<TPayload> Read<TPayload>(string json, string expectedType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedType);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new(null, ProtocolReadError.InvalidContract);
            }

            if (!document.RootElement.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
            {
                return new(null, ProtocolReadError.MissingMessageType);
            }

            var messageType = typeProperty.GetString();
            if (!string.Equals(messageType, expectedType, StringComparison.Ordinal))
            {
                return new(null, ProtocolReadError.MessageTypeMismatch);
            }

            if (!document.RootElement.TryGetProperty("protocolVersion", out var versionProperty) || !versionProperty.TryGetInt32(out var version))
            {
                return new(null, ProtocolReadError.MissingProtocolVersion);
            }

            if (version != ProtocolVersions.Current)
            {
                return new(null, ProtocolReadError.ProtocolVersionMismatch, version);
            }

            var message = JsonSerializer.Deserialize<ProtocolEnvelope<TPayload>>(json, Options);
            if (message is null)
            {
                return new(null, ProtocolReadError.InvalidContract);
            }

            ValidateEnvelope(message.Type, message.ProtocolVersion, message.MessageId, message.CorrelationId);
            return new(message, ProtocolReadError.None, version);
        }
        catch (JsonException)
        {
            return new(null, ProtocolReadError.InvalidJson);
        }
        catch (ArgumentException)
        {
            return new(null, ProtocolReadError.InvalidContract);
        }
    }

    private static void ValidateEnvelope(string type, int protocolVersion, string messageId, string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (protocolVersion != ProtocolVersions.Current)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), protocolVersion, "Only the current protocol version can be serialized.");
        }

        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be blank when present.", nameof(correlationId));
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new IdentifierJsonConverter<RoomId>(value => new RoomId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<PlayerId>(value => new PlayerId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<ConnectionId>(value => new ConnectionId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<AuthorityId>(value => new AuthorityId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<JoinCode>(value => new JoinCode(value), value => value.Value));
        options.Converters.Add(new ClientRoleJsonConverter());
        options.Converters.Add(new JoinRejectionCodeJsonConverter());
        return options;
    }

    private sealed class IdentifierJsonConverter<T>(Func<string, T> parse, Func<T, string> format) : JsonConverter<T>
        where T : struct
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (value is null)
            {
                throw new JsonException($"{typeof(T).Name} must be a string.");
            }

            try
            {
                return parse(value);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException($"Invalid {typeof(T).Name}.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var serialized = format(value);
            if (string.IsNullOrWhiteSpace(serialized))
            {
                throw new JsonException($"{typeof(T).Name} cannot be empty.");
            }

            writer.WriteStringValue(serialized);
        }
    }

    private sealed class ClientRoleJsonConverter : JsonConverter<ClientRole>
    {
        public override ClientRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "host" => ClientRole.Host,
                "player" => ClientRole.Player,
                "shared-screen" => ClientRole.SharedScreen,
                _ => throw new JsonException("Unknown client role."),
            };
        }

        public override void Write(Utf8JsonWriter writer, ClientRole value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ClientRole.Host => "host",
                ClientRole.Player => "player",
                ClientRole.SharedScreen => "shared-screen",
                _ => throw new JsonException("Unknown client role."),
            });
        }
    }

    private sealed class JoinRejectionCodeJsonConverter : JsonConverter<JoinRejectionCode>
    {
        public override JoinRejectionCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "room-not-found" => JoinRejectionCode.RoomNotFound,
                "room-closed" => JoinRejectionCode.RoomClosed,
                "room-full" => JoinRejectionCode.RoomFull,
                "protocol-mismatch" => JoinRejectionCode.ProtocolMismatch,
                "resume-rejected" => JoinRejectionCode.ResumeRejected,
                _ => throw new JsonException("Unknown join rejection code."),
            };
        }

        public override void Write(Utf8JsonWriter writer, JoinRejectionCode value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                JoinRejectionCode.RoomNotFound => "room-not-found",
                JoinRejectionCode.RoomClosed => "room-closed",
                JoinRejectionCode.RoomFull => "room-full",
                JoinRejectionCode.ProtocolMismatch => "protocol-mismatch",
                JoinRejectionCode.ResumeRejected => "resume-rejected",
                _ => throw new JsonException("Unknown join rejection code."),
            });
        }
    }
}

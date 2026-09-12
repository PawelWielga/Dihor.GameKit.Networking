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

            if (!document.RootElement.TryGetProperty("type", out var typeProperty) ||
                typeProperty.ValueKind != JsonValueKind.String)
            {
                return new(null, ProtocolReadError.MissingMessageType);
            }

            if (!string.Equals(typeProperty.GetString(), expectedType, StringComparison.Ordinal))
            {
                return new(null, ProtocolReadError.MessageTypeMismatch);
            }

            if (!document.RootElement.TryGetProperty("protocolVersion", out var versionProperty) ||
                !versionProperty.TryGetInt32(out var version))
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
                return new(null, ProtocolReadError.InvalidContract, version);
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

    private static void ValidateEnvelope(
        string type,
        int protocolVersion,
        string messageId,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (protocolVersion != ProtocolVersions.Current)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                "Only the current protocol version can be serialized.");
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
        options.Converters.Add(new IdentifierJsonConverter<PeerId>(value => new PeerId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<ConnectionId>(value => new ConnectionId(value), value => value.Value));
        options.Converters.Add(new IdentifierJsonConverter<ChannelId>(value => new ChannelId(value), value => value.Value));
        options.Converters.Add(new MessageSequenceJsonConverter());
        options.Converters.Add(new ConnectionRejectionCodeJsonConverter());
        return options;
    }

    private sealed class IdentifierJsonConverter<T>(Func<string, T> parse, Func<T, string> format) : JsonConverter<T>
        where T : struct
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"{typeof(T).Name} must be a string.");
            }

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

    private sealed class MessageSequenceJsonConverter : JsonConverter<MessageSequence>
    {
        public override MessageSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!reader.TryGetInt64(out var value))
            {
                throw new JsonException("Message sequence must be an integer.");
            }

            try
            {
                return new MessageSequence(value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new JsonException("Message sequence must be positive.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, MessageSequence value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private sealed class ConnectionRejectionCodeJsonConverter : JsonConverter<ConnectionRejectionCode>
    {
        public override ConnectionRejectionCode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Connection rejection code must be a string.");
            }

            return reader.GetString() switch
            {
                "protocol-mismatch" => ConnectionRejectionCode.ProtocolMismatch,
                "invalid-request" => ConnectionRejectionCode.InvalidRequest,
                "unknown-peer" => ConnectionRejectionCode.UnknownPeer,
                "invalid-resume-credential" => ConnectionRejectionCode.InvalidResumeCredential,
                "reconnect-window-expired" => ConnectionRejectionCode.ReconnectWindowExpired,
                "peer-already-connected" => ConnectionRejectionCode.PeerAlreadyConnected,
                "connection-already-bound" => ConnectionRejectionCode.ConnectionAlreadyBound,
                _ => throw new JsonException("Unknown connection rejection code."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            ConnectionRejectionCode value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                ConnectionRejectionCode.ProtocolMismatch => "protocol-mismatch",
                ConnectionRejectionCode.InvalidRequest => "invalid-request",
                ConnectionRejectionCode.UnknownPeer => "unknown-peer",
                ConnectionRejectionCode.InvalidResumeCredential => "invalid-resume-credential",
                ConnectionRejectionCode.ReconnectWindowExpired => "reconnect-window-expired",
                ConnectionRejectionCode.PeerAlreadyConnected => "peer-already-connected",
                ConnectionRejectionCode.ConnectionAlreadyBound => "connection-already-bound",
                _ => throw new JsonException("Unknown connection rejection code."),
            });
        }
    }
}

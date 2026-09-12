using System.Text.Json;
using System.Text.Json.Serialization;
using PartyGameKit.Core;

namespace PartyGameKit.Protocol;

public static class ProtocolVersions
{
    public const int Current = 2;
}

public static class ProtocolMessageTypes
{
    public const string ConnectRequest = "connection.connect.request";
    public const string ConnectAccepted = "connection.connect.accepted";
    public const string ConnectRejected = "connection.connect.rejected";
    public const string ResumeRequest = "connection.resume.request";
    public const string ResumeAccepted = "connection.resume.accepted";
    public const string ResumeRejected = "connection.resume.rejected";
    public const string Heartbeat = "connection.heartbeat";
    public const string Disconnect = "connection.disconnect";
    public const string ApplicationMessage = "application.message";
}

public sealed record ProtocolEnvelope<TPayload>(
    [property: JsonPropertyName("type"), JsonPropertyOrder(0)] string Type,
    [property: JsonPropertyName("protocolVersion"), JsonPropertyOrder(1)] int ProtocolVersion,
    [property: JsonPropertyName("messageId"), JsonPropertyOrder(2)] string MessageId,
    [property: JsonPropertyName("correlationId"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId,
    [property: JsonPropertyName("payload"), JsonPropertyOrder(4)] TPayload Payload);

public sealed record ConnectRequestPayload(
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(0), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PeerId? PeerId = null);

public sealed record ConnectAcceptedPayload(
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(0)] ConnectionId ConnectionId,
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PeerId? PeerId = null,
    [property: JsonPropertyName("resumeToken"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResumeToken = null);

public enum ConnectionRejectionCode
{
    ProtocolMismatch,
    InvalidRequest,
    UnknownPeer,
    InvalidResumeCredential,
    ReconnectWindowExpired,
    PeerAlreadyConnected,
    ConnectionAlreadyBound,
}

public sealed record ConnectRejectedPayload(
    [property: JsonPropertyName("code"), JsonPropertyOrder(0)] ConnectionRejectionCode Code,
    [property: JsonPropertyName("reason"), JsonPropertyOrder(1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

public sealed record ResumeRequestPayload(
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(0)] PeerId PeerId,
    [property: JsonPropertyName("resumeToken"), JsonPropertyOrder(1)] string ResumeToken);

public sealed record ResumeAcceptedPayload(
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(0)] ConnectionId ConnectionId,
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(1)] PeerId PeerId,
    [property: JsonPropertyName("resumeToken"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResumeToken = null);

public sealed record ResumeRejectedPayload(
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(0)] PeerId PeerId,
    [property: JsonPropertyName("code"), JsonPropertyOrder(1)] ConnectionRejectionCode Code,
    [property: JsonPropertyName("reason"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

public sealed record HeartbeatPayload(
    [property: JsonPropertyName("peerId"), JsonPropertyOrder(0), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PeerId? PeerId = null);

public sealed record DisconnectPayload(
    [property: JsonPropertyName("reason"), JsonPropertyOrder(0), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);

public sealed record ApplicationMessagePayload
{
    [JsonConstructor]
    public ApplicationMessagePayload(string applicationType, JsonElement data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationType);
        ApplicationType = applicationType.Trim();
        Data = data.Clone();
    }

    [JsonPropertyName("applicationType"), JsonPropertyOrder(0)]
    public string ApplicationType { get; }

    [JsonPropertyName("data"), JsonPropertyOrder(1)]
    public JsonElement Data { get; }
}

public static class PartyGameKitMessages
{
    public static ProtocolEnvelope<TPayload> Create<TPayload>(
        string type,
        string messageId,
        TPayload payload,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be blank when present.", nameof(correlationId));
        }

        return new ProtocolEnvelope<TPayload>(
            Type: type,
            ProtocolVersion: ProtocolVersions.Current,
            MessageId: messageId,
            CorrelationId: correlationId,
            Payload: payload);
    }
}

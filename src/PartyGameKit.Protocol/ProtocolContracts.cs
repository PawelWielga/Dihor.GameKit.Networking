using System.Text.Json.Serialization;
using PartyGameKit.Core;

namespace PartyGameKit.Protocol;

public static class ProtocolVersions
{
    public const int Current = 1;
}

public static class ProtocolMessageTypes
{
    public const string JoinRequest = "session.join.request";
    public const string JoinAccepted = "session.join.accepted";
    public const string JoinRejected = "session.join.rejected";
    public const string Leave = "session.leave";
    public const string Disconnected = "session.disconnected";
    public const string RejoinRequest = "session.rejoin.request";
    public const string RejoinAccepted = "session.rejoin.accepted";
    public const string StateSnapshot = "state.snapshot";
}

public sealed record ProtocolEnvelope<TPayload>(
    [property: JsonPropertyName("type"), JsonPropertyOrder(0)] string Type,
    [property: JsonPropertyName("protocolVersion"), JsonPropertyOrder(1)] int ProtocolVersion,
    [property: JsonPropertyName("messageId"), JsonPropertyOrder(2)] string MessageId,
    [property: JsonPropertyName("correlationId"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId,
    [property: JsonPropertyName("payload"), JsonPropertyOrder(4)] TPayload Payload);

public sealed record JoinRequestPayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("joinCode"), JsonPropertyOrder(1)] JoinCode JoinCode,
    [property: JsonPropertyName("role"), JsonPropertyOrder(2)] ClientRole Role,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerId? PlayerId);

public sealed record JoinAcceptedPayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(1)] ConnectionId ConnectionId,
    [property: JsonPropertyName("role"), JsonPropertyOrder(2)] ClientRole Role,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerId? PlayerId,
    [property: JsonPropertyName("authorityId"), JsonPropertyOrder(4)] AuthorityId AuthorityId);

public enum JoinRejectionCode
{
    RoomNotFound,
    RoomClosed,
    RoomFull,
    ProtocolMismatch,
    ResumeRejected,
}

public sealed record JoinRejectedPayload(
    [property: JsonPropertyName("joinCode"), JsonPropertyOrder(0)] JoinCode JoinCode,
    [property: JsonPropertyName("code"), JsonPropertyOrder(1)] JoinRejectionCode Code,
    [property: JsonPropertyName("reason"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

public sealed record LeavePayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(1)] ConnectionId ConnectionId,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerId? PlayerId);

public sealed record DisconnectedPayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(1)] ConnectionId ConnectionId,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerId? PlayerId,
    [property: JsonPropertyName("reason"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason);

public sealed record RejoinRequestPayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(1)] PlayerId PlayerId,
    [property: JsonPropertyName("reconnectToken"), JsonPropertyOrder(2)] string ReconnectToken,
    [property: JsonPropertyName("lastSeenSnapshotSequence"), JsonPropertyOrder(3)] long LastSeenSnapshotSequence);

public sealed record RejoinAcceptedPayload(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("playerId"), JsonPropertyOrder(1)] PlayerId PlayerId,
    [property: JsonPropertyName("connectionId"), JsonPropertyOrder(2)] ConnectionId ConnectionId,
    [property: JsonPropertyName("authorityId"), JsonPropertyOrder(3)] AuthorityId AuthorityId);

public sealed record StateSnapshotPayload<TState>(
    [property: JsonPropertyName("roomId"), JsonPropertyOrder(0)] RoomId RoomId,
    [property: JsonPropertyName("authorityId"), JsonPropertyOrder(1)] AuthorityId AuthorityId,
    [property: JsonPropertyName("sequence"), JsonPropertyOrder(2)] SnapshotSequence Sequence,
    [property: JsonPropertyName("target"), JsonPropertyOrder(3)] SnapshotTarget Target,
    [property: JsonPropertyName("state"), JsonPropertyOrder(4)] TState State);

public static class PartyGameKitMessages
{
    public static ProtocolEnvelope<TPayload> Create<TPayload>(
        string type,
        string messageId,
        TPayload payload,
        string? correlationId = null)
    {
        return new ProtocolEnvelope<TPayload>(
            Type: type,
            ProtocolVersion: ProtocolVersions.Current,
            MessageId: messageId,
            CorrelationId: correlationId,
            Payload: payload);
    }
}

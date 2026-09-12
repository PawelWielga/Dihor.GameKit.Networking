using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class ProtocolFixtureTests
{
    [Fact]
    public void SuccessfulJoinMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinAccepted,
            "msg-join-accepted-1",
            new JoinAcceptedPayload(
                new RoomId("room-001"),
                new ConnectionId("connection-002"),
                ClientRole.Player,
                new PlayerId("player-001"),
                new AuthorityId("authority-001")),
            "msg-join-request-1");

        AssertFixture("join-success.json", ProtocolJson.Serialize(message));
    }

    [Fact]
    public void RejectedJoinMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRejected,
            "msg-join-rejected-1",
            new JoinRejectedPayload(new JoinCode("room42"), JoinRejectionCode.RoomFull, "Room is full."),
            "msg-join-request-2");

        AssertFixture("join-rejected.json", ProtocolJson.Serialize(message));
    }

    [Fact]
    public void RejoinMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRequest,
            "msg-rejoin-1",
            new RejoinRequestPayload(new RoomId("room-001"), new PlayerId("player-001"), "opaque-reconnect-token", 41));

        AssertFixture("rejoin.json", ProtocolJson.Serialize(message));
    }

    [Fact]
    public void CanonicalFixtureCanBeReadWithoutClrTypeMetadata()
    {
        var json = ReadFixture("join-success.json");

        var result = ProtocolJson.Read<JoinAcceptedPayload>(json, ProtocolMessageTypes.JoinAccepted);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PlayerId("player-001"), result.Message!.Payload.PlayerId);
        Assert.Equal(new ConnectionId("connection-002"), result.Message.Payload.ConnectionId);
        Assert.Equal(ClientRole.Player, result.Message.Payload.Role);
    }

    [Fact]
    public void ProtocolVersionMismatchIsRejectedExplicitly()
    {
        var json = ReadFixture("join-success.json").Replace("\"protocolVersion\":1", "\"protocolVersion\":99", StringComparison.Ordinal);

        var result = ProtocolJson.Read<JoinAcceptedPayload>(json, ProtocolMessageTypes.JoinAccepted);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolReadError.ProtocolVersionMismatch, result.Error);
        Assert.Equal(99, result.ReceivedProtocolVersion);
    }

    [Fact]
    public void WrongMessageTypeIsRejectedBeforePayloadHandling()
    {
        var result = ProtocolJson.Read<JoinAcceptedPayload>(ReadFixture("join-rejected.json"), ProtocolMessageTypes.JoinAccepted);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolReadError.MessageTypeMismatch, result.Error);
    }

    private static void AssertFixture(string name, string actual)
    {
        Assert.Equal(ReadFixture(name), actual);
    }

    private static string ReadFixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", name)).TrimEnd();
    }
}

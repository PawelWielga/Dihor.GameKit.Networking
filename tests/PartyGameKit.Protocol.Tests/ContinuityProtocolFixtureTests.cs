using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class ContinuityProtocolFixtureTests
{
    [Fact]
    public void HeartbeatMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.Heartbeat,
            "heartbeat-1",
            new HeartbeatPayload(new RoomId("room-001"), 42));

        Assert.Equal(ReadFixture("heartbeat.json"), ProtocolJson.Serialize(message));
    }

    [Fact]
    public void RejoinRejectionMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRejected,
            "rejoin-rejected-1",
            new RejoinRejectedPayload(
                new RoomId("room-001"),
                RejoinRejectionCodes.ReconnectWindowExpired,
                "Reconnect window expired."),
            "msg-rejoin-1");

        Assert.Equal(ReadFixture("rejoin-rejected.json"), ProtocolJson.Serialize(message));
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", name)).TrimEnd();
}

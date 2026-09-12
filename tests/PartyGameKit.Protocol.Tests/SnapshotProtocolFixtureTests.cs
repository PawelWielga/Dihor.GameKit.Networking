using System.Text.Json;
using PartyGameKit.Core;
using PartyGameKit.Protocol;

namespace PartyGameKit.Protocol.Tests;

public sealed class SnapshotProtocolFixtureTests
{
    [Fact]
    public void PublicSnapshotMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.StateSnapshot,
            "snapshot-42-public",
            new StateSnapshotPayload<JsonElement>(
                new RoomId("room-001"),
                new AuthorityId("authority-001"),
                new SnapshotSequence(42),
                SnapshotTarget.Public,
                JsonState("{\"revision\":\"public-42\"}")));

        Assert.Equal(ReadFixture("snapshot-public.json"), ProtocolJson.Serialize(message));
    }

    [Fact]
    public void PlayerSnapshotMatchesCanonicalFixture()
    {
        var message = PartyGameKitMessages.Create(
            ProtocolMessageTypes.StateSnapshot,
            "snapshot-42-player-1",
            new StateSnapshotPayload<JsonElement>(
                new RoomId("room-001"),
                new AuthorityId("authority-001"),
                new SnapshotSequence(42),
                SnapshotTarget.ForPlayer(new PlayerId("player-001")),
                JsonState("{\"revision\":\"player-42\"}")));

        Assert.Equal(ReadFixture("snapshot-player.json"), ProtocolJson.Serialize(message));
    }

    [Fact]
    public void CanonicalPlayerSnapshotCanBeReadWithoutClrMetadata()
    {
        var result = ProtocolJson.Read<StateSnapshotPayload<JsonElement>>(
            ReadFixture("snapshot-player.json"),
            ProtocolMessageTypes.StateSnapshot);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Message!.Payload.Sequence.Value);
        Assert.Equal(SnapshotAudience.Player, result.Message.Payload.Target.Audience);
        Assert.Equal(new PlayerId("player-001"), result.Message.Payload.Target.PlayerId);
        Assert.Equal("player-42", result.Message.Payload.State.GetProperty("revision").GetString());
    }

    [Fact]
    public void InvalidPlayerTargetIsRejected()
    {
        var json = ReadFixture("snapshot-player.json")
            .Replace(",\"playerId\":\"player-001\"", string.Empty, StringComparison.Ordinal);

        var result = ProtocolJson.Read<StateSnapshotPayload<JsonElement>>(
            json,
            ProtocolMessageTypes.StateSnapshot);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProtocolReadError.InvalidJson, result.Error);
    }

    private static JsonElement JsonState(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "protocol", "fixtures", name)).TrimEnd();
}

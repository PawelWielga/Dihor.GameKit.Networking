using System.Net;
using System.Text;
using System.Text.Json;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Sample.DungeonPrototype;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Sample.DungeonPrototype.Tests;

public sealed class DungeonPrototypeHappyPathTests
{
    [Fact]
    public async Task DungeonFlowKeepsPrivateInventoryAndActiveTurnAcrossReconnect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await DungeonPrototypeHost.StartAsync(
            "127.0.0.1",
            IPAddress.Loopback,
            port: 0,
            cancellationToken: cancellationToken);

        await using var sharedScreen = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            JoinRequest(ClientRole.SharedScreen, playerId: null, "join-screen"),
            cancellationToken: cancellationToken);
        var sharedAccepted = await ReadJoinAcceptedAsync(sharedScreen, cancellationToken);
        Assert.Equal(ClientRole.SharedScreen, sharedAccepted.Role);
        _ = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("phase").GetString() == "lobby",
            cancellationToken);

        var player1Id = new PlayerId("dungeon-player-1");
        await using var player1 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            JoinRequest(ClientRole.Player, player1Id, "join-player-1"),
            cancellationToken: cancellationToken);
        var player1Accepted = await ReadJoinAcceptedAsync(player1, cancellationToken);
        Assert.Equal(player1Id, player1Accepted.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(player1Accepted.ReconnectToken));
        _ = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id),
            cancellationToken);

        var player2Id = new PlayerId("dungeon-player-2");
        await using var player2 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            JoinRequest(ClientRole.Player, player2Id, "join-player-2"),
            cancellationToken: cancellationToken);
        var player2Accepted = await ReadJoinAcceptedAsync(player2, cancellationToken);
        Assert.Equal(player2Id, player2Accepted.PlayerId);
        _ = await ReadSnapshotAsync(
            player2,
            snapshot => IsPrivateSnapshot(snapshot, player2Id),
            cancellationToken);

        Assert.Equal(3, host.Session.Clients.Count);
        Assert.Equal(2, host.Session.Players.Count);

        await SendCommandAsync(
            player1,
            "{\"type\":\"sample.dungeon.select-character\",\"character\":\"scout\"}",
            cancellationToken);
        await SendCommandAsync(
            player2,
            "{\"type\":\"sample.dungeon.select-character\",\"character\":\"guardian\"}",
            cancellationToken);

        var active = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("phase").GetString() == "active" &&
                snapshot.State.GetProperty("activePlayerId").GetString() == player1Id.Value &&
                snapshot.State.GetProperty("actionPoints").GetInt32() == 2,
            cancellationToken);
        Assert.Equal(2, active.State.GetProperty("players").GetArrayLength());

        _ = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id) &&
                snapshot.State.GetProperty("privateState").GetProperty("character").GetString() == "scout" &&
                snapshot.State.GetProperty("privateState").GetProperty("isYourTurn").GetBoolean(),
            cancellationToken);

        await SendCommandAsync(
            player1,
            "{\"type\":\"sample.dungeon.move\",\"dx\":1,\"dy\":0}",
            cancellationToken);
        var keyPickedUp = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id) &&
                InventoryContains(snapshot.State.GetProperty("privateState"), "Rusty Key") &&
                snapshot.State.GetProperty("privateState").GetProperty("actionPoints").GetInt32() == 1,
            cancellationToken);
        Assert.Equal(
            JsonValueKind.Null,
            keyPickedUp.State.GetProperty("publicState").GetProperty("keyPosition").ValueKind);

        await SendCommandAsync(
            player1,
            "{\"type\":\"sample.dungeon.end-turn\"}",
            cancellationToken);
        _ = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("activePlayerId").GetString() == player2Id.Value,
            cancellationToken);

        await SendCommandAsync(
            player2,
            "{\"type\":\"sample.dungeon.end-turn\"}",
            cancellationToken);
        _ = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("activePlayerId").GetString() == player1Id.Value &&
                snapshot.State.GetProperty("actionPoints").GetInt32() == 2,
            cancellationToken);

        await player1.DisposeAsync();
        await using var rejoinedPlayer1 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            RejoinRequest(player1Id, player1Accepted.ReconnectToken!, "rejoin-player-1"),
            cancellationToken: cancellationToken);
        var rejoinAccepted = await ReadRejoinAcceptedAsync(rejoinedPlayer1, cancellationToken);
        Assert.Equal(player1Id, rejoinAccepted.PlayerId);

        var restored = await ReadSnapshotAsync(
            rejoinedPlayer1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id) &&
                InventoryContains(snapshot.State.GetProperty("privateState"), "Rusty Key") &&
                snapshot.State.GetProperty("privateState").GetProperty("isYourTurn").GetBoolean() &&
                snapshot.State.GetProperty("privateState").GetProperty("actionPoints").GetInt32() == 2,
            cancellationToken);
        Assert.Equal(
            "scout",
            restored.State.GetProperty("privateState").GetProperty("character").GetString());

        await SendCommandAsync(
            rejoinedPlayer1,
            "{\"type\":\"sample.dungeon.move\",\"dx\":1,\"dy\":0}",
            cancellationToken);
        await SendCommandAsync(
            rejoinedPlayer1,
            "{\"type\":\"sample.dungeon.attack\"}",
            cancellationToken);

        var afterCombat = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("enemy").GetProperty("hitPoints").GetInt32() == 1 &&
                snapshot.State.GetProperty("activePlayerId").GetString() == player2Id.Value,
            cancellationToken);
        Assert.True(afterCombat.State.GetProperty("enemy").GetProperty("alive").GetBoolean());
        Assert.Equal(2, host.Session.Players.Count);
    }

    private static string JoinRequest(ClientRole role, PlayerId? playerId, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRequest,
            messageId,
            new JoinRequestPayload(
                new RoomId("dungeon-prototype-room"),
                new JoinCode("DUNGE1"),
                role,
                playerId)));

    private static string RejoinRequest(PlayerId playerId, string token, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRequest,
            messageId,
            new RejoinRequestPayload(
                new RoomId("dungeon-prototype-room"),
                playerId,
                token,
                0)));

    private static ValueTask SendCommandAsync(
        LanWebSocketClient client,
        string json,
        CancellationToken cancellationToken) =>
        client.SendAsync(Encoding.UTF8.GetBytes(json), cancellationToken);

    private static async Task<JoinAcceptedPayload> ReadJoinAcceptedAsync(
        LanWebSocketClient client,
        CancellationToken cancellationToken)
    {
        var frame = await ReceiveWithTimeoutAsync(client, cancellationToken);
        Assert.False(frame.IsClose);
        var read = ProtocolJson.Read<JoinAcceptedPayload>(
            Encoding.UTF8.GetString(frame.Payload.Span),
            ProtocolMessageTypes.JoinAccepted);
        Assert.True(read.IsSuccess);
        return read.Message!.Payload;
    }

    private static async Task<RejoinAcceptedPayload> ReadRejoinAcceptedAsync(
        LanWebSocketClient client,
        CancellationToken cancellationToken)
    {
        var frame = await ReceiveWithTimeoutAsync(client, cancellationToken);
        Assert.False(frame.IsClose);
        var read = ProtocolJson.Read<RejoinAcceptedPayload>(
            Encoding.UTF8.GetString(frame.Payload.Span),
            ProtocolMessageTypes.RejoinAccepted);
        Assert.True(read.IsSuccess);
        return read.Message!.Payload;
    }

    private static async Task<StateSnapshotPayload<JsonElement>> ReadSnapshotAsync(
        LanWebSocketClient client,
        Func<StateSnapshotPayload<JsonElement>, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var frame = await ReceiveWithTimeoutAsync(client, cancellationToken);
            Assert.False(frame.IsClose);
            var json = Encoding.UTF8.GetString(frame.Payload.Span);
            var read = ProtocolJson.Read<StateSnapshotPayload<JsonElement>>(json, ProtocolMessageTypes.StateSnapshot);
            if (read.IsSuccess && predicate(read.Message!.Payload))
            {
                return read.Message.Payload;
            }
        }

        throw new InvalidOperationException("Expected dungeon snapshot was not received within 40 frames.");
    }

    private static async Task<LanWebSocketClientMessage> ReceiveWithTimeoutAsync(
        LanWebSocketClient client,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await client.ReceiveAsync(timeout.Token);
    }

    private static bool IsPrivateSnapshot(
        StateSnapshotPayload<JsonElement> snapshot,
        PlayerId playerId) =>
        snapshot.Target.Audience == SnapshotAudience.Player &&
        snapshot.Target.PlayerId == playerId &&
        snapshot.State.TryGetProperty("privateState", out _);

    private static bool InventoryContains(JsonElement privateState, string item)
    {
        if (!privateState.TryGetProperty("inventory", out var inventory) ||
            inventory.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return inventory.EnumerateArray().Any(entry => entry.GetString() == item);
    }
}

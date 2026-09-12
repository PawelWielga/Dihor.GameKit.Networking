using System.Net;
using System.Text;
using System.Text.Json;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Sample.SharedCounter;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Sample.SharedCounter.Tests;

public sealed class SharedCounterHappyPathTests
{
    [Fact]
    public async Task TwoPlayersSharedScreenIncrementAndReconnectKeepAuthoritativeState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await SharedCounterHost.StartAsync(
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
        var initialPublic = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public,
            cancellationToken);
        Assert.Equal(0, initialPublic.State.GetProperty("total").GetInt32());

        var player1Id = new PlayerId("sample-player-1");
        await using var player1 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            JoinRequest(ClientRole.Player, player1Id, "join-player-1"),
            cancellationToken: cancellationToken);
        var player1Accepted = await ReadJoinAcceptedAsync(player1, cancellationToken);
        Assert.Equal(player1Id, player1Accepted.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(player1Accepted.ReconnectToken));
        _ = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 0),
            cancellationToken);

        var player2Id = new PlayerId("sample-player-2");
        await using var player2 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            JoinRequest(ClientRole.Player, player2Id, "join-player-2"),
            cancellationToken: cancellationToken);
        var player2Accepted = await ReadJoinAcceptedAsync(player2, cancellationToken);
        Assert.Equal(player2Id, player2Accepted.PlayerId);
        _ = await ReadSnapshotAsync(
            player2,
            snapshot => IsPrivateSnapshot(snapshot, player2Id, yourCount: 0),
            cancellationToken);

        Assert.Equal(3, host.Session.Clients.Count);
        Assert.Equal(2, host.Session.Players.Count);

        await player1.SendAsync(
            Encoding.UTF8.GetBytes("{\"type\":\"sample.counter.increment\"}"),
            cancellationToken);

        var sharedAfterIncrement = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public &&
                snapshot.State.GetProperty("total").GetInt32() == 1 &&
                snapshot.State.GetProperty("players").GetArrayLength() == 2,
            cancellationToken);
        Assert.Equal(1, sharedAfterIncrement.State.GetProperty("total").GetInt32());

        var player1AfterIncrement = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 1),
            cancellationToken);
        Assert.Equal(
            1,
            player1AfterIncrement.State.GetProperty("privateState").GetProperty("yourCount").GetInt32());

        var player2AfterIncrement = await ReadSnapshotAsync(
            player2,
            snapshot => IsPrivateSnapshot(snapshot, player2Id, yourCount: 0) &&
                snapshot.State.GetProperty("publicState").GetProperty("total").GetInt32() == 1,
            cancellationToken);
        Assert.Equal(
            0,
            player2AfterIncrement.State.GetProperty("privateState").GetProperty("yourCount").GetInt32());

        await player1.DisposeAsync();
        await using var rejoinedPlayer1 = await LanWebSocketClient.ConnectAsync(
            host.ClientUri,
            RejoinRequest(player1Id, player1Accepted.ReconnectToken!, "rejoin-player-1"),
            cancellationToken: cancellationToken);
        var rejoinAccepted = await ReadRejoinAcceptedAsync(rejoinedPlayer1, cancellationToken);
        Assert.Equal(player1Id, rejoinAccepted.PlayerId);

        var restored = await ReadSnapshotAsync(
            rejoinedPlayer1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 1) &&
                snapshot.State.GetProperty("publicState").GetProperty("total").GetInt32() == 1,
            cancellationToken);
        Assert.Equal(
            1,
            restored.State.GetProperty("privateState").GetProperty("yourCount").GetInt32());
        Assert.Equal(2, host.Session.Players.Count);
    }

    private static string JoinRequest(ClientRole role, PlayerId? playerId, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRequest,
            messageId,
            new JoinRequestPayload(
                new RoomId("shared-counter-room"),
                new JoinCode("COUNT1"),
                role,
                playerId)));

    private static string RejoinRequest(PlayerId playerId, string token, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRequest,
            messageId,
            new RejoinRequestPayload(
                new RoomId("shared-counter-room"),
                playerId,
                token,
                0)));

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
        for (var attempt = 0; attempt < 20; attempt++)
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

        throw new InvalidOperationException("Expected snapshot was not received within 20 frames.");
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
        PlayerId playerId,
        int yourCount) =>
        snapshot.Target.Audience == SnapshotAudience.Player &&
        snapshot.Target.PlayerId == playerId &&
        snapshot.State.TryGetProperty("privateState", out var privateState) &&
        privateState.TryGetProperty("yourCount", out var count) &&
        count.GetInt32() == yourCount;
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Sample.SharedCounter;
using PartyGameKit.Transport.SignalR;

namespace PartyGameKit.Transport.SignalR.Tests;

public sealed class SignalRSharedCounterTests
{
    [Fact]
    public async Task SharedCounterRunsThroughSignalRAndRejoinsWithStableIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddPartyGameKitSignalR();

        await using var app = builder.Build();
        app.MapPartyGameKitSignalR();
        await app.StartAsync(cancellationToken);

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose server addresses.");
        var baseAddress = new Uri(addresses.Addresses.Single());
        var endpoint = new Uri(baseAddress, "/partygamekit");

        var registry = app.Services.GetRequiredService<SignalRRoomRegistry>();
        var transport = registry.RegisterRoom(SharedCounterHost.RoomId, SharedCounterHost.JoinCode);
        var descriptor = new JoinDescriptor(
            SharedCounterHost.RoomId,
            SharedCounterHost.JoinCode,
            "signalr",
            endpoint.AbsoluteUri,
            ProtocolVersions.Current);
        await using var host = SharedCounterHost.StartWithTransport(transport, descriptor);

        await using var sharedScreen = await SignalRGameClient.ConnectAsync(
            endpoint,
            JoinRequest(ClientRole.SharedScreen, playerId: null, "join-screen"),
            cancellationToken);
        var sharedAccepted = await ReadJoinAcceptedAsync(sharedScreen, cancellationToken);
        Assert.Equal(ClientRole.SharedScreen, sharedAccepted.Role);
        var initialPublic = await ReadSnapshotAsync(
            sharedScreen,
            snapshot => snapshot.Target.Audience == SnapshotAudience.Public,
            cancellationToken);
        Assert.Equal(0, initialPublic.State.GetProperty("total").GetInt32());

        var player1Id = new PlayerId("signalr-player-1");
        await using var player1 = await SignalRGameClient.ConnectAsync(
            endpoint,
            JoinRequest(ClientRole.Player, player1Id, "join-player-1"),
            cancellationToken);
        var player1Accepted = await ReadJoinAcceptedAsync(player1, cancellationToken);
        Assert.Equal(player1Id, player1Accepted.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(player1Accepted.ReconnectToken));
        _ = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 0),
            cancellationToken);

        var player2Id = new PlayerId("signalr-player-2");
        await using var player2 = await SignalRGameClient.ConnectAsync(
            endpoint,
            JoinRequest(ClientRole.Player, player2Id, "join-player-2"),
            cancellationToken);
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
                snapshot.State.GetProperty("total").GetInt32() == 1,
            cancellationToken);
        Assert.Equal(1, sharedAfterIncrement.State.GetProperty("total").GetInt32());

        _ = await ReadSnapshotAsync(
            player1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 1),
            cancellationToken);
        _ = await ReadSnapshotAsync(
            player2,
            snapshot => IsPrivateSnapshot(snapshot, player2Id, yourCount: 0) &&
                snapshot.State.GetProperty("publicState").GetProperty("total").GetInt32() == 1,
            cancellationToken);

        await player1.DisposeAsync();
        await WaitUntilAsync(
            () => host.Session.FindPlayer(player1Id)?.Presence == PlayerPresence.Disconnected,
            cancellationToken);

        await using var rejoinedPlayer1 = await SignalRGameClient.ConnectAsync(
            endpoint,
            RejoinRequest(player1Id, player1Accepted.ReconnectToken!, "rejoin-player-1"),
            cancellationToken);
        var rejoinAccepted = await ReadRejoinAcceptedAsync(rejoinedPlayer1, cancellationToken);
        Assert.Equal(player1Id, rejoinAccepted.PlayerId);

        var restored = await ReadSnapshotAsync(
            rejoinedPlayer1,
            snapshot => IsPrivateSnapshot(snapshot, player1Id, yourCount: 1) &&
                snapshot.State.GetProperty("publicState").GetProperty("total").GetInt32() == 1,
            cancellationToken);
        Assert.Equal(1, restored.State.GetProperty("privateState").GetProperty("yourCount").GetInt32());
        Assert.Equal(2, host.Session.Players.Count);
    }

    [Fact]
    public async Task BackendRegistryRejectsDuplicateGlobalJoinCodes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddPartyGameKitSignalR();
        await using var app = builder.Build();
        var registry = app.Services.GetRequiredService<SignalRRoomRegistry>();

        await using var first = registry.RegisterRoom(new RoomId("room-one"), new JoinCode("GLOBAL"));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterRoom(new RoomId("room-two"), new JoinCode("GLOBAL")));
        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
    }

    private static string JoinRequest(ClientRole role, PlayerId? playerId, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRequest,
            messageId,
            new JoinRequestPayload(
                SharedCounterHost.RoomId,
                SharedCounterHost.JoinCode,
                role,
                playerId)));

    private static string RejoinRequest(PlayerId playerId, string token, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRequest,
            messageId,
            new RejoinRequestPayload(
                SharedCounterHost.RoomId,
                playerId,
                token,
                0)));

    private static async Task<JoinAcceptedPayload> ReadJoinAcceptedAsync(
        SignalRGameClient client,
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
        SignalRGameClient client,
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
        SignalRGameClient client,
        Func<StateSnapshotPayload<JsonElement>, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var frame = await ReceiveWithTimeoutAsync(client, cancellationToken);
            Assert.False(frame.IsClose, frame.CloseDescription);
            var json = Encoding.UTF8.GetString(frame.Payload.Span);
            var read = ProtocolJson.Read<StateSnapshotPayload<JsonElement>>(json, ProtocolMessageTypes.StateSnapshot);
            if (read.IsSuccess && predicate(read.Message!.Payload))
            {
                return read.Message.Payload;
            }
        }

        throw new InvalidOperationException("Expected SignalR snapshot was not received within 40 frames.");
    }

    private static async Task<SignalRGameClientMessage> ReceiveWithTimeoutAsync(
        SignalRGameClient client,
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

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}

using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.InMemory;

namespace PartyGameKit.Transport.Tests;

public sealed class ReconnectIntegrationTests
{
    [Fact]
    public async Task ReconnectRebindsNewTransportConnectionAndDeliversLatestState()
    {
        var session = new RoomSession(
            new RoomId("room-1"),
            new JoinCode("ROOM1"),
            playerCapacity: 2,
            new AuthorityId("authority-1"));
        var publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(session.RoomId);
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var continuity = new SessionContinuityCoordinator<PublicView, PrivateView>(
            session,
            publisher,
            new SessionContinuityOptions(
                hostTimeout: TimeSpan.FromSeconds(5),
                clientTimeout: TimeSpan.FromSeconds(10),
                reconnectWindow: TimeSpan.FromSeconds(30)),
            utcNow: () => now,
            reconnectTokenFactory: () => "resume-token");
        await using var transport = new InMemoryGameTransport();
        var playerId = new PlayerId("player-1");
        var firstConnection = new ConnectionId("connection-1");
        await using var firstPeer = await transport.OpenConnectionAsync(
            firstConnection,
            TestContext.Current.CancellationToken);
        var join = continuity.JoinPlayer(playerId, firstConnection);
        publisher.Publish(
            session.AuthorityId,
            new PublicView("public-1"),
            new Dictionary<PlayerId, PrivateView> { [playerId] = new("private-1") });
        publisher.Publish(
            session.AuthorityId,
            new PublicView("public-2"),
            new Dictionary<PlayerId, PrivateView> { [playerId] = new("private-2") });

        await transport.DisconnectAsync(
            firstConnection,
            TransportCloseReason.RemoteClosed,
            TestContext.Current.CancellationToken);
        continuity.MarkDisconnected(firstConnection);

        var secondConnection = new ConnectionId("connection-2");
        await using var secondPeer = await transport.OpenConnectionAsync(
            secondConnection,
            TestContext.Current.CancellationToken);
        await using var secondMessages = secondPeer
            .ReadMessagesAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var resumed = continuity.RejoinPlayer(playerId, secondConnection, join.ReconnectToken!);
        Assert.True(resumed.IsAccepted);
        Assert.Equal(2, resumed.PlayerSnapshot!.Sequence.Value);

        var restoredState = resumed.PlayerSnapshot.Projection.PrivateState.Value;
        await transport.SendAsync(
            secondConnection,
            Encoding.UTF8.GetBytes(restoredState),
            TestContext.Current.CancellationToken);

        Assert.True(await secondMessages.MoveNextAsync());
        Assert.Equal("private-2", Encoding.UTF8.GetString(secondMessages.Current.Span));
        Assert.Single(session.Players);
        Assert.Equal(secondConnection, session.FindPlayer(playerId)!.ConnectionId);
    }

    private sealed record PublicView(string Value);

    private sealed record PrivateView(string Value);
}

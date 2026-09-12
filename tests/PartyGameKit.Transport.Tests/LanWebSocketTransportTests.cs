using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Transport.Tests;

public sealed class LanWebSocketTransportTests
{
    [Fact]
    public async Task MultipleLoopbackClientsSupportTargetedBroadcastAndInboundMessages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = await StartHostAsync(cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var first = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            JoinRequest("player-1", "join-1"),
            cancellationToken: cancellationToken);
        var firstConnection = await ReadHandshakeAsync(events, "join-1", cancellationToken);
        await using var second = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            JoinRequest("player-2", "join-2"),
            cancellationToken: cancellationToken);
        var secondConnection = await ReadHandshakeAsync(events, "join-2", cancellationToken);

        await transport.SendAsync(firstConnection, Bytes("private"), cancellationToken);
        Assert.Equal("private", Text((await first.ReceiveAsync(cancellationToken)).Payload));

        await transport.BroadcastAsync(Bytes("public"), cancellationToken);
        Assert.Equal("public", Text((await first.ReceiveAsync(cancellationToken)).Payload));
        Assert.Equal("public", Text((await second.ReceiveAsync(cancellationToken)).Payload));

        await second.SendAsync(Bytes("from-client"), cancellationToken);
        Assert.True(await events.MoveNextAsync());
        var inbound = Assert.IsType<TransportMessageReceived>(events.Current);
        Assert.Equal(secondConnection, inbound.ConnectionId);
        Assert.Equal("from-client", Text(inbound.Payload));

        await transport.DisconnectAsync(firstConnection, cancellationToken: cancellationToken);
        Assert.True(await events.MoveNextAsync());
        var closed = Assert.IsType<TransportConnectionClosed>(events.Current);
        Assert.Equal(firstConnection, closed.ConnectionId);
        Assert.Equal(TransportCloseReason.Normal, closed.Reason);
        var closeFrame = await first.ReceiveAsync(cancellationToken);
        Assert.True(closeFrame.IsClose);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, closeFrame.CloseStatus);
    }

    [Fact]
    public async Task ProtocolVersionMismatchIsRejectedBeforeConnectionIsPublished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = await StartHostAsync(cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var invalidHandshake = JoinRequest("player-old", "join-old")
            .Replace("\"protocolVersion\":1", "\"protocolVersion\":999", StringComparison.Ordinal);
        await using var rejected = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            invalidHandshake,
            cancellationToken: cancellationToken);

        var rejection = await rejected.ReceiveAsync(cancellationToken);
        Assert.True(rejection.IsClose);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, rejection.CloseStatus);
        Assert.Equal("protocol-version-mismatch", rejection.CloseDescription);
        Assert.Equal(0, transport.ConnectionCount);

        await using var accepted = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            JoinRequest("player-current", "join-current"),
            cancellationToken: cancellationToken);
        var acceptedConnection = await ReadHandshakeAsync(events, "join-current", cancellationToken);
        Assert.NotEqual(default, acceptedConnection);
    }

    [Fact]
    public async Task ReconnectRebindsStablePlayerAndDeliversLatestSnapshotOverWebSocket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = await StartHostAsync(cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        var session = new RoomSession(
            new RoomId("room-1"),
            new JoinCode("ROOM1"),
            4,
            new AuthorityId("authority-1"));
        var publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(session.RoomId);
        var coordinator = new SessionContinuityCoordinator<PublicView, PrivateView>(
            session,
            publisher,
            new SessionContinuityOptions(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMinutes(1)),
            reconnectTokenFactory: () => "stable-resume-token");
        var playerId = new PlayerId("player-1");

        var firstClient = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            JoinRequest(playerId.Value, "join-first"),
            cancellationToken: cancellationToken);
        var firstConnection = await ReadHandshakeAsync(events, "join-first", cancellationToken);
        var join = coordinator.JoinPlayer(playerId, firstConnection);
        Assert.True(join.IsAccepted);
        var accepted = PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinAccepted,
            "join-accepted",
            new JoinAcceptedPayload(
                session.RoomId,
                firstConnection,
                ClientRole.Player,
                playerId,
                session.AuthorityId,
                join.ReconnectToken),
            "join-first");
        await transport.SendAsync(firstConnection, Bytes(ProtocolJson.Serialize(accepted)), cancellationToken);
        var acceptedWire = await firstClient.ReceiveAsync(cancellationToken);
        var acceptedRead = ProtocolJson.Read<JoinAcceptedPayload>(Text(acceptedWire.Payload), ProtocolMessageTypes.JoinAccepted);
        Assert.True(acceptedRead.IsSuccess);
        Assert.Equal("stable-resume-token", acceptedRead.Message!.Payload.ReconnectToken);

        publisher.Publish(
            session.AuthorityId,
            new PublicView("latest-public"),
            new Dictionary<PlayerId, PrivateView> { [playerId] = new("latest-private") });

        await firstClient.DisposeAsync();
        Assert.True(await events.MoveNextAsync());
        var oldClosed = Assert.IsType<TransportConnectionClosed>(events.Current);
        Assert.Equal(firstConnection, oldClosed.ConnectionId);
        coordinator.MarkDisconnected(firstConnection);

        await using var reconnected = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            RejoinRequest(playerId, join.ReconnectToken!, "rejoin-1"),
            cancellationToken: cancellationToken);
        var replacementConnection = await ReadHandshakeAsync(events, "rejoin-1", cancellationToken);
        Assert.NotEqual(firstConnection, replacementConnection);
        var resumed = coordinator.RejoinPlayer(playerId, replacementConnection, join.ReconnectToken!);
        Assert.True(resumed.IsAccepted);
        Assert.Single(session.Players);
        Assert.Equal(replacementConnection, session.FindPlayer(playerId)!.ConnectionId);

        var rejoinAccepted = PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinAccepted,
            "rejoin-accepted",
            new RejoinAcceptedPayload(session.RoomId, playerId, replacementConnection, session.AuthorityId),
            "rejoin-1");
        await transport.SendAsync(replacementConnection, Bytes(ProtocolJson.Serialize(rejoinAccepted)), cancellationToken);
        var snapshot = resumed.PlayerSnapshot!;
        var snapshotWire = PartyGameKitMessages.Create(
            ProtocolMessageTypes.StateSnapshot,
            "snapshot-latest",
            new StateSnapshotPayload<PlayerStateProjection<PublicView, PrivateView>>(
                snapshot.RoomId,
                snapshot.AuthorityId,
                snapshot.Sequence,
                snapshot.Target,
                snapshot.Projection));
        await transport.SendAsync(replacementConnection, Bytes(ProtocolJson.Serialize(snapshotWire)), cancellationToken);

        var rejoinFrame = await reconnected.ReceiveAsync(cancellationToken);
        var rejoinRead = ProtocolJson.Read<RejoinAcceptedPayload>(Text(rejoinFrame.Payload), ProtocolMessageTypes.RejoinAccepted);
        Assert.True(rejoinRead.IsSuccess);
        Assert.Equal(replacementConnection, rejoinRead.Message!.Payload.ConnectionId);
        var snapshotFrame = await reconnected.ReceiveAsync(cancellationToken);
        var snapshotRead = ProtocolJson.Read<StateSnapshotPayload<PlayerStateProjection<PublicView, PrivateView>>>(
            Text(snapshotFrame.Payload),
            ProtocolMessageTypes.StateSnapshot);
        Assert.True(snapshotRead.IsSuccess);
        Assert.Equal(1, snapshotRead.Message!.Payload.Sequence.Value);
        Assert.Equal("latest-private", snapshotRead.Message.Payload.State.PrivateState.Value);
    }

    [Fact]
    public async Task OversizedClientMessageIsClosedWithMessageTooBig()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new LanWebSocketHostOptions(
            IPAddress.Loopback,
            maxMessageBytes: 128,
            handshakeTimeout: TimeSpan.FromSeconds(2),
            keepAliveInterval: TimeSpan.FromSeconds(2));
        await using var transport = await LanWebSocketTransport.StartAsync(options, cancellationToken: cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            JoinRequest("player-1", "join-small"),
            maxMessageBytes: 1024,
            cancellationToken: cancellationToken);
        var connectionId = await ReadHandshakeAsync(events, "join-small", cancellationToken);

        await client.SendAsync(new byte[129], cancellationToken);
        Assert.True(await events.MoveNextAsync());
        Assert.IsType<TransportFaulted>(events.Current);
        Assert.True(await events.MoveNextAsync());
        var closed = Assert.IsType<TransportConnectionClosed>(events.Current);
        Assert.Equal(connectionId, closed.ConnectionId);
        Assert.Equal(TransportCloseReason.Faulted, closed.Reason);
        var closeFrame = await client.ReceiveAsync(cancellationToken);
        Assert.True(closeFrame.IsClose);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, closeFrame.CloseStatus);
    }

    [Fact]
    public async Task DisposalReleasesKestrelListener()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transport = await StartHostAsync(cancellationToken);
        var port = transport.BoundPort;

        await transport.DisposeAsync();

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Assert.NotEqual(0, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    private static async Task<LanWebSocketTransport> StartHostAsync(CancellationToken cancellationToken)
    {
        var nextConnection = 0;
        return await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(
                IPAddress.Loopback,
                port: 0,
                handshakeTimeout: TimeSpan.FromSeconds(2),
                keepAliveInterval: TimeSpan.FromSeconds(2)),
            connectionIdFactory: () => $"lan-test-{Interlocked.Increment(ref nextConnection)}",
            cancellationToken: cancellationToken);
    }

    private static async Task<ConnectionId> ReadHandshakeAsync(
        IAsyncEnumerator<TransportEvent> events,
        string expectedMessageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.True(await events.MoveNextAsync());
        var opened = Assert.IsType<TransportConnectionOpened>(events.Current);
        Assert.True(await events.MoveNextAsync());
        var handshake = Assert.IsType<TransportMessageReceived>(events.Current);
        Assert.Equal(opened.ConnectionId, handshake.ConnectionId);
        using var document = System.Text.Json.JsonDocument.Parse(handshake.Payload);
        Assert.Equal(expectedMessageId, document.RootElement.GetProperty("messageId").GetString());
        return opened.ConnectionId;
    }

    private static string JoinRequest(string playerId, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.JoinRequest,
            messageId,
            new JoinRequestPayload(
                new RoomId("room-1"),
                new JoinCode("ROOM1"),
                ClientRole.Player,
                new PlayerId(playerId))));

    private static string RejoinRequest(PlayerId playerId, string token, string messageId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.RejoinRequest,
            messageId,
            new RejoinRequestPayload(new RoomId("room-1"), playerId, token, 1)));

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlyMemory<byte> value) => Encoding.UTF8.GetString(value.Span);

    private sealed record PublicView(string Value);

    private sealed record PrivateView(string Value);
}

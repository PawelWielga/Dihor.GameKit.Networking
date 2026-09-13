using System.Net;
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
    public async Task DirectLanConnectionExchangesOpaqueMessages()
    {
        await using var transport = await StartTransportAsync(TestContext.Current.CancellationToken);
        await using var events = transport
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var handshake = ConnectHandshake("peer-a");

        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            handshake,
            cancellationToken: TestContext.Current.CancellationToken);

        var opened = Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        var receivedHandshake = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(opened.ConnectionId, receivedHandshake.ConnectionId);
        Assert.Equal(handshake, Text(receivedHandshake.Payload));

        await transport.SendAsync(
            opened.ConnectionId,
            Bytes("server-to-client"),
            TestContext.Current.CancellationToken);
        var outbound = await client.ReceiveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WebSocketMessageType.Binary, outbound.MessageType);
        Assert.Equal("server-to-client", Text(outbound.Payload));

        await client.SendAsync(Bytes("client-to-server"), TestContext.Current.CancellationToken);
        var inbound = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(opened.ConnectionId, inbound.ConnectionId);
        Assert.Equal("client-to-server", Text(inbound.Payload));
    }

    [Fact]
    public async Task ProtocolMismatchIsRejectedBeforeConnectionIsOpened()
    {
        await using var transport = await StartTransportAsync(TestContext.Current.CancellationToken);
        const string incompatibleHandshake =
            "{\"type\":\"connection.connect.request\",\"protocolVersion\":1,\"messageId\":\"old\",\"payload\":{}}";

        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            incompatibleHandshake,
            cancellationToken: TestContext.Current.CancellationToken);

        var close = await client.ReceiveAsync(TestContext.Current.CancellationToken);

        Assert.True(close.IsClose);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        Assert.Equal("protocol-version-mismatch", close.CloseDescription);
        Assert.Equal(0, transport.ConnectionCount);
    }

    [Theory]
    [InlineData("{\"type\":\"connection.resume.request\",\"protocolVersion\":2,\"messageId\":\"resume-missing\"}")]
    [InlineData("{\"type\":\"connection.resume.request\",\"protocolVersion\":2,\"messageId\":\"resume-empty\",\"payload\":{}}")]
    [InlineData("{\"type\":\"connection.resume.request\",\"protocolVersion\":2,\"messageId\":\"resume-blank\",\"payload\":{\"peerId\":\"peer-a\",\"resumeToken\":\" \"}}")]
    public async Task IncompleteResumeHandshakeIsRejectedBeforeConnectionIsOpened(string handshake)
    {
        await using var transport = await StartTransportAsync(TestContext.Current.CancellationToken);

        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            handshake,
            cancellationToken: TestContext.Current.CancellationToken);

        var close = await client.ReceiveAsync(TestContext.Current.CancellationToken);

        Assert.True(close.IsClose);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        Assert.Equal("invalid-handshake", close.CloseDescription);
        Assert.Equal(0, transport.ConnectionCount);
    }

    [Fact]
    public async Task StopPublishesCloseEventsInConnectionOpenOrder()
    {
        var connectionIds = new Queue<string>(["connection-a", "connection-b", "connection-c"]);
        await using var transport = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(
                IPAddress.Loopback,
                port: 0,
                handshakeTimeout: TimeSpan.FromSeconds(2),
                keepAliveInterval: TimeSpan.FromSeconds(2)),
            connectionIdFactory: () => connectionIds.Dequeue(),
            cancellationToken: TestContext.Current.CancellationToken);
        await using var events = transport
            .ReadEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await using var clientA = await ConnectAndConsumeHandshakeAsync(transport, events, "peer-a");
        await using var clientB = await ConnectAndConsumeHandshakeAsync(transport, events, "peer-b");
        await using var clientC = await ConnectAndConsumeHandshakeAsync(transport, events, "peer-c");

        await transport.StopAsync(TestContext.Current.CancellationToken);

        var firstClosed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        var secondClosed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        var thirdClosed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));

        Assert.Equal(new ConnectionId("connection-a"), firstClosed.ConnectionId);
        Assert.Equal(new ConnectionId("connection-b"), secondClosed.ConnectionId);
        Assert.Equal(new ConnectionId("connection-c"), thirdClosed.ConnectionId);
        Assert.Equal(TransportCloseReason.TransportStopped, firstClosed.Reason);
        Assert.Equal(TransportCloseReason.TransportStopped, secondClosed.Reason);
        Assert.Equal(TransportCloseReason.TransportStopped, thirdClosed.Reason);
    }

    [Fact]
    public void LanDescriptorContainsOnlyTechnicalConnectionMetadata()
    {
        var descriptor = LanConnectionDescriptor.Create(
            "192.168.1.10",
            45678,
            new ChannelId("channel-a"));

        Assert.Equal(LanConnectionDescriptor.TransportName, descriptor.Transport);
        Assert.Equal(ProtocolVersions.Current, descriptor.ProtocolVersion);
        Assert.Equal(new ChannelId("channel-a"), descriptor.ChannelId);
        Assert.Equal("ws://192.168.1.10:45678/partygamekit", descriptor.Endpoint);
    }

    private static Task<LanWebSocketTransport> StartTransportAsync(CancellationToken cancellationToken) =>
        LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(
                IPAddress.Loopback,
                port: 0,
                handshakeTimeout: TimeSpan.FromSeconds(2),
                keepAliveInterval: TimeSpan.FromSeconds(2)),
            cancellationToken: cancellationToken);

    private static async Task<LanWebSocketClient> ConnectAndConsumeHandshakeAsync(
        LanWebSocketTransport transport,
        IAsyncEnumerator<TransportEvent> events,
        string peerId)
    {
        var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            ConnectHandshake(peerId),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        return client;
    }

    private static string ConnectHandshake(string peerId) =>
        ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRequest,
            $"connect-{peerId}",
            new ConnectRequestPayload(new PeerId(peerId))));

    private static async ValueTask<TransportEvent> NextAsync(IAsyncEnumerator<TransportEvent> events)
    {
        Assert.True(await events.MoveNextAsync());
        return events.Current;
    }

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlyMemory<byte> value) => Encoding.UTF8.GetString(value.Span);
}

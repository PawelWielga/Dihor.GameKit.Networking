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
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = await StartTransportAsync(cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator();
        var handshake = ConnectHandshake("peer-a");

        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            handshake,
            cancellationToken: cancellationToken);

        var opened = Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        var receivedHandshake = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(opened.ConnectionId, receivedHandshake.ConnectionId);
        Assert.Equal(handshake, Text(receivedHandshake.Payload));

        await transport.SendAsync(opened.ConnectionId, Bytes("server-to-client"), cancellationToken);
        var outbound = await client.ReceiveAsync(cancellationToken);
        Assert.Equal(WebSocketMessageType.Binary, outbound.MessageType);
        Assert.Equal("server-to-client", Text(outbound.Payload));

        await client.SendAsync(Bytes("client-to-server"), cancellationToken);
        var inbound = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(opened.ConnectionId, inbound.ConnectionId);
        Assert.Equal("client-to-server", Text(inbound.Payload));
    }

    [Fact]
    public async Task ProtocolMismatchIsRejectedBeforeConnectionIsOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = await StartTransportAsync(cancellationToken);
        const string incompatibleHandshake =
            "{\"type\":\"connection.connect.request\",\"protocolVersion\":1,\"messageId\":\"old\",\"payload\":{}}";

        await using var client = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            incompatibleHandshake,
            cancellationToken: cancellationToken);

        var close = await client.ReceiveAsync(cancellationToken);

        Assert.True(close.IsClose);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        Assert.Equal("protocol-version-mismatch", close.CloseDescription);
        Assert.Equal(0, transport.ConnectionCount);
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

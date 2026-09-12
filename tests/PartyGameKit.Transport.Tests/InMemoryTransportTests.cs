using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.InMemory;

namespace PartyGameKit.Transport.Tests;

public sealed class InMemoryTransportTests
{
    [Fact]
    public async Task GenericPeersCanExchangeTargetedAndBroadcastPayloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = new InMemoryTransport();
        await using var peerA = await transport.OpenConnectionAsync(new ConnectionId("connection-a"), cancellationToken);
        await using var peerB = await transport.OpenConnectionAsync(new ConnectionId("connection-b"), cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator();

        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));

        await peerA.SendAsync(Bytes("from-a"), cancellationToken);
        var incoming = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(new ConnectionId("connection-a"), incoming.ConnectionId);
        Assert.Equal("from-a", Text(incoming.Payload));

        await transport.SendAsync(new ConnectionId("connection-b"), Bytes("only-b"), cancellationToken);
        await using var peerBMessages = peerB.ReadMessagesAsync(cancellationToken).GetAsyncEnumerator();
        Assert.True(await peerBMessages.MoveNextAsync());
        Assert.Equal("only-b", Text(peerBMessages.Current));

        await transport.BroadcastAsync(Bytes("everyone"), cancellationToken);
        await using var peerAMessages = peerA.ReadMessagesAsync(cancellationToken).GetAsyncEnumerator();
        Assert.True(await peerAMessages.MoveNextAsync());
        Assert.Equal("everyone", Text(peerAMessages.Current));
        Assert.True(await peerBMessages.MoveNextAsync());
        Assert.Equal("everyone", Text(peerBMessages.Current));
    }

    [Fact]
    public async Task DisconnectIsConnectionLifecycleOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var transport = new InMemoryTransport();
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-a"), cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator();
        await NextAsync(events);

        await transport.DisconnectAsync(
            new ConnectionId("connection-a"),
            TransportCloseReason.Timeout,
            cancellationToken);

        var closed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        Assert.Equal(new ConnectionId("connection-a"), closed.ConnectionId);
        Assert.Equal(TransportCloseReason.Timeout, closed.Reason);
    }

    private static async ValueTask<TransportEvent> NextAsync(IAsyncEnumerator<TransportEvent> events)
    {
        Assert.True(await events.MoveNextAsync());
        return events.Current;
    }

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlyMemory<byte> value) => Encoding.UTF8.GetString(value.Span);
}

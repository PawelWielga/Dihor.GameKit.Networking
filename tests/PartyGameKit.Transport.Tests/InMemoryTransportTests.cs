using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.InMemory;

namespace PartyGameKit.Transport.Tests;

public sealed class InMemoryTransportTests
{
    [Fact]
    public async Task GenericPeers_CanExchangeTargetedAndBroadcastPayloads()
    {
        await using var transport = new InMemoryTransport();
        await using var peerA = await transport.OpenConnectionAsync(new ConnectionId("connection-a"));
        await using var peerB = await transport.OpenConnectionAsync(new ConnectionId("connection-b"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = transport.ReadEventsAsync(timeout.Token).GetAsyncEnumerator();

        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));

        await peerA.SendAsync(Bytes("from-a"), timeout.Token);
        var incoming = Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        Assert.Equal(new ConnectionId("connection-a"), incoming.ConnectionId);
        Assert.Equal("from-a", Text(incoming.Payload));

        await transport.SendAsync(new ConnectionId("connection-b"), Bytes("only-b"), timeout.Token);
        await using var peerBMessages = peerB.ReadMessagesAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await peerBMessages.MoveNextAsync());
        Assert.Equal("only-b", Text(peerBMessages.Current));

        await transport.BroadcastAsync(Bytes("everyone"), timeout.Token);
        await using var peerAMessages = peerA.ReadMessagesAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await peerAMessages.MoveNextAsync());
        Assert.Equal("everyone", Text(peerAMessages.Current));
        Assert.True(await peerBMessages.MoveNextAsync());
        Assert.Equal("everyone", Text(peerBMessages.Current));
    }

    [Fact]
    public async Task Disconnect_IsConnectionLifecycleOnly()
    {
        await using var transport = new InMemoryTransport();
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-a"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var events = transport.ReadEventsAsync(timeout.Token).GetAsyncEnumerator();
        await NextAsync(events);

        await transport.DisconnectAsync(
            new ConnectionId("connection-a"),
            TransportCloseReason.Timeout,
            timeout.Token);

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

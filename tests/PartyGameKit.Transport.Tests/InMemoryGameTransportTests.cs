using System.Text;
using PartyGameKit.Core;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.InMemory;

namespace PartyGameKit.Transport.Tests;

public sealed class InMemoryGameTransportTests
{
    [Fact]
    public async Task IncomingEventsPreserveConnectionAndMessageOrder()
    {
        await using var transport = new InMemoryGameTransport();
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        await using var events = transport.ReadEventsAsync().GetAsyncEnumerator();

        await peer.SendAsync(Bytes("first"));
        await peer.SendAsync(Bytes("second"));

        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.Equal("first", Text(Assert.IsType<TransportMessageReceived>(await NextAsync(events)).Payload));
        Assert.Equal("second", Text(Assert.IsType<TransportMessageReceived>(await NextAsync(events)).Payload));
    }

    [Fact]
    public async Task TargetedSendReachesOnlyRequestedConnection()
    {
        await using var transport = new InMemoryGameTransport();
        await using var first = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        await using var second = await transport.OpenConnectionAsync(new ConnectionId("connection-2"));
        await using var firstMessages = first.ReadMessagesAsync().GetAsyncEnumerator();

        await transport.SendAsync(first.ConnectionId, Bytes("private"));

        Assert.True(await firstMessages.MoveNextAsync());
        Assert.Equal("private", Text(firstMessages.Current));
        Assert.False(second.ReadMessagesAsync().GetAsyncEnumerator().MoveNextAsync().IsCompleted);
    }

    [Fact]
    public async Task BroadcastPreservesSendOrderForEveryConnection()
    {
        await using var transport = new InMemoryGameTransport();
        await using var first = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        await using var second = await transport.OpenConnectionAsync(new ConnectionId("connection-2"));
        await using var firstMessages = first.ReadMessagesAsync().GetAsyncEnumerator();
        await using var secondMessages = second.ReadMessagesAsync().GetAsyncEnumerator();

        await transport.BroadcastAsync(Bytes("one"));
        await transport.BroadcastAsync(Bytes("two"));

        Assert.Equal("one", Text(await NextMessageAsync(firstMessages)));
        Assert.Equal("two", Text(await NextMessageAsync(firstMessages)));
        Assert.Equal("one", Text(await NextMessageAsync(secondMessages)));
        Assert.Equal("two", Text(await NextMessageAsync(secondMessages)));
    }

    [Fact]
    public async Task DisconnectCompletesPeerAndEmitsCloseEvent()
    {
        await using var transport = new InMemoryGameTransport();
        var connectionId = new ConnectionId("connection-1");
        await using var peer = await transport.OpenConnectionAsync(connectionId);
        await using var events = transport.ReadEventsAsync().GetAsyncEnumerator();
        await using var messages = peer.ReadMessagesAsync().GetAsyncEnumerator();

        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        await transport.DisconnectAsync(connectionId, TransportCloseReason.Replaced);

        var closed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        Assert.Equal(connectionId, closed.ConnectionId);
        Assert.Equal(TransportCloseReason.Replaced, closed.Reason);
        Assert.False(await messages.MoveNextAsync());

        var error = await Assert.ThrowsAsync<PartyGameTransportException>(
            async () => await transport.SendAsync(connectionId, Bytes("late")));
        Assert.Equal(TransportErrorCode.ConnectionNotFound, error.Error.Code);
    }

    [Fact]
    public async Task CancellationIsObservedBeforeAnOperationMutatesTransport()
    {
        await using var transport = new InMemoryGameTransport();
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await transport.SendAsync(peer.ConnectionId, Bytes("cancelled"), cancellation.Token));
    }

    [Fact]
    public async Task StopIsIdempotentClosesPeersAndRejectsFutureOperations()
    {
        await using var transport = new InMemoryGameTransport();
        await using var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        await using var events = transport.ReadEventsAsync().GetAsyncEnumerator();
        await using var messages = peer.ReadMessagesAsync().GetAsyncEnumerator();

        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        await transport.StopAsync();
        await transport.StopAsync();

        var closed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        Assert.Equal(TransportCloseReason.TransportStopped, closed.Reason);
        Assert.False(await events.MoveNextAsync());
        Assert.False(await messages.MoveNextAsync());

        var error = await Assert.ThrowsAsync<PartyGameTransportException>(
            async () => await transport.BroadcastAsync(Bytes("late")));
        Assert.Equal(TransportErrorCode.TransportClosed, error.Error.Code);
    }

    [Fact]
    public async Task PeerDisposeReportsRemoteCloseExactlyOnce()
    {
        await using var transport = new InMemoryGameTransport();
        var peer = await transport.OpenConnectionAsync(new ConnectionId("connection-1"));
        await using var events = transport.ReadEventsAsync().GetAsyncEnumerator();
        Assert.IsType<TransportConnectionOpened>(await NextAsync(events));

        await peer.DisposeAsync();
        await peer.DisposeAsync();

        var closed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        Assert.Equal(TransportCloseReason.RemoteClosed, closed.Reason);
    }

    [Fact]
    public async Task CoreSessionCanRouteToTheCurrentConnectionWithoutTransportKnowingPlayerIdentity()
    {
        var session = new RoomSession(
            new RoomId("room-1"),
            new JoinCode("room42"),
            playerCapacity: 1,
            new AuthorityId("authority-1"));
        await using var transport = new InMemoryGameTransport();
        var connectionId = new ConnectionId("connection-1");
        var playerId = new PlayerId("player-1");
        await using var peer = await transport.OpenConnectionAsync(connectionId);
        await using var peerMessages = peer.ReadMessagesAsync().GetAsyncEnumerator();

        var join = session.JoinPlayer(playerId, connectionId);
        Assert.True(join.IsAccepted);

        var currentConnection = session.FindPlayer(playerId)!.ConnectionId;
        Assert.NotNull(currentConnection);
        await transport.SendAsync(currentConnection.Value, Bytes("session-state"));

        Assert.Equal("session-state", Text(await NextMessageAsync(peerMessages)));
        Assert.Equal(playerId, session.FindClient(connectionId)!.PlayerId);
    }

    [Fact]
    public void AbstractionsDoNotReferenceConcreteNetworkingPackages()
    {
        var references = typeof(IGameTransport).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.Contains("SignalR", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains("WebSocket", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains("WebRTC", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Text(ReadOnlyMemory<byte> value) => Encoding.UTF8.GetString(value.Span);

    private static async Task<TransportEvent> NextAsync(IAsyncEnumerator<TransportEvent> events)
    {
        Assert.True(await events.MoveNextAsync());
        return events.Current;
    }

    private static async Task<ReadOnlyMemory<byte>> NextMessageAsync(
        IAsyncEnumerator<ReadOnlyMemory<byte>> messages)
    {
        Assert.True(await messages.MoveNextAsync());
        return messages.Current;
    }
}

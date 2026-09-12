using System.Net;
using PartyGameKit.Core;
using PartyGameKit.Protocol;
using PartyGameKit.Transport.Abstractions;
using PartyGameKit.Transport.Lan;

namespace PartyGameKit.Transport.Tests;

public sealed class ReconnectIntegrationTests
{
    [Fact]
    public async Task ReplacementLanConnectionResumesSameLogicalPeerWithoutDuplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);
        var tokens = new Queue<string>(["token-1", "token-2"]);
        var continuity = new ConnectionContinuityCoordinator(
            new ConnectionContinuityOptions(
                peerTimeout: TimeSpan.FromSeconds(30),
                reconnectWindow: TimeSpan.FromMinutes(2)),
            () => now,
            () => tokens.Dequeue());
        var peerId = new PeerId("peer-a");

        await using var transport = await LanWebSocketTransport.StartAsync(
            new LanWebSocketHostOptions(IPAddress.Loopback, port: 0),
            cancellationToken: cancellationToken);
        await using var events = transport.ReadEventsAsync(cancellationToken).GetAsyncEnumerator();

        var connectHandshake = ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ConnectRequest,
            "connect-1",
            new ConnectRequestPayload(peerId)));
        var firstClient = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            connectHandshake,
            cancellationToken: cancellationToken);
        var firstOpened = Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        var registered = continuity.Register(peerId, firstOpened.ConnectionId);
        Assert.Equal(RegisterPeerStatus.Connected, registered.Status);

        await firstClient.DisposeAsync();
        var firstClosed = Assert.IsType<TransportConnectionClosed>(await NextAsync(events));
        Assert.Equal(firstOpened.ConnectionId, firstClosed.ConnectionId);
        Assert.Equal(DisconnectPeerStatus.Disconnected, continuity.MarkDisconnected(firstClosed.ConnectionId).Status);

        now = now.AddSeconds(5);
        var resumeHandshake = ProtocolJson.Serialize(PartyGameKitMessages.Create(
            ProtocolMessageTypes.ResumeRequest,
            "resume-1",
            new ResumeRequestPayload(peerId, registered.ResumeToken!)));
        await using var replacementClient = await LanWebSocketClient.ConnectAsync(
            transport.CreateClientUri("127.0.0.1"),
            resumeHandshake,
            cancellationToken: cancellationToken);
        var replacementOpened = Assert.IsType<TransportConnectionOpened>(await NextAsync(events));
        Assert.IsType<TransportMessageReceived>(await NextAsync(events));
        var resumed = continuity.Resume(peerId, registered.ResumeToken!, replacementOpened.ConnectionId);

        Assert.Equal(ResumePeerStatus.Resumed, resumed.Status);
        Assert.Equal("token-2", resumed.ResumeToken);
        Assert.Equal(1, continuity.PeerCount);
        Assert.Equal(1, continuity.ConnectedPeerCount);
        Assert.Null(continuity.GetPeerId(firstOpened.ConnectionId));
        Assert.Equal(peerId, continuity.GetPeerId(replacementOpened.ConnectionId));
    }

    private static async ValueTask<TransportEvent> NextAsync(IAsyncEnumerator<TransportEvent> events)
    {
        Assert.True(await events.MoveNextAsync());
        return events.Current;
    }
}

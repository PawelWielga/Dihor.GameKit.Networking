using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class ConnectionContinuityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegisterCreatesNeutralPeerAndResumeCredential()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1"));

        var result = coordinator.Register(new PeerId("peer-a"), new ConnectionId("connection-1"));

        Assert.Equal(RegisterPeerStatus.Connected, result.Status);
        Assert.Equal("token-1", result.ResumeToken);
        Assert.Equal(1, coordinator.PeerCount);
        Assert.Equal(1, coordinator.ConnectedPeerCount);
        Assert.Equal(new PeerId("peer-a"), coordinator.GetPeerId(new ConnectionId("connection-1")));
    }

    [Fact]
    public void ResumeRebindsReplacementConnectionWithoutDuplicatingPeer()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1", "token-2"));
        var peerId = new PeerId("peer-a");
        var firstConnection = new ConnectionId("connection-1");
        var replacementConnection = new ConnectionId("connection-2");
        var registered = coordinator.Register(peerId, firstConnection);

        var disconnected = coordinator.MarkDisconnected(firstConnection);
        now = now.AddSeconds(10);
        var resumed = coordinator.Resume(peerId, registered.ResumeToken!, replacementConnection);

        Assert.Equal(DisconnectPeerStatus.Disconnected, disconnected.Status);
        Assert.Equal(ResumePeerStatus.Resumed, resumed.Status);
        Assert.Equal("token-2", resumed.ResumeToken);
        Assert.Equal(1, coordinator.PeerCount);
        Assert.Equal(1, coordinator.ConnectedPeerCount);
        Assert.Null(coordinator.GetPeerId(firstConnection));
        Assert.Equal(peerId, coordinator.GetPeerId(replacementConnection));
        Assert.Equal(PeerConnectionState.Connected, coordinator.GetPresence(peerId)!.State);
    }

    [Fact]
    public void ResumeWithWrongCredentialIsRejectedWithoutChangingBinding()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1"));
        var peerId = new PeerId("peer-a");
        var firstConnection = new ConnectionId("connection-1");
        coordinator.Register(peerId, firstConnection);
        coordinator.MarkDisconnected(firstConnection);

        var result = coordinator.Resume(peerId, "wrong-token", new ConnectionId("connection-2"));

        Assert.Equal(ResumePeerStatus.InvalidResumeCredential, result.Status);
        Assert.Equal(1, coordinator.PeerCount);
        Assert.Equal(0, coordinator.ConnectedPeerCount);
        Assert.Equal(PeerConnectionState.AwaitingResume, coordinator.GetPresence(peerId)!.State);
    }

    [Fact]
    public void HeartbeatTimeoutMarksConnectivityLossButKeepsLogicalPeer()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1"));
        var peerId = new PeerId("peer-a");
        var connectionId = new ConnectionId("connection-1");
        coordinator.Register(peerId, connectionId);

        now = now.AddSeconds(31);
        var timedOut = coordinator.SweepTimeouts();

        var timeout = Assert.Single(timedOut);
        Assert.Equal(peerId, timeout.PeerId);
        Assert.Equal(connectionId, timeout.ConnectionId);
        Assert.Equal(1, coordinator.PeerCount);
        Assert.Equal(0, coordinator.ConnectedPeerCount);
        Assert.Equal(PeerConnectionState.AwaitingResume, coordinator.GetPresence(peerId)!.State);
    }

    [Fact]
    public void RecordHeartbeatExtendsConnectivityWithoutProductLifecycleSemantics()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1"));
        var connectionId = new ConnectionId("connection-1");
        coordinator.Register(new PeerId("peer-a"), connectionId);

        now = now.AddSeconds(20);
        Assert.True(coordinator.RecordHeartbeat(connectionId));
        now = now.AddSeconds(20);

        Assert.Empty(coordinator.SweepTimeouts());
    }

    [Fact]
    public void ExpireReconnectWindowsRemovesOnlyExpiredCommunicationIdentity()
    {
        var now = Start;
        var coordinator = CreateCoordinator(() => now, Tokens("token-1"));
        var peerId = new PeerId("peer-a");
        var connectionId = new ConnectionId("connection-1");
        coordinator.Register(peerId, connectionId);
        coordinator.MarkDisconnected(connectionId);

        now = now.AddMinutes(3);
        var expired = coordinator.ExpireReconnectWindows();

        Assert.Equal(peerId, Assert.Single(expired));
        Assert.Equal(0, coordinator.PeerCount);
        Assert.Null(coordinator.GetPresence(peerId));
    }

    private static ConnectionContinuityCoordinator CreateCoordinator(
        Func<DateTimeOffset> utcNow,
        Func<string> tokenFactory) =>
        new(
            new ConnectionContinuityOptions(
                peerTimeout: TimeSpan.FromSeconds(30),
                reconnectWindow: TimeSpan.FromMinutes(2)),
            utcNow,
            tokenFactory);

    private static Func<string> Tokens(params string[] tokens)
    {
        var queue = new Queue<string>(tokens);
        return () => queue.Dequeue();
    }
}

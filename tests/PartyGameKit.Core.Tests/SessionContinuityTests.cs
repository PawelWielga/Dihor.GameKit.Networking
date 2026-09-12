namespace PartyGameKit.Core.Tests;

public sealed class SessionContinuityTests
{
    [Fact]
    public void MissedHeartbeatBeforeTimeoutDoesNotRemoveOrDisconnectPlayer()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        var connectionId = new ConnectionId("connection-1");
        var join = harness.Coordinator.JoinPlayer(playerId, connectionId);

        harness.Advance(TimeSpan.FromSeconds(9));
        var expired = harness.Coordinator.SweepTimeouts();

        Assert.True(join.IsAccepted);
        Assert.Empty(expired);
        Assert.Equal(PlayerPresence.Connected, harness.Session.FindPlayer(playerId)!.Presence);
        Assert.Single(harness.Session.Players);
    }

    [Fact]
    public void TimedOutPlayerIsDisconnectedButMembershipIsPreserved()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        harness.Coordinator.JoinPlayer(playerId, new ConnectionId("connection-1"));

        harness.Advance(TimeSpan.FromSeconds(10));
        var timeout = Assert.Single(harness.Coordinator.SweepTimeouts());

        Assert.Equal(PresenceTimeoutKind.ClientTimedOut, timeout.Kind);
        Assert.Equal(playerId, timeout.PlayerId);
        Assert.Equal(PlayerPresence.Disconnected, harness.Session.FindPlayer(playerId)!.Presence);
        Assert.Single(harness.Session.Players);
        Assert.Empty(harness.Session.Clients);
        Assert.Equal(harness.Now + TimeSpan.FromSeconds(30), timeout.ReconnectUntil);
    }

    [Fact]
    public void RejoinPreservesIdentityAndRestoresLatestPlayerSnapshot()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        var join = harness.Coordinator.JoinPlayer(playerId, new ConnectionId("connection-1"));
        harness.Publisher.Publish(
            harness.Session.AuthorityId,
            new PublicView("old-public"),
            new Dictionary<PlayerId, PrivateView> { [playerId] = new("old-private") });
        harness.Publisher.Publish(
            harness.Session.AuthorityId,
            new PublicView("current-public"),
            new Dictionary<PlayerId, PrivateView> { [playerId] = new("current-private") });
        harness.Coordinator.MarkDisconnected(new ConnectionId("connection-1"));
        harness.Advance(TimeSpan.FromSeconds(5));

        var resumed = harness.Coordinator.RejoinPlayer(
            playerId,
            new ConnectionId("connection-2"),
            join.ReconnectToken!);

        Assert.True(resumed.IsAccepted);
        Assert.Equal(playerId, resumed.Player!.PlayerId);
        Assert.Equal(new ConnectionId("connection-2"), resumed.Player.ConnectionId);
        Assert.Single(harness.Session.Players);
        Assert.Equal(2, resumed.PlayerSnapshot!.Sequence.Value);
        Assert.Equal("current-public", resumed.PlayerSnapshot.Projection.PublicState.Value);
        Assert.Equal("current-private", resumed.PlayerSnapshot.Projection.PrivateState.Value);
    }

    [Fact]
    public void ReconnectWindowExpiryIsTerminalButDoesNotDeletePlayer()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        var join = harness.Coordinator.JoinPlayer(playerId, new ConnectionId("connection-1"));
        harness.Coordinator.MarkDisconnected(new ConnectionId("connection-1"));
        harness.Advance(TimeSpan.FromSeconds(30));

        var resumed = harness.Coordinator.RejoinPlayer(
            playerId,
            new ConnectionId("connection-2"),
            join.ReconnectToken!);

        Assert.Equal(ResumePlayerStatus.ReconnectWindowExpired, resumed.Status);
        Assert.Single(harness.Session.Players);
        Assert.Equal(PlayerPresence.Disconnected, harness.Session.FindPlayer(playerId)!.Presence);
    }

    [Fact]
    public void InvalidCredentialDoesNotRebindPlayer()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        harness.Coordinator.JoinPlayer(playerId, new ConnectionId("connection-1"));
        harness.Coordinator.MarkDisconnected(new ConnectionId("connection-1"));

        var resumed = harness.Coordinator.RejoinPlayer(
            playerId,
            new ConnectionId("connection-2"),
            "wrong-token");

        Assert.Equal(ResumePlayerStatus.InvalidResumeIdentity, resumed.Status);
        Assert.Equal(PlayerPresence.Disconnected, harness.Session.FindPlayer(playerId)!.Presence);
    }

    [Fact]
    public void ExplicitLeaveRevokesReconnectCredentialAndDeadline()
    {
        var harness = new ContinuityHarness();
        var playerId = new PlayerId("player-1");
        var join = harness.Coordinator.JoinPlayer(playerId, new ConnectionId("connection-1"));
        harness.Coordinator.MarkDisconnected(new ConnectionId("connection-1"));
        Assert.NotNull(harness.Coordinator.GetReconnectDeadline(playerId));

        Assert.Equal(LeavePlayerStatus.Left, harness.Coordinator.LeavePlayer(playerId));
        var resumed = harness.Coordinator.RejoinPlayer(
            playerId,
            new ConnectionId("connection-2"),
            join.ReconnectToken!);

        Assert.Null(harness.Coordinator.GetReconnectDeadline(playerId));
        Assert.Equal(ResumePlayerStatus.InvalidResumeIdentity, resumed.Status);
        Assert.Empty(harness.Session.Players);
    }

    [Fact]
    public void HostTimeoutIsReportedWithoutMigratingAuthority()
    {
        var harness = new ContinuityHarness();
        var hostConnection = new ConnectionId("host-connection");
        var authority = harness.Session.AuthorityId;
        Assert.True(harness.Coordinator.ConnectClient(hostConnection, ClientRole.Host).IsAccepted);

        harness.Advance(TimeSpan.FromSeconds(5));
        var timeout = Assert.Single(harness.Coordinator.SweepTimeouts());

        Assert.Equal(PresenceTimeoutKind.HostLost, timeout.Kind);
        Assert.Equal(ClientRole.Host, timeout.Role);
        Assert.Equal(authority, harness.Session.AuthorityId);
        Assert.Equal(RoomSessionState.Open, harness.Session.State);
    }

    [Fact]
    public void HeartbeatRefreshesPresenceDeadline()
    {
        var harness = new ContinuityHarness();
        var connection = new ConnectionId("connection-1");
        harness.Coordinator.JoinPlayer(new PlayerId("player-1"), connection);
        harness.Advance(TimeSpan.FromSeconds(8));
        Assert.True(harness.Coordinator.RecordHeartbeat(connection));
        harness.Advance(TimeSpan.FromSeconds(8));

        Assert.Empty(harness.Coordinator.SweepTimeouts());
    }

    [Fact]
    public void RoomFullAndClosedRoomHaveExplicitResults()
    {
        var harness = new ContinuityHarness(capacity: 1);
        var first = harness.Coordinator.JoinPlayer(new PlayerId("player-1"), new ConnectionId("connection-1"));
        var full = harness.Coordinator.JoinPlayer(new PlayerId("player-2"), new ConnectionId("connection-2"));
        harness.Session.Close();
        var closed = harness.Coordinator.RejoinPlayer(
            new PlayerId("player-1"),
            new ConnectionId("connection-3"),
            first.ReconnectToken!);

        Assert.True(first.IsAccepted);
        Assert.Equal(JoinPlayerStatus.RoomFull, full.Status);
        Assert.Equal(ResumePlayerStatus.RoomClosed, closed.Status);
    }

    private sealed class ContinuityHarness
    {
        private DateTimeOffset _now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        public ContinuityHarness(int capacity = 4)
        {
            Session = new RoomSession(
                new RoomId("room-1"),
                new JoinCode("ROOM1"),
                capacity,
                new AuthorityId("authority-1"));
            Publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(Session.RoomId);
            var tokenNumber = 0;
            Coordinator = new SessionContinuityCoordinator<PublicView, PrivateView>(
                Session,
                Publisher,
                new SessionContinuityOptions(
                    hostTimeout: TimeSpan.FromSeconds(5),
                    clientTimeout: TimeSpan.FromSeconds(10),
                    reconnectWindow: TimeSpan.FromSeconds(30)),
                utcNow: () => _now,
                reconnectTokenFactory: () => $"resume-token-{++tokenNumber}");
        }

        public RoomSession Session { get; }

        public AuthoritativeSnapshotPublisher<PublicView, PrivateView> Publisher { get; }

        public SessionContinuityCoordinator<PublicView, PrivateView> Coordinator { get; }

        public DateTimeOffset Now => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed record PublicView(string Value);

    private sealed record PrivateView(string Value);
}

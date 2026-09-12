using PartyGameKit.Core;

namespace PartyGameKit.Core.Tests;

public sealed class RoomSessionLifecycleTests
{
    [Fact]
    public void CreateAssignsLogicalAuthorityWithoutAConnection()
    {
        var session = CreateSession(capacity: 2);

        Assert.Equal(new AuthorityId("authority-1"), session.AuthorityId);
        Assert.Equal(RoomSessionState.Open, session.State);
        Assert.Empty(session.Clients);
        Assert.Empty(session.Players);
        Assert.Equal(
            new SessionLifecycleEvent(1, SessionLifecycleEventKind.SessionCreated),
            Assert.Single(session.Events));
    }

    [Fact]
    public void PlayerJoinConsumesCapacityAndReturnsExplicitFullResult()
    {
        var session = CreateSession(capacity: 1);

        var first = session.JoinPlayer(new PlayerId("player-1"), new ConnectionId("connection-1"));
        var second = session.JoinPlayer(new PlayerId("player-2"), new ConnectionId("connection-2"));

        Assert.True(first.IsAccepted);
        Assert.Equal(JoinPlayerStatus.RoomFull, second.Status);
        Assert.Single(session.Players);
        Assert.Single(session.Clients);
    }

    [Fact]
    public void SharedScreenDoesNotConsumePlayerCapacity()
    {
        var session = CreateSession(capacity: 1);

        var screen = session.ConnectClient(new ConnectionId("screen-connection"), ClientRole.SharedScreen);
        var player = session.JoinPlayer(new PlayerId("player-1"), new ConnectionId("player-connection"));

        Assert.True(screen.IsAccepted);
        Assert.True(player.IsAccepted);
        Assert.Single(session.Players);
        Assert.Equal(2, session.Clients.Count);
        Assert.Null(screen.Client!.PlayerId);
    }

    [Fact]
    public void NonPlayerHostClientDoesNotDefineLogicalAuthority()
    {
        var session = CreateSession(capacity: 1);

        var hostClient = session.ConnectClient(new ConnectionId("host-connection"), ClientRole.Host);

        Assert.True(hostClient.IsAccepted);
        Assert.Equal(new AuthorityId("authority-1"), session.AuthorityId);
        Assert.NotEqual(session.AuthorityId.Value, hostClient.Client!.ConnectionId.Value);
    }

    [Fact]
    public void PlayerRoleCannotBeConnectedWithoutPlayerIdentity()
    {
        var session = CreateSession(capacity: 1);

        var result = session.ConnectClient(new ConnectionId("connection-1"), ClientRole.Player);

        Assert.Equal(ConnectClientStatus.PlayerRoleRequiresPlayerIdentity, result.Status);
        Assert.Empty(session.Clients);
    }

    [Fact]
    public void DisconnectKeepsPlayerSlotAndMarksPresenceDisconnected()
    {
        var session = CreateSession(capacity: 1);
        var playerId = new PlayerId("player-1");
        var connectionId = new ConnectionId("connection-1");
        session.JoinPlayer(playerId, connectionId);

        var result = session.Disconnect(connectionId);
        var player = session.FindPlayer(playerId);
        var replacementJoin = session.JoinPlayer(new PlayerId("player-2"), new ConnectionId("connection-2"));

        Assert.Equal(DisconnectClientStatus.Disconnected, result);
        Assert.NotNull(player);
        Assert.Equal(PlayerPresence.Disconnected, player.Presence);
        Assert.Null(player.ConnectionId);
        Assert.Equal(JoinPlayerStatus.RoomFull, replacementJoin.Status);
    }

    [Fact]
    public void ExplicitLeaveRemovesPlayerAndFreesCapacity()
    {
        var session = CreateSession(capacity: 1);
        var playerId = new PlayerId("player-1");
        session.JoinPlayer(playerId, new ConnectionId("connection-1"));
        session.Disconnect(new ConnectionId("connection-1"));

        var leave = session.LeavePlayer(playerId);
        var replacement = session.JoinPlayer(new PlayerId("player-2"), new ConnectionId("connection-2"));

        Assert.Equal(LeavePlayerStatus.Left, leave);
        Assert.True(replacement.IsAccepted);
        Assert.Null(session.FindPlayer(playerId));
    }

    [Fact]
    public void RejoinRebindsStablePlayerWithoutCreatingDuplicate()
    {
        var session = CreateSession(capacity: 2);
        var playerId = new PlayerId("player-1");
        var oldConnection = new ConnectionId("connection-old");
        var newConnection = new ConnectionId("connection-new");
        session.JoinPlayer(playerId, oldConnection);
        session.Disconnect(oldConnection);

        var result = session.RejoinPlayer(playerId, newConnection);

        Assert.True(result.IsAccepted);
        Assert.Single(session.Players);
        Assert.Equal(PlayerPresence.Connected, session.FindPlayer(playerId)!.Presence);
        Assert.Equal(newConnection, session.FindPlayer(playerId)!.ConnectionId);
        Assert.Null(session.FindClient(oldConnection));
        Assert.Equal(playerId, session.FindClient(newConnection)!.PlayerId);
    }

    [Fact]
    public void RejoinCanReplaceAStaleStillConnectedConnection()
    {
        var session = CreateSession(capacity: 2);
        var playerId = new PlayerId("player-1");
        var oldConnection = new ConnectionId("connection-old");
        var newConnection = new ConnectionId("connection-new");
        session.JoinPlayer(playerId, oldConnection);

        var result = session.RejoinPlayer(playerId, newConnection);

        Assert.True(result.IsAccepted);
        Assert.Single(session.Players);
        Assert.Null(session.FindClient(oldConnection));
        Assert.Equal(playerId, session.FindClient(newConnection)!.PlayerId);
    }

    [Fact]
    public void NewJoinCannotReuseExistingPlayerIdentity()
    {
        var session = CreateSession(capacity: 2);
        var playerId = new PlayerId("player-1");
        session.JoinPlayer(playerId, new ConnectionId("connection-1"));

        var result = session.JoinPlayer(playerId, new ConnectionId("connection-2"));

        Assert.Equal(JoinPlayerStatus.PlayerAlreadyExists, result.Status);
        Assert.Single(session.Players);
    }

    [Fact]
    public void ConnectionIdentityCannotBelongToTwoClients()
    {
        var session = CreateSession(capacity: 2);
        var connectionId = new ConnectionId("connection-1");
        session.ConnectClient(connectionId, ClientRole.SharedScreen);

        var result = session.JoinPlayer(new PlayerId("player-1"), connectionId);

        Assert.Equal(JoinPlayerStatus.ConnectionAlreadyInUse, result.Status);
        Assert.Empty(session.Players);
    }

    [Fact]
    public void CloseRejectsFutureJoinAndConnectOperations()
    {
        var session = CreateSession(capacity: 2);
        session.Close();

        var playerJoin = session.JoinPlayer(new PlayerId("player-1"), new ConnectionId("connection-1"));
        var screenJoin = session.ConnectClient(new ConnectionId("screen-1"), ClientRole.SharedScreen);
        var rejoin = session.RejoinPlayer(new PlayerId("player-1"), new ConnectionId("connection-2"));

        Assert.Equal(RoomSessionState.Closed, session.State);
        Assert.Equal(JoinPlayerStatus.RoomClosed, playerJoin.Status);
        Assert.Equal(ConnectClientStatus.RoomClosed, screenJoin.Status);
        Assert.Equal(RejoinPlayerStatus.RoomClosed, rejoin.Status);
    }

    [Fact]
    public void LifecycleEventsHaveDeterministicMonotonicOrder()
    {
        var session = CreateSession(capacity: 2);
        var playerId = new PlayerId("player-1");
        var firstConnection = new ConnectionId("connection-1");
        var secondConnection = new ConnectionId("connection-2");

        session.ConnectClient(new ConnectionId("screen-1"), ClientRole.SharedScreen);
        session.JoinPlayer(playerId, firstConnection);
        session.Disconnect(firstConnection);
        session.RejoinPlayer(playerId, secondConnection);
        session.LeavePlayer(playerId);
        session.Close();

        Assert.Equal(
            new[]
            {
                SessionLifecycleEventKind.SessionCreated,
                SessionLifecycleEventKind.ClientConnected,
                SessionLifecycleEventKind.PlayerJoined,
                SessionLifecycleEventKind.PlayerDisconnected,
                SessionLifecycleEventKind.PlayerRejoined,
                SessionLifecycleEventKind.PlayerLeft,
                SessionLifecycleEventKind.SessionClosed,
            },
            session.Events.Select(item => item.Kind));
        Assert.Equal(Enumerable.Range(1, session.Events.Count).Select(value => (long)value), session.Events.Select(item => item.Sequence));
    }

    [Fact]
    public void CoreAssemblyDoesNotReferenceConcreteNetworkingOrPlatformFrameworks()
    {
        var referencedAssemblies = typeof(RoomSession).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("SignalR", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("WebSocket", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Flutter", StringComparison.Ordinal));
    }

    private static RoomSession CreateSession(int capacity) =>
        new(
            new RoomId("room-1"),
            new JoinCode("room42"),
            capacity,
            new AuthorityId("authority-1"));
}

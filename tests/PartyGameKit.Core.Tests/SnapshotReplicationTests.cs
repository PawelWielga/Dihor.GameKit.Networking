namespace PartyGameKit.Core.Tests;

public sealed class SnapshotReplicationTests
{
    [Fact]
    public void PublisherUsesOneMonotonicSequenceForAllProjections()
    {
        var publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(new RoomId("room-1"));
        var player = new PlayerId("player-1");

        var first = publisher.Publish(
            new AuthorityId("authority-1"),
            new PublicView("public-1"),
            new Dictionary<PlayerId, PrivateView> { [player] = new("private-1") });
        var second = publisher.Publish(
            new AuthorityId("authority-1"),
            new PublicView("public-2"),
            new Dictionary<PlayerId, PrivateView> { [player] = new("private-2") });

        Assert.Equal(1, first.PublicSnapshot.Sequence.Value);
        Assert.Equal(1, first.PlayerSnapshots[player].Sequence.Value);
        Assert.Equal(2, second.PublicSnapshot.Sequence.Value);
        Assert.Equal(2, second.PlayerSnapshots[player].Sequence.Value);
        Assert.Equal(2, publisher.CurrentSequence);
        Assert.Same(second, publisher.Latest);
    }

    [Fact]
    public void SequenceGateRejectsDuplicateAndOutOfOrderSnapshots()
    {
        var gate = new SnapshotSequenceGate();

        Assert.True(gate.TryAccept(new SnapshotSequence(5)));
        Assert.False(gate.TryAccept(new SnapshotSequence(4)));
        Assert.False(gate.TryAccept(new SnapshotSequence(5)));
        Assert.True(gate.TryAccept(new SnapshotSequence(6)));
        Assert.Equal(6, gate.LastAppliedSequence);
    }

    [Fact]
    public void LatestStateRestoresRejoiningPlayerWithoutHistoryReplay()
    {
        var publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(new RoomId("room-1"));
        var player = new PlayerId("player-1");
        publisher.Publish(
            new AuthorityId("authority-1"),
            new PublicView("old-public"),
            new Dictionary<PlayerId, PrivateView> { [player] = new("old-private") });
        publisher.Publish(
            new AuthorityId("authority-1"),
            new PublicView("current-public"),
            new Dictionary<PlayerId, PrivateView> { [player] = new("current-private") });

        Assert.True(publisher.TryGetLatestForPlayer(player, out var latest));
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Sequence.Value);
        Assert.Equal("current-public", latest.Projection.PublicState.Value);
        Assert.Equal("current-private", latest.Projection.PrivateState.Value);
    }

    [Fact]
    public void PublicAndPrivatePlayerProjectionsAreStructurallySeparated()
    {
        var publisher = new AuthoritativeSnapshotPublisher<PublicView, PrivateView>(new RoomId("room-1"));
        var firstPlayer = new PlayerId("player-1");
        var secondPlayer = new PlayerId("player-2");

        var published = publisher.Publish(
            new AuthorityId("authority-1"),
            new PublicView("public"),
            new Dictionary<PlayerId, PrivateView>
            {
                [firstPlayer] = new("private-1"),
                [secondPlayer] = new("private-2"),
            });

        Assert.Equal(SnapshotAudience.Public, published.PublicSnapshot.Target.Audience);
        Assert.Null(published.PublicSnapshot.Target.PlayerId);
        Assert.Equal("public", published.PublicSnapshot.Projection.State.Value);
        Assert.Null(typeof(PublicStateProjection<PublicView>).GetProperty("PrivateState"));

        var first = published.PlayerSnapshots[firstPlayer];
        var second = published.PlayerSnapshots[secondPlayer];
        Assert.Equal(firstPlayer, first.Target.PlayerId);
        Assert.Equal("private-1", first.Projection.PrivateState.Value);
        Assert.Equal("private-2", second.Projection.PrivateState.Value);
    }

    [Fact]
    public void SnapshotSequenceMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotSequence(-1));
    }

    [Fact]
    public void DefaultSnapshotSequenceCannotEnterReplication()
    {
        var gate = new SnapshotSequenceGate();

        Assert.Throws<ArgumentOutOfRangeException>(() => gate.TryAccept(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateSnapshot<PublicStateProjection<PublicView>>(
            new RoomId("room-1"),
            new AuthorityId("authority-1"),
            default,
            SnapshotTarget.Public,
            new PublicStateProjection<PublicView>(new PublicView("public"))));
    }

    [Fact]
    public void PlayerTargetRequiresNonDefaultPlayerId()
    {
        Assert.Throws<ArgumentException>(() => SnapshotTarget.ForPlayer(default));
    }

    private sealed record PublicView(string Value);

    private sealed record PrivateView(string Value);
}

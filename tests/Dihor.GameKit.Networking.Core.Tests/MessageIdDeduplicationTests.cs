using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Core.Tests;

public sealed class MessageIdDeduplicationTests
{
    [Fact]
    public void FirstPeerMessagePairIsAcceptedAndDuplicateIsRejected()
    {
        var deduplicator = new MessageIdDeduplicator();
        var peer = new PeerId("peer-a");

        Assert.True(deduplicator.TryAccept(peer, "message-1"));
        Assert.False(deduplicator.TryAccept(peer, "message-1"));
        Assert.Equal(1, deduplicator.Count);
    }

    [Fact]
    public void SameMessageIdFromDifferentPeerIsAccepted()
    {
        var deduplicator = new MessageIdDeduplicator();

        Assert.True(deduplicator.TryAccept(new PeerId("peer-a"), "message-1"));
        Assert.True(deduplicator.TryAccept(new PeerId("peer-b"), "message-1"));
        Assert.Equal(2, deduplicator.Count);
    }

    [Fact]
    public void DifferentMessageIdsFromSamePeerAreAccepted()
    {
        var deduplicator = new MessageIdDeduplicator();
        var peer = new PeerId("peer-a");

        Assert.True(deduplicator.TryAccept(peer, "message-1"));
        Assert.True(deduplicator.TryAccept(peer, "message-2"));
        Assert.Equal(2, deduplicator.Count);
    }

    [Fact]
    public void ConnectionReplacementDoesNotResetStablePeerDeduplication()
    {
        var deduplicator = new MessageIdDeduplicator();
        var peer = new PeerId("peer-stable");

        Assert.True(deduplicator.TryAccept(peer, "replayed-message"));

        // ConnectionId and physical transport are intentionally not inputs to
        // deduplication. A replacement connection for the same PeerId therefore
        // observes the same logical message as a duplicate.
        Assert.False(deduplicator.TryAccept(peer, "replayed-message"));
    }

    [Fact]
    public void CapacityEvictsOldestAcceptedPairDeterministically()
    {
        var deduplicator = new MessageIdDeduplicator(capacity: 2);
        var peer = new PeerId("peer-a");

        Assert.True(deduplicator.TryAccept(peer, "message-1"));
        Assert.True(deduplicator.TryAccept(peer, "message-2"));
        Assert.True(deduplicator.TryAccept(peer, "message-3"));

        Assert.Equal(2, deduplicator.Count);

        // message-1 was the oldest pair and was evicted by capacity.
        Assert.True(deduplicator.TryAccept(peer, "message-1"));
        Assert.Equal(2, deduplicator.Count);
    }

    [Fact]
    public void ExpiredPairCanBeAcceptedAgainAndDuplicateDoesNotRefreshRetention()
    {
        var time = new ManualTimeProvider();
        var deduplicator = new MessageIdDeduplicator(
            capacity: 10,
            retention: TimeSpan.FromSeconds(10),
            timeProvider: time);
        var peer = new PeerId("peer-a");

        Assert.True(deduplicator.TryAccept(peer, "message-1"));

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(deduplicator.TryAccept(peer, "message-1"));

        // Duplicate observation does not refresh the original acceptance time.
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(deduplicator.TryAccept(peer, "message-1"));
    }

    [Fact]
    public void ForgetPeerReleasesOnlyThatPeersDeduplicationState()
    {
        var deduplicator = new MessageIdDeduplicator();
        var peerA = new PeerId("peer-a");
        var peerB = new PeerId("peer-b");

        Assert.True(deduplicator.TryAccept(peerA, "message-1"));
        Assert.True(deduplicator.TryAccept(peerA, "message-2"));
        Assert.True(deduplicator.TryAccept(peerB, "message-1"));

        Assert.Equal(2, deduplicator.ForgetPeer(peerA));
        Assert.Equal(1, deduplicator.Count);

        Assert.True(deduplicator.TryAccept(peerA, "message-1"));
        Assert.False(deduplicator.TryAccept(peerB, "message-1"));
    }

    [Fact]
    public void ClearResetsEntireDeduplicationWindow()
    {
        var deduplicator = new MessageIdDeduplicator();
        var peer = new PeerId("peer-a");

        Assert.True(deduplicator.TryAccept(peer, "message-1"));
        deduplicator.Clear();

        Assert.Equal(0, deduplicator.Count);
        Assert.True(deduplicator.TryAccept(peer, "message-1"));
    }

    [Fact]
    public void InvalidOptionsAndIdentifiersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessageIdDeduplicator(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessageIdDeduplicator(retention: TimeSpan.Zero));

        var deduplicator = new MessageIdDeduplicator();

        Assert.Throws<ArgumentException>(
            () => deduplicator.TryAccept(default, "message-1"));
        Assert.Throws<ArgumentException>(
            () => deduplicator.TryAccept(new PeerId("peer-a"), " "));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            _timestamp = checked(_timestamp + duration.Ticks);
        }
    }
}

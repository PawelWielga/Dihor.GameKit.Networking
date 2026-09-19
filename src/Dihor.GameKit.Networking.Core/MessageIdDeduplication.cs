namespace Dihor.GameKit.Networking.Core;

/// <summary>
/// Tracks recently processed logical messages by stable peer identity and
/// protocol message id so reconnect/replay duplicates can be ignored.
/// </summary>
/// <remarks>
/// This utility provides bounded deduplication only. It does not provide
/// acknowledgements, retries or exactly-once delivery.
/// </remarks>
public sealed class MessageIdDeduplicator
{
    public const int DefaultCapacity = 1024;
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<MessageKey, LinkedListNode<SeenMessage>> _entries = new();
    private readonly LinkedList<SeenMessage> _oldestFirst = new();

    public MessageIdDeduplicator(
        int capacity = DefaultCapacity,
        TimeSpan? retention = null,
        TimeProvider? timeProvider = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Deduplication capacity must be positive.");
        }

        var effectiveRetention = retention ?? DefaultRetention;
        if (effectiveRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                effectiveRetention,
                "Deduplication retention must be positive.");
        }

        Capacity = capacity;
        Retention = effectiveRetention;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Maximum number of distinct peer/message-id pairs retained at once.
    /// The oldest accepted pair is evicted when the capacity is exceeded.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Amount of elapsed time for which an accepted pair remains a duplicate.
    /// Duplicate observations do not extend this retention window.
    /// </summary>
    public TimeSpan Retention { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                RemoveExpiredLocked(_timeProvider.GetTimestamp());
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Returns true exactly when this peer/message-id pair has not been accepted
    /// inside the current bounded deduplication window.
    /// </summary>
    public bool TryAccept(PeerId peerId, string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var normalizedMessageId = messageId.Trim();
        var key = new MessageKey(peerId, normalizedMessageId);

        lock (_gate)
        {
            // Capture the timestamp while holding the same lock that defines
            // acceptance/insertion order. Otherwise two concurrent callers can
            // observe timestamps in one order and enter the oldest-first list
            // in the opposite order, breaking deterministic expiration.
            var now = _timeProvider.GetTimestamp();
            RemoveExpiredLocked(now);

            if (_entries.ContainsKey(key))
            {
                return false;
            }

            var node = _oldestFirst.AddLast(new SeenMessage(key, now));
            _entries.Add(key, node);
            TrimToCapacityLocked();
            return true;
        }
    }

    /// <summary>
    /// Removes all remembered message ids for one stable peer.
    /// Call this when the peer is deliberately forgotten or when its reconnect
    /// window expires and continuity will no longer be resumed.
    /// </summary>
    public int ForgetPeer(PeerId peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId.Value, nameof(peerId));

        lock (_gate)
        {
            RemoveExpiredLocked(_timeProvider.GetTimestamp());

            var removed = 0;
            var node = _oldestFirst.First;

            while (node is not null)
            {
                var next = node.Next;

                if (node.Value.Key.PeerId == peerId)
                {
                    _entries.Remove(node.Value.Key);
                    _oldestFirst.Remove(node);
                    removed++;
                }

                node = next;
            }

            return removed;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _oldestFirst.Clear();
        }
    }

    private void RemoveExpiredLocked(long now)
    {
        while (_oldestFirst.First is { } first)
        {
            var elapsed = _timeProvider.GetElapsedTime(
                first.Value.AcceptedTimestamp,
                now);

            if (elapsed < Retention)
            {
                return;
            }

            _entries.Remove(first.Value.Key);
            _oldestFirst.RemoveFirst();
        }
    }

    private void TrimToCapacityLocked()
    {
        while (_entries.Count > Capacity && _oldestFirst.First is { } first)
        {
            _entries.Remove(first.Value.Key);
            _oldestFirst.RemoveFirst();
        }
    }

    private readonly record struct MessageKey(PeerId PeerId, string MessageId);

    private readonly record struct SeenMessage(
        MessageKey Key,
        long AcceptedTimestamp);
}

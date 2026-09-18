namespace Dihor.GameKit.Networking.Transport.Abstractions;

/// <summary>
/// Buffers at most one replayable application payload per caller-owned key and
/// replays the newest still-valid value whenever a replacement sender is bound.
/// </summary>
/// <remarks>
/// The buffer intentionally operates on opaque wire payloads. Callers remain
/// responsible for constructing an <c>application.message</c> envelope and for
/// assigning a new protocol message id whenever the logical value changes.
/// Replays of the same staged value reuse the exact buffered bytes and therefore
/// preserve that message id.
/// </remarks>
public sealed class LatestValueReplayBuffer : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, BufferedMessage> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<FlushKey, Task<long>> _activeFlushes = new();

    private Binding? _binding;
    private long _nextVersion;
    private long _nextGeneration;
    private bool _disposed;

    /// <summary>
    /// Gets the number of caller-owned replay keys that currently have a
    /// staged latest value. Successfully sent values remain staged until they
    /// are replaced, cleared or invalidated so reconnect can replay them.
    /// </summary>
    public int BufferedCount
    {
        get
        {
            lock (_sync)
            {
                return _latest.Count;
            }
        }
    }

    /// <summary>
    /// Replaces the buffered value for <paramref name="key"/> and, when a sender
    /// is currently bound, immediately attempts to deliver the newest value.
    /// </summary>
    public async Task StageLatestAsync(
        string key,
        string scope,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var normalizedKey = RequiredToken(key, nameof(key));
        var normalizedScope = RequiredToken(scope, nameof(scope));
        Binding? binding;
        long stagedVersion;

        lock (_sync)
        {
            ThrowIfDisposed();
            stagedVersion = checked(++_nextVersion);
            _latest[normalizedKey] = new BufferedMessage(
                normalizedScope,
                message.ToArray(),
                stagedVersion);
            binding = _binding;
        }

        if (binding is null)
        {
            return;
        }

        while (true)
        {
            var sentVersion = await WaitForFlushAsync(
                    GetOrStartFlush(normalizedKey, binding),
                    cancellationToken)
                .ConfigureAwait(false);

            if (sentVersion >= stagedVersion)
            {
                return;
            }

            lock (_sync)
            {
                if (!IsCurrentBinding(binding) ||
                    !_latest.ContainsKey(normalizedKey))
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Binds an active connection and immediately replays every currently
    /// buffered latest value. Binding a replacement connection cancels retries
    /// against the previous sender without disposing either transport.
    /// </summary>
    public async Task BindSenderAsync(
        IMessageTransportClient sender,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ThrowIfDisposed();

        Binding? previous;
        Binding current;
        string[] keys;

        lock (_sync)
        {
            ThrowIfDisposed();
            previous = _binding;
            current = new Binding(
                checked(++_nextGeneration),
                sender);
            _binding = current;
            keys = _latest.Keys.ToArray();
        }

        CancelBinding(previous);

        var flushes = keys
            .Select(key => GetOrStartFlush(key, current))
            .ToArray();

        if (flushes.Length == 0)
        {
            return;
        }

        await WaitForFlushAsync(Task.WhenAll(flushes), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the current sender binding. When <paramref name="sender"/> is
    /// supplied, a stale connection can only unbind itself and cannot detach a
    /// newer replacement connection.
    /// </summary>
    public void UnbindSender(IMessageTransportClient? sender = null)
    {
        ThrowIfDisposed();

        Binding? removed;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_binding is null)
            {
                return;
            }

            if (sender is not null && !ReferenceEquals(_binding.Sender, sender))
            {
                return;
            }

            removed = _binding;
            _binding = null;
        }

        CancelBinding(removed);
    }

    /// <summary>
    /// Clears one buffered value. When <paramref name="scope"/> is supplied,
    /// the value is removed only when the current buffered scope still matches.
    /// </summary>
    public bool ClearLatest(string key, string? scope = null)
    {
        ThrowIfDisposed();

        var normalizedKey = RequiredToken(key, nameof(key));
        var normalizedScope = scope is null ? null : RequiredToken(scope, nameof(scope));

        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_latest.TryGetValue(normalizedKey, out var current))
            {
                return false;
            }

            if (normalizedScope is not null &&
                !StringComparer.Ordinal.Equals(current.Scope, normalizedScope))
            {
                return false;
            }

            return _latest.Remove(normalizedKey);
        }
    }

    /// <summary>
    /// Invalidates every buffered value that belongs to the supplied caller-owned
    /// scope/epoch token.
    /// </summary>
    public int InvalidateScope(string scope)
    {
        ThrowIfDisposed();

        var normalizedScope = RequiredToken(scope, nameof(scope));

        lock (_sync)
        {
            ThrowIfDisposed();

            var keys = _latest
                .Where(pair => StringComparer.Ordinal.Equals(pair.Value.Scope, normalizedScope))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in keys)
            {
                _latest.Remove(key);
            }

            return keys.Length;
        }
    }

    public void Dispose()
    {
        Binding? binding;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            binding = _binding;
            _binding = null;
            _latest.Clear();
        }

        CancelBinding(binding);
        GC.SuppressFinalize(this);
    }

    private Task<long> GetOrStartFlush(string key, Binding binding)
    {
        TaskCompletionSource<long> completion;
        var flushKey = new FlushKey(binding.Generation, key);

        lock (_sync)
        {
            if (_activeFlushes.TryGetValue(flushKey, out var active))
            {
                return active;
            }

            completion = new TaskCompletionSource<long>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeFlushes.Add(flushKey, completion.Task);
        }

        _ = RunFlushAsync(key, binding, flushKey, completion);
        return completion.Task;
    }

    private async Task RunFlushAsync(
        string key,
        Binding binding,
        FlushKey flushKey,
        TaskCompletionSource<long> completion)
    {
        long sentVersion = 0;
        Exception? failure = null;

        try
        {
            sentVersion = await FlushCoreAsync(key, binding).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            lock (_sync)
            {
                _activeFlushes.Remove(flushKey);
            }

            TryDisposeRetiredBinding(binding);
        }

        if (failure is null)
        {
            completion.TrySetResult(sentVersion);
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private async Task<long> FlushCoreAsync(string key, Binding binding)
    {
        long lastSentVersion = 0;

        while (true)
        {
            try
            {
                await binding.SendGate
                    .WaitAsync(binding.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (binding.Token.IsCancellationRequested)
            {
                return lastSentVersion;
            }

            BufferedMessage buffered;
            ValueTask sendTask;

            try
            {
                lock (_sync)
                {
                    if (!IsCurrentBinding(binding) ||
                        !_latest.TryGetValue(key, out var currentBuffered))
                    {
                        return lastSentVersion;
                    }

                    buffered = currentBuffered;

                    // Starting the send while holding _sync makes ClearLatest /
                    // InvalidateScope atomic with respect to send start. A clear
                    // that completes first prevents this transport call; a clear
                    // that happens after this point cannot retract an in-flight
                    // transport operation.
                    sendTask = binding.Sender.SendAsync(
                        buffered.Payload,
                        binding.Token);
                }

                try
                {
                    await sendTask.ConfigureAwait(false);
                    lastSentVersion = buffered.Version;
                }
                catch (OperationCanceledException) when (binding.Token.IsCancellationRequested)
                {
                    return lastSentVersion;
                }
            }
            finally
            {
                binding.SendGate.Release();
            }

            lock (_sync)
            {
                if (!IsCurrentBinding(binding))
                {
                    // Delivery on a connection that was replaced while the send
                    // was in flight is ambiguous. Keep the value so the current
                    // connection can replay it with the same message id.
                    return lastSentVersion;
                }

                if (!_latest.TryGetValue(key, out var current))
                {
                    return lastSentVersion;
                }

                if (current.Version == buffered.Version)
                {
                    // A locally completed send is not an acknowledgement from
                    // the remote peer. Keep the latest value staged so a later
                    // replacement connection can replay the exact same message.
                    return lastSentVersion;
                }

                // A newer value replaced the one that just completed. Continue
                // on the same sender and deliver only the current latest value.
            }
        }
    }

    private bool IsCurrentBinding(Binding binding) =>
        _binding is not null && _binding.Generation == binding.Generation;

    private static async Task<T> WaitForFlushAsync<T>(
        Task<T> flush,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await flush.ConfigureAwait(false);
        }

        return await flush.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void CancelBinding(Binding? binding)
    {
        if (binding is null)
        {
            return;
        }

        binding.Cancel();
        TryDisposeRetiredBinding(binding);
    }

    private void TryDisposeRetiredBinding(Binding binding)
    {
        bool shouldDispose;

        lock (_sync)
        {
            shouldDispose =
                !IsCurrentBinding(binding) &&
                !_activeFlushes.Keys.Any(key => key.Generation == binding.Generation);
        }

        if (shouldDispose)
        {
            binding.Dispose();
        }
    }

    private static string RequiredToken(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        }

        return normalized;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record BufferedMessage(string Scope, byte[] Payload, long Version);

    private sealed class Binding : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _disposed;

        public Binding(long generation, IMessageTransportClient sender)
        {
            Generation = generation;
            Sender = sender;
            Token = _cancellation.Token;
        }

        public long Generation { get; }

        public IMessageTransportClient Sender { get; }

        public CancellationToken Token { get; }

        public SemaphoreSlim SendGate { get; } = new(1, 1);

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A completed stale flush may dispose the retired binding at
                // the same time an unbind/rebind path requests cancellation.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _cancellation.Dispose();
                SendGate.Dispose();
            }
        }
    }

    private readonly record struct FlushKey(long Generation, string Key);
}

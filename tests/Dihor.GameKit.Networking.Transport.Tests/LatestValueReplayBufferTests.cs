using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Transport.Tests;

public sealed class LatestValueReplayBufferTests
{
    [Fact]
    public async Task RebindReplaysOnlyNewestBufferedValueForKey()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 3 },
            TestContext.Current.CancellationToken);

        var sender = new FakeClient();
        await buffer.BindSenderAsync(sender, TestContext.Current.CancellationToken);

        var sent = Assert.Single(sender.SentPayloads);
        Assert.Equal(new byte[] { 3 }, sent);
        Assert.Equal(1, buffer.BufferedCount);

        buffer.UnbindSender(sender);

        var replacement = new FakeClient();
        await buffer.BindSenderAsync(
            replacement,
            TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 3 }, Assert.Single(replacement.SentPayloads));
        Assert.Equal(1, buffer.BufferedCount);

        Assert.True(buffer.ClearLatest("draft", "round-1"));
        Assert.Equal(0, buffer.BufferedCount);
    }

    [Fact]
    public async Task ScopeReplacementPreventsOldScopeFromReplaying()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft",
            "round-old",
            new byte[] { 1, 1 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft",
            "round-new",
            new byte[] { 2, 2 },
            TestContext.Current.CancellationToken);

        var sender = new FakeClient();
        await buffer.BindSenderAsync(sender, TestContext.Current.CancellationToken);

        var sent = Assert.Single(sender.SentPayloads);
        Assert.Equal(new byte[] { 2, 2 }, sent);
    }

    [Fact]
    public async Task FailedSendRetainsNewestValueForReplacementSender()
    {
        using var buffer = new LatestValueReplayBuffer();
        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 4, 2 },
            TestContext.Current.CancellationToken);

        var failingSender = new FakeClient
        {
            SendException = new IOException("connection lost"),
        };

        await Assert.ThrowsAsync<IOException>(
            () => buffer.BindSenderAsync(
                failingSender,
                TestContext.Current.CancellationToken));

        Assert.Equal(1, buffer.BufferedCount);

        buffer.UnbindSender(failingSender);
        var replacement = new FakeClient();
        await buffer.BindSenderAsync(replacement, TestContext.Current.CancellationToken);

        var sent = Assert.Single(replacement.SentPayloads);
        Assert.Equal(new byte[] { 4, 2 }, sent);
        Assert.Equal(1, buffer.BufferedCount);
    }

    [Fact]
    public async Task UnboundStagingNeverUsesStaleSenderAndStaleUnbindCannotDetachReplacement()
    {
        using var buffer = new LatestValueReplayBuffer();
        var stale = new FakeClient();

        await buffer.BindSenderAsync(stale, TestContext.Current.CancellationToken);
        buffer.UnbindSender(stale);

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 5 },
            TestContext.Current.CancellationToken);

        Assert.Empty(stale.SentPayloads);

        var replacement = new FakeClient();
        await buffer.BindSenderAsync(replacement, TestContext.Current.CancellationToken);
        buffer.UnbindSender(stale);

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 6 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, replacement.SentPayloads.Count);
        Assert.Equal(new byte[] { 5 }, replacement.SentPayloads[0]);
        Assert.Equal(new byte[] { 6 }, replacement.SentPayloads[1]);
    }

    [Fact]
    public async Task NewerValueStagedDuringSendIsDeliveredBeforeStageCompletes()
    {
        using var buffer = new LatestValueReplayBuffer();
        var sender = new BlockingClient();

        await buffer.BindSenderAsync(
            sender,
            TestContext.Current.CancellationToken);

        var firstStage = buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        await sender.SendStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        var secondStage = buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);

        sender.Release.TrySetResult();
        await Task.WhenAll(firstStage, secondStage);

        Assert.Equal(2, sender.SentPayloads.Count);
        Assert.Equal(new byte[] { 1 }, sender.SentPayloads[0]);
        Assert.Equal(new byte[] { 2 }, sender.SentPayloads[1]);
        Assert.Equal(1, buffer.BufferedCount);
    }

    [Fact]
    public async Task BufferedKeysAreSerializedPerBoundSender()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft-a",
            "scope-a",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft-b",
            "scope-b",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);

        var sender = new BlockingClient();
        var binding = buffer.BindSenderAsync(
            sender,
            TestContext.Current.CancellationToken);

        await sender.SendStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Single(sender.SentPayloads);

        sender.Release.TrySetResult();
        await binding;

        Assert.Equal(2, sender.SentPayloads.Count);
    }

    [Fact]
    public async Task ScopeInvalidationPreventsQueuedSendFromStarting()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft-a",
            "scope-a",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft-b",
            "scope-b",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);

        var sender = new BlockingClient();
        var binding = buffer.BindSenderAsync(
            sender,
            TestContext.Current.CancellationToken);

        await sender.SendStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        var firstPayload = Assert.Single(sender.SentPayloads);
        var scopeStillWaiting = firstPayload[0] == 1 ? "scope-b" : "scope-a";
        Assert.Equal(1, buffer.InvalidateScope(scopeStillWaiting));

        sender.Release.TrySetResult();
        await binding;

        Assert.Single(sender.SentPayloads);
    }

    [Fact]
    public async Task StaleInFlightSendCompletionDoesNotClearNewerBufferedValue()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);

        var stale = new BlockingClient();
        var binding = buffer.BindSenderAsync(
            stale,
            TestContext.Current.CancellationToken);

        await stale.SendStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        buffer.UnbindSender(stale);

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);

        stale.Release.TrySetResult();
        await binding;

        Assert.Equal(1, buffer.BufferedCount);

        var replacement = new FakeClient();
        await buffer.BindSenderAsync(
            replacement,
            TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 2 }, Assert.Single(replacement.SentPayloads));
        Assert.Equal(1, buffer.BufferedCount);
    }

    [Fact]
    public async Task ClearAndScopeInvalidationRemoveReplayStateDeterministically()
    {
        using var buffer = new LatestValueReplayBuffer();

        await buffer.StageLatestAsync(
            "draft-a",
            "round-1",
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft-b",
            "round-1",
            new byte[] { 2 },
            TestContext.Current.CancellationToken);
        await buffer.StageLatestAsync(
            "draft-c",
            "round-2",
            new byte[] { 3 },
            TestContext.Current.CancellationToken);

        Assert.False(buffer.ClearLatest("draft-a", "round-2"));
        Assert.True(buffer.ClearLatest("draft-a", "round-1"));
        Assert.Equal(1, buffer.InvalidateScope("round-1"));
        Assert.Equal(1, buffer.BufferedCount);

        var sender = new FakeClient();
        await buffer.BindSenderAsync(sender, TestContext.Current.CancellationToken);

        var sent = Assert.Single(sender.SentPayloads);
        Assert.Equal(new byte[] { 3 }, sent);
    }

    [Fact]
    public async Task AutomaticReconnectFallbackReplaysBufferedValueAcrossTransportChange()
    {
        using var buffer = new LatestValueReplayBuffer();
        var webRtcAvailable = true;
        var webRtc = new FakeClient();
        var signalR = new FakeClient();

        var selector = new AutomaticTransportSelector(
            [
                new TransportClientCandidate(
                    TransportIds.LanWebSocket,
                    (_, _) => Task.FromException<IMessageTransportClient>(
                        new IOException("LAN unavailable"))),
                new TransportClientCandidate(
                    TransportIds.WebRtcDataChannel,
                    (_, _) => webRtcAvailable
                        ? Task.FromResult<IMessageTransportClient>(webRtc)
                        : Task.FromException<IMessageTransportClient>(
                            new IOException("WebRTC unavailable"))),
                new TransportClientCandidate(
                    TransportIds.SignalRRelay,
                    (_, _) => Task.FromResult<IMessageTransportClient>(signalR)),
            ],
            new ConnectivitySelectionOptions(
                TimeSpan.FromMilliseconds(250),
                [TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel, TransportIds.SignalRRelay]));

        await using (var initial = await selector.ConnectAsync(
                         "peer=stable;phase=connect",
                         cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(TransportIds.WebRtcDataChannel, initial.TransportId);
            await buffer.BindSenderAsync(initial, TestContext.Current.CancellationToken);
            await buffer.StageLatestAsync(
                "draft",
                "round-1",
                new byte[] { 7 },
                TestContext.Current.CancellationToken);

            buffer.UnbindSender(initial);
        }

        await buffer.StageLatestAsync(
            "draft",
            "round-1",
            new byte[] { 8 },
            TestContext.Current.CancellationToken);
        webRtcAvailable = false;

        await using var replacement = await selector.ReconnectAsync(
            "peer=stable;phase=resume",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.SignalRRelay, replacement.TransportId);
        await buffer.BindSenderAsync(replacement, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 7 }, Assert.Single(webRtc.SentPayloads));
        Assert.Equal(new byte[] { 8 }, Assert.Single(signalR.SentPayloads));
    }

    private sealed class BlockingClient : IMessageTransportClient
    {
        public List<byte[]> SentPayloads { get; } = new();

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            SentPayloads.Add(payload.ToArray());
            SendStarted.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }

        public ValueTask<ClientTransportMessage> ReceiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new ClientTransportMessage(ReadOnlyMemory<byte>.Empty));

        public ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeClient : IMessageTransportClient
    {
        public List<byte[]> SentPayloads { get; } = new();

        public Exception? SendException { get; init; }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SendException is not null)
            {
                throw SendException;
            }

            SentPayloads.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<ClientTransportMessage> ReceiveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ClientTransportMessage(ReadOnlyMemory<byte>.Empty));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

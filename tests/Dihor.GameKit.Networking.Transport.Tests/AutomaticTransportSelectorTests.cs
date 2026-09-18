using System.Collections.Concurrent;
using Dihor.GameKit.Networking.Transport.Abstractions;

namespace Dihor.GameKit.Networking.Transport.Tests;

public sealed class AutomaticTransportSelectorTests
{
    [Fact]
    public async Task AutoSelectsLanWithoutUnnecessaryFallback()
    {
        var attempts = new List<string>();
        var selector = CreateSelector(
            Success(TransportIds.LanWebSocket, attempts),
            Success(TransportIds.WebRtcDataChannel, attempts),
            Success(TransportIds.SignalRRelay, attempts));

        await using var connection = await selector.ConnectAsync(
            "connect-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.LanWebSocket, connection.TransportId);
        Assert.Equal(new[] { TransportIds.LanWebSocket }, attempts);
        Assert.Single(connection.Diagnostics.Attempts);
        Assert.Equal(ConnectivityAttemptOutcome.Selected, connection.Diagnostics.Attempts[0].Outcome);
    }

    [Fact]
    public async Task AutoFallsBackFromLanToWebRtc()
    {
        var attempts = new List<string>();
        var selector = CreateSelector(
            Failure(TransportIds.LanWebSocket, attempts),
            Success(TransportIds.WebRtcDataChannel, attempts),
            Success(TransportIds.SignalRRelay, attempts));

        await using var connection = await selector.ConnectAsync(
            "connect-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.WebRtcDataChannel, connection.TransportId);
        Assert.Equal(new[] { TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel }, attempts);
        Assert.Equal(ConnectivityAttemptOutcome.Failed, connection.Diagnostics.Attempts[0].Outcome);
        Assert.Equal(ConnectivityAttemptOutcome.Selected, connection.Diagnostics.Attempts[1].Outcome);
    }

    [Fact]
    public async Task AutoFallsBackToSignalRWhenDirectPathsFail()
    {
        var attempts = new List<string>();
        var selector = CreateSelector(
            Failure(TransportIds.LanWebSocket, attempts),
            Failure(TransportIds.WebRtcDataChannel, attempts),
            Success(TransportIds.SignalRRelay, attempts));

        await using var connection = await selector.ConnectAsync(
            "connect-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.SignalRRelay, connection.TransportId);
        Assert.Equal(
            new[] { TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel, TransportIds.SignalRRelay },
            attempts);
    }

    [Fact]
    public async Task AutoReportsEveryFailureWhenNoPathSucceeds()
    {
        var selector = CreateSelector(
            Failure(TransportIds.LanWebSocket),
            Failure(TransportIds.WebRtcDataChannel),
            Failure(TransportIds.SignalRRelay));

        var exception = await Assert.ThrowsAsync<ConnectivitySelectionException>(
            () => selector.ConnectAsync(
                "connect-handshake",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(exception.Diagnostics.SelectedTransport);
        Assert.Collection(
            exception.Diagnostics.Attempts,
            attempt => Assert.Equal(TransportIds.LanWebSocket, attempt.TransportId),
            attempt => Assert.Equal(TransportIds.WebRtcDataChannel, attempt.TransportId),
            attempt => Assert.Equal(TransportIds.SignalRRelay, attempt.TransportId));
        Assert.All(
            exception.Diagnostics.Attempts,
            attempt => Assert.Equal(ConnectivityAttemptOutcome.Failed, attempt.Outcome));
    }

    [Fact]
    public async Task AttemptTimeoutIsBoundedAndFallsBack()
    {
        var neverCompletes = new TaskCompletionSource<IMessageTransportClient>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var selector = new AutomaticTransportSelector(
            [
                new TransportClientCandidate(
                    TransportIds.LanWebSocket,
                    (_, _) => neverCompletes.Task,
                    TimeSpan.FromMilliseconds(25)),
                Success(TransportIds.WebRtcDataChannel),
            ],
            new ConnectivitySelectionOptions(
                TimeSpan.FromSeconds(1),
                [TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel]));

        await using var connection = await selector.ConnectAsync(
            "connect-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.WebRtcDataChannel, connection.TransportId);
        Assert.Equal(ConnectivityAttemptOutcome.TimedOut, connection.Diagnostics.Attempts[0].Outcome);
    }

    [Fact]
    public async Task CancellationStopsSelectionWithoutFallback()
    {
        var secondAttempted = false;
        var selector = new AutomaticTransportSelector(
            [
                new TransportClientCandidate(
                    TransportIds.LanWebSocket,
                    async (_, cancellationToken) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        throw new InvalidOperationException("unreachable");
                    }),
                new TransportClientCandidate(
                    TransportIds.WebRtcDataChannel,
                    (_, _) =>
                    {
                        secondAttempted = true;
                        return Task.FromResult<IMessageTransportClient>(new FakeClient());
                    }),
            ],
            new ConnectivitySelectionOptions(
                TimeSpan.FromSeconds(5),
                [TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel]));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => selector.ConnectAsync("connect-handshake", cancellationToken: cancellation.Token));

        Assert.False(secondAttempted);
    }

    [Fact]
    public async Task CancellationDisposesLateSuccessFromCandidateThatIgnoresCancellation()
    {
        var lateResult = new TaskCompletionSource<IMessageTransportClient>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lateClient = new FakeClient();
        var selector = new AutomaticTransportSelector(
            [
                new TransportClientCandidate(
                    TransportIds.LanWebSocket,
                    (_, _) => lateResult.Task),
            ],
            new ConnectivitySelectionOptions(
                TimeSpan.FromSeconds(5),
                [TransportIds.LanWebSocket]));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var connecting = selector.ConnectAsync(
            "connect-handshake",
            cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);

        lateResult.SetResult(lateClient);
        await lateClient.Disposed.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.True(lateClient.IsDisposed);
    }

    [Fact]
    public async Task ReconnectPrefersPreviouslySuccessfulTransport()
    {
        var attempts = new List<string>();
        var lanAvailable = false;
        var selector = CreateSelector(
            Conditional(TransportIds.LanWebSocket, attempts, () => lanAvailable),
            Success(TransportIds.WebRtcDataChannel, attempts),
            Success(TransportIds.SignalRRelay, attempts));

        await using (var initial = await selector.ConnectAsync(
                         "connect-handshake",
                         cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(TransportIds.WebRtcDataChannel, initial.TransportId);
        }

        attempts.Clear();
        lanAvailable = true;
        await using var reconnected = await selector.ReconnectAsync(
            "resume-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.WebRtcDataChannel, reconnected.TransportId);
        Assert.Equal(new[] { TransportIds.WebRtcDataChannel }, attempts);
        Assert.Equal(TransportIds.WebRtcDataChannel, reconnected.Diagnostics.PreferredTransport);
        Assert.True(reconnected.Diagnostics.PreferredTransportReused);
    }

    [Fact]
    public async Task ReconnectFallsBackDeterministicallyWhenPreviousPathDisappears()
    {
        var attempts = new List<string>();
        var webRtcAvailable = true;
        var selector = CreateSelector(
            Failure(TransportIds.LanWebSocket, attempts),
            Conditional(TransportIds.WebRtcDataChannel, attempts, () => webRtcAvailable),
            Success(TransportIds.SignalRRelay, attempts));

        await using (var initial = await selector.ConnectAsync(
                         "connect-handshake",
                         cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(TransportIds.WebRtcDataChannel, initial.TransportId);
        }

        attempts.Clear();
        webRtcAvailable = false;
        await using var reconnected = await selector.ReconnectAsync(
            "resume-handshake",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TransportIds.SignalRRelay, reconnected.TransportId);
        Assert.Equal(
            new[] { TransportIds.WebRtcDataChannel, TransportIds.LanWebSocket, TransportIds.SignalRRelay },
            attempts);
        Assert.False(reconnected.Diagnostics.PreferredTransportReused);
    }

    [Theory]
    [InlineData(ConnectivityMode.Lan, TransportIds.LanWebSocket)]
    [InlineData(ConnectivityMode.WebRtc, TransportIds.WebRtcDataChannel)]
    [InlineData(ConnectivityMode.SignalR, TransportIds.SignalRRelay)]
    public async Task ForcedModeAttemptsOnlyRequestedTransport(
        ConnectivityMode mode,
        string expectedTransport)
    {
        var attempts = new List<string>();
        var selector = CreateSelector(
            Success(TransportIds.LanWebSocket, attempts),
            Success(TransportIds.WebRtcDataChannel, attempts),
            Success(TransportIds.SignalRRelay, attempts));

        await using var connection = await selector.ConnectAsync(
            "connect-handshake",
            mode,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedTransport, connection.TransportId);
        Assert.Equal(new[] { expectedTransport }, attempts);
    }

    [Fact]
    public async Task HandshakeAndPayloadRemainOpaqueAcrossTransportChange()
    {
        var handshakes = new ConcurrentQueue<(string Transport, string Handshake)>();
        var clients = new ConcurrentQueue<FakeClient>();
        var signalRAvailable = false;
        var selector = new AutomaticTransportSelector(
            [
                new TransportClientCandidate(
                    TransportIds.LanWebSocket,
                    (_, _) => Task.FromException<IMessageTransportClient>(new IOException("LAN unavailable"))),
                new TransportClientCandidate(
                    TransportIds.WebRtcDataChannel,
                    (handshake, _) =>
                    {
                        handshakes.Enqueue((TransportIds.WebRtcDataChannel, handshake));
                        if (signalRAvailable)
                        {
                            return Task.FromException<IMessageTransportClient>(new IOException("WebRTC unavailable"));
                        }

                        var client = new FakeClient();
                        clients.Enqueue(client);
                        return Task.FromResult<IMessageTransportClient>(client);
                    }),
                new TransportClientCandidate(
                    TransportIds.SignalRRelay,
                    (handshake, _) =>
                    {
                        handshakes.Enqueue((TransportIds.SignalRRelay, handshake));
                        var client = new FakeClient();
                        clients.Enqueue(client);
                        return Task.FromResult<IMessageTransportClient>(client);
                    }),
            ]);

        var payloadBefore = new byte[] { 1, 2, 3, 4 };
        await using (var initial = await selector.ConnectAsync(
                         "peer=stable-id;phase=connect",
                         cancellationToken: TestContext.Current.CancellationToken))
        {
            await initial.SendAsync(payloadBefore, TestContext.Current.CancellationToken);
            Assert.Equal(payloadBefore, clients.Last().SentPayloads.Single());
        }

        signalRAvailable = true;
        var payloadAfter = new byte[] { 5, 6, 7, 8 };
        await using (var reconnected = await selector.ReconnectAsync(
                         "peer=stable-id;phase=resume",
                         cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(TransportIds.SignalRRelay, reconnected.TransportId);
            await reconnected.SendAsync(payloadAfter, TestContext.Current.CancellationToken);
            Assert.Equal(payloadAfter, clients.Last().SentPayloads.Single());
        }

        Assert.Contains(
            handshakes,
            value => value == (TransportIds.WebRtcDataChannel, "peer=stable-id;phase=connect"));
        Assert.Contains(
            handshakes,
            value => value == (TransportIds.WebRtcDataChannel, "peer=stable-id;phase=resume"));
        Assert.Contains(
            handshakes,
            value => value == (TransportIds.SignalRRelay, "peer=stable-id;phase=resume"));
    }

    private static AutomaticTransportSelector CreateSelector(params TransportClientCandidate[] candidates) =>
        new(
            candidates,
            new ConnectivitySelectionOptions(
                TimeSpan.FromMilliseconds(250),
                [TransportIds.LanWebSocket, TransportIds.WebRtcDataChannel, TransportIds.SignalRRelay]));

    private static TransportClientCandidate Success(
        string transportId,
        List<string>? attempts = null) =>
        new(
            transportId,
            (_, _) =>
            {
                attempts?.Add(transportId);
                return Task.FromResult<IMessageTransportClient>(new FakeClient());
            });

    private static TransportClientCandidate Failure(
        string transportId,
        List<string>? attempts = null) =>
        new(
            transportId,
            (_, _) =>
            {
                attempts?.Add(transportId);
                return Task.FromException<IMessageTransportClient>(
                    new IOException($"{transportId} unavailable"));
            });

    private static TransportClientCandidate Conditional(
        string transportId,
        List<string> attempts,
        Func<bool> isAvailable) =>
        new(
            transportId,
            (_, _) =>
            {
                attempts.Add(transportId);
                return isAvailable()
                    ? Task.FromResult<IMessageTransportClient>(new FakeClient())
                    : Task.FromException<IMessageTransportClient>(new IOException($"{transportId} unavailable"));
            });

    private sealed class FakeClient : IMessageTransportClient
    {
        public List<byte[]> SentPayloads { get; } = new();

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsDisposed { get; private set; }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SentPayloads.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<ClientTransportMessage> ReceiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ClientTransportMessage(ReadOnlyMemory<byte>.Empty));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}

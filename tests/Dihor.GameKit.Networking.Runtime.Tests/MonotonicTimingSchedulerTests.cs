using Dihor.GameKit.Networking.Core;
using Dihor.GameKit.Networking.Runtime;

namespace Dihor.GameKit.Networking.Runtime.Tests;

public sealed class MonotonicTimingSchedulerTests
{
    [Fact]
    public async Task ActivationSendsProbeAndReplyBuildsPeerModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var peerId = new PeerId("peer-1");
        var now = 1_000d;
        TimingProbe? sentProbe = null;
        await using var scheduler = new MonotonicTimingScheduler(
            (actualPeer, probe, _) =>
            {
                Assert.Equal(peerId, actualPeer);
                sentProbe = probe;
                return ValueTask.CompletedTask;
            },
            new MonotonicTimingSchedulerOptions(TimeSpan.FromMinutes(1)),
            clock: () => now);
        await scheduler.StartAsync(cancellationToken);

        await scheduler.ActivatePeerAsync(peerId, cancellationToken);

        var probe = Assert.IsType<TimingProbe>(sentProbe);
        now = 1_010d;
        var observation = scheduler.ObserveReply(
            peerId,
            new TimingProbeReply(probe.ProbeId, 1_003d, 1_004d));
        Assert.Equal(TimingSampleStatus.Accepted, observation.Status);
        Assert.NotNull(scheduler.GetDiagnostics(peerId).Model);

        await scheduler.ResetPeerAsync(peerId, TimingResetReason.Reconnect, cancellationToken);
        var diagnostics = scheduler.GetDiagnostics(peerId);
        Assert.Equal(1, diagnostics.Generation);
        Assert.Equal(TimingResetReason.Reconnect, diagnostics.LastResetReason);
    }
}

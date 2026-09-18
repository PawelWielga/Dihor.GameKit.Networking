using Dihor.GameKit.Networking.Core;

namespace Dihor.GameKit.Networking.Core.Tests;

public sealed class MessageOrderingTests
{
    [Fact]
    public void SequenceGateRejectsDuplicateAndStaleMessages()
    {
        var gate = new SequenceGate();

        Assert.True(gate.TryAccept(new MessageSequence(1)));
        Assert.True(gate.TryAccept(new MessageSequence(2)));
        Assert.False(gate.TryAccept(new MessageSequence(2)));
        Assert.False(gate.TryAccept(new MessageSequence(1)));
        Assert.Equal(new MessageSequence(2), gate.LastAccepted);
    }

    [Fact]
    public void SequenceGateResetAllowsNewSequenceStream()
    {
        var gate = new SequenceGate();
        gate.TryAccept(new MessageSequence(10));

        gate.Reset();

        Assert.Null(gate.LastAccepted);
        Assert.True(gate.TryAccept(new MessageSequence(1)));
    }

    [Fact]
    public void MessageSequenceRejectsNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageSequence(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageSequence(-1));
    }
}

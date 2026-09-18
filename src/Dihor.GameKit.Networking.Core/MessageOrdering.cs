namespace Dihor.GameKit.Networking.Core;

public readonly record struct MessageSequence
{
    public MessageSequence(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Message sequence must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class SequenceGate
{
    private readonly object _gate = new();
    private MessageSequence? _lastAccepted;

    public MessageSequence? LastAccepted
    {
        get
        {
            lock (_gate)
            {
                return _lastAccepted;
            }
        }
    }

    public bool TryAccept(MessageSequence sequence)
    {
        lock (_gate)
        {
            if (_lastAccepted is { } current && sequence.Value <= current.Value)
            {
                return false;
            }

            _lastAccepted = sequence;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastAccepted = null;
        }
    }
}

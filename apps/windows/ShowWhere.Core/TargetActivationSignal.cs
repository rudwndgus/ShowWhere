namespace ShowWhere.Core;

/// <summary>
/// Carries one explicit user activation from the click/touch overlay to the
/// guidance monitor without treating unrelated screen animation as input.
/// </summary>
public sealed class TargetActivationSignal
{
    private readonly object _gate = new();
    private long _sequence;
    private long _consumedSequence;
    private double _x;
    private double _y;

    public void Record(double physicalScreenX, double physicalScreenY)
    {
        lock (_gate)
        {
            _x = physicalScreenX;
            _y = physicalScreenY;
            _sequence++;
        }
    }

    public bool TryConsumeInside(UiBounds target)
    {
        lock (_gate)
        {
            if (_sequence == _consumedSequence) return false;
            _consumedSequence = _sequence;
            return _x >= target.X && _x <= target.X + target.Width
                && _y >= target.Y && _y <= target.Y + target.Height;
        }
    }
}

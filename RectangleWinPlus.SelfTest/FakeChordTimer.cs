namespace RectangleWinPlus.SelfTest;

/// <summary>A chord timer that only fires when the test says so.</summary>
internal sealed class FakeChordTimer : IChordTimer
{
    public event Action? Elapsed;

    public bool Running { get; private set; }
    public int Interval { get; private set; }

    public void Restart(int intervalMs)
    {
        Running = true;
        Interval = intervalMs;
    }

    public void Stop() => Running = false;

    /// <summary>Simulates the chord window expiring. A stopped timer does nothing, as in real life.</summary>
    public void Fire()
    {
        if (!Running) return;
        Running = false;
        Elapsed?.Invoke();
    }
}

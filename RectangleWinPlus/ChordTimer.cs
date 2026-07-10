namespace RectangleWinPlus;

/// <summary>
/// The delay a lone arrow waits to see whether a second arrow joins it. Abstracted so the chord
/// engine can be driven deterministically in tests without a message pump.
/// </summary>
internal interface IChordTimer
{
    event Action? Elapsed;
    void Restart(int intervalMs);
    void Stop();
}

internal sealed class FormsChordTimer : IChordTimer, IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();

    public FormsChordTimer() => _timer.Tick += (_, _) => Elapsed?.Invoke();

    public event Action? Elapsed;

    public void Restart(int intervalMs)
    {
        _timer.Stop();
        _timer.Interval = Math.Max(1, intervalMs);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Dispose();
}

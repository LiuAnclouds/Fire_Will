using System.Diagnostics;

namespace FireWill.Core.Execution;

public interface IInputSink
{
    void KeyDown(string key);

    void KeyUp(string key);

    void MoveMouse(
        int x,
        int y,
        double? clientXRatio = null,
        double? clientYRatio = null);

    void LeftButtonDown();

    void LeftButtonUp();

    void SendChat(string text);
}

public interface IClock
{
    long ElapsedMilliseconds { get; }

    ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
}

public sealed class SystemClock : IClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        return milliseconds <= 0
            ? ValueTask.CompletedTask
            : new ValueTask(Task.Delay(milliseconds, cancellationToken));
    }
}

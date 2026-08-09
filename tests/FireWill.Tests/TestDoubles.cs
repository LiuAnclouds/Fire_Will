using FireWill.Core.Execution;

namespace FireWill.Tests;

internal sealed class TraceLog
{
    public List<string> Entries { get; } = [];
}

internal sealed class RecordingInput(TraceLog trace) : IInputSink
{
    public (int X, int Y, double? ClientXRatio, double? ClientYRatio, double? CaptureAspectRatio)? LastMove { get; private set; }

    public void KeyDown(string key) => trace.Entries.Add($"key-down:{key}");

    public void KeyUp(string key) => trace.Entries.Add($"key-up:{key}");

    public void MoveMouse(
        int x,
        int y,
        double? clientXRatio = null,
        double? clientYRatio = null,
        double? captureAspectRatio = null)
    {
        LastMove = (x, y, clientXRatio, clientYRatio, captureAspectRatio);
        trace.Entries.Add($"move:{x},{y}");
    }

    public void LeftButtonDown() => trace.Entries.Add("mouse-down:left");

    public void LeftButtonUp() => trace.Entries.Add("mouse-up:left");

    public void SendChat(string text) => trace.Entries.Add($"chat:{text}");
}

internal sealed class FakeClock(TraceLog trace, long initialMilliseconds = 1_000) : IClock
{
    private long _elapsedMilliseconds = initialMilliseconds;

    public long ElapsedMilliseconds => _elapsedMilliseconds;

    public Action<int, bool, CancellationToken>? BeforeDelay { get; set; }

    public ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        var interruptible = cancellationToken.CanBeCanceled;
        trace.Entries.Add($"delay:{milliseconds}:{(interruptible ? "interruptible" : "fixed")}");
        BeforeDelay?.Invoke(milliseconds, interruptible, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _elapsedMilliseconds += milliseconds;
        return ValueTask.CompletedTask;
    }

    public void Advance(int milliseconds)
    {
        _elapsedMilliseconds += milliseconds;
    }
}

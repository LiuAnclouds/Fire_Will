using FireWill.Core.Configuration;

namespace FireWill.Core.Execution;

public enum FlowRunStatus
{
    Completed,
    Stopped,
    Disabled,
    Busy,
    FlowDisabled,
    Failed,
}

public enum StopTapResult
{
    Stopped,
    Resumed,
}

public sealed record FlowRunResult(
    FlowRunStatus Status,
    int FlowSlot,
    string FlowName,
    IReadOnlyList<string> Warnings,
    Exception? Error = null);

public sealed class FlowScheduler
{
    private const int StopDoubleTapMilliseconds = 350;

    private readonly object _stateLock = new();
    private readonly IInputSink _input;
    private readonly IClock _clock;
    private readonly MacroActionCompiler _compiler;
    private CancellationTokenSource? _activeRun;
    private TaskCompletionSource? _activeRunCompletion;
    private long? _lastStopTap;
    private bool _enabled = true;
    private bool _running;

    public FlowScheduler(IInputSink input, IClock clock, MacroActionCompiler? compiler = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _compiler = compiler ?? new MacroActionCompiler();
    }

    public bool IsEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _enabled;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _running;
            }
        }
    }

    public Task<FlowRunResult> RunFlowAsync(
        MacroConfiguration configuration,
        int slot,
        CancellationToken cancellationToken = default)
    {
        return RunFlowAsync(_compiler.CompileFlow(configuration, slot), cancellationToken);
    }

    public async Task<FlowRunResult> RunFlowAsync(
        CompiledFlow flow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);

        CancellationTokenSource? runSource;
        TaskCompletionSource? runCompletion;
        lock (_stateLock)
        {
            if (!_enabled)
            {
                return Result(FlowRunStatus.Disabled, flow);
            }

            if (_running)
            {
                return Result(FlowRunStatus.Busy, flow);
            }

            if (!flow.Enabled)
            {
                return Result(FlowRunStatus.FlowDisabled, flow);
            }

            _running = true;
            runSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRun = runSource;
            _activeRunCompletion = runCompletion;
        }

        var warnings = new List<string>();
        try
        {
            foreach (var group in flow.Groups)
            {
                runSource.Token.ThrowIfCancellationRequested();
                foreach (var action in group.Actions)
                {
                    await ExecuteActionAsync(action, warnings, runSource.Token).ConfigureAwait(false);
                }

                if (group.WaitMilliseconds > 0)
                {
                    await DelayLikeLegacyAsync(
                        group.WaitMilliseconds,
                        interruptible: true,
                        runSource.Token).ConfigureAwait(false);
                }
            }

            runSource.Token.ThrowIfCancellationRequested();
            return new FlowRunResult(FlowRunStatus.Completed, flow.Slot, flow.Name, warnings);
        }
        catch (OperationCanceledException) when (runSource.IsCancellationRequested)
        {
            return new FlowRunResult(FlowRunStatus.Stopped, flow.Slot, flow.Name, warnings);
        }
        catch (Exception error)
        {
            return new FlowRunResult(FlowRunStatus.Failed, flow.Slot, flow.Name, warnings, error);
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_activeRun, runSource))
                {
                    _activeRun = null;
                }

                if (ReferenceEquals(_activeRunCompletion, runCompletion))
                {
                    _activeRunCompletion = null;
                }

                _running = false;
            }

            runSource.Dispose();
            runCompletion.TrySetResult();
        }
    }

    public void Stop()
    {
        _ = StopAndGetCompletion();
    }

    public async Task StopAndWaitAsync(CancellationToken cancellationToken = default)
    {
        var completion = StopAndGetCompletion();
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (_stateLock)
        {
            completion = _activeRunCompletion?.Task ?? Task.CompletedTask;
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            _enabled = true;
            _lastStopTap = null;
        }
    }

    public StopTapResult HandleStopTap()
    {
        CancellationTokenSource? activeRun = null;
        lock (_stateLock)
        {
            var now = _clock.ElapsedMilliseconds;
            if (_lastStopTap is not null
                && now >= _lastStopTap.Value
                && now - _lastStopTap.Value <= StopDoubleTapMilliseconds)
            {
                _lastStopTap = null;
                _enabled = true;
                return StopTapResult.Resumed;
            }

            _lastStopTap = now;
            _enabled = false;
            activeRun = _activeRun;
        }

        CancelQuietly(activeRun);
        return StopTapResult.Stopped;
    }

    private async ValueTask ExecuteActionAsync(
        MacroAction action,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case KeyPressAction keyPress:
                _input.KeyDown(keyPress.Key);
                try
                {
                    await DelayLikeLegacyAsync(
                        keyPress.HoldMilliseconds,
                        keyPress.HoldIsInterruptible,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _input.KeyUp(keyPress.Key);
                }

                await DelayLikeLegacyAsync(
                    keyPress.RestMilliseconds,
                    keyPress.RestIsInterruptible,
                    cancellationToken).ConfigureAwait(false);
                break;

            case MoveMouseAction move:
                _input.MoveMouse(
                    move.X,
                    move.Y,
                    move.ClientXRatio,
                    move.ClientYRatio,
                    move.CaptureAspectRatio);
                break;

            case LeftClickAction click:
                _input.LeftButtonDown();
                try
                {
                    await _clock.DelayAsync(click.HoldMilliseconds, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _input.LeftButtonUp();
                }

                break;

            case SendChatAction chat:
                _input.SendChat(chat.Text);
                break;

            case DelayAction delay:
                await DelayLikeLegacyAsync(delay.Milliseconds, delay.IsInterruptible, cancellationToken).ConfigureAwait(false);
                break;

            case WarningAction warning:
                warnings.Add(warning.Message);
                break;

            case StopBoundaryAction:
                cancellationToken.ThrowIfCancellationRequested();
                break;

            default:
                throw new InvalidOperationException($"Unsupported macro action: {action.GetType().Name}");
        }
    }

    private async ValueTask DelayLikeLegacyAsync(
        int milliseconds,
        bool interruptible,
        CancellationToken cancellationToken)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        try
        {
            await _clock.DelayAsync(
                milliseconds,
                interruptible ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (interruptible && cancellationToken.IsCancellationRequested)
        {
            // AHK SleepInterrupt returns to its caller. The caller then releases held
            // keys and reaches the next explicit flow stop boundary.
        }
    }

    private static FlowRunResult Result(FlowRunStatus status, CompiledFlow flow)
    {
        return new FlowRunResult(status, flow.Slot, flow.Name, Array.Empty<string>());
    }

    private static void CancelQuietly(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed run can dispose its source immediately after Stop captured it.
        }
    }

    private Task StopAndGetCompletion()
    {
        CancellationTokenSource? activeRun;
        Task completion;
        lock (_stateLock)
        {
            _enabled = false;
            _lastStopTap = null;
            activeRun = _activeRun;
            completion = _activeRunCompletion?.Task ?? Task.CompletedTask;
        }

        CancelQuietly(activeRun);
        return completion;
    }
}

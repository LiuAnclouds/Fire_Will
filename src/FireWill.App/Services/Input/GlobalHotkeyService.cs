namespace FireWill.App.Services.Input;

public readonly record struct HotkeyInvocation(
    HotkeyGesture Gesture,
    uint MessageTime,
    ScreenPoint? PointerPosition);

public sealed class HotkeyHandlerErrorEventArgs(
    HotkeyGesture gesture,
    Exception exception) : EventArgs
{
    public HotkeyGesture Gesture { get; } = gesture;

    public Exception Exception { get; } = exception;
}

public sealed class GlobalHotkeyService : IDisposable, IAsyncDisposable
{
    private readonly object registrationsLock = new();
    private readonly object handlerDrainLock = new();
    private readonly Dictionary<HotkeyGesture, List<Registration>> registrations = [];
    private readonly LowLevelInputHook inputHook;
    private Registration[] routingSnapshot = [];
    private TaskCompletionSource? handlerDrainCompletion;
    private long nextRegistrationId;
    private int pendingHandlerCount;
    private int disposed;

    public GlobalHotkeyService()
    {
        inputHook = new LowLevelInputHook();
        inputHook.InputProcessed += HandleInput;
        inputHook.Error += ForwardHookError;
        inputHook.SetHotkeyDecisionProvider(EvaluateHotkey);
    }

    public event EventHandler<InputHookErrorEventArgs>? HookError;

    public event EventHandler<HotkeyHandlerErrorEventArgs>? HandlerError;

    public bool IsRunning => inputHook.IsRunning;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return inputHook.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        inputHook.StopAsync(cancellationToken);

    public IDisposable Register(
        string hotkey,
        Action<HotkeyInvocation> handler,
        bool allowAutoRepeat = false,
        bool suppressInput = true,
        Func<bool>? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        return Register(HotkeyGesture.Parse(hotkey), handler, allowAutoRepeat, suppressInput, isActive);
    }

    public IDisposable Register(
        HotkeyGesture gesture,
        Action<HotkeyInvocation> handler,
        bool allowAutoRepeat = false,
        bool suppressInput = true,
        Func<bool>? isActive = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);

        var registration = new Registration(
            this,
            Interlocked.Increment(ref nextRegistrationId),
            gesture,
            handler,
            allowAutoRepeat,
            suppressInput,
            isActive);

        lock (registrationsLock)
        {
            if (!registrations.TryGetValue(gesture, out var bucket))
            {
                bucket = [];
                registrations.Add(gesture, bucket);
            }

            bucket.Add(registration);
            RefreshRoutingSnapshotLocked();
        }

        return registration;
    }

    public void ClearRegistrations()
    {
        lock (registrationsLock)
        {
            foreach (var registration in registrations.Values.SelectMany(static bucket => bucket))
            {
                registration.MarkRemoved();
            }

            registrations.Clear();
            RefreshRoutingSnapshotLocked();
        }
    }

    /// <summary>
    /// Dispatches one already-decoded gesture through the normal routing and handler
    /// drain path. This is intentionally internal so lifecycle tests do not depend on
    /// desktop-wide synthetic input, which can be blocked by an elevated game window.
    /// </summary>
    internal void DispatchForTesting(HotkeyGesture gesture)
    {
        var decision = EvaluateHotkey(gesture);
        var input = new LowLevelInputEvent(
            gesture.IsKeyboard
                ? LowLevelInputEventKind.KeyDown
                : LowLevelInputEventKind.MouseButtonDown,
            gesture,
            gesture.VirtualKey,
            gesture.Button,
            gesture.Modifiers,
            null,
            0,
            IsRepeat: false,
            IsInjected: false,
            IsSelfInjected: false);
        HandleInput(input, decision);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        ClearRegistrations();
        inputHook.SetHotkeyDecisionProvider(null);
        inputHook.InputProcessed -= HandleInput;
        inputHook.Error -= ForwardHookError;
        inputHook.Dispose();
        WaitForHandlersAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        ClearRegistrations();
        inputHook.SetHotkeyDecisionProvider(null);
        inputHook.InputProcessed -= HandleInput;
        inputHook.Error -= ForwardHookError;
        await inputHook.DisposeAsync().ConfigureAwait(false);
        await WaitForHandlersAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void HandleInput(LowLevelInputEvent input, HookHotkeyDecision decision)
    {
        if (!decision.Route || input.IsSelfInjected || input.Gesture is not { } gesture ||
            input.Kind is not LowLevelInputEventKind.KeyDown and not LowLevelInputEventKind.MouseButtonDown)
        {
            return;
        }

        var activeRegistrationIds = decision.Context as long[];
        if (activeRegistrationIds is null || activeRegistrationIds.Length == 0)
        {
            return;
        }

        Registration[] matching;
        lock (registrationsLock)
        {
            if (!registrations.TryGetValue(gesture, out var bucket) || bucket.Count == 0)
            {
                return;
            }

            matching = bucket
                .Where(registration => Array.IndexOf(activeRegistrationIds, registration.Id) >= 0)
                .ToArray();
        }

        var invocation = new HotkeyInvocation(gesture, input.MessageTime, input.Position);
        foreach (var registration in matching)
        {
            if (registration.IsRemoved || input.IsRepeat && !registration.AllowAutoRepeat)
            {
                continue;
            }

            if (!TryBeginHandlerWork())
            {
                continue;
            }

            try
            {
                _ = ThreadPool.QueueUserWorkItem(
                    static state =>
                    {
                        var work = (HandlerWorkItem)state!;
                        try
                        {
                            if (!work.Registration.IsRemoved)
                            {
                                work.Registration.Handler(work.Invocation);
                            }
                        }
                        catch (Exception exception)
                        {
                            work.Owner.QueueHandlerError(work.Invocation.Gesture, exception);
                        }
                        finally
                        {
                            work.Owner.EndHandlerWork();
                        }
                    },
                    new HandlerWorkItem(this, registration, invocation),
                    preferLocal: false);
            }
            catch
            {
                EndHandlerWork();
                throw;
            }
        }
    }

    private bool TryBeginHandlerWork()
    {
        lock (handlerDrainLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            checked
            {
                pendingHandlerCount++;
            }

            return true;
        }
    }

    private void EndHandlerWork()
    {
        TaskCompletionSource? completion = null;
        lock (handlerDrainLock)
        {
            pendingHandlerCount--;
            if (pendingHandlerCount == 0)
            {
                completion = handlerDrainCompletion;
                handlerDrainCompletion = null;
            }
        }

        completion?.TrySetResult();
    }

    private Task WaitForHandlersAsync()
    {
        lock (handlerDrainLock)
        {
            if (pendingHandlerCount == 0)
            {
                return Task.CompletedTask;
            }

            handlerDrainCompletion ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return handlerDrainCompletion.Task;
        }
    }

    private void Remove(Registration registration)
    {
        lock (registrationsLock)
        {
            if (!registrations.TryGetValue(registration.Gesture, out var bucket))
            {
                return;
            }

            bucket.RemoveAll(item => item.Id == registration.Id);
            if (bucket.Count == 0)
            {
                registrations.Remove(registration.Gesture);
            }

            RefreshRoutingSnapshotLocked();
        }
    }

    private void RefreshRoutingSnapshotLocked()
    {
        Volatile.Write(
            ref routingSnapshot,
            registrations.Values
                .SelectMany(static bucket => bucket)
                .Where(static registration => !registration.IsRemoved)
                .ToArray());
    }

    private HookHotkeyDecision EvaluateHotkey(HotkeyGesture gesture)
    {
        List<long>? activeIds = null;
        var suppress = false;
        foreach (var registration in Volatile.Read(ref routingSnapshot))
        {
            if (registration.IsRemoved || registration.Gesture != gesture)
            {
                continue;
            }

            bool active;
            try
            {
                active = registration.IsActive?.Invoke() ?? true;
            }
            catch (Exception exception)
            {
                QueueConditionError(registration.Gesture, exception);
                continue;
            }

            if (!active)
            {
                continue;
            }

            activeIds ??= [];
            activeIds.Add(registration.Id);
            suppress |= registration.SuppressInput;
        }

        return activeIds is null
            ? HookHotkeyDecision.Inactive
            : new HookHotkeyDecision(Route: true, suppress, activeIds.ToArray());
    }

    private void ForwardHookError(object? sender, InputHookErrorEventArgs arguments)
    {
        var handlers = HookError;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<InputHookErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, arguments);
            }
            catch
            {
                // Diagnostics must not break input processing.
            }
        }
    }

    private void QueueHandlerError(HotkeyGesture gesture, Exception exception)
    {
        var handlers = HandlerError;
        if (handlers is null)
        {
            return;
        }

        var arguments = new HotkeyHandlerErrorEventArgs(gesture, exception);
        foreach (EventHandler<HotkeyHandlerErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, arguments);
            }
            catch
            {
                // Diagnostics must not break other registered handlers.
            }
        }
    }

    private void QueueConditionError(HotkeyGesture gesture, Exception exception)
    {
        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var work = (ConditionErrorWorkItem)state!;
                work.Owner.QueueHandlerError(work.Gesture, work.Exception);
            },
            new ConditionErrorWorkItem(this, gesture, exception),
            preferLocal: false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    private sealed class Registration(
        GlobalHotkeyService owner,
        long id,
        HotkeyGesture gesture,
        Action<HotkeyInvocation> handler,
        bool allowAutoRepeat,
        bool suppressInput,
        Func<bool>? isActive) : IDisposable
    {
        private int removed;

        internal long Id { get; } = id;

        internal HotkeyGesture Gesture { get; } = gesture;

        internal Action<HotkeyInvocation> Handler { get; } = handler;

        internal bool AllowAutoRepeat { get; } = allowAutoRepeat;

        internal bool SuppressInput { get; } = suppressInput;

        internal Func<bool>? IsActive { get; } = isActive;

        internal bool IsRemoved => Volatile.Read(ref removed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref removed, 1) == 0)
            {
                owner.Remove(this);
            }
        }

        internal void MarkRemoved() => Interlocked.Exchange(ref removed, 1);
    }

    private sealed record HandlerWorkItem(
        GlobalHotkeyService Owner,
        Registration Registration,
        HotkeyInvocation Invocation);

    private sealed record ConditionErrorWorkItem(
        GlobalHotkeyService Owner,
        HotkeyGesture Gesture,
        Exception Exception);
}

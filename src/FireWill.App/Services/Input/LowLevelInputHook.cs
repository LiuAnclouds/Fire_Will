using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using FireWill.App.Interop;

namespace FireWill.App.Services.Input;

public enum LowLevelInputEventKind
{
    KeyDown,
    KeyUp,
    MouseButtonDown,
    MouseButtonUp,
}

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct LowLevelInputEvent(
    LowLevelInputEventKind Kind,
    HotkeyGesture? Gesture,
    ushort VirtualKey,
    HotkeyButton Button,
    HotkeyModifiers Modifiers,
    ScreenPoint? Position,
    uint MessageTime,
    bool IsRepeat,
    bool IsInjected,
    bool IsSelfInjected);

public sealed class LowLevelInputEventArgs(LowLevelInputEvent input) : EventArgs
{
    public LowLevelInputEvent Input { get; } = input;
}

public sealed class InputHookErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

public sealed class LowLevelInputHook : IDisposable, IAsyncDisposable
{
    private const int StateCreated = 0;
    private const int StateStartingOrRunning = 1;
    private const int StateStopping = 2;
    private const int StateStopped = 3;

    private readonly object lifecycleLock = new();
    private readonly Channel<RawInputEvent> inputQueue;
    private readonly TaskCompletionSource started = NewCompletionSource();
    private readonly TaskCompletionSource<uint> messagePumpReady = NewCompletionSource<uint>();
    private readonly TaskCompletionSource threadExited = NewCompletionSource();
    private readonly NativeMethods.HookProc keyboardCallback;
    private readonly NativeMethods.HookProc mouseCallback;
    private readonly Dictionary<ushort, HookHotkeyDecision> keyboardDecisions = [];
    private readonly Dictionary<HotkeyButton, HookHotkeyDecision> mouseDecisions = [];

    private Thread? hookThread;
    private Task? dispatcherTask;
    private nint keyboardHook;
    private nint mouseHook;
    private int state;
    private int disposed;
    private Func<HotkeyGesture, HookHotkeyDecision>? hotkeyDecisionProvider;

    public LowLevelInputHook()
    {
        inputQueue = Channel.CreateUnbounded<RawInputEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        keyboardCallback = KeyboardHookCallback;
        mouseCallback = MouseHookCallback;
    }

    public event EventHandler<LowLevelInputEventArgs>? InputReceived;

    public event EventHandler<InputHookErrorEventArgs>? Error;

    public bool IsRunning => Volatile.Read(ref state) == StateStartingOrRunning && started.Task.IsCompletedSuccessfully;

    internal event Action<LowLevelInputEvent, HookHotkeyDecision>? InputProcessed;

    internal void SetHotkeyDecisionProvider(Func<HotkeyGesture, HookHotkeyDecision>? provider) =>
        Volatile.Write(ref hotkeyDecisionProvider, provider);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (lifecycleLock)
        {
            if (state != StateCreated)
            {
                throw new InvalidOperationException("A low-level input hook instance can only be started once.");
            }

            state = StateStartingOrRunning;
            dispatcherTask = Task.Run(DispatchInputAsync);
            hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "FireWill.Win32InputHook",
            };
            hookThread.Start();
        }

        try
        {
            await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAfterFailedStartAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? dispatchTask;
        var shouldPostQuit = false;

        lock (lifecycleLock)
        {
            if (state == StateCreated)
            {
                state = StateStopped;
                inputQueue.Writer.TryComplete();
                messagePumpReady.TrySetResult(0);
                threadExited.TrySetResult();
                return;
            }

            if (state == StateStopped)
            {
                dispatchTask = dispatcherTask;
            }
            else
            {
                shouldPostQuit = state == StateStartingOrRunning;
                state = StateStopping;
                dispatchTask = dispatcherTask;
            }
        }

        if (shouldPostQuit)
        {
            uint threadId;
            try
            {
                threadId = await messagePumpReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (threadExited.Task.IsCompleted)
            {
                threadId = 0;
            }

            if (threadId != 0 && !threadExited.Task.IsCompleted &&
                !NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, 0, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (!threadExited.Task.IsCompleted)
                {
                    throw new Win32Exception(error, "Failed to stop the low-level input message pump.");
                }
            }
        }

        await threadExited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (dispatchTask is not null)
        {
            await dispatchTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (lifecycleLock)
        {
            state = StateStopped;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void HookThreadMain()
    {
        Exception? failure = null;

        try
        {
            _ = NativeMethods.PeekMessage(out _, 0, 0, 0, NativeMethods.PmNoRemove);
            var threadId = NativeMethods.GetCurrentThreadId();
            messagePumpReady.TrySetResult(threadId);

            var moduleHandle = NativeMethods.GetModuleHandle(null);
            if (moduleHandle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get the application module handle.");
            }

            keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                keyboardCallback,
                moduleHandle,
                0);
            if (keyboardHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_KEYBOARD_LL.");
            }

            mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhMouseLl,
                mouseCallback,
                moduleHandle,
                0);
            if (mouseHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_MOUSE_LL.");
            }

            started.TrySetResult();

            while (true)
            {
                var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The input hook message pump failed.");
                }

                _ = NativeMethods.TranslateMessage(in message);
                _ = NativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            started.TrySetException(exception);
            messagePumpReady.TrySetException(exception);
            QueueError(exception);
        }
        finally
        {
            ReleaseHook(ref mouseHook, "WH_MOUSE_LL");
            ReleaseHook(ref keyboardHook, "WH_KEYBOARD_LL");

            if (!started.Task.IsCompleted)
            {
                var exception = failure ?? new InvalidOperationException("The input hook stopped before startup completed.");
                started.TrySetException(exception);
            }

            inputQueue.Writer.TryComplete(failure);
            threadExited.TrySetResult();
            lock (lifecycleLock)
            {
                state = StateStopped;
            }
        }
    }

    private nint KeyboardHookCallback(int code, nuint message, nint dataPointer)
    {
        var suppress = false;
        if (code >= 0 && dataPointer != 0 && message is
            NativeMethods.WmKeyDown or NativeMethods.WmKeyUp or
            NativeMethods.WmSysKeyDown or NativeMethods.WmSysKeyUp)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(dataPointer);
            var kind = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown
                ? RawInputEventKind.KeyboardDown
                : RawInputEventKind.KeyboardUp;
            var virtualKey = (ushort)data.VirtualKey;
            var decision = HookHotkeyDecision.Inactive;

            if (data.ExtraInfo != InputInjectionMarker.Value && !HotkeyGesture.IsModifierKey(virtualKey))
            {
                if (kind == RawInputEventKind.KeyboardDown)
                {
                    if (!keyboardDecisions.TryGetValue(virtualKey, out decision))
                    {
                        decision = GetHotkeyDecision(HotkeyGesture.Keyboard(ReadModifierSnapshot(), virtualKey));
                        keyboardDecisions.Add(virtualKey, decision);
                    }
                }
                else if (keyboardDecisions.Remove(virtualKey, out var keyDownDecision))
                {
                    decision = keyDownDecision;
                }

                suppress = decision.SuppressInput;
            }

            _ = inputQueue.Writer.TryWrite(new RawInputEvent(
                kind,
                virtualKey,
                HotkeyButton.Keyboard,
                null,
                data.Flags,
                data.Time,
                data.ExtraInfo,
                decision));
        }

        return suppress ? 1 : NativeMethods.CallNextHookEx(keyboardHook, code, message, dataPointer);
    }

    private nint MouseHookCallback(int code, nuint message, nint dataPointer)
    {
        var suppress = false;
        if (code >= 0 && dataPointer != 0 && TryDecodeMouseMessage(message, dataPointer, out var input))
        {
            var decision = HookHotkeyDecision.Inactive;
            if (input.ExtraInfo != InputInjectionMarker.Value)
            {
                if (input.Kind == RawInputEventKind.MouseDown)
                {
                    if (!mouseDecisions.TryGetValue(input.Button, out decision))
                    {
                        decision = GetHotkeyDecision(HotkeyGesture.Mouse(ReadModifierSnapshot(), input.Button));
                        mouseDecisions.Add(input.Button, decision);
                    }
                }
                else if (mouseDecisions.Remove(input.Button, out var mouseDownDecision))
                {
                    decision = mouseDownDecision;
                }

                suppress = decision.SuppressInput;
            }

            _ = inputQueue.Writer.TryWrite(input with { HotkeyDecision = decision });
        }

        return suppress ? 1 : NativeMethods.CallNextHookEx(mouseHook, code, message, dataPointer);
    }

    private static bool TryDecodeMouseMessage(nuint message, nint dataPointer, out RawInputEvent input)
    {
        input = default;
        if (message is not NativeMethods.WmMButtonDown and not NativeMethods.WmMButtonUp and
            not NativeMethods.WmXButtonDown and not NativeMethods.WmXButtonUp)
        {
            return false;
        }

        var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(dataPointer);
        HotkeyButton button;
        if (message is NativeMethods.WmMButtonDown or NativeMethods.WmMButtonUp)
        {
            button = HotkeyButton.MiddleMouse;
        }
        else
        {
            var xButton = (data.MouseData >> 16) & 0xffff;
            button = xButton switch
            {
                NativeMethods.XButton1 => HotkeyButton.XButton1,
                NativeMethods.XButton2 => HotkeyButton.XButton2,
                _ => HotkeyButton.Keyboard,
            };

            if (button == HotkeyButton.Keyboard)
            {
                return false;
            }
        }

        var kind = message is NativeMethods.WmMButtonDown or NativeMethods.WmXButtonDown
            ? RawInputEventKind.MouseDown
            : RawInputEventKind.MouseUp;
        input = new RawInputEvent(
            kind,
            0,
            button,
            new ScreenPoint(data.Point.X, data.Point.Y),
            data.Flags,
            data.Time,
            data.ExtraInfo);
        return true;
    }

    private async Task DispatchInputAsync()
    {
        var pressedKeys = ReadInitiallyPressedModifierKeys();
        var pressedMouseButtons = new HashSet<HotkeyButton>();

        try
        {
            await foreach (var raw in inputQueue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                LowLevelInputEvent input;
                if (raw.Kind is RawInputEventKind.KeyboardDown or RawInputEventKind.KeyboardUp)
                {
                    var isDown = raw.Kind == RawInputEventKind.KeyboardDown;
                    var isSelfInjected = raw.ExtraInfo == InputInjectionMarker.Value;
                    var wasPressed = pressedKeys.Contains(raw.VirtualKey);
                    if (isDown && !isSelfInjected)
                    {
                        pressedKeys.Add(raw.VirtualKey);
                    }

                    var modifiers = GetModifiers(pressedKeys);
                    HotkeyGesture? gesture = HotkeyGesture.IsModifierKey(raw.VirtualKey)
                        ? null
                        : HotkeyGesture.Keyboard(modifiers, raw.VirtualKey);
                    input = new LowLevelInputEvent(
                        isDown ? LowLevelInputEventKind.KeyDown : LowLevelInputEventKind.KeyUp,
                        gesture,
                        raw.VirtualKey,
                        HotkeyButton.Keyboard,
                        modifiers,
                        null,
                        raw.MessageTime,
                        isDown && !isSelfInjected && wasPressed,
                        (raw.Flags & NativeMethods.LlkhfInjected) != 0,
                        isSelfInjected);

                    if (!isDown && !isSelfInjected)
                    {
                        pressedKeys.Remove(raw.VirtualKey);
                    }
                }
                else
                {
                    var isDown = raw.Kind == RawInputEventKind.MouseDown;
                    var isSelfInjected = raw.ExtraInfo == InputInjectionMarker.Value;
                    var wasPressed = pressedMouseButtons.Contains(raw.Button);
                    if (isDown && !isSelfInjected)
                    {
                        pressedMouseButtons.Add(raw.Button);
                    }

                    var modifiers = GetModifiers(pressedKeys);
                    input = new LowLevelInputEvent(
                        isDown ? LowLevelInputEventKind.MouseButtonDown : LowLevelInputEventKind.MouseButtonUp,
                        HotkeyGesture.Mouse(modifiers, raw.Button),
                        0,
                        raw.Button,
                        modifiers,
                        raw.Position,
                        raw.MessageTime,
                        isDown && !isSelfInjected && wasPressed,
                        (raw.Flags & NativeMethods.LlmhfInjected) != 0,
                        isSelfInjected);

                    if (!isDown && !isSelfInjected)
                    {
                        pressedMouseButtons.Remove(raw.Button);
                    }
                }

                DispatchProcessedInput(input, raw.HotkeyDecision);
                QueuePublicInput(input);
            }
        }
        catch (Exception exception)
        {
            QueueError(exception);
            throw;
        }
    }

    private void DispatchProcessedInput(LowLevelInputEvent input, HookHotkeyDecision decision)
    {
        var handlers = InputProcessed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<LowLevelInputEvent, HookHotkeyDecision> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(input, decision);
            }
            catch (Exception exception)
            {
                QueueError(exception);
            }
        }
    }

    private void QueuePublicInput(LowLevelInputEvent input)
    {
        var handlers = InputReceived;
        if (handlers is null)
        {
            return;
        }

        var arguments = new LowLevelInputEventArgs(input);
        foreach (EventHandler<LowLevelInputEventArgs> handler in handlers.GetInvocationList())
        {
            ThreadPool.QueueUserWorkItem(
                static state =>
                {
                    var work = (InputEventWorkItem)state!;
                    try
                    {
                        work.Handler(work.Sender, work.Arguments);
                    }
                    catch (Exception exception)
                    {
                        work.Sender.QueueError(exception);
                    }
                },
                new InputEventWorkItem(this, handler, arguments),
                preferLocal: false);
        }
    }

    private void QueueError(Exception exception)
    {
        var handlers = Error;
        if (handlers is null)
        {
            return;
        }

        var arguments = new InputHookErrorEventArgs(exception);
        foreach (EventHandler<InputHookErrorEventArgs> handler in handlers.GetInvocationList())
        {
            ThreadPool.QueueUserWorkItem(
                static state =>
                {
                    var work = (ErrorEventWorkItem)state!;
                    try
                    {
                        work.Handler(work.Sender, work.Arguments);
                    }
                    catch
                    {
                        // Error observers must never terminate the hook or dispatcher threads.
                    }
                },
                new ErrorEventWorkItem(this, handler, arguments),
                preferLocal: false);
        }
    }

    private static HashSet<ushort> ReadInitiallyPressedModifierKeys()
    {
        ushort[] modifierKeys =
        [
            VirtualKeyCodes.LeftControl,
            VirtualKeyCodes.RightControl,
            VirtualKeyCodes.LeftMenu,
            VirtualKeyCodes.RightMenu,
            VirtualKeyCodes.LeftShift,
            VirtualKeyCodes.RightShift,
            VirtualKeyCodes.LeftWindows,
            VirtualKeyCodes.RightWindows,
        ];

        var result = new HashSet<ushort>();
        foreach (var key in modifierKeys)
        {
            if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static HotkeyModifiers GetModifiers(HashSet<ushort> pressedKeys)
    {
        var modifiers = HotkeyModifiers.None;
        foreach (var key in pressedKeys)
        {
            modifiers |= HotkeyGesture.ModifierForVirtualKey(key);
        }

        return modifiers;
    }

    private static HotkeyModifiers ReadModifierSnapshot()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsKeyDown(VirtualKeyCodes.Control) || IsKeyDown(VirtualKeyCodes.LeftControl) ||
            IsKeyDown(VirtualKeyCodes.RightControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsKeyDown(VirtualKeyCodes.Menu) || IsKeyDown(VirtualKeyCodes.LeftMenu) ||
            IsKeyDown(VirtualKeyCodes.RightMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsKeyDown(VirtualKeyCodes.Shift) || IsKeyDown(VirtualKeyCodes.LeftShift) ||
            IsKeyDown(VirtualKeyCodes.RightShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsKeyDown(VirtualKeyCodes.LeftWindows) || IsKeyDown(VirtualKeyCodes.RightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private HookHotkeyDecision GetHotkeyDecision(HotkeyGesture gesture)
    {
        var provider = Volatile.Read(ref hotkeyDecisionProvider);
        return provider?.Invoke(gesture) ?? HookHotkeyDecision.Inactive;
    }

    private static bool IsKeyDown(ushort virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void ReleaseHook(ref nint hook, string name)
    {
        var handle = Interlocked.Exchange(ref hook, 0);
        if (handle != 0 && !NativeMethods.UnhookWindowsHookEx(handle))
        {
            QueueError(new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to release {name}."));
        }
    }

    private async Task StopAfterFailedStartAsync()
    {
        if (!threadExited.Task.IsCompleted)
        {
            try
            {
                var threadId = await messagePumpReady.Task.ConfigureAwait(false);
                if (threadId != 0)
                {
                    _ = NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, 0, 0);
                }
            }
            catch
            {
                // The hook thread owns final cleanup and reports the original startup exception.
            }
        }

        await threadExited.Task.ConfigureAwait(false);
        if (dispatcherTask is not null)
        {
            try
            {
                await dispatcherTask.ConfigureAwait(false);
            }
            catch
            {
                // Preserve the startup exception observed by StartAsync.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum RawInputEventKind
    {
        KeyboardDown,
        KeyboardUp,
        MouseDown,
        MouseUp,
    }

    private readonly record struct RawInputEvent(
        RawInputEventKind Kind,
        ushort VirtualKey,
        HotkeyButton Button,
        ScreenPoint? Position,
        uint Flags,
        uint MessageTime,
        nuint ExtraInfo,
        HookHotkeyDecision HotkeyDecision = default);

    private sealed record InputEventWorkItem(
        LowLevelInputHook Sender,
        EventHandler<LowLevelInputEventArgs> Handler,
        LowLevelInputEventArgs Arguments);

    private sealed record ErrorEventWorkItem(
        LowLevelInputHook Sender,
        EventHandler<InputHookErrorEventArgs> Handler,
        InputHookErrorEventArgs Arguments);
}

internal readonly record struct HookHotkeyDecision(
    bool Route,
    bool SuppressInput,
    object? Context)
{
    internal static HookHotkeyDecision Inactive => default;
}

internal static class InputInjectionMarker
{
    // Fits both x86 and x64 ULONG_PTR and reads as "FWIL" in diagnostics.
    internal static readonly nuint Value = unchecked((nuint)0x4657494Cu);
}

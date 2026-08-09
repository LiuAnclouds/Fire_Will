# Windows input layer

This folder owns the Windows-only input boundary. It does not depend on WPF UI
objects and does not execute workflow code on a low-level hook callback.

## Global hotkeys

```csharp
await using var hotkeys = new GlobalHotkeyService();

using var g = hotkeys.Register("G", invocation => flowQueue.Enqueue("G"));
using var alt2 = hotkeys.Register("Alt+2", invocation => flowQueue.Enqueue("Alt2"));
using var mouse = hotkeys.Register("XButton2", invocation => flowQueue.Enqueue("Mouse5"));
using var visibleF9 = hotkeys.Register(
    "F9",
    invocation => flowQueue.Enqueue("F9"),
    suppressInput: false,
    isActive: () => state.SkipGameCheck || gameWindow.IsBoundWindowForeground);

await hotkeys.StartAsync(cancellationToken);
```

Dispose a registration to remove only that binding. Dispose the service (or call
`StopAsync`) to post `WM_QUIT`, release both hooks, and wait for the dedicated
message-pump thread and its dispatcher to exit.

Registered handlers run on the thread pool. A handler should enqueue work into the
single workflow executor; it must not update WPF controls directly. Hook events are
swallowed by default to match AHK hotkeys without the `~` prefix. Set
`suppressInput: false` only when the original key must continue to Warcraft III.
Use `isActive` for the legacy `HotIf` behavior. The condition is sampled once on
the initial key-down and held through repeats and key-up, so a foreground change
cannot swallow only half of a key cycle. It runs on the hook thread and must be a
fast, thread-safe state read; it must never access the WPF dispatcher.

Supported names include `Ctrl`, `Alt`, `Shift`, `Win`, `F1` through `F24`, letters,
digits, `XButton1`, `XButton2`, and `MButton`. Compact AHK modifier prefixes such as
`!2` and `^!F12` are accepted during legacy import.

## Warcraft III window

```csharp
var gameWindow = new War3WindowService();
if (gameWindow.TryBindForeground(out var game) ||
    gameWindow.TryFindAndBind(out game))
{
    if (gameWindow.TryGetBoundProjectionContext(out var context) &&
        ClientCoordinateProjector.TryNormalize(
            capturedScreenPoint,
            context,
            out var xRatio,
            out var yRatio,
            out var captureAspectRatio))
    {
        // Persist xRatio, yRatio, and captureAspectRatio together. At runtime,
        // read a fresh projection context immediately before moving the mouse.
    }
}
```

Bindings are revalidated against the HWND, PID, and executable name before use.
The default executable names are `War3.exe` and `Warcraft III.exe`.

## Sending input

```csharp
var input = new WindowsInputSender(
    () => gameWindow.TryGetBoundProjectionContext(out var context) ? context : null);
input.KeyPress((ushort)'G');
input.SendHotkey(HotkeyGesture.Parse("Alt+2"));
input.SendUnicodeText("text");
input.MoveMouseAbsolute(screenX, screenY);
input.MoveMouse(fallbackX, fallbackY, xRatio, yRatio, captureAspectRatio);
input.ClickAbsolute(screenX, screenY, MouseInputButton.Left);
if (input.TryGetCursorPosition(out var cursor))
{
    // Use cursor.X/cursor.Y for F5-F8 coordinate capture.
}
```

Keyboard events use scan codes, text uses `KEYEVENTF_UNICODE`, and absolute mouse
coordinates use the complete Windows virtual desktop. Every event carries the
`FWIL` `dwExtraInfo` marker; `GlobalHotkeyService` ignores these events so macros
cannot recursively trigger themselves.

`WindowsInputSender` also implements `FireWill.Core.Execution.IInputSink`, so it
can be passed directly to `FlowScheduler` without a second input adapter.

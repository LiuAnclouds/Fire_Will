using System.ComponentModel;
using System.Runtime.InteropServices;
using FireWill.App.Interop;
using FireWill.Core.Execution;

namespace FireWill.App.Services.Input;

public enum MouseInputButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
}

public sealed class WindowsInputSender : IInputSink
{
    private readonly object sendLock = new();
    private readonly Func<ScreenProjectionContext?>? projectionContextProvider;
    private readonly Func<bool>? adaptiveModeProvider;

    public WindowsInputSender(
        Func<ScreenProjectionContext?>? projectionContextProvider = null,
        Func<bool>? adaptiveModeProvider = null)
    {
        this.projectionContextProvider = projectionContextProvider;
        this.adaptiveModeProvider = adaptiveModeProvider;
    }

    public void KeyDown(ushort virtualKey) => SendKeyboardInput(virtualKey, keyUp: false);

    public void KeyUp(ushort virtualKey) => SendKeyboardInput(virtualKey, keyUp: true);

    public void KeyDown(string key)
    {
        var gesture = HotkeyGesture.Parse(key);
        var inputs = new List<NativeMethods.Input>(5);
        AddModifierInputs(inputs, gesture.Modifiers, keyUp: false);
        inputs.Add(gesture.IsKeyboard
            ? CreateKeyboardInput(gesture.VirtualKey, keyUp: false)
            : CreateMouseButtonInput(gesture.Button, keyUp: false));
        SendInputs(inputs);
    }

    public void KeyUp(string key)
    {
        var gesture = HotkeyGesture.Parse(key);
        var inputs = new List<NativeMethods.Input>(5)
        {
            gesture.IsKeyboard
                ? CreateKeyboardInput(gesture.VirtualKey, keyUp: true)
                : CreateMouseButtonInput(gesture.Button, keyUp: true),
        };
        AddModifierInputs(inputs, gesture.Modifiers, keyUp: true);
        SendInputs(inputs);
    }

    public void KeyPress(ushort virtualKey)
    {
        SendKeyboardInputs(
        [
            CreateKeyboardInput(virtualKey, keyUp: false),
            CreateKeyboardInput(virtualKey, keyUp: true),
        ]);
    }

    public void KeyPress(char character) => SendUnicodeText(character.ToString());

    public void SendHotkey(HotkeyGesture gesture)
    {
        var inputs = new List<NativeMethods.Input>(10);
        AddModifierInputs(inputs, gesture.Modifiers, keyUp: false);

        if (gesture.IsKeyboard)
        {
            inputs.Add(CreateKeyboardInput(gesture.VirtualKey, keyUp: false));
            inputs.Add(CreateKeyboardInput(gesture.VirtualKey, keyUp: true));
        }
        else
        {
            inputs.Add(CreateMouseButtonInput(gesture.Button, keyUp: false));
            inputs.Add(CreateMouseButtonInput(gesture.Button, keyUp: true));
        }

        AddModifierInputs(inputs, gesture.Modifiers, keyUp: true);
        SendInputs(inputs);
    }

    public void SendUnicodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        var inputs = new NativeMethods.Input[text.Length * 2];
        var index = 0;
        foreach (var character in text)
        {
            inputs[index++] = CreateUnicodeInput(character, keyUp: false);
            inputs[index++] = CreateUnicodeInput(character, keyUp: true);
        }

        SendInputs(inputs);
    }

    public void SendChat(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var inputs = new List<NativeMethods.Input>(text.Length * 2 + 4)
        {
            CreateKeyboardInput(VirtualKeyCodes.Return, keyUp: false),
            CreateKeyboardInput(VirtualKeyCodes.Return, keyUp: true),
        };
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        inputs.Add(CreateKeyboardInput(VirtualKeyCodes.Return, keyUp: false));
        inputs.Add(CreateKeyboardInput(VirtualKeyCodes.Return, keyUp: true));
        SendInputs(inputs);
    }

    public void MoveMouseAbsolute(int screenX, int screenY)
    {
        SendInputs([CreateAbsoluteMouseMove(screenX, screenY)]);
    }

    public void MoveMouse(
        int x,
        int y,
        double? clientXRatio = null,
        double? clientYRatio = null,
        double? captureAspectRatio = null)
    {
        ScreenProjectionContext? projectionContext = null;
        if (clientXRatio is not null || clientYRatio is not null)
        {
            if (clientXRatio is null || clientYRatio is null)
            {
                throw new InvalidOperationException("自适应鼠标坐标不完整，流程已停止。重新采集该点位后再试。");
            }

            if (!ClientCoordinateProjector.IsNormalizedRatio(clientXRatio) ||
                !ClientCoordinateProjector.IsNormalizedRatio(clientYRatio))
            {
                throw new InvalidOperationException(
                    "自适应鼠标坐标已损坏或超出有效范围，流程已停止。请重新录入该点位。");
            }

            if (captureAspectRatio is not > 0d || !double.IsFinite(captureAspectRatio.Value))
            {
                throw new InvalidOperationException(
                    "该点缺少采集时的窗口比例，流程已停止。请在当前版本重新录入 NPC 和技能鼠标点。");
            }

            projectionContext = projectionContextProvider?.Invoke();
            if (projectionContext is not { } context || !context.IsValid)
            {
                throw new InvalidOperationException("Warcraft III 窗口客户区不可用，流程已停止以避免误点。");
            }

            var projected = ClientCoordinateProjector.ProjectWidescreenOrFallback(
                new ScreenPoint(x, y),
                clientXRatio,
                clientYRatio,
                captureAspectRatio,
                context);
            if (!context.ClientBounds.Contains(projected))
            {
                throw new InvalidOperationException(
                    "该点在当前 Warcraft III 视野外，流程已停止以避免点击边界位置。");
            }

            MoveMouseAbsolute(projected.X, projected.Y);
            return;
        }
        else if (adaptiveModeProvider?.Invoke() == true)
        {
            throw new InvalidOperationException(
                "当前配置已启用窗口自适应，但这个点仍是旧桌面坐标。请重新录入对应的 NPC 和技能鼠标点。");
        }

        MoveMouseAbsolute(x, y);
    }

    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        if (!NativeMethods.GetCursorPos(out var nativePoint))
        {
            position = default;
            return false;
        }

        position = new ScreenPoint(nativePoint.X, nativePoint.Y);
        return true;
    }

    public void MouseButtonDown(MouseInputButton button) =>
        SendInputs([CreateMouseButtonInput(button, keyUp: false)]);

    public void MouseButtonUp(MouseInputButton button) =>
        SendInputs([CreateMouseButtonInput(button, keyUp: true)]);

    public void LeftButtonDown() => MouseButtonDown(MouseInputButton.Left);

    public void LeftButtonUp() => MouseButtonUp(MouseInputButton.Left);

    public void Click(MouseInputButton button = MouseInputButton.Left)
    {
        SendInputs(
        [
            CreateMouseButtonInput(button, keyUp: false),
            CreateMouseButtonInput(button, keyUp: true),
        ]);
    }

    public void ClickAbsolute(int screenX, int screenY, MouseInputButton button = MouseInputButton.Left)
    {
        SendInputs(
        [
            CreateAbsoluteMouseMove(screenX, screenY),
            CreateMouseButtonInput(button, keyUp: false),
            CreateMouseButtonInput(button, keyUp: true),
        ]);
    }

    private void SendKeyboardInput(ushort virtualKey, bool keyUp)
    {
        SendInputs([CreateKeyboardInput(virtualKey, keyUp)]);
    }

    private void SendKeyboardInputs(NativeMethods.Input[] inputs) => SendInputs(inputs);

    private void SendInputs(IReadOnlyList<NativeMethods.Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var nativeInputs = inputs is NativeMethods.Input[] array
            ? array
            : inputs.ToArray();

        lock (sendLock)
        {
            var sent = NativeMethods.SendInput(
                checked((uint)nativeInputs.Length),
                nativeInputs,
                Marshal.SizeOf<NativeMethods.Input>());
            if (sent != nativeInputs.Length)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"SendInput sent {sent} of {nativeInputs.Length} events.");
            }
        }
    }

    private static NativeMethods.Input CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        var mappedScanCode = NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MapvkVkToVscEx);
        if (mappedScanCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "Windows did not provide a scan code for this key.");
        }

        var flags = NativeMethods.KeyeventfScanCode;
        if ((mappedScanCode & 0xFF00) is 0xE000 or 0xE100 || IsExtendedVirtualKey(virtualKey))
        {
            flags |= NativeMethods.KeyeventfExtendedKey;
        }

        if (keyUp)
        {
            flags |= NativeMethods.KeyeventfKeyUp;
        }

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = (ushort)(mappedScanCode & 0xFF),
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = InputInjectionMarker.Value,
                },
            },
        };
    }

    private static NativeMethods.Input CreateUnicodeInput(char character, bool keyUp)
    {
        var flags = NativeMethods.KeyeventfUnicode;
        if (keyUp)
        {
            flags |= NativeMethods.KeyeventfKeyUp;
        }

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = InputInjectionMarker.Value,
                },
            },
        };
    }

    private static NativeMethods.Input CreateAbsoluteMouseMove(int screenX, int screenY)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (width < 2 || height < 2)
        {
            throw new InvalidOperationException("Windows returned an invalid virtual screen size.");
        }

        screenX = Math.Clamp(screenX, left, left + width - 1);
        screenY = Math.Clamp(screenY, top, top + height - 1);
        var normalizedX = (int)Math.Round((screenX - (double)left) * 65535d / (width - 1), MidpointRounding.AwayFromZero);
        var normalizedY = (int)Math.Round((screenY - (double)top) * 65535d / (height - 1), MidpointRounding.AwayFromZero);

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputMouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = normalizedX,
                    Y = normalizedY,
                    MouseData = 0,
                    Flags = NativeMethods.MouseeventfMove | NativeMethods.MouseeventfAbsolute | NativeMethods.MouseeventfVirtualDesk,
                    Time = 0,
                    ExtraInfo = InputInjectionMarker.Value,
                },
            },
        };
    }

    private static NativeMethods.Input CreateMouseButtonInput(HotkeyButton button, bool keyUp) =>
        button switch
        {
            HotkeyButton.XButton1 => CreateMouseButtonInput(MouseInputButton.XButton1, keyUp),
            HotkeyButton.XButton2 => CreateMouseButtonInput(MouseInputButton.XButton2, keyUp),
            HotkeyButton.MiddleMouse => CreateMouseButtonInput(MouseInputButton.Middle, keyUp),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "The gesture is not a mouse button."),
        };

    private static NativeMethods.Input CreateMouseButtonInput(MouseInputButton button, bool keyUp)
    {
        var flags = button switch
        {
            MouseInputButton.Left => keyUp ? NativeMethods.MouseeventfLeftUp : NativeMethods.MouseeventfLeftDown,
            MouseInputButton.Right => keyUp ? NativeMethods.MouseeventfRightUp : NativeMethods.MouseeventfRightDown,
            MouseInputButton.Middle => keyUp ? NativeMethods.MouseeventfMiddleUp : NativeMethods.MouseeventfMiddleDown,
            MouseInputButton.XButton1 or MouseInputButton.XButton2 => keyUp ? NativeMethods.MouseeventfXUp : NativeMethods.MouseeventfXDown,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, null),
        };
        var mouseData = button switch
        {
            MouseInputButton.XButton1 => NativeMethods.XButton1,
            MouseInputButton.XButton2 => NativeMethods.XButton2,
            _ => 0u,
        };

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputMouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = 0,
                    Y = 0,
                    MouseData = mouseData,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = InputInjectionMarker.Value,
                },
            },
        };
    }

    private static void AddModifierInputs(List<NativeMethods.Input> inputs, HotkeyModifiers modifiers, bool keyUp)
    {
        var keys = new List<ushort>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            keys.Add(VirtualKeyCodes.LeftControl);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            keys.Add(VirtualKeyCodes.LeftMenu);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            keys.Add(VirtualKeyCodes.LeftShift);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            keys.Add(VirtualKeyCodes.LeftWindows);
        }

        if (keyUp)
        {
            keys.Reverse();
        }

        foreach (var key in keys)
        {
            inputs.Add(CreateKeyboardInput(key, keyUp));
        }
    }

    private static bool IsExtendedVirtualKey(ushort virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or
        0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA5;
}

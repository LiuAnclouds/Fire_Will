using System.Globalization;

namespace FireWill.App.Services.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Windows = 1 << 3,
}

public enum HotkeyButton
{
    Keyboard = 0,
    XButton1,
    XButton2,
    MiddleMouse,
}

public readonly record struct HotkeyGesture
{
    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = VirtualKeyCodes.Back,
            ["Back"] = VirtualKeyCodes.Back,
            ["Tab"] = VirtualKeyCodes.Tab,
            ["Enter"] = VirtualKeyCodes.Return,
            ["Return"] = VirtualKeyCodes.Return,
            ["Pause"] = VirtualKeyCodes.Pause,
            ["CapsLock"] = VirtualKeyCodes.Capital,
            ["Escape"] = VirtualKeyCodes.Escape,
            ["Esc"] = VirtualKeyCodes.Escape,
            ["Space"] = VirtualKeyCodes.Space,
            ["PageUp"] = VirtualKeyCodes.Prior,
            ["PgUp"] = VirtualKeyCodes.Prior,
            ["PageDown"] = VirtualKeyCodes.Next,
            ["PgDn"] = VirtualKeyCodes.Next,
            ["End"] = VirtualKeyCodes.End,
            ["Home"] = VirtualKeyCodes.Home,
            ["Left"] = VirtualKeyCodes.Left,
            ["Up"] = VirtualKeyCodes.Up,
            ["Right"] = VirtualKeyCodes.Right,
            ["Down"] = VirtualKeyCodes.Down,
            ["Insert"] = VirtualKeyCodes.Insert,
            ["Ins"] = VirtualKeyCodes.Insert,
            ["Delete"] = VirtualKeyCodes.Delete,
            ["Del"] = VirtualKeyCodes.Delete,
            ["NumLock"] = VirtualKeyCodes.NumLock,
            ["ScrollLock"] = VirtualKeyCodes.Scroll,
            ["NumpadMult"] = VirtualKeyCodes.Multiply,
            ["NumpadMultiply"] = VirtualKeyCodes.Multiply,
            ["NumpadAdd"] = VirtualKeyCodes.Add,
            ["NumpadSub"] = VirtualKeyCodes.Subtract,
            ["NumpadSubtract"] = VirtualKeyCodes.Subtract,
            ["NumpadDot"] = VirtualKeyCodes.Decimal,
            ["NumpadDecimal"] = VirtualKeyCodes.Decimal,
            ["NumpadDiv"] = VirtualKeyCodes.Divide,
            ["NumpadDivide"] = VirtualKeyCodes.Divide,
            ["Semicolon"] = VirtualKeyCodes.Oem1,
            [";"] = VirtualKeyCodes.Oem1,
            ["Equals"] = VirtualKeyCodes.OemPlus,
            ["="] = VirtualKeyCodes.OemPlus,
            ["Comma"] = VirtualKeyCodes.OemComma,
            [","] = VirtualKeyCodes.OemComma,
            ["Minus"] = VirtualKeyCodes.OemMinus,
            ["-"] = VirtualKeyCodes.OemMinus,
            ["Period"] = VirtualKeyCodes.OemPeriod,
            ["."] = VirtualKeyCodes.OemPeriod,
            ["Slash"] = VirtualKeyCodes.Oem2,
            ["/"] = VirtualKeyCodes.Oem2,
            ["Backtick"] = VirtualKeyCodes.Oem3,
            ["`"] = VirtualKeyCodes.Oem3,
            ["LeftBracket"] = VirtualKeyCodes.Oem4,
            ["["] = VirtualKeyCodes.Oem4,
            ["Backslash"] = VirtualKeyCodes.Oem5,
            ["\\"] = VirtualKeyCodes.Oem5,
            ["RightBracket"] = VirtualKeyCodes.Oem6,
            ["]"] = VirtualKeyCodes.Oem6,
            ["Quote"] = VirtualKeyCodes.Oem7,
            ["'"] = VirtualKeyCodes.Oem7,
            ["Oem102"] = VirtualKeyCodes.Oem102,
        };

    public HotkeyGesture(HotkeyModifiers modifiers, HotkeyButton button, ushort virtualKey = 0)
    {
        if ((modifiers & ~AllModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers));
        }

        if (button == HotkeyButton.Keyboard && virtualKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), "A keyboard hotkey needs a virtual key.");
        }

        if (button != HotkeyButton.Keyboard && virtualKey != 0)
        {
            throw new ArgumentException("Mouse hotkeys cannot carry a virtual key.", nameof(virtualKey));
        }

        Modifiers = modifiers;
        Button = button;
        VirtualKey = virtualKey;
    }

    public static HotkeyModifiers AllModifiers =>
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Windows;

    public HotkeyModifiers Modifiers { get; }

    public HotkeyButton Button { get; }

    public ushort VirtualKey { get; }

    public bool IsKeyboard => Button == HotkeyButton.Keyboard;

    public static HotkeyGesture Keyboard(HotkeyModifiers modifiers, ushort virtualKey) =>
        new(modifiers, HotkeyButton.Keyboard, virtualKey);

    public static HotkeyGesture Mouse(HotkeyModifiers modifiers, HotkeyButton button) =>
        button == HotkeyButton.Keyboard
            ? throw new ArgumentException("Use Keyboard for a keyboard gesture.", nameof(button))
            : new HotkeyGesture(modifiers, button);

    public static HotkeyGesture Parse(string text)
    {
        if (!TryParse(text, out var gesture, out var error))
        {
            throw new FormatException(error);
        }

        return gesture;
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture) =>
        TryParse(text, out gesture, out _);

    public static bool TryParse(string? text, out HotkeyGesture gesture, out string? error)
    {
        gesture = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        var remaining = text.Trim();
        var modifiers = HotkeyModifiers.None;

        while (remaining.Length > 1 && TryParseAhkModifierPrefix(remaining[0], out var prefixModifier))
        {
            modifiers |= prefixModifier;
            remaining = remaining[1..].TrimStart();
        }

        var tokens = remaining.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(static token => token.Length == 0))
        {
            error = $"Invalid hotkey '{text}'.";
            return false;
        }

        string? primaryToken = null;
        foreach (var token in tokens)
        {
            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (primaryToken is not null)
            {
                error = $"Hotkey '{text}' has more than one primary key.";
                return false;
            }

            primaryToken = token;
        }

        if (primaryToken is null)
        {
            error = $"Hotkey '{text}' does not contain a primary key.";
            return false;
        }

        if (TryParseMouseButton(primaryToken, out var mouseButton))
        {
            gesture = Mouse(modifiers, mouseButton);
            return true;
        }

        if (!TryParseVirtualKey(primaryToken, out var virtualKey))
        {
            error = $"Unsupported key '{primaryToken}'.";
            return false;
        }

        gesture = Keyboard(modifiers, virtualKey);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Button switch
        {
            HotkeyButton.XButton1 => "XButton1",
            HotkeyButton.XButton2 => "XButton2",
            HotkeyButton.MiddleMouse => "MButton",
            _ => FormatVirtualKey(VirtualKey),
        });

        return string.Join('+', parts);
    }

    internal static bool IsModifierKey(ushort virtualKey) => virtualKey is
        VirtualKeyCodes.Shift or VirtualKeyCodes.LeftShift or VirtualKeyCodes.RightShift or
        VirtualKeyCodes.Control or VirtualKeyCodes.LeftControl or VirtualKeyCodes.RightControl or
        VirtualKeyCodes.Menu or VirtualKeyCodes.LeftMenu or VirtualKeyCodes.RightMenu or
        VirtualKeyCodes.LeftWindows or VirtualKeyCodes.RightWindows;

    internal static HotkeyModifiers ModifierForVirtualKey(ushort virtualKey) => virtualKey switch
    {
        VirtualKeyCodes.Shift or VirtualKeyCodes.LeftShift or VirtualKeyCodes.RightShift => HotkeyModifiers.Shift,
        VirtualKeyCodes.Control or VirtualKeyCodes.LeftControl or VirtualKeyCodes.RightControl => HotkeyModifiers.Control,
        VirtualKeyCodes.Menu or VirtualKeyCodes.LeftMenu or VirtualKeyCodes.RightMenu => HotkeyModifiers.Alt,
        VirtualKeyCodes.LeftWindows or VirtualKeyCodes.RightWindows => HotkeyModifiers.Windows,
        _ => HotkeyModifiers.None,
    };

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" or "MENU" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" or "LWIN" or "RWIN" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }

    private static bool TryParseAhkModifierPrefix(char token, out HotkeyModifiers modifier)
    {
        modifier = token switch
        {
            '^' => HotkeyModifiers.Control,
            '!' => HotkeyModifiers.Alt,
            '+' => HotkeyModifiers.Shift,
            '#' => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }

    private static bool TryParseMouseButton(string token, out HotkeyButton button)
    {
        button = token.ToUpperInvariant() switch
        {
            "XBUTTON1" => HotkeyButton.XButton1,
            "XBUTTON2" => HotkeyButton.XButton2,
            "MBUTTON" or "MIDDLE" or "MIDDLEMOUSE" or "MIDDLEBUTTON" => HotkeyButton.MiddleMouse,
            _ => HotkeyButton.Keyboard,
        };

        return button != HotkeyButton.Keyboard;
    }

    private static bool TryParseVirtualKey(string token, out ushort virtualKey)
    {
        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        if (token.Length is 2 or 3 && token[0] is 'F' or 'f' &&
            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            virtualKey = checked((ushort)(VirtualKeyCodes.F1 + functionNumber - 1));
            return true;
        }

        if (token.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase) && token.Length == 7 &&
            token[6] is >= '0' and <= '9')
        {
            virtualKey = checked((ushort)(VirtualKeyCodes.Numpad0 + token[6] - '0'));
            return true;
        }

        if (token.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
            token.Length is >= 5 and <= 7 &&
            ushort.TryParse(
                token.AsSpan(3),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out virtualKey) &&
            virtualKey != 0)
        {
            return true;
        }

        return NamedKeys.TryGetValue(token, out virtualKey);
    }

    private static string FormatVirtualKey(ushort virtualKey)
    {
        if (virtualKey is >= (ushort)'A' and <= (ushort)'Z' or >= (ushort)'0' and <= (ushort)'9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= VirtualKeyCodes.F1 and <= VirtualKeyCodes.F24)
        {
            return $"F{virtualKey - VirtualKeyCodes.F1 + 1}";
        }

        if (virtualKey is >= VirtualKeyCodes.Numpad0 and <= VirtualKeyCodes.Numpad9)
        {
            return $"Numpad{virtualKey - VirtualKeyCodes.Numpad0}";
        }

        foreach (var pair in NamedKeys)
        {
            if (pair.Value == virtualKey)
            {
                return pair.Key;
            }
        }

        return $"VK_{virtualKey:X2}";
    }
}

internal static class VirtualKeyCodes
{
    internal const ushort Back = 0x08;
    internal const ushort Tab = 0x09;
    internal const ushort Return = 0x0D;
    internal const ushort Shift = 0x10;
    internal const ushort Control = 0x11;
    internal const ushort Menu = 0x12;
    internal const ushort Pause = 0x13;
    internal const ushort Capital = 0x14;
    internal const ushort Escape = 0x1B;
    internal const ushort Space = 0x20;
    internal const ushort Prior = 0x21;
    internal const ushort Next = 0x22;
    internal const ushort End = 0x23;
    internal const ushort Home = 0x24;
    internal const ushort Left = 0x25;
    internal const ushort Up = 0x26;
    internal const ushort Right = 0x27;
    internal const ushort Down = 0x28;
    internal const ushort Insert = 0x2D;
    internal const ushort Delete = 0x2E;
    internal const ushort LeftWindows = 0x5B;
    internal const ushort RightWindows = 0x5C;
    internal const ushort Numpad0 = 0x60;
    internal const ushort Numpad9 = 0x69;
    internal const ushort Multiply = 0x6A;
    internal const ushort Add = 0x6B;
    internal const ushort Subtract = 0x6D;
    internal const ushort Decimal = 0x6E;
    internal const ushort Divide = 0x6F;
    internal const ushort F1 = 0x70;
    internal const ushort F24 = 0x87;
    internal const ushort NumLock = 0x90;
    internal const ushort Scroll = 0x91;
    internal const ushort LeftShift = 0xA0;
    internal const ushort RightShift = 0xA1;
    internal const ushort LeftControl = 0xA2;
    internal const ushort RightControl = 0xA3;
    internal const ushort LeftMenu = 0xA4;
    internal const ushort RightMenu = 0xA5;
    internal const ushort Oem1 = 0xBA;
    internal const ushort OemPlus = 0xBB;
    internal const ushort OemComma = 0xBC;
    internal const ushort OemMinus = 0xBD;
    internal const ushort OemPeriod = 0xBE;
    internal const ushort Oem2 = 0xBF;
    internal const ushort Oem3 = 0xC0;
    internal const ushort Oem4 = 0xDB;
    internal const ushort Oem5 = 0xDC;
    internal const ushort Oem6 = 0xDD;
    internal const ushort Oem7 = 0xDE;
    internal const ushort Oem102 = 0xE2;
}

using System.Reflection;
using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData(0x6A, "NumpadMult")]
    [InlineData(0x6B, "NumpadAdd")]
    [InlineData(0x6D, "NumpadSub")]
    [InlineData(0x6E, "NumpadDot")]
    [InlineData(0x6F, "NumpadDiv")]
    [InlineData(0xBA, "Semicolon")]
    [InlineData(0xBB, "Equals")]
    [InlineData(0xBC, "Comma")]
    [InlineData(0xBD, "Minus")]
    [InlineData(0xBE, "Period")]
    [InlineData(0xBF, "Slash")]
    [InlineData(0xC0, "Backtick")]
    [InlineData(0xDB, "LeftBracket")]
    [InlineData(0xDC, "Backslash")]
    [InlineData(0xDD, "RightBracket")]
    [InlineData(0xDE, "Quote")]
    [InlineData(0xE2, "Oem102")]
    public void CapturedSpecialKey_ToStringAndParse_RoundTrips(int virtualKey, string expectedName)
    {
        var captured = HotkeyGesture.Keyboard(
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            checked((ushort)virtualKey));

        var serialized = captured.ToString();
        var reparsed = HotkeyGesture.Parse(serialized);

        Assert.Equal($"Ctrl+Shift+{expectedName}", serialized);
        Assert.Equal(captured, reparsed);
    }

    [Fact]
    public void UnknownCapturedVirtualKey_UsesParseableHexNotation()
    {
        var captured = HotkeyGesture.Keyboard(HotkeyModifiers.Alt, 0xE8);

        var serialized = captured.ToString();

        Assert.Equal("Alt+VK_E8", serialized);
        Assert.Equal(captured, HotkeyGesture.Parse(serialized));
    }

    [Theory]
    [InlineData("VK_00")]
    [InlineData("VK_G1")]
    [InlineData("VK_10000")]
    public void InvalidHexVirtualKey_IsRejected(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Escape_IsReservedFromUserFlowAndStopBindings()
    {
        var field = typeof(MainWindow).GetField(
            "ReservedHotkeys",
            BindingFlags.NonPublic | BindingFlags.Static);
        var values = Assert.IsType<string[]>(field?.GetValue(null));

        var reserved = values.Select(HotkeyGesture.Parse).ToHashSet();

        Assert.Contains(HotkeyGesture.Parse("Esc"), reserved);
    }
}

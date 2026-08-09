using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class WindowBindingTests
{
    [Fact]
    public void ManualBindingPolicy_AcceptsPlatformWrapper_AndRejectsConfigurator()
    {
        var wrapper = Binding(100, 200, "PlatformHost", "Warcraft III Platform", "PlatformWindow");
        var configurator = Binding(101, 201, "Fire Will", "Fire Will", "HwndWrapper");

        Assert.True(War3WindowService.IsManualBindingCandidate(wrapper, excludedProcessId: 999));
        Assert.False(War3WindowService.IsManualBindingCandidate(wrapper, excludedProcessId: 200));
        Assert.False(War3WindowService.IsManualBindingCandidate(configurator, excludedProcessId: 999));
    }

    [Fact]
    public void WindowDiagnostics_ContainsLegacyIdentityFields()
    {
        var window = Binding(12345, 6789, "PlatformHost", "Warcraft III", "PlatformWindow");

        var text = WindowDiagnosticFormatter.Format(window);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "HWND: 12345",
                "Title: Warcraft III",
                "Exe: PlatformHost.exe",
                "Class: PlatformWindow",
                "PID: 6789"),
            text);
    }

    private static War3WindowBinding Binding(
        nint handle,
        uint processId,
        string processName,
        string title,
        string className) =>
        new(
            handle,
            processId,
            processName,
            title,
            className,
            new ScreenRectangle(10, 20, 1280, 720));
}

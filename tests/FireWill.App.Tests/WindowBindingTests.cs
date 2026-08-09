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

    [Fact]
    public void ProjectionContext_UsesOuterWindowAspectLikeWarcraftHelper()
    {
        var window = Binding(12345, 6789, "War3", "Warcraft III", "Warcraft III");

        Assert.Equal(1296d / 759d, window.ProjectionContext.ProjectionAspectRatio);
        Assert.Equal(window.ClientBounds, window.ProjectionContext.ClientBounds);
    }

    [Fact]
    public void ScreenRectangle_ContainsDoesNotOverflowAtDesktopIntegerBoundary()
    {
        var rectangle = new ScreenRectangle(int.MaxValue, int.MaxValue, 2, 2);

        Assert.True(rectangle.Contains(new ScreenPoint(int.MaxValue, int.MaxValue)));
        Assert.Equal((long)int.MaxValue + 2, rectangle.Right);
        Assert.Equal((long)int.MaxValue + 2, rectangle.Bottom);
    }

    [Theory]
    [InlineData(0, 0, 0L, 1L)]
    [InlineData(0, 0, 1L, 0L)]
    [InlineData(10, 20, 9L, 21L)]
    [InlineData(10, 20, 11L, 19L)]
    [InlineData(int.MinValue, 0, 0L, 1L)]
    public void TryCreateRectangle_RejectsEmptyInvertedOrOversizedGeometry(
        int left,
        int top,
        long right,
        long bottom)
    {
        Assert.False(War3WindowService.TryCreateRectangle(
            left,
            top,
            right,
            bottom,
            out _));
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
            new ScreenRectangle(10, 20, 1280, 720),
            new ScreenRectangle(2, -10, 1296, 759));
}

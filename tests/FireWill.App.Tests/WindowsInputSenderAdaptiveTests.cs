using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class WindowsInputSenderAdaptiveTests
{
    [Fact]
    public void MoveMouse_AdaptivePointWithoutClientBounds_StopsBeforeSendingInput()
    {
        var sender = new WindowsInputSender(() => null);

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, 0.6, 16d / 9d));

        Assert.Contains("避免误点", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.4d, null)]
    [InlineData(null, 0.6d)]
    public void MoveMouse_IncompleteAdaptivePoint_StopsBeforeReadingClientBounds(
        double? xRatio,
        double? yRatio)
    {
        var providerCalled = false;
        var sender = new WindowsInputSender(
            () =>
            {
                providerCalled = true;
                return new ScreenProjectionContext(
                    new ScreenRectangle(0, 0, 1920, 1080),
                    16d / 9d);
            });

        Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, xRatio, yRatio, 16d / 9d));
        Assert.False(providerCalled);
    }

    [Fact]
    public void MoveMouse_InvalidClientBounds_StopsBeforeSendingInput()
    {
        var sender = new WindowsInputSender(
            () => new ScreenProjectionContext(
                new ScreenRectangle(0, 0, 0, 720),
                16d / 9d));

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, 0.6, 16d / 9d));

        Assert.Contains("避免误点", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveMouse_MixedConfiguration_RejectsLegacyPoint()
    {
        var sender = new WindowsInputSender(
            () => new ScreenProjectionContext(
                new ScreenRectangle(0, 0, 1920, 1080),
                16d / 9d),
            () => true);

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413));

        Assert.Contains("点位信息不完整", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MoveMouse_InvalidCaptureAspect_StopsBeforeReadingWindow(
        double? captureAspectRatio)
    {
        var providerCalled = false;
        var sender = new WindowsInputSender(
            () =>
            {
                providerCalled = true;
                return new ScreenProjectionContext(
                    new ScreenRectangle(0, 0, 1920, 1080),
                    16d / 9d);
            });

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, 0.6, captureAspectRatio));

        Assert.Contains("缺少窗口自适应信息", error.Message, StringComparison.Ordinal);
        Assert.False(providerCalled);
    }

    [Theory]
    [InlineData(-0.0001d, 0.5d)]
    [InlineData(1.0001d, 0.5d)]
    [InlineData(0.5d, -0.0001d)]
    [InlineData(0.5d, 1.0001d)]
    [InlineData(double.NaN, 0.5d)]
    [InlineData(0.5d, double.PositiveInfinity)]
    public void MoveMouse_InvalidRatioStopsBeforeReadingWindow(double xRatio, double yRatio)
    {
        var providerCalled = false;
        var sender = new WindowsInputSender(
            () =>
            {
                providerCalled = true;
                return new ScreenProjectionContext(
                    new ScreenRectangle(0, 0, 1920, 1080),
                    16d / 9d);
            });

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, xRatio, yRatio, 16d / 9d));

        Assert.Contains("已损坏或超出有效范围", error.Message, StringComparison.Ordinal);
        Assert.False(providerCalled);
    }

    [Fact]
    public void MoveMouse_ProjectedPointOutsideCurrentView_StopsBeforeSendingInput()
    {
        var sender = new WindowsInputSender(
            () => new ScreenProjectionContext(
                new ScreenRectangle(100, 200, 640, 480),
                4d / 3d));

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(1919, 540, 1d, 0.5d, 4d));

        Assert.Contains("当前 Warcraft III 视野外", error.Message, StringComparison.Ordinal);
    }
}

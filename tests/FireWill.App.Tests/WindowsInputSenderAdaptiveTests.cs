using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class WindowsInputSenderAdaptiveTests
{
    [Fact]
    public void MoveMouse_AdaptivePointWithoutClientBounds_StopsBeforeSendingInput()
    {
        var sender = new WindowsInputSender(() => null);

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, 0.6));

        Assert.Contains("避免误点", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveMouse_IncompleteAdaptivePoint_StopsBeforeReadingClientBounds()
    {
        var providerCalled = false;
        var sender = new WindowsInputSender(
            () =>
            {
                providerCalled = true;
                return new ScreenRectangle(0, 0, 1920, 1080);
            });

        Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, null));
        Assert.False(providerCalled);
    }

    [Fact]
    public void MoveMouse_InvalidClientBounds_StopsBeforeSendingInput()
    {
        var sender = new WindowsInputSender(() => new ScreenRectangle(0, 0, 0, 720));

        var error = Assert.Throws<InvalidOperationException>(
            () => sender.MoveMouse(843, 413, 0.4, 0.6));

        Assert.Contains("避免误点", error.Message, StringComparison.Ordinal);
    }
}

using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class GameWindowAutoBinderTests
{
    [Fact]
    public void Poll_BindsAfterLateStart_AndRebindsAfterRestart()
    {
        var first = Binding(101, 1001);
        var second = Binding(202, 2002);
        var candidates = new Queue<War3WindowBinding?>([null, first, second]);
        var bindingAlive = false;
        var searchCount = 0;
        var binder = new GameWindowAutoBinder(
            () => bindingAlive,
            () =>
            {
                searchCount++;
                var candidate = candidates.Dequeue();
                bindingAlive = candidate is not null;
                return candidate;
            });

        Assert.Equal(GameWindowAutoBindState.Waiting, binder.Poll().State);

        var lateStart = binder.Poll();
        Assert.Equal(GameWindowAutoBindState.BoundNow, lateStart.State);
        Assert.Same(first, lateStart.Binding);

        Assert.Equal(GameWindowAutoBindState.AlreadyBound, binder.Poll().State);
        Assert.Equal(2, searchCount);

        bindingAlive = false;
        var restarted = binder.Poll();
        Assert.Equal(GameWindowAutoBindState.BoundNow, restarted.State);
        Assert.Same(second, restarted.Binding);
        Assert.Equal(3, searchCount);
    }

    private static War3WindowBinding Binding(nint handle, uint processId) =>
        new(
            handle,
            processId,
            "War3",
            "Warcraft III",
            "Warcraft III",
            new ScreenRectangle(10, 20, 1280, 720),
            new ScreenRectangle(2, -10, 1296, 759));
}

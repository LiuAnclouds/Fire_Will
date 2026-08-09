using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Win32InputLifecycleCollection
{
    public const string Name = "Win32 input lifecycle";
}

[Collection(Win32InputLifecycleCollection.Name)]
public sealed class InputLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task StopThenDispose_BeforeStart_Completes()
    {
        var hook = new LowLevelInputHook();

        await hook.StopAsync().WaitAsync(Timeout);
        await hook.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.False(hook.IsRunning);
    }

    [Fact]
    public async Task Dispose_BeforeStart_Completes()
    {
        var hook = new LowLevelInputHook();

        await hook.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.False(hook.IsRunning);
    }

    [Fact]
    public async Task StartStopDispose_Completes()
    {
        var hook = new LowLevelInputHook();

        await hook.StartAsync().WaitAsync(Timeout);
        Assert.True(hook.IsRunning);

        await hook.StopAsync().WaitAsync(Timeout);
        await hook.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.False(hook.IsRunning);
    }

    [Fact]
    public async Task StartThenDispose_Completes()
    {
        var hook = new LowLevelInputHook();

        await hook.StartAsync().WaitAsync(Timeout);
        Assert.True(hook.IsRunning);

        await hook.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.False(hook.IsRunning);
    }

    [Fact]
    public async Task GlobalHotkeyDispose_WaitsForRunningHandler()
    {
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var service = new GlobalHotkeyService();
        Task? disposing = null;

        try
        {
            using var registration = service.Register(
                "F24",
                _ =>
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                });

            await service.StartAsync().WaitAsync(Timeout);
            service.DispatchForTesting(HotkeyGesture.Parse("F24"));
            Assert.True(entered.Wait(Timeout), "The F24 handler was not dispatched.");

            disposing = service.DisposeAsync().AsTask();
            await Task.Delay(100);
            Assert.False(disposing.IsCompleted, "Disposal completed while a hotkey handler was still running.");

            release.Set();
            await disposing.WaitAsync(Timeout);
        }
        finally
        {
            release.Set();
            if (disposing is not null)
            {
                await disposing.WaitAsync(Timeout);
            }
            else
            {
                await service.DisposeAsync().AsTask().WaitAsync(Timeout);
            }

            entered.Dispose();
            release.Dispose();
        }
    }

}

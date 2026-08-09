using FireWill.Core.Configuration;
using FireWill.Core.Execution;

namespace FireWill.Tests;

public sealed class FlowSchedulerGoldenTests
{
    [Fact]
    public async Task ExecuteFlow_ForwardsAdaptiveMouseCoordinatesToInputSink()
    {
        var trace = new TraceLog();
        var input = new RecordingInput(trace);
        var scheduler = new FlowScheduler(input, new FakeClock(trace));
        var flow = new CompiledFlow(
            1,
            "adaptive",
            Enabled: true,
            [new CompiledGroup(1, [new MoveMouseAction(843, 413, 0.4, 0.6)], 0, 0)]);

        var result = await scheduler.RunFlowAsync(flow);

        Assert.Equal(FlowRunStatus.Completed, result.Status);
        Assert.Equal((843, 413, 0.4, 0.6), input.LastMove);
    }

    [Fact]
    public async Task RunFlow_ProducesLegacyInputAndDelayOrder()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var flow = configuration.GetFlow(1);

        var releaseGroup = flow.Groups[0];
        releaseGroup.Enabled = true;
        releaseGroup.PreType = LegacyValues.KeyPreCommand;
        releaseGroup.PreValue = "F2";
        releaseGroup.FarmName = "家里挑战自我x5";
        releaseGroup.WaitMs = 280;

        var emptyKeyGroup = flow.Groups[1];
        emptyKeyGroup.Enabled = true;
        emptyKeyGroup.PreType = LegacyValues.KeyPreCommand;
        emptyKeyGroup.PreValue = string.Empty;
        emptyKeyGroup.FarmName = LegacyValues.None;
        emptyKeyGroup.WaitMs = 7;

        var chatGroup = flow.Groups[2];
        chatGroup.Enabled = true;
        chatGroup.PreType = LegacyValues.ChatPreCommand;
        chatGroup.PreValue = "jlhcd";
        chatGroup.FarmName = LegacyValues.None;
        chatGroup.WaitMs = 0;

        var trace = new TraceLog();
        var scheduler = new FlowScheduler(new RecordingInput(trace), new FakeClock(trace));

        var result = await scheduler.RunFlowAsync(configuration, 1);

        Assert.Equal(FlowRunStatus.Completed, result.Status);
        Assert.Empty(result.Warnings);
        Assert.Equal(
        [
            "key-down:F2",
            "delay:15:fixed",
            "key-up:F2",
            "delay:200:interruptible",
            "move:1131,679",
            "delay:30:interruptible",
            "mouse-down:left",
            "delay:10:fixed",
            "mouse-up:left",
            "delay:20:interruptible",
            "key-down:Q",
            "delay:15:fixed",
            "key-up:Q",
            "delay:5:interruptible",
            "key-down:F1",
            "delay:50:interruptible",
            "key-up:F1",
            "move:942,705",
            "delay:110:interruptible",
            "key-down:Q",
            "delay:15:interruptible",
            "key-up:Q",
            "delay:280:interruptible",
            "delay:7:interruptible",
            "chat:jlhcd",
            "delay:1:interruptible",
        ], trace.Entries);
    }

    [Fact]
    public async Task Stop_DuringInterruptibleDelay_ReleasesKeyAndSuppressesFollowingInput()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var group = configuration.GetFlow(1).Groups[0];
        group.Enabled = true;
        group.PreType = LegacyValues.KeyPreCommand;
        group.PreValue = "F2";
        group.FarmName = "家里挑战自我x5";

        var trace = new TraceLog();
        var clock = new FakeClock(trace);
        FlowScheduler? scheduler = null;
        scheduler = new FlowScheduler(new RecordingInput(trace), clock);
        clock.BeforeDelay = (milliseconds, interruptible, _) =>
        {
            if (milliseconds == 200 && interruptible)
            {
                scheduler.Stop();
            }
        };

        var result = await scheduler.RunFlowAsync(configuration, 1);

        Assert.Equal(FlowRunStatus.Stopped, result.Status);
        Assert.False(scheduler.IsEnabled);
        Assert.False(scheduler.IsRunning);
        Assert.Equal(
        [
            "key-down:F2",
            "delay:15:fixed",
            "key-up:F2",
            "delay:200:interruptible",
        ], trace.Entries);
    }

    [Fact]
    public async Task Stop_AfterFarmStarts_CompletesAtomicFarmButStopsBeforeNextGroup()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var flow = configuration.GetFlow(1);
        var farmGroup = flow.Groups[0];
        farmGroup.Enabled = true;
        farmGroup.PreType = LegacyValues.None;
        farmGroup.FarmName = "家里挑战自我x5";
        farmGroup.WaitMs = 500;

        var nextGroup = flow.Groups[1];
        nextGroup.Enabled = true;
        nextGroup.PreType = LegacyValues.ChatPreCommand;
        nextGroup.PreValue = "must-not-run";

        var trace = new TraceLog();
        var clock = new FakeClock(trace);
        FlowScheduler? scheduler = null;
        scheduler = new FlowScheduler(new RecordingInput(trace), clock);
        var stopped = false;
        clock.BeforeDelay = (milliseconds, interruptible, _) =>
        {
            if (!stopped && milliseconds == 30 && interruptible)
            {
                stopped = true;
                scheduler.Stop();
            }
        };

        var result = await scheduler.RunFlowAsync(configuration, 1);

        Assert.Equal(FlowRunStatus.Stopped, result.Status);
        Assert.DoesNotContain("chat:must-not-run", trace.Entries);
        Assert.Contains("key-up:F1", trace.Entries);
        Assert.Contains("key-up:Q", trace.Entries);
        Assert.Equal("delay:500:interruptible", trace.Entries[^1]);
    }

    [Fact]
    public async Task StopAndWait_WaitsForAtomicKeyReleaseBeforeCompleting()
    {
        var configuration = MacroActionCompilerTests.CreateReleaseConfiguration();
        var group = configuration.GetFlow(1).Groups[0];
        group.Enabled = true;
        group.PreType = LegacyValues.KeyPreCommand;
        group.PreValue = "F2";
        group.FarmName = LegacyValues.None;

        var trace = new TraceLog();
        var clock = new BlockingClock(trace);
        var scheduler = new FlowScheduler(new RecordingInput(trace), clock);

        var runTask = scheduler.RunFlowAsync(configuration, 1);
        await clock.DelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopTask = scheduler.StopAndWaitAsync();
        Assert.False(stopTask.IsCompleted);

        clock.ReleaseDelay.TrySetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(FlowRunStatus.Stopped, result.Status);
        Assert.False(scheduler.IsRunning);
        Assert.Contains("key-up:F2", trace.Entries);
    }

    [Fact]
    public void StopTap_FirstStops_SecondWithin350MillisecondsResumes()
    {
        var trace = new TraceLog();
        var clock = new FakeClock(trace);
        var scheduler = new FlowScheduler(new RecordingInput(trace), clock);

        Assert.Equal(StopTapResult.Stopped, scheduler.HandleStopTap());
        Assert.False(scheduler.IsEnabled);

        clock.Advance(349);
        Assert.Equal(StopTapResult.Resumed, scheduler.HandleStopTap());
        Assert.True(scheduler.IsEnabled);
    }

    [Fact]
    public async Task Warning_DoesNotAbortLaterGroups()
    {
        var configuration = ConfigurationDefaults.Create();
        var flow = configuration.GetFlow(1);
        flow.Enabled = true;

        flow.Groups[0].Enabled = true;
        flow.Groups[0].FarmName = "家里挑战自我x5";
        flow.Groups[0].WaitMs = 2;

        flow.Groups[1].Enabled = true;
        flow.Groups[1].PreType = LegacyValues.ChatPreCommand;
        flow.Groups[1].PreValue = "next";
        flow.Groups[1].WaitMs = 0;

        var trace = new TraceLog();
        var scheduler = new FlowScheduler(new RecordingInput(trace), new FakeClock(trace));

        var result = await scheduler.RunFlowAsync(configuration, 1);

        Assert.Equal(FlowRunStatus.Completed, result.Status);
        Assert.Single(result.Warnings);
        Assert.Equal(["delay:2:interruptible", "chat:next", "delay:500:interruptible"], trace.Entries);
    }
}

internal sealed class BlockingClock(TraceLog trace) : IClock
{
    public TaskCompletionSource DelayStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseDelay { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long ElapsedMilliseconds => 1_000;

    public ValueTask DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        trace.Entries.Add($"delay:{milliseconds}:{(cancellationToken.CanBeCanceled ? "interruptible" : "fixed")}");
        if (!DelayStarted.Task.IsCompleted)
        {
            DelayStarted.TrySetResult();
            return new ValueTask(ReleaseDelay.Task);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

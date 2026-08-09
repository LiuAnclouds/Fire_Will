using System.Collections.Concurrent;
using System.IO;
using FireWill.App.Services.Configuration;
using FireWill.App.ViewModels;
using FireWill.Core.Configuration;

namespace FireWill.App.Tests;

public sealed class ConfigurationAutosaveCoordinatorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task DefaultConfiguration_UsesLocalAppDataAndThreeHundredMillisecondDebounce()
    {
        await using var coordinator = new ConfigurationAutosaveCoordinator();

        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireWill",
            "war3_macro_gui.ini");
        Assert.Equal(expectedPath, coordinator.ConfigurationPath);
        Assert.Equal(TimeSpan.FromMilliseconds(300), coordinator.DebounceDelay);
    }

    [Fact]
    public async Task ChangesInsideDebounceWindow_AreSavedOnce()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        coordinator.Attach(state);

        state.StopHotkey = "F10";
        state.Farms[0].ActionKey = "Q";
        state.SkillMappings[0].Key = "W";

        await writer.WaitForCountAsync(1);
        await Task.Delay(TimeSpan.FromMilliseconds(375));

        Assert.Equal(1, writer.Count);
        Assert.Equal(ConfigurationAutosaveState.Saved, coordinator.State);
        Assert.False(coordinator.IsDirty);
    }

    [Fact]
    public async Task FlushBeforeDebounce_ImmediatelySavesPendingChange()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        coordinator.Attach(state);

        state.StopHotkey = "F11";
        Assert.Equal(0, writer.Count);

        await coordinator.FlushAsync().WaitAsync(Timeout);

        Assert.Equal(1, writer.Count);
        Assert.Contains("stopHotkey=F11", writer.Snapshots.Single(), StringComparison.Ordinal);
        Assert.Equal(ConfigurationAutosaveState.Saved, coordinator.State);
    }

    [Fact]
    public async Task Flush_CancelsPendingDebounceWithoutDuplicateSave()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        coordinator.Attach(state);

        state.Flows[0].Hotkey = "F12";
        await coordinator.FlushAsync().WaitAsync(Timeout);
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.Equal(1, writer.Count);
    }

    [Fact]
    public async Task Detach_CancelsOldStateAndAttachObservesReplacement()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer, TimeSpan.FromMilliseconds(80));
        var oldState = CreateState();
        var newState = CreateState();
        coordinator.Attach(oldState);

        oldState.StopHotkey = "F10";
        coordinator.Detach();
        oldState.StopHotkey = "F11";
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.Equal(0, writer.Count);

        coordinator.Attach(newState);
        oldState.StopHotkey = "F12";
        newState.StopHotkey = "F8";
        await writer.WaitForCountAsync(1);

        Assert.Equal(1, writer.Count);
        Assert.Contains("stopHotkey=F8", writer.Snapshots.Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("stopHotkey=F12", writer.Snapshots.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateNotifications_CoverEveryPersistedEditorArea()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        var sources = new HashSet<ConfigurationChangeSource>();
        state.ConfigurationChanged += (_, args) => sources.Add(args.Source);
        coordinator.Attach(state);

        state.StopHotkey = "F10";
        state.Farms[0].ActionKey = "Q";
        state.Npcs[0].X = "123";
        state.Flows[0].Hotkey = "F11";
        state.Flows[0].Groups[0].Enabled = true;
        state.SkillMappings[0].Key = "W";
        state.ItemMappings[0].Key = "E";

        await coordinator.FlushAsync().WaitAsync(Timeout);

        Assert.Equal(
            Enum.GetValues<ConfigurationChangeSource>().Order(),
            sources.Order());
        Assert.Equal(1, writer.Count);
    }

    [Fact]
    public async Task RefreshDurations_DoesNotCreateAutosaveEventLoop()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer, TimeSpan.FromMilliseconds(60));
        var state = CreateState();
        coordinator.Attach(state);

        state.RefreshDurations();
        await coordinator.FlushAsync().WaitAsync(Timeout);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, writer.Count);
        Assert.False(coordinator.IsDirty);
    }

    [Fact]
    public async Task NotifyChanged_PersistsDirectModelMutation()
    {
        var writer = new RecordingWriter();
        await using var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        coordinator.Attach(state);

        state.Configuration.General.GameWindowMatcher = "ahk_id 1234";
        coordinator.NotifyChanged();
        await coordinator.FlushAsync().WaitAsync(Timeout);

        Assert.Equal(1, writer.Count);
        Assert.Contains("gameWindowMatcher=ahk_id 1234", writer.Snapshots.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_FlushesLatestAttachedConfiguration()
    {
        var writer = new RecordingWriter();
        var coordinator = CreateCoordinator(writer);
        var state = CreateState();
        coordinator.Attach(state);
        state.ItemMappings[0].Key = "R";

        await coordinator.DisposeAsync().AsTask().WaitAsync(Timeout);

        Assert.Equal(1, writer.Count);
        Assert.Equal(ConfigurationAutosaveState.Disposed, coordinator.State);
        Assert.Null(coordinator.AttachedState);

        await coordinator.DisposeAsync();
        Assert.Equal(1, writer.Count);
    }

    [Fact]
    public async Task EditingDuringActiveSave_UsesImmutableSnapshotsForBothRevisions()
    {
        var writer = new BlockingRecordingWriter();
        await using var coordinator = new ConfigurationAutosaveCoordinator(
            Path.Combine(Path.GetTempPath(), "FireWill.Tests", Guid.NewGuid().ToString("N"), "war3_macro_gui.ini"),
            TimeSpan.FromMilliseconds(20),
            writer.SaveAsync);
        var state = CreateState();
        coordinator.Attach(state);

        state.StopHotkey = "F10";
        await writer.FirstWriteStarted.Task.WaitAsync(Timeout);
        state.StopHotkey = "F11";

        var flush = coordinator.FlushAsync();
        writer.ReleaseFirstWrite.TrySetResult();
        await flush.WaitAsync(Timeout);

        Assert.Equal(2, writer.Snapshots.Count);
        Assert.Contains("stopHotkey=F10", writer.Snapshots[0], StringComparison.Ordinal);
        Assert.Contains("stopHotkey=F11", writer.Snapshots[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachReplacementDuringActiveSave_WritesReplacementLast()
    {
        var writer = new BlockingRecordingWriter();
        await using var coordinator = new ConfigurationAutosaveCoordinator(
            Path.Combine(Path.GetTempPath(), "FireWill.Tests", Guid.NewGuid().ToString("N"), "war3_macro_gui.ini"),
            TimeSpan.FromMilliseconds(20),
            writer.SaveAsync);
        var oldState = CreateState();
        coordinator.Attach(oldState);

        oldState.StopHotkey = "F10";
        await writer.FirstWriteStarted.Task.WaitAsync(Timeout);

        var replacement = CreateState();
        coordinator.Attach(replacement);
        replacement.StopHotkey = "F8";
        var flush = coordinator.FlushAsync();
        writer.ReleaseFirstWrite.TrySetResult();
        await flush.WaitAsync(Timeout);

        Assert.Equal(2, writer.Snapshots.Count);
        Assert.Contains("stopHotkey=F10", writer.Snapshots[0], StringComparison.Ordinal);
        Assert.Contains("stopHotkey=F8", writer.Snapshots[^1], StringComparison.Ordinal);
    }

    private static ConfigurationAutosaveCoordinator CreateCoordinator(
        RecordingWriter writer,
        TimeSpan? debounce = null) =>
        new(
            Path.Combine(Path.GetTempPath(), "FireWill.Tests", Guid.NewGuid().ToString("N"), "war3_macro_gui.ini"),
            debounce,
            writer.SaveAsync);

    private static MainWindowState CreateState() =>
        new(ConfigurationDefaults.Create());

    private sealed class RecordingWriter
    {
        private readonly ConcurrentQueue<string> _snapshots = new();
        private readonly SemaphoreSlim _saveSignal = new(0);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IReadOnlyCollection<string> Snapshots => _snapshots.ToArray();

        public Task SaveAsync(
            string path,
            MacroConfiguration configuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots.Enqueue(LegacyIniProfileSerializer.Serialize(configuration));
            Interlocked.Increment(ref _count);
            _saveSignal.Release();
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int expected)
        {
            while (Count < expected)
            {
                var signaled = await _saveSignal.WaitAsync(Timeout);
                Assert.True(signaled, $"Timed out waiting for save {expected}; observed {Count}.");
            }
        }
    }

    private sealed class BlockingRecordingWriter
    {
        private readonly object _gate = new();
        private readonly List<string> _snapshots = [];
        private int _writeCount;

        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Snapshots
        {
            get
            {
                lock (_gate)
                {
                    return _snapshots.ToArray();
                }
            }
        }

        public async Task SaveAsync(
            string path,
            MacroConfiguration configuration,
            CancellationToken cancellationToken)
        {
            var writeNumber = Interlocked.Increment(ref _writeCount);
            if (writeNumber == 1)
            {
                FirstWriteStarted.TrySetResult();
                // Model a filesystem write that has passed its cancellable phase.
                await ReleaseFirstWrite.Task.ConfigureAwait(false);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var snapshot = LegacyIniProfileSerializer.Serialize(configuration);
            lock (_gate)
            {
                _snapshots.Add(snapshot);
            }
        }
    }
}

using FireWill.App.Services.Background;

namespace FireWill.App.Tests;

public sealed class BackgroundControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeImmediatelyAfterOpacityChange_PersistsFinalSnapshot()
    {
        var store = new RecordingPreferencesStore(blockFirstWrite: true);
        await using var controller = new BackgroundController(
            new TestCatalog(),
            new TestExtractor(),
            store);

        controller.Opacity = 0.73;
        await store.FirstWriteStarted.Task.WaitAsync(Timeout);

        var disposing = controller.DisposeAsync().AsTask();
        await store.SecondWriteRequested.Task.WaitAsync(Timeout);
        Assert.False(disposing.IsCompleted);

        store.ReleaseFirstWrite.TrySetResult();
        await disposing.WaitAsync(Timeout);

        var saved = store.Saves[^1];
        Assert.Equal(0.73, saved.Opacity, precision: 6);
    }

    [Fact]
    public async Task DisposeAfterMultipleChanges_PersistsLatestSnapshot()
    {
        var store = new RecordingPreferencesStore();
        await using var controller = new BackgroundController(
            new TestCatalog(),
            new TestExtractor(),
            store);

        controller.Opacity = 0.22;
        controller.Opacity = 0.44;
        controller.RotationInterval = TimeSpan.FromSeconds(42);
        controller.SelectedMode = BackgroundSelection.FlowingSasuke;

        await controller.DisposeAsync().AsTask().WaitAsync(Timeout);

        var saved = store.Saves[^1];
        Assert.Equal(BackgroundSelection.FlowingSasuke, saved.SelectedMode);
        Assert.Equal(0.44, saved.Opacity, precision: 6);
        Assert.Equal(TimeSpan.FromSeconds(42), saved.RotationInterval);
    }

    private sealed class RecordingPreferencesStore(bool blockFirstWrite = false) : IBackgroundPreferencesStore
    {
        private readonly object gate = new();
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private readonly bool blockFirstWrite = blockFirstWrite;
        private int writeCount;

        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondWriteRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<BackgroundPreferences> Saves { get; } = [];

        public Task<BackgroundPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackgroundPreferences());

        public async Task SaveAsync(
            BackgroundPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            var snapshot = Clone(preferences);
            var call = Interlocked.Increment(ref writeCount);
            if (call == 1)
            {
                FirstWriteStarted.TrySetResult();
            }
            else
            {
                SecondWriteRequested.TrySetResult();
            }

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (blockFirstWrite && call == 1)
                {
                    await ReleaseFirstWrite.Task.ConfigureAwait(false);
                }

                lock (gate)
                {
                    Saves.Add(snapshot);
                }
            }
            finally
            {
                writeLock.Release();
            }
        }

        private static BackgroundPreferences Clone(BackgroundPreferences source) =>
            new()
            {
                SelectedMode = source.SelectedMode,
                Opacity = source.Opacity,
                RotationInterval = source.RotationInterval,
            };
    }

    private sealed class TestExtractor : IBackgroundAssetExtractor
    {
        public Task<string> ExtractAsync(
            BackgroundDescriptor descriptor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"{descriptor.FileName}.test");
    }

    private sealed class TestCatalog : IBackgroundCatalog
    {
        private static readonly BackgroundDescriptor First = new(
            BackgroundSelection.SusanooMadara,
            "First",
            "first.mp4",
            "first",
            new string('0', 64),
            TimeSpan.FromSeconds(1));

        private static readonly BackgroundDescriptor Second = new(
            BackgroundSelection.FlowingSasuke,
            "Second",
            "second.mp4",
            "second",
            new string('1', 64),
            TimeSpan.FromSeconds(1));

        public IReadOnlyList<BackgroundOption> Options { get; } =
        [
            new(BackgroundSelection.SusanooMadara, "First"),
            new(BackgroundSelection.FlowingSasuke, "Second"),
            new(BackgroundSelection.DynamicRotation, "Dynamic"),
        ];

        public IReadOnlyList<BackgroundDescriptor> RotationItems { get; } = [First, Second];

        public BackgroundDescriptor Get(BackgroundSelection selection) => selection switch
        {
            BackgroundSelection.SusanooMadara => First,
            BackgroundSelection.FlowingSasuke => Second,
            BackgroundSelection.DynamicRotation => First,
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, null),
        };
    }
}

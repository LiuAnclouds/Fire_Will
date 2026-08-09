using System.Security.Cryptography;
using FireWill.App.Services.Background;

var developmentRoot = FindDevelopmentRoot(AppContext.BaseDirectory);
var runtimeDirectory = Path.Combine(developmentRoot, "tools", "wallpaper", ".smoke-runtime");
var cacheDirectory = Path.Combine(runtimeDirectory, "cache");
var preferencesPath = Path.Combine(runtimeDirectory, "background.json");

if (Directory.Exists(runtimeDirectory))
{
    Directory.Delete(runtimeDirectory, recursive: true);
}
Directory.CreateDirectory(runtimeDirectory);

try
{
    var catalog = new BackgroundCatalog();
    Assert(catalog.Options.Count == 3, "catalog must expose three UI choices");
    Assert(catalog.RotationItems.Count == 2, "dynamic rotation must contain two media items");

    var extractor = new EmbeddedBackgroundAssetExtractor(
        typeof(BackgroundCatalog).Assembly,
        cacheDirectory);

    foreach (var descriptor in catalog.RotationItems)
    {
        var extractedPath = await extractor.ExtractAsync(descriptor);
        Assert(File.Exists(extractedPath), $"extracted file missing: {descriptor.FileName}");
        Assert(await Sha256Async(extractedPath) == descriptor.Sha256, $"hash mismatch: {descriptor.FileName}");

        await File.WriteAllBytesAsync(extractedPath, new byte[] { 1, 2, 3, 4 });
        var repairedPath = await extractor.ExtractAsync(descriptor);
        Assert(repairedPath == extractedPath, "cache location should remain stable");
        Assert(await Sha256Async(repairedPath) == descriptor.Sha256, $"cache repair failed: {descriptor.FileName}");
    }

    var store = new JsonBackgroundPreferencesStore(preferencesPath);
    var preferences = new BackgroundPreferences
    {
        SelectedMode = BackgroundSelection.DynamicRotation,
        Opacity = 0.42,
        RotationInterval = TimeSpan.FromSeconds(5),
    };
    await store.SaveAsync(preferences);

    var loaded = await store.LoadAsync();
    Assert(loaded.SelectedMode == BackgroundSelection.DynamicRotation, "selected mode did not round-trip");
    Assert(Math.Abs(loaded.Opacity - 0.42) < 0.0001, "opacity did not round-trip");
    Assert(loaded.RotationInterval == TimeSpan.FromSeconds(5), "rotation interval did not round-trip");

    await using var controller = new BackgroundController(catalog, extractor, store);
    var currentChanges = 0;
    controller.CurrentChanged += (_, _) => currentChanges++;
    await controller.InitializeAsync();
    Assert(controller.SelectedMode == BackgroundSelection.DynamicRotation, "controller did not load mode");
    Assert(controller.Current?.Descriptor.Selection == BackgroundSelection.SusanooMadara, "rotation must start at 须佐斑");

    await WaitUntilAsync(
        () => controller.Current?.Descriptor.Selection == BackgroundSelection.FlowingSasuke,
        TimeSpan.FromSeconds(7));
    Assert(controller.Current?.Descriptor.Selection == BackgroundSelection.FlowingSasuke, "timer rotation did not select 流年佐助");

    await controller.MoveNextAsync();
    Assert(controller.Current?.Descriptor.Selection == BackgroundSelection.SusanooMadara, "manual rotation did not return to 须佐斑");
    Assert(controller.CurrentPath is not null && File.Exists(controller.CurrentPath), "current path is invalid");
    Assert(currentChanges == 3, "current change event count is incorrect");
    Assert(controller.LastError is null, $"controller reported an error: {controller.LastError}");

    await controller.SetSelectedModeAsync(BackgroundSelection.SusanooMadara);
    Assert(controller.SelectedMode == BackgroundSelection.SusanooMadara, "direct selection did not update");
    Assert(controller.CurrentTitle == "须佐斑", "current title did not update");

    await RunConcurrentDisposeSmokeAsync(catalog);

    Console.WriteLine("PASS Background smoke tests");
    Console.WriteLine($"choices={controller.Options.Count}; current={controller.CurrentTitle}; opacity={controller.Opacity:F2}");
}
finally
{
    Directory.Delete(runtimeDirectory, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task<string> Sha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    var hash = await SHA256.HashDataAsync(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (!condition())
    {
        await Task.Delay(50, cancellation.Token);
    }
}

static string FindDevelopmentRoot(string startPath)
{
    for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "assets", "backgrounds")))
        {
            return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("Could not locate assets/backgrounds from smoke-test output.");
}

static async Task RunConcurrentDisposeSmokeAsync(IBackgroundCatalog catalog)
{
    var extractor = new SlowBackgroundAssetExtractor(TimeSpan.FromMilliseconds(350));
    var store = new SlowBackgroundPreferencesStore(TimeSpan.FromMilliseconds(350));
    var controller = new BackgroundController(catalog, extractor, store);
    await controller.InitializeAsync();

    var selectionTask = controller.SetSelectedModeAsync(BackgroundSelection.FlowingSasuke);
    controller.Opacity = 0.37;

    await WaitUntilAsync(
        () => extractor.ActiveOperations > 0 && store.ActiveSaves > 0,
        TimeSpan.FromSeconds(2));

    var firstDispose = controller.DisposeAsync().AsTask();
    var secondDispose = controller.DisposeAsync().AsTask();
    await Task.WhenAll(firstDispose, secondDispose);

    var selectionCancelled = false;
    try
    {
        await selectionTask;
    }
    catch (OperationCanceledException)
    {
        selectionCancelled = true;
    }
    catch (ObjectDisposedException exception)
    {
        throw new InvalidOperationException("in-flight selection used a disposed controller resource", exception);
    }

    Assert(selectionCancelled, "in-flight selection should be cancelled during disposal");
    Assert(extractor.ActiveOperations == 0, "DisposeAsync returned before extraction stopped");
    Assert(store.ActiveSaves == 0, "DisposeAsync returned before preference save stopped");

    var extractionCountAfterDispose = extractor.TotalOperations;
    var saveCountAfterDispose = store.TotalSaves;
    controller.SelectedMode = BackgroundSelection.SusanooMadara;
    controller.Opacity = 0.61;
    controller.RotationInterval = TimeSpan.FromSeconds(7);
    await Task.Delay(100);
    Assert(extractor.TotalOperations == extractionCountAfterDispose, "property setter queued extraction after disposal");
    Assert(store.TotalSaves == saveCountAfterDispose, "property setter queued save after disposal");

    var rejectedAfterDispose = false;
    try
    {
        await controller.SetSelectedModeAsync(BackgroundSelection.SusanooMadara);
    }
    catch (ObjectDisposedException)
    {
        rejectedAfterDispose = true;
    }

    Assert(rejectedAfterDispose, "public selection should reject calls after disposal");
    Console.WriteLine("PASS Background concurrent Dispose smoke");
}

file sealed class SlowBackgroundAssetExtractor(TimeSpan delay) : IBackgroundAssetExtractor
{
    private int _activeOperations;
    private int _totalOperations;

    public int ActiveOperations => Volatile.Read(ref _activeOperations);

    public int TotalOperations => Volatile.Read(ref _totalOperations);

    public async Task<string> ExtractAsync(
        BackgroundDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activeOperations);
        Interlocked.Increment(ref _totalOperations);
        try
        {
            await Task.Delay(delay, CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return Path.Combine(Path.GetTempPath(), descriptor.FileName);
        }
        finally
        {
            Interlocked.Decrement(ref _activeOperations);
        }
    }
}

file sealed class SlowBackgroundPreferencesStore(TimeSpan delay) : IBackgroundPreferencesStore
{
    private int _activeSaves;
    private int _totalSaves;

    public int ActiveSaves => Volatile.Read(ref _activeSaves);

    public int TotalSaves => Volatile.Read(ref _totalSaves);

    public Task<BackgroundPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BackgroundPreferences
        {
            SelectedMode = BackgroundSelection.SusanooMadara,
            Opacity = 0.58,
            RotationInterval = TimeSpan.FromSeconds(20),
        });
    }

    public async Task SaveAsync(
        BackgroundPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activeSaves);
        Interlocked.Increment(ref _totalSaves);
        try
        {
            await Task.Delay(delay, CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            Interlocked.Decrement(ref _activeSaves);
        }
    }
}

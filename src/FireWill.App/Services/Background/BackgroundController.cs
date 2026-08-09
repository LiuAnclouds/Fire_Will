using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FireWill.App.Services.Background;

public sealed class BackgroundController : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IBackgroundCatalog _catalog;
    private readonly IBackgroundAssetExtractor _extractor;
    private readonly IBackgroundPreferencesStore _preferencesStore;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private readonly object _taskGate = new();
    private readonly object _rotationGate = new();
    private readonly HashSet<Task> _pendingTasks = [];
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private BackgroundPreferences _preferences = new();
    private BackgroundPlaybackItem? _current;
    private CancellationTokenSource? _rotationCancellation;
    private long _selectionVersion;
    private int _disposeState;
    private string? _lastError;

    public BackgroundController(
        IBackgroundCatalog catalog,
        IBackgroundAssetExtractor extractor,
        IBackgroundPreferencesStore preferencesStore)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _preferencesStore = preferencesStore ?? throw new ArgumentNullException(nameof(preferencesStore));
        _synchronizationContext = SynchronizationContext.Current;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<BackgroundPlaybackItem>? CurrentChanged;

    public IReadOnlyList<BackgroundOption> Options => _catalog.Options;

    public BackgroundSelection SelectedMode
    {
        get => _preferences.SelectedMode;
        set
        {
            if (IsDisposingOrDisposed
                || _preferences.SelectedMode == value
                || !Enum.IsDefined(value))
            {
                return;
            }

            _preferences.SelectedMode = value;
            OnPropertyChanged();
            _ = StartTrackedOperation(
                token => ApplySelectionSafelyAsync(value, persist: true, token),
                CancellationToken.None,
                throwIfDisposing: false);
        }
    }

    public double Opacity
    {
        get => _preferences.Opacity;
        set
        {
            if (IsDisposingOrDisposed)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0.05, 1.0);
            if (Math.Abs(_preferences.Opacity - clamped) < 0.0001)
            {
                return;
            }

            _preferences.Opacity = clamped;
            OnPropertyChanged();
            _ = StartTrackedOperation(
                SavePreferencesSafelyAsync,
                CancellationToken.None,
                throwIfDisposing: false);
        }
    }

    public TimeSpan RotationInterval
    {
        get => _preferences.RotationInterval;
        set
        {
            if (IsDisposingOrDisposed)
            {
                return;
            }

            var clamped = BackgroundPreferences.ClampRotationInterval(value);
            if (_preferences.RotationInterval == clamped)
            {
                return;
            }

            _preferences.RotationInterval = clamped;
            OnPropertyChanged();
            _ = StartTrackedOperation(
                SavePreferencesSafelyAsync,
                CancellationToken.None,
                throwIfDisposing: false);
            if (SelectedMode == BackgroundSelection.DynamicRotation)
            {
                StartRotation();
            }
        }
    }

    public BackgroundPlaybackItem? Current
    {
        get => _current;
        private set
        {
            if (Equals(_current, value))
            {
                return;
            }

            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(CurrentTitle));
            if (value is not null)
            {
                CurrentChanged?.Invoke(this, value);
            }
        }
    }

    public string? CurrentPath => Current?.LocalPath;

    public string CurrentTitle => Current?.Descriptor.DisplayName ?? string.Empty;

    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (_lastError == value)
            {
                return;
            }

            _lastError = value;
            OnPropertyChanged();
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return StartTrackedOperation(InitializeCoreAsync, cancellationToken, throwIfDisposing: true);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        _preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await RunOnCapturedContextAsync(() =>
        {
            OnPropertyChanged(nameof(SelectedMode));
            OnPropertyChanged(nameof(Opacity));
            OnPropertyChanged(nameof(RotationInterval));
        }).ConfigureAwait(false);

        await ApplySelectionAsync(_preferences.SelectedMode, persist: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SetSelectedModeAsync(
        BackgroundSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(selection))
        {
            throw new ArgumentOutOfRangeException(nameof(selection), selection, null);
        }

        return StartTrackedOperation(async token =>
        {
            _preferences.SelectedMode = selection;
            OnPropertyChanged(nameof(SelectedMode));
            await ApplySelectionAsync(selection, persist: true, token).ConfigureAwait(false);
        }, cancellationToken, throwIfDisposing: true);
    }

    public Task MoveNextAsync(CancellationToken cancellationToken = default)
    {
        return StartTrackedOperation(MoveNextCoreAsync, cancellationToken, throwIfDisposing: true);
    }

    private async Task MoveNextCoreAsync(CancellationToken cancellationToken)
    {
        var rotationItems = _catalog.RotationItems;
        var currentIndex = Current is null
            ? -1
            : rotationItems.ToList().FindIndex(item => item.Selection == Current.Descriptor.Selection);
        var next = rotationItems[(currentIndex + 1 + rotationItems.Count) % rotationItems.Count];
        await ActivateAsync(next, Interlocked.Read(ref _selectionVersion), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_taskGate)
        {
            if (_disposeState != 0)
            {
                goto WaitForExistingDispose;
            }

            _disposeState = 1;
        }

        try
        {
            // Property changes are fire-and-forget. Persist the last state before
            // cancelling the lifetime so a fast window close cannot discard it.
            await SavePreferencesForDisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disposal still tears down background work when persistence fails.
        }

        _lifetime.Cancel();
        StopRotation();

        try
        {
            Task[] pending;
            lock (_taskGate)
            {
                pending = _pendingTasks.ToArray();
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Public operations propagate their own errors to their callers. Disposal only
            // guarantees that every operation has reached a terminal state.
        }
        finally
        {
            _selectionLock.Dispose();
            _lifetime.Dispose();
            Volatile.Write(ref _disposeState, 2);
            _disposeCompletion.TrySetResult();
        }

        return;

    WaitForExistingDispose:
        await _disposeCompletion.Task.ConfigureAwait(false);
    }

    private async Task ApplySelectionSafelyAsync(
        BackgroundSelection selection,
        bool persist,
        CancellationToken cancellationToken)
    {
        try
        {
            await ApplySelectionAsync(selection, persist, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await SetLastErrorAsync(exception.Message).ConfigureAwait(false);
        }
    }

    private async Task ApplySelectionAsync(
        BackgroundSelection selection,
        bool persist,
        CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _selectionVersion);
        await _selectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopRotation();
            var descriptor = selection == BackgroundSelection.DynamicRotation
                ? _catalog.RotationItems[0]
                : _catalog.Get(selection);

            await ActivateAsync(descriptor, version, cancellationToken).ConfigureAwait(false);
            if (persist)
            {
                await _preferencesStore.SaveAsync(_preferences, cancellationToken).ConfigureAwait(false);
            }

            if (selection == BackgroundSelection.DynamicRotation
                && version == Interlocked.Read(ref _selectionVersion))
            {
                StartRotation();
            }
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    private async Task ActivateAsync(
        BackgroundDescriptor descriptor,
        long version,
        CancellationToken cancellationToken)
    {
        var localPath = await _extractor.ExtractAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (version != Interlocked.Read(ref _selectionVersion))
        {
            return;
        }

        var item = new BackgroundPlaybackItem(descriptor, localPath);
        await RunOnCapturedContextAsync(() =>
        {
            LastError = null;
            Current = item;
        }).ConfigureAwait(false);
    }

    private void StartRotation()
    {
        CancellationTokenSource? previous;
        Task task;

        lock (_taskGate)
        {
            if (_disposeState != 0)
            {
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            lock (_rotationGate)
            {
                previous = _rotationCancellation;
                _rotationCancellation = cancellation;
                task = RotateAsync(cancellation.Token);
            }

            _pendingTasks.Add(task);
        }

        RemoveTrackedTaskWhenComplete(task);
        CancelAndDispose(previous);
    }

    private void StopRotation()
    {
        CancellationTokenSource? cancellation;
        lock (_rotationGate)
        {
            cancellation = _rotationCancellation;
            _rotationCancellation = null;
        }

        CancelAndDispose(cancellation);
    }

    private async Task RotateAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RotationInterval, cancellationToken).ConfigureAwait(false);
                await MoveNextCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await SetLastErrorAsync(exception.Message).ConfigureAwait(false);
        }
    }

    private async Task SavePreferencesSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _preferencesStore.SaveAsync(CreatePreferencesSnapshot(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await SetLastErrorAsync(exception.Message).ConfigureAwait(false);
        }
    }

    private Task SavePreferencesForDisposeAsync()
    {
        return _preferencesStore.SaveAsync(CreatePreferencesSnapshot(), CancellationToken.None);
    }

    private BackgroundPreferences CreatePreferencesSnapshot()
    {
        return new BackgroundPreferences
        {
            SelectedMode = _preferences.SelectedMode,
            Opacity = _preferences.Opacity,
            RotationInterval = _preferences.RotationInterval,
        };
    }

    private Task SetLastErrorAsync(string message)
    {
        return RunOnCapturedContextAsync(() => LastError = message);
    }

    private bool IsDisposingOrDisposed => Volatile.Read(ref _disposeState) != 0;

    private Task StartTrackedOperation(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool throwIfDisposing)
    {
        Task task;
        lock (_taskGate)
        {
            if (_disposeState != 0)
            {
                if (throwIfDisposing)
                {
                    throw new ObjectDisposedException(nameof(BackgroundController));
                }

                return Task.CompletedTask;
            }

            task = RunWithLinkedCancellationAsync(operation, cancellationToken);
            _pendingTasks.Add(task);
        }

        RemoveTrackedTaskWhenComplete(task);
        return task;
    }

    private async Task RunWithLinkedCancellationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token,
            cancellationToken);
        await operation(linkedCancellation.Token).ConfigureAwait(false);
    }

    private void RemoveTrackedTaskWhenComplete(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_taskGate)
                {
                    _pendingTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private Task RunOnCapturedContextAsync(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(_ =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

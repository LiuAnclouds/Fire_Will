using System.IO;
using FireWill.App.ViewModels;
using FireWill.Core.Configuration;

namespace FireWill.App.Services.Configuration;

public enum ConfigurationAutosaveState
{
    Detached,
    Waiting,
    Saving,
    Saved,
    Failed,
    Disposed,
}

/// <summary>
/// Debounces working-configuration changes and serializes all writes to one local file.
/// </summary>
public sealed class ConfigurationAutosaveCoordinator : IDisposable, IAsyncDisposable
{
    public const int DefaultDebounceMilliseconds = 300;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Func<string, MacroConfiguration, CancellationToken, Task> _writer;
    private readonly HashSet<DebounceRegistration> _debounces = [];
    private readonly TaskCompletionSource _disposeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _debounceDelay;

    private MainWindowState? _attachedState;
    private DebounceRegistration? _currentDebounce;
    private CancellationTokenSource? _activeSaveCancellation;
    private long _generation;
    private long _revision;
    private bool _dirty;
    private int _disposeState;
    private ConfigurationAutosaveState _status = ConfigurationAutosaveState.Detached;
    private Exception? _lastError;

    public ConfigurationAutosaveCoordinator(
        string? configurationPath = null,
        TimeSpan? debounceDelay = null,
        Func<string, MacroConfiguration, CancellationToken, Task>? writer = null)
    {
        ConfigurationPath = configurationPath ?? GetDefaultConfigurationPath();
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(DefaultDebounceMilliseconds);
        if (_debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay), "Debounce delay cannot be negative.");
        }

        _writer = writer ?? SaveWithLegacySerializerAsync;
    }

    public string ConfigurationPath { get; }

    public TimeSpan DebounceDelay => _debounceDelay;

    public ConfigurationAutosaveState State
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public Exception? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_gate)
            {
                return _dirty;
            }
        }
    }

    public MainWindowState? AttachedState
    {
        get
        {
            lock (_gate)
            {
                return _attachedState;
            }
        }
    }

    public void Attach(MainWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        MainWindowState? previous;
        DebounceRegistration? pending;
        CancellationTokenSource? activeSave;
        lock (_gate)
        {
            ThrowIfDisposingOrDisposed();
            if (ReferenceEquals(_attachedState, state))
            {
                return;
            }

            previous = _attachedState;
            pending = TakeCurrentDebounce();
            activeSave = _activeSaveCancellation;
            _attachedState = state;
            _generation++;
            _revision = 0;
            _dirty = false;
            _lastError = null;
            _status = ConfigurationAutosaveState.Saved;
        }

        previous?.ConfigurationChanged -= State_ConfigurationChanged;
        CancelQuietly(pending?.Cancellation);
        CancelQuietly(activeSave);
        state.ConfigurationChanged += State_ConfigurationChanged;
    }

    public void Detach()
    {
        MainWindowState? previous;
        DebounceRegistration? pending;
        CancellationTokenSource? activeSave;
        lock (_gate)
        {
            if (_disposeState == 2)
            {
                return;
            }

            (previous, pending, activeSave) = DetachLocked();
        }

        previous?.ConfigurationChanged -= State_ConfigurationChanged;
        CancelQuietly(pending?.Cancellation);
        CancelQuietly(activeSave);
    }

    /// <summary>
    /// Marks direct model edits that do not pass through a view-model property as dirty.
    /// </summary>
    public void NotifyChanged()
    {
        ScheduleDebouncedSave(expectedSender: null);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposingOrDisposed();
        }

        return FlushCoreAsync(cancellationToken, allowDisposing: false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeState != 0)
            {
                goto WaitForExistingDispose;
            }

            _disposeState = 1;
        }

        Exception? failure = null;
        try
        {
            await FlushCoreAsync(CancellationToken.None, allowDisposing: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        MainWindowState? previous;
        DebounceRegistration? pending;
        CancellationTokenSource? activeSave;
        lock (_gate)
        {
            (previous, pending, activeSave) = DetachLocked();
        }

        previous?.ConfigurationChanged -= State_ConfigurationChanged;
        CancelQuietly(pending?.Cancellation);
        CancelQuietly(activeSave);

        Task[] debounceCompletions;
        lock (_gate)
        {
            debounceCompletions = _debounces.Select(item => item.Completion.Task).ToArray();
        }

        await Task.WhenAll(debounceCompletions).ConfigureAwait(false);
        await _writeGate.WaitAsync().ConfigureAwait(false);
        _writeGate.Release();

        lock (_gate)
        {
            _status = ConfigurationAutosaveState.Disposed;
            _disposeState = 2;
        }

        if (failure is null)
        {
            _disposeCompletion.TrySetResult();
        }
        else
        {
            _disposeCompletion.TrySetException(failure);
        }

        await _disposeCompletion.Task.ConfigureAwait(false);
        return;

    WaitForExistingDispose:
        await _disposeCompletion.Task.ConfigureAwait(false);
    }

    private static string GetDefaultConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FireWill",
        "war3_macro_gui.ini");

    private static Task SaveWithLegacySerializerAsync(
        string path,
        MacroConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            LegacyIniProfileSerializer.Save(path, configuration);
        }, cancellationToken);
    }

    private void State_ConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        ScheduleDebouncedSave(sender as MainWindowState);
    }

    private void ScheduleDebouncedSave(MainWindowState? expectedSender)
    {
        DebounceRegistration? previous;
        DebounceRegistration registration;
        long generation;
        long revision;
        lock (_gate)
        {
            if (_disposeState != 0 || _attachedState is null)
            {
                return;
            }

            if (expectedSender is not null && !ReferenceEquals(expectedSender, _attachedState))
            {
                return;
            }

            previous = TakeCurrentDebounce();
            registration = new DebounceRegistration(CreateSnapshot(_attachedState.Configuration));
            _currentDebounce = registration;
            _debounces.Add(registration);
            generation = _generation;
            revision = ++_revision;
            _dirty = true;
            _lastError = null;
            _status = ConfigurationAutosaveState.Waiting;
        }

        CancelQuietly(previous?.Cancellation);
        _ = RunDebounceAsync(registration, generation, revision);
    }

    private async Task RunDebounceAsync(
        DebounceRegistration registration,
        long generation,
        long revision)
    {
        try
        {
            await Task.Delay(_debounceDelay, registration.Cancellation.Token).ConfigureAwait(false);
            await SaveRevisionAsync(
                    generation,
                    revision,
                    registration.Snapshot,
                    registration.Cancellation.Token,
                    suppressCancellation: true,
                    suppressWriterErrors: true)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (registration.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_currentDebounce, registration))
                {
                    _currentDebounce = null;
                }

                _debounces.Remove(registration);
            }

            registration.Completion.TrySetResult();
            registration.Cancellation.Dispose();
        }
    }

    private async Task FlushCoreAsync(CancellationToken cancellationToken, bool allowDisposing)
    {
        while (true)
        {
            DebounceRegistration? pending;
            MacroConfiguration snapshot;
            long generation;
            long revision;
            lock (_gate)
            {
                if (!allowDisposing)
                {
                    ThrowIfDisposingOrDisposed();
                }

                if (_attachedState is null || !_dirty)
                {
                    return;
                }

                pending = TakeCurrentDebounce();
                generation = _generation;
                revision = _revision;
                snapshot = pending?.Snapshot ?? CreateSnapshot(_attachedState.Configuration);
            }

            CancelQuietly(pending?.Cancellation);
            await SaveRevisionAsync(
                    generation,
                    revision,
                    snapshot,
                    cancellationToken,
                    suppressCancellation: false,
                    suppressWriterErrors: false)
                .ConfigureAwait(false);
        }
    }

    private async Task SaveRevisionAsync(
        long generation,
        long revision,
        MacroConfiguration snapshot,
        CancellationToken cancellationToken,
        bool suppressCancellation,
        bool suppressWriterErrors)
    {
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (suppressCancellation)
        {
            return;
        }

        CancellationTokenSource? saveCancellation = null;
        MainWindowState? state = null;
        try
        {
            lock (_gate)
            {
                if (_attachedState is null
                    || !_dirty
                    || _generation != generation
                    || _revision != revision)
                {
                    return;
                }

                state = _attachedState;
                saveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeSaveCancellation = saveCancellation;
                _status = ConfigurationAutosaveState.Saving;
            }

            try
            {
                await _writer(ConfigurationPath, snapshot, saveCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (saveCancellation.IsCancellationRequested)
            {
                var noLongerCurrent = false;
                lock (_gate)
                {
                    noLongerCurrent = _generation != generation
                        || !ReferenceEquals(_attachedState, state)
                        || _revision != revision;
                }

                if (!suppressCancellation && !noLongerCurrent)
                {
                    throw;
                }

                return;
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    if (_generation == generation && ReferenceEquals(_attachedState, state))
                    {
                        _lastError = exception;
                        _status = ConfigurationAutosaveState.Failed;
                    }
                }

                if (!suppressWriterErrors)
                {
                    throw;
                }

                return;
            }

            lock (_gate)
            {
                if (_generation != generation || !ReferenceEquals(_attachedState, state))
                {
                    return;
                }

                if (_revision == revision)
                {
                    _dirty = false;
                    _lastError = null;
                    _status = ConfigurationAutosaveState.Saved;
                }
                else
                {
                    _status = ConfigurationAutosaveState.Waiting;
                }
            }
        }
        finally
        {
            if (saveCancellation is not null)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeSaveCancellation, saveCancellation))
                    {
                        _activeSaveCancellation = null;
                    }
                }

                saveCancellation.Dispose();
            }

            _writeGate.Release();
        }
    }

    private (MainWindowState? State, DebounceRegistration? Debounce, CancellationTokenSource? ActiveSave)
        DetachLocked()
    {
        var previous = _attachedState;
        var pending = TakeCurrentDebounce();
        var activeSave = _activeSaveCancellation;
        _attachedState = null;
        _generation++;
        _revision = 0;
        _dirty = false;
        _lastError = null;
        _status = ConfigurationAutosaveState.Detached;
        return (previous, pending, activeSave);
    }

    private DebounceRegistration? TakeCurrentDebounce()
    {
        var pending = _currentDebounce;
        _currentDebounce = null;
        return pending;
    }

    private void ThrowIfDisposingOrDisposed()
    {
        if (_disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(ConfigurationAutosaveCoordinator));
        }
    }

    private static void CancelQuietly(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static MacroConfiguration CreateSnapshot(MacroConfiguration configuration)
    {
        var serialized = LegacyIniProfileSerializer.Serialize(configuration);
        return LegacyIniProfileSerializer.Parse(serialized);
    }

    private sealed class DebounceRegistration(MacroConfiguration snapshot)
    {
        public MacroConfiguration Snapshot { get; } = snapshot;

        public CancellationTokenSource Cancellation { get; } = new();

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

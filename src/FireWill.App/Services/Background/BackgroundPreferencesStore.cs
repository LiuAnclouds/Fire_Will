using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FireWill.App.Services.Background;

public interface IBackgroundPreferencesStore
{
    Task<BackgroundPreferences> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(BackgroundPreferences preferences, CancellationToken cancellationToken = default);
}

public sealed class JsonBackgroundPreferencesStore : IBackgroundPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonBackgroundPreferencesStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireWill",
            "background.json");
    }

    public async Task<BackgroundPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new BackgroundPreferences();
            }

            await using var stream = File.OpenRead(_settingsPath);
            var dto = await JsonSerializer.DeserializeAsync<PreferencesDto>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            return dto is null
                ? new BackgroundPreferences()
                : new BackgroundPreferences
                {
                    SelectedMode = Enum.IsDefined(dto.SelectedMode)
                        ? dto.SelectedMode
                        : BackgroundSelection.SusanooMadara,
                    Opacity = dto.Opacity,
                    RotationInterval = TimeSpan.FromSeconds(dto.RotationIntervalSeconds),
                };
        }
        catch (JsonException)
        {
            return new BackgroundPreferences();
        }
        catch (IOException)
        {
            return new BackgroundPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new BackgroundPreferences();
        }
    }

    public async Task SaveAsync(
        BackgroundPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("Background settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = _settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var dto = new PreferencesDto(
                    preferences.SelectedMode,
                    preferences.Opacity,
                    preferences.RotationInterval.TotalSeconds);

                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, _settingsPath, true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private sealed record PreferencesDto(
        BackgroundSelection SelectedMode,
        double Opacity,
        double RotationIntervalSeconds);
}

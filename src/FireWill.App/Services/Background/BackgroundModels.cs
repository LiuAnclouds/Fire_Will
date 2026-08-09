using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FireWill.App.Services.Background;

public enum BackgroundSelection
{
    SusanooMadara,
    FlowingSasuke,
    DynamicRotation,
}

public sealed record BackgroundOption(BackgroundSelection Value, string DisplayName);

public sealed record BackgroundDescriptor(
    BackgroundSelection Selection,
    string DisplayName,
    string FileName,
    string ResourceName,
    string Sha256,
    TimeSpan Duration);

public sealed record BackgroundPlaybackItem(
    BackgroundDescriptor Descriptor,
    string LocalPath);

public sealed class BackgroundPreferences : INotifyPropertyChanged
{
    public const double DefaultOpacity = 0.58;
    public static readonly TimeSpan DefaultRotationInterval = TimeSpan.FromSeconds(20);

    private BackgroundSelection _selectedMode = BackgroundSelection.SusanooMadara;
    private double _opacity = DefaultOpacity;
    private TimeSpan _rotationInterval = DefaultRotationInterval;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BackgroundSelection SelectedMode
    {
        get => _selectedMode;
        set => SetField(ref _selectedMode, Enum.IsDefined(value) ? value : BackgroundSelection.SusanooMadara);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, Math.Clamp(value, 0.05, 1.0));
    }

    public TimeSpan RotationInterval
    {
        get => _rotationInterval;
        set => SetField(ref _rotationInterval, ClampRotationInterval(value));
    }

    internal static TimeSpan ClampRotationInterval(TimeSpan value)
    {
        return TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 5, 300));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

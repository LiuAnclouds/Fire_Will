using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FireWill.Core.Configuration;
using FireWill.Core.Execution;

namespace FireWill.App.ViewModels;

public enum ConfigurationChangeSource
{
    General,
    Farm,
    Npc,
    Flow,
    FlowGroup,
    SkillMapping,
    ItemMapping,
}

public sealed class ConfigurationChangedEventArgs(ConfigurationChangeSource source) : EventArgs
{
    public ConfigurationChangeSource Source { get; } = source;
}

public sealed class MainWindowState : BindableObject
{
    private readonly MacroActionCompiler _compiler = new();
    private FlowRowViewModel _selectedFlow;
    private NpcRowViewModel _selectedNpc;

    public MainWindowState(MacroConfiguration configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ReleaseTypeOptions =
        [
            LegacyValues.None,
            LegacyValues.SkillKeyRelease,
            LegacyValues.ItemKeyRelease,
            LegacyValues.SkillSlotRelease,
            LegacyValues.ItemSlotRelease,
        ];
        PreTypeOptions = [LegacyValues.None, LegacyValues.KeyPreCommand, LegacyValues.ChatPreCommand];
        FarmOptions = [LegacyValues.None, .. LegacyCatalog.FarmNames];

        Farms = new ObservableCollection<FarmRowViewModel>(
            LegacyCatalog.FarmNames.Select(name => new FarmRowViewModel(configuration.Farms[name])));
        Npcs = new ObservableCollection<NpcRowViewModel>(
            LegacyCatalog.NpcNames.Select(name => new NpcRowViewModel(configuration.Npcs[name])));
        Flows = new ObservableCollection<FlowRowViewModel>(
            configuration.Flows
                .OrderBy(flow => flow.Slot)
                .Select(flow => new FlowRowViewModel(configuration, flow, _compiler)));
        SkillMappings = new ObservableCollection<KeyMappingRowViewModel>(
            Enumerable.Range(1, LegacyCatalog.SkillSlotCount)
                .Select(index => new KeyMappingRowViewModel(
                    $"技能 {index}",
                    () => configuration.KeyMap.Skills[index - 1],
                    value => configuration.KeyMap.Skills[index - 1] = LegacyNormalization.Key(value))));
        ItemMappings = new ObservableCollection<KeyMappingRowViewModel>(
            Enumerable.Range(1, LegacyCatalog.ItemSlotCount)
                .Select(index => new KeyMappingRowViewModel(
                    $"装备 {index}",
                    () => configuration.KeyMap.Items[index - 1],
                    value => configuration.KeyMap.Items[index - 1] = LegacyNormalization.Key(value))));

        foreach (var farm in Farms)
        {
            farm.ValueChanged += RefreshDurations;
            farm.ValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.Farm);
        }

        foreach (var npc in Npcs)
        {
            npc.ValueChanged += RefreshDurations;
            npc.ValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.Npc);
        }

        foreach (var mapping in SkillMappings)
        {
            mapping.ValueChanged += RefreshDurations;
            mapping.ValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.SkillMapping);
        }

        foreach (var mapping in ItemMappings)
        {
            mapping.ValueChanged += RefreshDurations;
            mapping.ValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.ItemMapping);
        }

        foreach (var flow in Flows)
        {
            flow.ValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.Flow);
            flow.GroupValueChanged += () => RaiseConfigurationChanged(ConfigurationChangeSource.FlowGroup);
        }

        _selectedFlow = Flows[0];
        _selectedNpc = Npcs[0];
        RefreshDurations();
    }

    public MacroConfiguration Configuration { get; }

    public ObservableCollection<FarmRowViewModel> Farms { get; }

    public ObservableCollection<FlowRowViewModel> Flows { get; }

    public ObservableCollection<NpcRowViewModel> Npcs { get; }

    public ObservableCollection<KeyMappingRowViewModel> SkillMappings { get; }

    public ObservableCollection<KeyMappingRowViewModel> ItemMappings { get; }

    public IReadOnlyList<string> ReleaseTypeOptions { get; }

    public IReadOnlyList<string> PreTypeOptions { get; }

    public IReadOnlyList<string> FarmOptions { get; }

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public FlowRowViewModel SelectedFlow
    {
        get => _selectedFlow;
        set
        {
            if (value is null || ReferenceEquals(_selectedFlow, value))
            {
                return;
            }

            _selectedFlow = value;
            _selectedFlow.RefreshDurations();
            OnPropertyChanged();
        }
    }

    public NpcRowViewModel SelectedNpc
    {
        get => _selectedNpc;
        set => SetField(ref _selectedNpc, value);
    }

    public string StopHotkey
    {
        get => Configuration.General.StopHotkey;
        set
        {
            var normalized = LegacyNormalization.Hotkey(value);
            if (Configuration.General.StopHotkey == normalized)
            {
                return;
            }

            Configuration.General.StopHotkey = normalized;
            OnPropertyChanged();
            RaiseConfigurationChanged(ConfigurationChangeSource.General);
        }
    }

    public bool SkipGameCheck
    {
        get => Configuration.General.SkipGameCheck;
        set
        {
            if (Configuration.General.SkipGameCheck == value)
            {
                return;
            }

            Configuration.General.SkipGameCheck = value;
            OnPropertyChanged();
            RaiseConfigurationChanged(ConfigurationChangeSource.General);
        }
    }

    public void RefreshDurations()
    {
        foreach (var flow in Flows)
        {
            flow.RefreshDurations();
        }
    }

    public void ClearFarmSettings()
    {
        foreach (var farm in Farms)
        {
            farm.Clear();
        }

        RefreshDurations();
        RaiseConfigurationChanged(ConfigurationChangeSource.Farm);
    }

    public void ClearCurrentFlow()
    {
        SelectedFlow.Clear();
        OnPropertyChanged(nameof(SelectedFlow));
        RaiseConfigurationChanged(ConfigurationChangeSource.Flow);
    }

    public void ClearNpcAndMappings()
    {
        foreach (var npc in Npcs)
        {
            npc.ClearToLegacyDefault();
        }

        foreach (var mapping in SkillMappings.Concat(ItemMappings))
        {
            mapping.Key = string.Empty;
        }

        RefreshDurations();
        RaiseConfigurationChanged(ConfigurationChangeSource.Npc);
    }

    private void RaiseConfigurationChanged(ConfigurationChangeSource source) =>
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(source));
}

public sealed class FarmRowViewModel : BindableObject
{
    private readonly FarmSettings _model;

    public FarmRowViewModel(FarmSettings model)
    {
        _model = model;
    }

    public event Action? ValueChanged;

    public FarmSettings Model => _model;

    public string Name => _model.Name;

    public string NpcAction => _model.NpcAction;

    public string ActionKey
    {
        get => _model.ActionKey;
        set => SetNormalized(value, _model.ActionKey, normalized => _model.ActionKey = normalized);
    }

    public string ReleaseType
    {
        get => _model.ReleaseType;
        set
        {
            var normalized = LegacyNormalization.ReleaseType(value);
            if (_model.ReleaseType == normalized)
            {
                return;
            }

            _model.ReleaseType = normalized;
            _model.ReleaseKey = LegacyNormalization.ReleaseKey(normalized, _model.ReleaseKey);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReleaseKey));
            ValueChanged?.Invoke();
        }
    }

    public string ReleaseKey
    {
        get => _model.ReleaseKey;
        set
        {
            var normalized = LegacyNormalization.ReleaseKey(_model.ReleaseType, value);
            if (_model.ReleaseKey == normalized)
            {
                return;
            }

            _model.ReleaseKey = normalized;
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }

    public string TargetX
    {
        get => Format(_model.TargetX);
        set => SetCoordinate(
            value,
            _model.TargetX,
            coordinate =>
            {
                _model.TargetX = coordinate;
                ClearTargetClientRatios();
            });
    }

    public string TargetY
    {
        get => Format(_model.TargetY);
        set => SetCoordinate(
            value,
            _model.TargetY,
            coordinate =>
            {
                _model.TargetY = coordinate;
                ClearTargetClientRatios();
            });
    }

    public void SetTarget(
        int x,
        int y,
        double? clientXRatio = null,
        double? clientYRatio = null)
    {
        _model.TargetX = x;
        _model.TargetY = y;
        if (IsValidRatioPair(clientXRatio, clientYRatio))
        {
            _model.TargetClientXRatio = clientXRatio;
            _model.TargetClientYRatio = clientYRatio;
        }
        else
        {
            ClearTargetClientRatios();
        }

        OnPropertyChanged(nameof(TargetX));
        OnPropertyChanged(nameof(TargetY));
        ValueChanged?.Invoke();
    }

    public void Clear()
    {
        _model.ActionKey = string.Empty;
        _model.ReleaseType = LegacyValues.None;
        _model.ReleaseKey = string.Empty;
        _model.TargetX = null;
        _model.TargetY = null;
        ClearTargetClientRatios();
        OnPropertyChanged(string.Empty);
        ValueChanged?.Invoke();
    }

    private void ClearTargetClientRatios()
    {
        _model.TargetClientXRatio = null;
        _model.TargetClientYRatio = null;
    }

    private static bool IsValidRatioPair(double? x, double? y) =>
        x is >= 0d and <= 1d &&
        y is >= 0d and <= 1d &&
        double.IsFinite(x.Value) &&
        double.IsFinite(y.Value);

    private void SetNormalized(string value, string current, Action<string> setter)
    {
        var normalized = LegacyNormalization.Key(value);
        if (current == normalized)
        {
            return;
        }

        setter(normalized);
        OnPropertyChanged();
        ValueChanged?.Invoke();
    }

    private void SetCoordinate(string value, int? current, Action<int?> setter)
    {
        var coordinate = LegacyNormalization.Coordinate(value);
        if (current == coordinate)
        {
            return;
        }

        setter(coordinate);
        OnPropertyChanged();
        ValueChanged?.Invoke();
    }

    private static string Format(int? value) => value?.ToString() ?? string.Empty;
}

public sealed class FlowRowViewModel : BindableObject
{
    private readonly MacroConfiguration _configuration;
    private readonly FlowSettings _model;
    private readonly MacroActionCompiler _compiler;

    public FlowRowViewModel(
        MacroConfiguration configuration,
        FlowSettings model,
        MacroActionCompiler compiler)
    {
        _configuration = configuration;
        _model = model;
        _compiler = compiler;
        Groups = new ObservableCollection<FlowGroupRowViewModel>(
            model.Groups.OrderBy(group => group.Slot).Select(group => new FlowGroupRowViewModel(group)));
        foreach (var group in Groups)
        {
            group.ValueChanged += RefreshDurations;
            group.ValueChanged += OnGroupValueChanged;
        }
    }

    /// <summary>
    /// Raised when any setting owned by this flow changes.
    /// </summary>
    public event Action? ValueChanged;

    /// <summary>
    /// Raised when a setting inside one of the flow groups changes.
    /// </summary>
    public event Action? GroupValueChanged;

    public FlowSettings Model => _model;

    public int Slot => _model.Slot;

    public string DisplayName => $"{Slot}. {Name}";

    public string Name
    {
        get => _model.Name;
        set
        {
            var normalized = LegacyNormalization.FlowName(value);
            if (_model.Name == normalized)
            {
                return;
            }

            _model.Name = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            ValueChanged?.Invoke();
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set => SetModelValue(_model.Enabled, value, normalized => _model.Enabled = normalized);
    }

    public string Hotkey
    {
        get => _model.Hotkey;
        set
        {
            var normalized = LegacyNormalization.Hotkey(value);
            if (_model.Hotkey == normalized)
            {
                return;
            }

            _model.Hotkey = normalized;
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }

    public int KeyDelayMilliseconds
    {
        get => _model.KeyDelayMs;
        set => SetDelay(_model.KeyDelayMs, value, 1000, normalized => _model.KeyDelayMs = normalized);
    }

    public int SkillKeyDelayMilliseconds
    {
        get => _model.SkillKeyDelayMs;
        set => SetDelay(_model.SkillKeyDelayMs, value, 1000, normalized => _model.SkillKeyDelayMs = normalized);
    }

    public int HeroSelectDelayMilliseconds
    {
        get => _model.HeroSelectDelayMs;
        set => SetDelay(_model.HeroSelectDelayMs, value, 1000, normalized => _model.HeroSelectDelayMs = normalized);
    }

    public int NpcClickDelayMilliseconds
    {
        get => _model.NpcClickDelayMs;
        set => SetDelay(_model.NpcClickDelayMs, value, 1000, normalized => _model.NpcClickDelayMs = normalized);
    }

    public int ChatDelayMilliseconds
    {
        get => _model.ChatDelayMs;
        set => SetDelay(_model.ChatDelayMs, value, 5000, normalized => _model.ChatDelayMs = normalized);
    }

    public int TeleportKeyDelayMilliseconds
    {
        get => _model.TeleportKeyDelayMs;
        set => SetDelay(_model.TeleportKeyDelayMs, value, 5000, normalized => _model.TeleportKeyDelayMs = normalized);
    }

    public int MouseMoveDelayMilliseconds
    {
        get => _model.MouseMoveDelayMs;
        set => SetDelay(_model.MouseMoveDelayMs, value, 1000, normalized => _model.MouseMoveDelayMs = normalized);
    }

    public int ReleaseMouseMoveDelayMilliseconds
    {
        get => _model.ReleaseMouseMoveDelayMs;
        set => SetDelay(_model.ReleaseMouseMoveDelayMs, value, 1000, normalized => _model.ReleaseMouseMoveDelayMs = normalized);
    }

    public ObservableCollection<FlowGroupRowViewModel> Groups { get; }

    public void AdjustDelay(string propertyName, int delta)
    {
        var property = GetType().GetProperty(propertyName)
            ?? throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null);
        var current = (int)(property.GetValue(this) ?? 0);
        property.SetValue(this, current + delta);
    }

    public void Clear()
    {
        Name = $"自定义流程{Slot}";
        Enabled = false;
        Hotkey = string.Empty;
        foreach (var group in Groups)
        {
            group.Clear();
        }

        RefreshDurations();
        OnPropertyChanged(string.Empty);
        ValueChanged?.Invoke();
    }

    public void RefreshDurations()
    {
        foreach (var group in Groups)
        {
            var compiled = _compiler.CompileGroup(_configuration, _model, group.Model);
            group.SetCalculatedDurations(compiled.CountedActionDurationMs, compiled.WaitMilliseconds);
        }
    }

    private void SetDelay(int current, int value, int maximum, Action<int> setter)
    {
        var normalized = Math.Clamp(value, 0, maximum);
        if (current == normalized)
        {
            return;
        }

        setter(normalized);
        OnPropertyChanged();
        RefreshDurations();
        ValueChanged?.Invoke();
    }

    private void SetModelValue<T>(T current, T value, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        setter(value);
        OnPropertyChanged();
        ValueChanged?.Invoke();
    }

    private void OnGroupValueChanged() => GroupValueChanged?.Invoke();
}

public sealed class FlowGroupRowViewModel : BindableObject
{
    private readonly FlowGroupSettings _model;
    private int _usedMilliseconds;
    private int _calculatedWaitMilliseconds;

    public FlowGroupRowViewModel(FlowGroupSettings model)
    {
        _model = model;
    }

    public event Action? ValueChanged;

    public FlowGroupSettings Model => _model;

    public int Slot => _model.Slot;

    public bool Enabled
    {
        get => _model.Enabled;
        set => SetValue(_model.Enabled, value, normalized => _model.Enabled = normalized);
    }

    public string PreType
    {
        get => _model.PreType;
        set
        {
            var normalized = value?.Trim() ?? LegacyValues.None;
            if (_model.PreType == normalized)
            {
                return;
            }

            _model.PreType = normalized;
            _model.PreValue = LegacyNormalization.PreValue(normalized, _model.PreValue);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreValue));
            ValueChanged?.Invoke();
        }
    }

    public string PreValue
    {
        get => _model.PreValue;
        set
        {
            var normalized = LegacyNormalization.PreValue(_model.PreType, value);
            if (_model.PreValue == normalized)
            {
                return;
            }

            _model.PreValue = normalized;
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }

    public string FarmName
    {
        get => _model.FarmName;
        set
        {
            var normalized = LegacyNormalization.FarmName(value);
            if (normalized.Length == 0)
            {
                normalized = LegacyValues.None;
            }

            if (_model.FarmName == normalized)
            {
                return;
            }

            _model.FarmName = normalized;
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }

    public int WaitMilliseconds
    {
        get => _model.WaitMs ?? _calculatedWaitMilliseconds;
        set
        {
            var normalized = Math.Clamp(value, 0, 30_000);
            if (_model.WaitMs == normalized)
            {
                return;
            }

            _model.WaitMs = normalized;
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }

    public int UsedMilliseconds => _usedMilliseconds;

    public int DurationMilliseconds => checked(_usedMilliseconds + WaitMilliseconds);

    public void AdjustWait(int delta) => WaitMilliseconds = WaitMilliseconds + delta;

    public void Clear()
    {
        _model.Enabled = false;
        _model.PreType = LegacyValues.None;
        _model.PreValue = string.Empty;
        _model.FarmName = LegacyValues.None;
        _model.WaitMs = 0;
        _model.DurationMs = 0;
        _usedMilliseconds = 0;
        _calculatedWaitMilliseconds = 0;
        OnPropertyChanged(string.Empty);
        ValueChanged?.Invoke();
    }

    public void SetCalculatedDurations(int usedMilliseconds, int waitMilliseconds)
    {
        _usedMilliseconds = Math.Max(0, usedMilliseconds);
        _calculatedWaitMilliseconds = Math.Max(0, waitMilliseconds);
        _model.DurationMs = checked(_usedMilliseconds + WaitMilliseconds);
        OnPropertyChanged(nameof(UsedMilliseconds));
        OnPropertyChanged(nameof(WaitMilliseconds));
        OnPropertyChanged(nameof(DurationMilliseconds));
    }

    private void SetValue<T>(T current, T value, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        setter(value);
        OnPropertyChanged();
        ValueChanged?.Invoke();
    }
}

public sealed class NpcRowViewModel : BindableObject
{
    private readonly NpcSettings _model;

    public NpcRowViewModel(NpcSettings model)
    {
        _model = model;
    }

    public event Action? ValueChanged;

    public NpcSettings Model => _model;

    public string Name => _model.Name;

    public string X
    {
        get => _model.X?.ToString() ?? string.Empty;
        set => SetCoordinate(
            value,
            _model.X,
            coordinate =>
            {
                _model.X = coordinate;
                ClearClientRatios();
            });
    }

    public string Y
    {
        get => _model.Y?.ToString() ?? string.Empty;
        set => SetCoordinate(
            value,
            _model.Y,
            coordinate =>
            {
                _model.Y = coordinate;
                ClearClientRatios();
            });
    }

    public void SetPoint(
        int x,
        int y,
        double? clientXRatio = null,
        double? clientYRatio = null)
    {
        _model.X = x;
        _model.Y = y;
        if (IsValidRatioPair(clientXRatio, clientYRatio))
        {
            _model.ClientXRatio = clientXRatio;
            _model.ClientYRatio = clientYRatio;
        }
        else
        {
            ClearClientRatios();
        }

        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        ValueChanged?.Invoke();
    }

    public void ClearToLegacyDefault()
    {
        (int? X, int? Y) point = _model.Name switch
        {
            "妙木山大蛤蟆" => (845, 390),
            "妙木山挑战自我NPC" => (1172, 689),
            "尾兽处追捕逃忍NPC" => (977, 509),
            _ => (null, null),
        };
        (_model.X, _model.Y) = point;
        ClearClientRatios();
        _model.Camera = string.Empty;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        ValueChanged?.Invoke();
    }

    private void ClearClientRatios()
    {
        _model.ClientXRatio = null;
        _model.ClientYRatio = null;
    }

    private static bool IsValidRatioPair(double? x, double? y) =>
        x is >= 0d and <= 1d &&
        y is >= 0d and <= 1d &&
        double.IsFinite(x.Value) &&
        double.IsFinite(y.Value);

    private void SetCoordinate(string value, int? current, Action<int?> setter)
    {
        var normalized = LegacyNormalization.Coordinate(value);
        if (current == normalized)
        {
            return;
        }

        setter(normalized);
        OnPropertyChanged();
        ValueChanged?.Invoke();
    }
}

public sealed class KeyMappingRowViewModel : BindableObject
{
    private readonly Func<string> _getter;
    private readonly Action<string> _setter;

    public KeyMappingRowViewModel(string label, Func<string> getter, Action<string> setter)
    {
        Label = label;
        _getter = getter;
        _setter = setter;
    }

    public event Action? ValueChanged;

    public string Label { get; }

    public string Key
    {
        get => _getter();
        set
        {
            var normalized = LegacyNormalization.Key(value);
            if (_getter() == normalized)
            {
                return;
            }

            _setter(normalized);
            OnPropertyChanged();
            ValueChanged?.Invoke();
        }
    }
}

public abstract class BindableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FireWill.App.Services.Background;
using FireWill.App.Services.Configuration;
using FireWill.App.Services.Input;
using FireWill.App.Services.Notifications;
using FireWill.App.ViewModels;
using FireWill.Core.Configuration;
using FireWill.Core.Execution;
using Microsoft.Win32;

namespace FireWill.App;

public partial class MainWindow : Window
{
    private const string DefaultProfileName = "默认/未读取";
    private const int SampleDoubleTapMilliseconds = 260;
    private static readonly TimeSpan GameBindingPollInterval = TimeSpan.FromMilliseconds(750);

    private static readonly string[] ReservedHotkeys =
    [
        "Esc",
        "F5",
        "F6",
        "F7",
        "F8",
        "Ctrl+F9",
        "Ctrl+Alt+B",
    ];

    private readonly object _hotkeyLock = new();
    private readonly object _sampleLock = new();
    private readonly object _pointCaptureLock = new();
    private readonly HashSet<Task> _pointCaptureTasks = [];
    private readonly CancellationTokenSource _windowLifetime = new();
    private readonly string _settingsRoot;
    private readonly string _configurationPath;
    private readonly string _profilesDirectory;
    private readonly War3WindowService _gameWindow = new();
    private readonly WindowsInputSender _inputSender;
    private readonly GameWindowAutoBinder _gameWindowAutoBinder;
    private readonly DispatcherTimer _gameBindingTimer;
    private readonly GlobalHotkeyService _hotkeys = new();
    private readonly QuietTipService _quietTip;
    private readonly ConfigurationAutosaveCoordinator _configurationAutosave;
    private readonly FlowScheduler _scheduler;

    private MainWindowState _state;
    private int _skipGameCheck;
    private BackgroundController? _backgroundController;
    private bool _backgroundUiUpdating;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _hasBoundGameWindow;
    private bool _waitingForGameWindow;
    private Action<string>? _captureCompletion;
    private bool _captureAsHotkey;
    private CancellationTokenSource? _farmSampleCancellation;
    private CancellationTokenSource? _npcSampleCancellation;
    private string? _pendingFarmSampleKey;
    private string? _pendingNpcSampleKey;

    public MainWindow()
    {
        InitializeComponent();
        _quietTip = new QuietTipService(Dispatcher);
        _inputSender = new WindowsInputSender(
            () => _gameWindow.TryGetBoundClientBounds(out var bounds) ? bounds : null);

        _settingsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireWill");
        _configurationPath = Path.Combine(_settingsRoot, "war3_macro_gui.ini");
        _profilesDirectory = Path.Combine(_settingsRoot, "profiles");
        Directory.CreateDirectory(_profilesDirectory);
        _configurationAutosave = new ConfigurationAutosaveCoordinator(_configurationPath);

        var configuration = LoadInitialConfiguration(out var startupStatus);
        _state = new MainWindowState(configuration);
        AttachState(_state);
        _configurationAutosave.Attach(_state);
        DataContext = _state;
        FarmCaptureTargetComboBox.SelectedIndex = 0;
        CurrentProfileText.Text = configuration.General.CurrentProfileName;
        StatusText.Text = startupStatus;

        _scheduler = new FlowScheduler(_inputSender, new SystemClock());
        _gameWindowAutoBinder = new GameWindowAutoBinder(
            () => _gameWindow.IsBound,
            FindAndBindGameWindow);
        _gameBindingTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = GameBindingPollInterval,
        };
        _gameBindingTimer.Tick += GameBindingTimer_Tick;
        _hotkeys.HookError += (_, args) =>
        {
            var message = $"热键钩子错误：{args.Exception.Message}";
            SetStatus(message);
            ShowQuietTip(message, 1800);
        };
        _hotkeys.HandlerError += (_, args) =>
        {
            var message = $"热键处理错误：{args.Exception.Message}";
            SetStatus(message);
            ShowQuietTip(message, 1800);
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _hotkeys.StartAsync();
            ApplyHotkeys(_scheduler.IsEnabled);
            SetMacroState(_scheduler.IsEnabled);
        }
        catch (Exception exception)
        {
            var message = $"全局热键启动失败：{exception.Message}";
            SetStatus(message);
            ShowQuietTip(message, 1800);
        }

        AutoBindGameWindow();
        ReportLegacyCoordinateStatus();
        _gameBindingTimer.Start();
        await InitializeBackgroundAsync();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _gameBindingTimer.Stop();
        _gameBindingTimer.Tick -= GameBindingTimer_Tick;
        IsEnabled = false;
        SetStatus("正在关闭...");

        CancelPendingSamples();
        _windowLifetime.Cancel();
        var configurationSaveTask = _configurationAutosave.DisposeAsync().AsTask();
        var schedulerStopTask = _scheduler.StopAndWaitAsync();
        var pointCaptureStopTask = WaitForPointCapturesAsync();

        try
        {
            await _hotkeys.DisposeAsync();
        }
        catch (Exception exception)
        {
            AppendShutdownLog(exception);
        }

        try
        {
            await Task.WhenAll(configurationSaveTask, schedulerStopTask, pointCaptureStopTask);
        }
        catch (Exception exception)
        {
            AppendShutdownLog(exception);
        }

        if (_backgroundController is not null)
        {
            try
            {
                _backgroundController.CurrentChanged -= BackgroundController_CurrentChanged;
                await _backgroundController.DisposeAsync();
            }
            catch (Exception exception)
            {
                AppendShutdownLog(exception);
            }
        }

        BackgroundVideo.Stop();
        BackgroundVideo.Source = null;
        _quietTip.Dispose();
        _windowLifetime.Dispose();
        _shutdownComplete = true;
        Close();
    }

    private async Task InitializeBackgroundAsync()
    {
        try
        {
            var catalog = new BackgroundCatalog();
            var extractor = new EmbeddedBackgroundAssetExtractor(
                typeof(App).Assembly,
                developmentAssetDirectory: FindDevelopmentBackgroundDirectory());
            _backgroundController = new BackgroundController(
                catalog,
                extractor,
                new JsonBackgroundPreferencesStore());
            _backgroundController.CurrentChanged += BackgroundController_CurrentChanged;

            await _backgroundController.InitializeAsync();

            _backgroundUiUpdating = true;
            BackgroundModeComboBox.ItemsSource = _backgroundController.Options;
            BackgroundModeComboBox.SelectedValue = _backgroundController.SelectedMode;
            BackgroundOpacitySlider.Value = _backgroundController.Opacity;
            BackgroundVideo.Opacity = _backgroundController.Opacity;
            BackgroundOpacityText.Text = $"{_backgroundController.Opacity:P0}";
            _backgroundUiUpdating = false;

            if (_backgroundController.Current is { } current)
            {
                PlayBackground(current.LocalPath);
            }

            if (!string.IsNullOrWhiteSpace(_backgroundController.LastError))
            {
                SetStatus($"动态背景加载失败：{_backgroundController.LastError}");
            }
        }
        catch (Exception exception)
        {
            _backgroundUiUpdating = false;
            SetStatus($"动态背景不可用，宏功能不受影响：{exception.Message}");
        }
    }

    private void BackgroundController_CurrentChanged(object? sender, BackgroundPlaybackItem item)
    {
        if (Dispatcher.CheckAccess())
        {
            PlayBackground(item.LocalPath);
            return;
        }

        _ = Dispatcher.BeginInvoke(() => PlayBackground(item.LocalPath));
    }

    private async void BackgroundModeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_backgroundUiUpdating || _backgroundController is null ||
            BackgroundModeComboBox.SelectedItem is not BackgroundOption option)
        {
            return;
        }

        try
        {
            await _backgroundController.SetSelectedModeAsync(option.Value);
        }
        catch (Exception exception)
        {
            SetStatus($"切换动态背景失败：{exception.Message}");
        }
    }

    private void BackgroundOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundVideo is null || BackgroundOpacityText is null)
        {
            return;
        }

        var opacity = Math.Clamp(e.NewValue, 0.05, 1.0);
        BackgroundVideo.Opacity = opacity;
        BackgroundOpacityText.Text = $"{opacity:P0}";
        if (!_backgroundUiUpdating && _backgroundController is not null)
        {
            _backgroundController.Opacity = opacity;
        }
    }

    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    private void BackgroundVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        SetStatus($"动态背景播放失败，宏功能不受影响：{e.ErrorException.Message}");
    }

    private void PlayBackground(string path)
    {
        if (!File.Exists(path) || _shutdownStarted)
        {
            return;
        }

        BackgroundVideo.Stop();
        BackgroundVideo.Source = new Uri(path, UriKind.Absolute);
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    private void ApplyHotkeys(bool enableMacroHotkeys)
    {
        lock (_hotkeyLock)
        {
            _hotkeys.ClearRegistrations();

            var condition = new Func<bool>(IsMacroHotkeyContextActive);
            var stopGesture = ParseStopHotkey();
            _hotkeys.Register(
                stopGesture,
                _ => HandleStopHotkey(),
                suppressInput: true,
                isActive: condition);

            if (!enableMacroHotkeys)
            {
                return;
            }

            var seen = new HashSet<HotkeyGesture> { stopGesture };
            var reserved = new HashSet<HotkeyGesture>(ReservedHotkeys.Select(HotkeyGesture.Parse));

            RegisterFixedHotkey("F5", _ => HandleSampleTap(farm: true, "F5", -1), condition);
            RegisterFixedHotkey("F6", _ => HandleSampleTap(farm: true, "F6", 1), condition);
            RegisterFixedHotkey("F7", _ => HandleSampleTap(farm: false, "F7", -1), condition);
            RegisterFixedHotkey("F8", _ => HandleSampleTap(farm: false, "F8", 1), condition);
            RegisterFixedHotkey("Ctrl+F9", _ => CopyActiveWindowInfo(), static () => true);
            RegisterFixedHotkey("Ctrl+Alt+B", _ => BindForegroundGameWindow(), static () => true);

            foreach (var flow in _state.Flows.Where(item => item.Enabled))
            {
                if (!HotkeyGesture.TryParse(flow.Hotkey, out var gesture, out var error))
                {
                    if (!string.IsNullOrWhiteSpace(flow.Hotkey))
                    {
                        SetStatus($"跳过无效流程热键：{flow.DisplayName} / {error}");
                    }

                    continue;
                }

                if (reserved.Contains(gesture))
                {
                    SetStatus($"跳过保留热键：{flow.DisplayName} / {flow.Hotkey}");
                    continue;
                }

                if (!seen.Add(gesture))
                {
                    SetStatus($"跳过重复热键：{flow.DisplayName} / {flow.Hotkey}");
                    continue;
                }

                var slot = flow.Slot;
                _hotkeys.Register(
                    gesture,
                    invocation =>
                    {
                        _ = RunFlowAsync(slot);
                    },
                    suppressInput: true,
                    isActive: condition);
            }
        }
    }

    private void RegisterFixedHotkey(
        string hotkey,
        Action<HotkeyInvocation> handler,
        Func<bool> condition)
    {
        _hotkeys.Register(
            hotkey,
            handler,
            suppressInput: true,
            isActive: condition);
    }

    private HotkeyGesture ParseStopHotkey()
    {
        if (HotkeyGesture.TryParse(_state.StopHotkey, out var gesture) &&
            !ReservedHotkeys.Select(HotkeyGesture.Parse).Contains(gesture))
        {
            return gesture;
        }

        _state.StopHotkey = "Z";
        return HotkeyGesture.Parse("Z");
    }

    private bool IsMacroHotkeyContextActive()
    {
        return Volatile.Read(ref _skipGameCheck) != 0 || _gameWindow.IsBoundWindowForeground;
    }

    private bool CanRunMacro()
    {
        if (_state.SkipGameCheck)
        {
            return true;
        }

        if (_gameWindow.IsBoundWindowForeground)
        {
            return true;
        }

        if (_gameWindow.TryFindAndBind(out var binding))
        {
            UpdateGameBinding(binding);
            return _gameWindow.IsBoundWindowForeground;
        }

        return false;
    }

    private async Task RunFlowAsync(int slot)
    {
        if (!CanRunMacro())
        {
            const string message = "游戏窗口未激活，流程未执行。";
            SetStatus(message);
            ShowQuietTip(message, 1200);
            return;
        }

        var flowName = _state.Configuration.GetFlow(slot).Name;
        SetStatus($"正在执行流程：{flowName}");
        var result = await _scheduler.RunFlowAsync(_state.Configuration, slot);
        ReportFlowResult(result);
    }

    private void ReportFlowResult(FlowRunResult result)
    {
        var status = result.Status switch
        {
            FlowRunStatus.Completed when result.Warnings.Count == 0 => $"流程执行完成：{result.FlowName}",
            FlowRunStatus.Completed => $"流程完成但有警告：{string.Join("；", result.Warnings)}",
            FlowRunStatus.Stopped => "流程已停止。",
            FlowRunStatus.Disabled => "宏已暂停。连续按两次停止热键可恢复。",
            FlowRunStatus.Busy => "已有流程正在执行。",
            FlowRunStatus.FlowDisabled => $"流程未启用：{result.FlowName}",
            FlowRunStatus.Failed => $"流程执行失败：{result.Error?.Message}",
            _ => $"流程状态：{result.Status}",
        };
        SetStatus(status);
        if (result.Status is not FlowRunStatus.Completed || result.Warnings.Count > 0)
        {
            ShowQuietTip(status, result.Status == FlowRunStatus.Failed ? 1800 : 1200);
        }
    }

    private void HandleStopHotkey()
    {
        var result = _scheduler.HandleStopTap();
        var enabled = result == StopTapResult.Resumed;
        ApplyHotkeys(enabled);
        SetMacroState(enabled);
        var message = enabled
            ? "已重新启用全部触发。"
            : "已停止当前流程并暂停触发；连续再按两次停止热键可恢复。";
        SetStatus(message);
        ShowQuietTip(enabled ? "已重新启用触发" : "已停止并暂停触发");
    }

    private void StopMacro_Click(object sender, RoutedEventArgs e)
    {
        _scheduler.Stop();
        ApplyHotkeys(enableMacroHotkeys: false);
        SetMacroState(enabled: false);
        const string message = "已停止当前流程并暂停触发；连续按两次停止热键可恢复。";
        SetStatus(message);
        ShowQuietTip("已停止并暂停触发");
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (IsDefaultProfile(_state.Configuration.General.CurrentProfileName))
        {
            await SaveProfileAsAsync();
            return;
        }

        var profileName = SanitizeProfileName(_state.Configuration.General.CurrentProfileName);
        var profilePath = Path.Combine(_profilesDirectory, profileName + ".ini");
        await SaveConfigurationAsync(profilePath, profileName, "已保存当前英雄配置");
    }

    private async void SaveProfileAs_Click(object sender, RoutedEventArgs e)
    {
        await SaveProfileAsAsync();
    }

    private async Task SaveProfileAsAsync()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "请输入英雄名称",
            "另存为英雄",
            IsDefaultProfile(_state.Configuration.General.CurrentProfileName)
                ? string.Empty
                : _state.Configuration.General.CurrentProfileName);
        var safeName = SanitizeProfileName(name);
        if (safeName.Length == 0)
        {
            return;
        }

        var path = Path.Combine(_profilesDirectory, safeName + ".ini");
        await SaveConfigurationAsync(path, safeName, "已保存为新英雄配置");
    }

    private async Task SaveConfigurationAsync(
        string profilePath,
        string profileName,
        string successMessage)
    {
        try
        {
            _state.RefreshDurations();
            _state.Configuration.General.CurrentProfileName = profileName;
            _state.Configuration.General.CurrentProfilePath = profilePath;
            LegacyIniProfileSerializer.Save(profilePath, _state.Configuration);
            _configurationAutosave.NotifyChanged();
            await _configurationAutosave.FlushAsync();
            CurrentProfileText.Text = profileName;
            ApplyHotkeys(_scheduler.IsEnabled);
            SetStatus($"{successMessage}：{profileName}");
        }
        catch (Exception exception)
        {
            SetStatus($"保存配置失败：{exception.Message}");
        }
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "读取英雄配置",
            Filter = "INI 配置 (*.ini)|*.ini|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(_profilesDirectory) ? _profilesDirectory : _settingsRoot,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var configuration = LegacyIniProfileSerializer.Load(dialog.FileName);
            var profileName = SanitizeProfileName(Path.GetFileNameWithoutExtension(dialog.FileName));
            configuration.General.CurrentProfileName = profileName;
            configuration.General.CurrentProfilePath = Path.Combine(_profilesDirectory, profileName + ".ini");
            ReplaceConfiguration(configuration);
            await _configurationAutosave.FlushAsync();
            ApplyHotkeys(_scheduler.IsEnabled);
            SetStatus($"已读取英雄配置：{profileName}");
            ReportLegacyCoordinateStatus();
        }
        catch (Exception exception)
        {
            SetStatus($"读取配置失败：{exception.Message}");
        }
    }

    private void ReplaceConfiguration(MacroConfiguration configuration)
    {
        _state.PropertyChanged -= State_PropertyChanged;
        _state = new MainWindowState(configuration);
        AttachState(_state);
        _configurationAutosave.Attach(_state);
        _configurationAutosave.NotifyChanged();
        DataContext = _state;
        CurrentProfileText.Text = configuration.General.CurrentProfileName;
        FarmCaptureTargetComboBox.SelectedIndex = 0;
    }

    private void AttachState(MainWindowState state)
    {
        Volatile.Write(ref _skipGameCheck, state.SkipGameCheck ? 1 : 0);
        state.PropertyChanged += State_PropertyChanged;
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowState.SkipGameCheck) or null or "")
        {
            Volatile.Write(ref _skipGameCheck, _state.SkipGameCheck ? 1 : 0);
        }
    }

    private void ClearFarmSettings_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "清空 7 个刷本项的动作键、释放设置和技能鼠标点？",
                "清空刷本设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _state.ClearFarmSettings();
        SetStatus("已清空刷本设置。");
    }

    private void ClearCurrentFlow_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                $"清空 {_state.SelectedFlow.DisplayName}？",
                "清空当前流程",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _state.ClearCurrentFlow();
        ApplyHotkeys(_scheduler.IsEnabled);
        SetStatus("已清空当前流程。");
    }

    private void ClearNpcSettings_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "重置 NPC 点击点和全部技能、装备按键映射？",
                "清空 NPC 与平台按键",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _state.ClearNpcAndMappings();
        SetStatus("已重置 NPC 与平台按键。");
    }

    private void AdjustFlowDelay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var delta))
        {
            return;
        }

        _state.SelectedFlow.AdjustDelay(parts[0], delta);
    }

    private void AdjustGroupWait_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FlowGroupRowViewModel group, Tag: string tag } &&
            int.TryParse(tag, out var delta))
        {
            group.AdjustWait(delta);
        }
    }

    private void CaptureFarmActionKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FarmRowViewModel farm })
        {
            StartKeyCapture(value => farm.ActionKey = value, captureHotkey: false, $"{farm.Name} 动作键");
        }
    }

    private void CaptureFarmReleaseKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FarmRowViewModel farm })
        {
            StartKeyCapture(value => farm.ReleaseKey = value, captureHotkey: false, $"{farm.Name} 释放键");
        }
    }

    private void CaptureGroupPreKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FlowGroupRowViewModel group })
        {
            StartKeyCapture(value => group.PreValue = value, captureHotkey: false, $"ID {group.Slot} 组前按键");
        }
    }

    private void CaptureMappedKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeyMappingRowViewModel mapping })
        {
            StartKeyCapture(value => mapping.Key = value, captureHotkey: false, mapping.Label);
        }
    }

    private void CaptureFlowHotkey_Click(object sender, RoutedEventArgs e)
    {
        StartKeyCapture(
            value => _state.SelectedFlow.Hotkey = value,
            captureHotkey: true,
            "流程触发键");
    }

    private void CaptureStopHotkey_Click(object sender, RoutedEventArgs e)
    {
        StartKeyCapture(value => _state.StopHotkey = value, captureHotkey: true, "停止热键");
    }

    private void StartKeyCapture(Action<string> completion, bool captureHotkey, string label)
    {
        _captureCompletion = completion;
        _captureAsHotkey = captureHotkey;
        _hotkeys.ClearRegistrations();
        Activate();
        Focus();
        SetStatus($"正在采集 {label}，请按键。");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_captureCompletion is null)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0 || virtualKey > ushort.MaxValue)
        {
            return;
        }

        var modifiers = _captureAsHotkey ? GetCurrentHotkeyModifiers() : HotkeyModifiers.None;
        CompleteKeyCapture(HotkeyGesture.Keyboard(modifiers, checked((ushort)virtualKey)).ToString());
        e.Handled = true;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_captureCompletion is null)
        {
            return;
        }

        var button = e.ChangedButton switch
        {
            MouseButton.Middle => HotkeyButton.MiddleMouse,
            MouseButton.XButton1 => HotkeyButton.XButton1,
            MouseButton.XButton2 => HotkeyButton.XButton2,
            _ => HotkeyButton.Keyboard,
        };
        if (button == HotkeyButton.Keyboard)
        {
            return;
        }

        var modifiers = _captureAsHotkey ? GetCurrentHotkeyModifiers() : HotkeyModifiers.None;
        CompleteKeyCapture(HotkeyGesture.Mouse(modifiers, button).ToString());
        e.Handled = true;
    }

    private void CompleteKeyCapture(string value)
    {
        var completion = _captureCompletion;
        _captureCompletion = null;
        completion?.Invoke(value);
        ApplyHotkeys(_scheduler.IsEnabled);
        SetStatus($"已采集按键：{value}");
    }

    private static HotkeyModifiers GetCurrentHotkeyModifiers()
    {
        var result = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotkeyModifiers.Windows;
        }

        return result;
    }

    private async void CaptureFarmPoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FarmRowViewModel farm })
        {
            await CapturePointAfterDelayAsync(
                (point, xRatio, yRatio) => farm.SetTarget(point.X, point.Y, xRatio, yRatio),
                farm.Name);
        }
    }

    private async void CaptureNpcPoint_Click(object sender, RoutedEventArgs e)
    {
        var npc = _state.SelectedNpc;
        await CapturePointAfterDelayAsync(
            (point, xRatio, yRatio) => npc.SetPoint(point.X, point.Y, xRatio, yRatio),
            npc.Name);
    }

    private Task CapturePointAfterDelayAsync(
        Action<ScreenPoint, double, double> setter,
        string label)
    {
        lock (_pointCaptureLock)
        {
            if (_shutdownStarted)
            {
                return Task.CompletedTask;
            }

            var task = CapturePointAfterDelayCoreAsync(setter, label, _windowLifetime.Token);
            _pointCaptureTasks.Add(task);
            _ = task.ContinueWith(
                completedTask =>
                {
                    lock (_pointCaptureLock)
                    {
                        _pointCaptureTasks.Remove(completedTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    private async Task CapturePointAfterDelayCoreAsync(
        Action<ScreenPoint, double, double> setter,
        string label,
        CancellationToken cancellationToken)
    {
        SetStatus($"1.5 秒后记录 {label} 的鼠标位置。");
        try
        {
            await Task.Delay(1500, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_shutdownStarted)
        {
            return;
        }

        if (!_inputSender.TryGetCursorPosition(out var point))
        {
            var message = $"记录 {label} 失败：无法读取鼠标位置。";
            SetStatus(message);
            ShowQuietTip(message, 1400);
            return;
        }

        if (!TryNormalizeCapturedPoint(point, out var xRatio, out var yRatio, out var error))
        {
            var message = $"记录 {label} 失败：{error}";
            SetStatus(message);
            ShowQuietTip(message, 1400);
            return;
        }

        setter(point, xRatio, yRatio);
        _state.RefreshDurations();
        SetStatus($"已记录 {label}：窗口自适应坐标已启用");
        ShowQuietTip($"已记录 {label}\n窗口缩放自适应已启用", 1200);
    }

    private Task WaitForPointCapturesAsync()
    {
        lock (_pointCaptureLock)
        {
            return _pointCaptureTasks.Count == 0
                ? Task.CompletedTask
                : Task.WhenAll(_pointCaptureTasks.ToArray());
        }
    }

    private void HandleSampleTap(bool farm, string key, int direction)
    {
        lock (_sampleLock)
        {
            ref var pendingKey = ref farm ? ref _pendingFarmSampleKey : ref _pendingNpcSampleKey;
            ref var cancellation = ref farm ? ref _farmSampleCancellation : ref _npcSampleCancellation;
            if (pendingKey == key && cancellation is { IsCancellationRequested: false })
            {
                cancellation.Cancel();
                cancellation.Dispose();
                cancellation = null;
                pendingKey = null;
                _ = Dispatcher.BeginInvoke(() => MoveSampleSelection(farm, direction));
                return;
            }

            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            pendingKey = key;
            TrackPointCaptureTask(CommitSampleAfterDelayAsync(farm, key, cancellation.Token));
        }
    }

    private void TrackPointCaptureTask(Task task)
    {
        lock (_pointCaptureLock)
        {
            _pointCaptureTasks.Add(task);
            _ = task.ContinueWith(
                completedTask =>
                {
                    lock (_pointCaptureLock)
                    {
                        _pointCaptureTasks.Remove(completedTask);
                    }

                    if (completedTask.IsFaulted)
                    {
                        var exception = completedTask.Exception?.GetBaseException();
                        if (exception is not null && !_shutdownStarted)
                        {
                            SetStatus($"采点任务失败：{exception.Message}");
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task CommitSampleAfterDelayAsync(bool farm, string key, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SampleDoubleTapMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_shutdownStarted || _windowLifetime.IsCancellationRequested)
        {
            return;
        }

        lock (_sampleLock)
        {
            if (farm)
            {
                if (_pendingFarmSampleKey != key)
                {
                    return;
                }

                _pendingFarmSampleKey = null;
                _farmSampleCancellation?.Dispose();
                _farmSampleCancellation = null;
            }
            else
            {
                if (_pendingNpcSampleKey != key)
                {
                    return;
                }

                _pendingNpcSampleKey = null;
                _npcSampleCancellation?.Dispose();
                _npcSampleCancellation = null;
            }
        }

        if (!_inputSender.TryGetCursorPosition(out var point))
        {
            const string message = "采点失败：无法读取鼠标位置。";
            SetStatus(message);
            ShowQuietTip(message, 1400);
            return;
        }

        if (!TryNormalizeCapturedPoint(point, out var xRatio, out var yRatio, out var error))
        {
            var message = $"采点失败：{error}";
            SetStatus(message);
            ShowQuietTip(message, 1400);
            return;
        }

        if (_shutdownStarted || _windowLifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (farm)
                {
                    var target = FarmCaptureTargetComboBox.SelectedItem as FarmRowViewModel ?? _state.Farms[0];
                    target.SetTarget(point.X, point.Y, xRatio, yRatio);
                    SetStatus($"已记录 {target.Name}：窗口自适应坐标已启用");
                    ShowQuietTip($"已记录 {target.Name}\n窗口缩放自适应已启用", 1200);
                }
                else
                {
                    _state.SelectedNpc.SetPoint(point.X, point.Y, xRatio, yRatio);
                    SetStatus($"已记录 {_state.SelectedNpc.Name}：窗口自适应坐标已启用");
                    ShowQuietTip($"已记录 {_state.SelectedNpc.Name}\n窗口缩放自适应已启用", 1200);
                }

                _state.RefreshDurations();
            });
        }
        catch (InvalidOperationException) when (_shutdownStarted || _windowLifetime.IsCancellationRequested)
        {
            // The window dispatcher can close after the final cancellation check.
        }
    }

    private bool TryNormalizeCapturedPoint(
        ScreenPoint point,
        out double xRatio,
        out double yRatio,
        out string error)
    {
        if (!_gameWindow.TryGetBoundClientBounds(out var clientBounds))
        {
            if (!_gameWindow.TryFindAndBind(out var binding))
            {
                xRatio = 0;
                yRatio = 0;
                error = "尚未绑定 Warcraft III 窗口。";
                return false;
            }

            UpdateGameBinding(binding);
            clientBounds = binding.ClientBounds;
        }

        if (!ClientCoordinateProjector.TryNormalize(point, clientBounds, out xRatio, out yRatio))
        {
            error = "鼠标不在已绑定的 Warcraft III 客户区内。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void MoveSampleSelection(bool farm, int direction)
    {
        if (farm)
        {
            var count = _state.Farms.Count;
            var current = Math.Max(0, FarmCaptureTargetComboBox.SelectedIndex);
            FarmCaptureTargetComboBox.SelectedIndex = (current + direction + count) % count;
            var target = ((FarmRowViewModel)FarmCaptureTargetComboBox.SelectedItem).Name;
            SetStatus($"采点目标：{target}");
            ShowQuietTip($"鼠标点目标：{target}");
            return;
        }

        var npcIndex = _state.Npcs.IndexOf(_state.SelectedNpc);
        var next = (npcIndex + direction + _state.Npcs.Count) % _state.Npcs.Count;
        _state.SelectedNpc = _state.Npcs[next];
        SetStatus($"NPC 采点目标：{_state.SelectedNpc.Name}");
        ShowQuietTip($"NPC目标：{_state.SelectedNpc.Name}");
    }

    private void CancelPendingSamples()
    {
        lock (_sampleLock)
        {
            _farmSampleCancellation?.Cancel();
            _farmSampleCancellation?.Dispose();
            _farmSampleCancellation = null;
            _npcSampleCancellation?.Cancel();
            _npcSampleCancellation?.Dispose();
            _npcSampleCancellation = null;
            _pendingFarmSampleKey = null;
            _pendingNpcSampleKey = null;
        }
    }

    private async void BindGameWindow_Click(object sender, RoutedEventArgs e)
    {
        const string message = "请在 3 秒内切到游戏窗口；也可以在游戏里按 Ctrl+Alt+B 立即绑定。";
        SetStatus(message);
        ShowQuietTip("3秒内切到游戏窗口", 2500);
        try
        {
            await Task.Delay(3000, _windowLifetime.Token);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
            return;
        }

        BindForegroundGameWindow();
    }

    private void AutoBindGameWindow()
    {
        if (_gameWindow.TryFindAndBind(out var binding))
        {
            UpdateGameBinding(binding);
            var message = $"已绑定游戏窗口：PID {binding.ProcessId}";
            SetStatus(message);
            ShowQuietTip("已绑定游戏窗口", 1200);
            return;
        }

        _waitingForGameWindow = true;
        GameBindingText.Text = "等待 War3.exe 启动";
        SetStatus("未找到 Warcraft III；游戏启动后程序会自动绑定。");
    }

    private void ReportLegacyCoordinateStatus()
    {
        var npcCount = _state.Npcs.Count(
            npc => npc.Model.X is not null &&
                   npc.Model.Y is not null &&
                   (npc.Model.ClientXRatio is null || npc.Model.ClientYRatio is null));
        var farmCount = _state.Farms.Count(
            farm => farm.Model.TargetX is not null &&
                    farm.Model.TargetY is not null &&
                    (farm.Model.TargetClientXRatio is null || farm.Model.TargetClientYRatio is null));
        var total = npcCount + farmCount;
        if (total == 0)
        {
            return;
        }

        var message = $"有 {total} 个旧桌面坐标未启用窗口自适应，请在当前 War3 尺寸下用 F5-F8 或采点按钮重新记录。";
        SetStatus(message);
        ShowQuietTip("旧坐标需要重新采集一次", 3600);
    }

    private void GameBindingTimer_Tick(object? sender, EventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        var result = _gameWindowAutoBinder.Poll();
        if (result.State == GameWindowAutoBindState.BoundNow && result.Binding is { } binding)
        {
            UpdateGameBinding(binding);
            var message = $"已自动绑定游戏窗口：PID {binding.ProcessId}";
            SetStatus(message);
            ShowQuietTip("已自动绑定游戏窗口", 1200);
            return;
        }

        if (result.State == GameWindowAutoBindState.Waiting && !_waitingForGameWindow)
        {
            _waitingForGameWindow = true;
            GameBindingText.Text = "等待 War3.exe 启动";
            if (_hasBoundGameWindow)
            {
                SetStatus("游戏窗口已关闭或失效，正在等待 Warcraft III 自动重绑。");
            }
        }
    }

    private War3WindowBinding? FindAndBindGameWindow() =>
        _gameWindow.TryFindAndBind(out var binding) ? binding : null;

    private void BindForegroundGameWindow()
    {
        if (_gameWindow.TryBindForegroundWindow(checked((uint)Environment.ProcessId), out var binding))
        {
            UpdateGameBinding(binding);
            var message = $"已绑定当前游戏窗口：PID {binding.ProcessId}";
            SetStatus(message);
            ShowQuietTip("已绑定当前游戏窗口", 1200);
        }
        else if (_gameWindow.TryFindAndBind(out binding))
        {
            UpdateGameBinding(binding);
            var message = $"前台不是游戏，已自动绑定 Warcraft III：PID {binding.ProcessId}";
            SetStatus(message);
            ShowQuietTip("已自动绑定 Warcraft III", 1200);
        }
        else
        {
            _ = Dispatcher.BeginInvoke(() => GameBindingText.Text = "未找到 War3.exe");
            const string message = "绑定失败：当前是配置器或无效窗口，也没有找到可用的 Warcraft III 窗口。";
            SetStatus(message);
            ShowQuietTip("绑定失败：未找到游戏窗口", 1600);
        }
    }

    private void UpdateGameBinding(War3WindowBinding binding)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => UpdateGameBinding(binding));
            return;
        }

        var matcher = $"ahk_id {binding.WindowHandle}";
        if (!string.Equals(
                _state.Configuration.General.GameWindowMatcher,
                matcher,
                StringComparison.Ordinal))
        {
            _state.Configuration.General.GameWindowMatcher = matcher;
            _configurationAutosave.NotifyChanged();
        }
        GameBindingText.Text = $"已绑定 {binding.ProcessName}.exe / PID {binding.ProcessId}";
        _hasBoundGameWindow = true;
        _waitingForGameWindow = false;
    }

    private void CopyWindowInfo_Click(object sender, RoutedEventArgs e)
    {
        CopyActiveWindowInfo();
    }

    private void CopyActiveWindowInfo()
    {
        if (!_gameWindow.TryGetForegroundWindowInfo(out var window))
        {
            const string message = "复制失败：没有读到当前前台窗口信息。";
            SetStatus(message);
            ShowQuietTip(message, 1400);
            return;
        }

        var text = WindowDiagnosticFormatter.Format(window);
        _ = Dispatcher.BeginInvoke(() =>
        {
            Clipboard.SetText(text);
            var message = $"已复制当前窗口信息：HWND {window.WindowHandle} / PID {window.ProcessId}";
            SetStatus(message);
            ShowQuietTip("已复制当前窗口信息");
        });
    }

    private MacroConfiguration LoadInitialConfiguration(out string status)
    {
        try
        {
            if (File.Exists(_configurationPath))
            {
                status = "已加载本地配置。";
                return LegacyIniProfileSerializer.Load(_configurationPath);
            }

            var legacyPath = FindLegacyConfigurationPath();
            if (legacyPath is not null)
            {
                var migrated = LegacyIniProfileSerializer.Load(legacyPath);
                MoveProfilePathToLocalStorage(migrated);
                LegacyIniProfileSerializer.Save(_configurationPath, migrated);
                status = "已从旧版 AHK 配置迁移，旧文件保持不变。";
                return migrated;
            }

            var defaults = ConfigurationDefaults.Create();
            LegacyIniProfileSerializer.Save(_configurationPath, defaults);
            status = "已创建默认配置。";
            return defaults;
        }
        catch (Exception exception)
        {
            status = $"配置加载失败，已使用默认值：{exception.Message}";
            return ConfigurationDefaults.Create();
        }
    }

    private void MoveProfilePathToLocalStorage(MacroConfiguration configuration)
    {
        var name = SanitizeProfileName(configuration.General.CurrentProfileName);
        configuration.General.CurrentProfilePath = IsDefaultProfile(name)
            ? string.Empty
            : Path.Combine(_profilesDirectory, name + ".ini");
    }

    private static string? FindLegacyConfigurationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "war3_macro_gui.ini");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindDevelopmentBackgroundDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "assets", "backgrounds");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string SanitizeProfileName(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .Where(character => !invalid.Contains(character))
            .ToArray())
            .Trim()
            .TrimEnd('.');
        return normalized;
    }

    private static bool IsDefaultProfile(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == DefaultProfileName;
    }

    private void SetStatus(string message)
    {
        if (_shutdownComplete)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => SetStatus(message));
            return;
        }

        StatusText.Text = message;
    }

    private void ShowQuietTip(string message, int durationMilliseconds = 900)
    {
        _quietTip.Show(message, durationMilliseconds);
    }

    private void SetMacroState(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => SetMacroState(enabled));
            return;
        }

        MacroStateText.Text = enabled ? "宏已启用" : "宏已暂停";
        MacroStateText.Foreground = enabled
            ? (Brush)FindResource("AccentBrush")
            : Brushes.Gold;
    }

    private static void AppendShutdownLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FireWill",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "shutdown.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // Shutdown must continue even if diagnostics cannot be written.
        }
    }
}

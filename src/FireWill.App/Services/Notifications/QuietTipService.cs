using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FireWill.App.Services.Notifications;

public sealed class QuietTipService : IDisposable
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(1);

    private readonly Dispatcher _dispatcher;
    private QuietTipWindow? _window;
    private DispatcherTimer? _hideTimer;
    private long _requestVersion;
    private int _disposed;

    public QuietTipService()
        : this(Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("A WPF application dispatcher is required."))
    {
    }

    public QuietTipService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Show(string message, int durationMilliseconds = 900)
    {
        Show(message, TimeSpan.FromMilliseconds(durationMilliseconds));
    }

    public void Show(string message, TimeSpan duration)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var normalizedMessage = message?.Trim() ?? string.Empty;
        if (normalizedMessage.Length == 0)
        {
            Hide();
            return;
        }

        var normalizedDuration = TimeSpan.FromMilliseconds(Math.Clamp(
            duration.TotalMilliseconds,
            MinimumDuration.TotalMilliseconds,
            MaximumDuration.TotalMilliseconds));
        var version = Interlocked.Increment(ref _requestVersion);
        Dispatch(() => ShowCore(normalizedMessage, normalizedDuration, version));
    }

    public void Hide()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var version = Interlocked.Increment(ref _requestVersion);
        Dispatch(() => HideCore(version));
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Increment(ref _requestVersion);
        if (_dispatcher.CheckAccess())
        {
            DisposeCore();
        }
        else if (!_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
        {
            try
            {
                _dispatcher.Invoke(DisposeCore, DispatcherPriority.Send);
            }
            catch (TaskCanceledException)
            {
                // The application dispatcher completed shutdown first.
            }
            catch (InvalidOperationException) when (
                _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                // Dispatcher shutdown owns the remaining WPF resources.
            }
        }

        GC.SuppressFinalize(this);
    }

    private void ShowCore(string message, TimeSpan duration, long version)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            version != Volatile.Read(ref _requestVersion))
        {
            return;
        }

        _window ??= new QuietTipWindow();
        _window.Message = message;

        _hideTimer ??= CreateHideTimer();
        _hideTimer.Stop();
        _hideTimer.Interval = duration;

        if (!_window.IsVisible)
        {
            _window.Opacity = 0;
            _window.Show();
        }

        _window.UpdateLayout();
        _window.PositionNearCursor();
        _window.Opacity = 1;
        _hideTimer.Start();
    }

    private void HideCore(long version)
    {
        if (version != Volatile.Read(ref _requestVersion))
        {
            return;
        }

        _hideTimer?.Stop();
        _window?.Hide();
    }

    private DispatcherTimer CreateHideTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
        timer.Tick += HideTimer_Tick;
        return timer;
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        _window?.Hide();
    }

    private void DisposeCore()
    {
        if (_hideTimer is not null)
        {
            _hideTimer.Stop();
            _hideTimer.Tick -= HideTimer_Tick;
            _hideTimer = null;
        }

        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(action, DispatcherPriority.Send);
        }
        catch (InvalidOperationException) when (
            _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            // The application is already closing.
        }
    }

    private sealed class QuietTipWindow : Window
    {
        private const int ExtendedWindowStyleIndex = -20;
        private const int ExtendedStyleTransparent = 0x00000020;
        private const int ExtendedStyleToolWindow = 0x00000080;
        private const int ExtendedStyleNoActivate = 0x08000000;
        private const int HitTestTransparent = -1;
        private const int MouseActivateNoActivate = 3;
        private const int WindowMessageMouseActivate = 0x0021;
        private const int WindowMessageNonClientHitTest = 0x0084;
        private const int SystemMetricVirtualScreenX = 76;
        private const int SystemMetricVirtualScreenY = 77;
        private const int SystemMetricVirtualScreenWidth = 78;
        private const int SystemMetricVirtualScreenHeight = 79;
        private const uint SetWindowPositionNoSize = 0x0001;
        private const uint SetWindowPositionNoActivate = 0x0010;
        private const uint SetWindowPositionShowWindow = 0x0040;
        private const uint SetWindowPositionNoOwnerOrder = 0x0200;
        private static readonly nint TopmostWindow = new(-1);

        private readonly TextBlock _messageText;
        private HwndSource? _source;

        public QuietTipWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = false;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            _messageText = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                MaxWidth = 440,
                TextWrapping = TextWrapping.Wrap,
            };

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(232, 12, 15, 18)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(235, 226, 72, 52)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Child = _messageText,
            };
        }

        public string Message
        {
            get => _messageText.Text;
            set => _messageText.Text = value;
        }

        public void PositionNearCursor()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == 0 || !NativeMethods.GetCursorPos(out var cursor))
            {
                return;
            }

            var transform = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var width = Math.Max(1, checked((int)Math.Ceiling(ActualWidth * transform.M11)));
            var height = Math.Max(1, checked((int)Math.Ceiling(ActualHeight * transform.M22)));

            var virtualLeft = NativeMethods.GetSystemMetrics(SystemMetricVirtualScreenX);
            var virtualTop = NativeMethods.GetSystemMetrics(SystemMetricVirtualScreenY);
            var virtualWidth = Math.Max(1, NativeMethods.GetSystemMetrics(SystemMetricVirtualScreenWidth));
            var virtualHeight = Math.Max(1, NativeMethods.GetSystemMetrics(SystemMetricVirtualScreenHeight));
            var virtualRight = checked(virtualLeft + virtualWidth);
            var virtualBottom = checked(virtualTop + virtualHeight);

            var x = cursor.X + 18;
            var y = cursor.Y + 22;
            if (x + width > virtualRight)
            {
                x = cursor.X - width - 18;
            }

            if (y + height > virtualBottom)
            {
                y = cursor.Y - height - 22;
            }

            x = Math.Clamp(x, virtualLeft, Math.Max(virtualLeft, virtualRight - width));
            y = Math.Clamp(y, virtualTop, Math.Max(virtualTop, virtualBottom - height));

            _ = NativeMethods.SetWindowPos(
                handle,
                TopmostWindow,
                x,
                y,
                0,
                0,
                SetWindowPositionNoSize |
                SetWindowPositionNoActivate |
                SetWindowPositionShowWindow |
                SetWindowPositionNoOwnerOrder);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            var currentStyles = NativeMethods.GetWindowLong(handle, ExtendedWindowStyleIndex);
            _ = NativeMethods.SetWindowLong(
                handle,
                ExtendedWindowStyleIndex,
                currentStyles |
                ExtendedStyleNoActivate |
                ExtendedStyleTransparent |
                ExtendedStyleToolWindow);

            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowProcedure);
        }

        protected override void OnClosed(EventArgs e)
        {
            _source?.RemoveHook(WindowProcedure);
            _source = null;
            base.OnClosed(e);
        }

        private static nint WindowProcedure(
            nint windowHandle,
            int message,
            nint wordParameter,
            nint longParameter,
            ref bool handled)
        {
            switch (message)
            {
                case WindowMessageMouseActivate:
                    handled = true;
                    return MouseActivateNoActivate;
                case WindowMessageNonClientHitTest:
                    handled = true;
                    return HitTestTransparent;
                default:
                    return 0;
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern int GetSystemMetrics(int index);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetCursorPos(out NativePoint point);

            [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
            internal static extern int GetWindowLong(nint windowHandle, int index);

            [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
            internal static extern int SetWindowLong(nint windowHandle, int index, int newValue);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetWindowPos(
                nint windowHandle,
                nint insertAfter,
                int x,
                int y,
                int width,
                int height,
                uint flags);

            [StructLayout(LayoutKind.Sequential)]
            internal struct NativePoint
            {
                internal int X;
                internal int Y;
            }
        }
    }
}

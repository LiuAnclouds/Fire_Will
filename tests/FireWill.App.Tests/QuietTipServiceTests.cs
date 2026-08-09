using System.Runtime.InteropServices;
using System.Windows.Threading;
using FireWill.App.Services.Notifications;

namespace FireWill.App.Tests;

[Collection(Win32InputLifecycleCollection.Name)]
public sealed class QuietTipServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void RepeatedShow_IsClickThrough_AndDisposeLeavesNoWindow()
    {
        using var ready = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        QuietTipService? service = null;
        Dispatcher? dispatcher = null;
        uint dispatcherThreadId = 0;
        var foregroundBefore = GetForegroundWindow();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherThreadId = GetCurrentThreadId();
            service = new QuietTipService(dispatcher);
            ready.Set();
            Dispatcher.Run();
            stopped.Set();
        })
        {
            IsBackground = true,
            Name = "FireWill QuietTip test dispatcher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            Assert.True(ready.Wait(Timeout), "The WPF test dispatcher did not start.");
            Assert.NotNull(service);
            Assert.NotNull(dispatcher);

            service.Show("first", 1_000);
            dispatcher.Invoke(static () => { }, DispatcherPriority.Send);
            service.Show("second", 1_000);
            dispatcher.Invoke(static () => { }, DispatcherPriority.Send);

            var visibleWindows = dispatcher.Invoke<nint[]>(
                () => EnumerateVisibleThreadWindows(dispatcherThreadId),
                DispatcherPriority.Send);
            Assert.Single(visibleWindows);

            var handle = visibleWindows[0];
            var extendedStyle = GetWindowLong(handle, -20);
            Assert.Equal(
                0x08000000 | 0x00000020 | 0x00000080,
                extendedStyle & (0x08000000 | 0x00000020 | 0x00000080));

            Assert.True(GetWindowRect(handle, out var rectangle));
            var virtualLeft = GetSystemMetrics(76);
            var virtualTop = GetSystemMetrics(77);
            var virtualRight = virtualLeft + Math.Max(1, GetSystemMetrics(78));
            var virtualBottom = virtualTop + Math.Max(1, GetSystemMetrics(79));
            Assert.InRange(rectangle.Left, virtualLeft, virtualRight - 1);
            Assert.InRange(rectangle.Top, virtualTop, virtualBottom - 1);
            Assert.InRange(rectangle.Right, rectangle.Left + 1, virtualRight);
            Assert.InRange(rectangle.Bottom, rectangle.Top + 1, virtualBottom);

            if (foregroundBefore != 0)
            {
                Assert.Equal(foregroundBefore, GetForegroundWindow());
            }

            service.Hide();
            dispatcher.Invoke(static () => { }, DispatcherPriority.Send);
            Assert.Empty(dispatcher.Invoke<nint[]>(
                () => EnumerateVisibleThreadWindows(dispatcherThreadId),
                DispatcherPriority.Send));

            service.Dispose();
            dispatcher.InvokeShutdown();
            Assert.True(stopped.Wait(Timeout), "The WPF test dispatcher did not stop.");
            Assert.False(thread.IsAlive);

            // Dispose is intentionally idempotent, including after dispatcher shutdown.
            service.Dispose();
        }
        finally
        {
            if (service is not null)
            {
                try
                {
                    service.Dispose();
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.InvokeShutdown();
            }

            if (thread.IsAlive)
            {
                thread.Join(Timeout);
            }
        }
    }

    private static nint[] EnumerateVisibleThreadWindows(uint threadId)
    {
        var handles = new List<nint>();
        EnumThreadWindows(
            threadId,
            (windowHandle, _) =>
            {
                if (IsWindowVisible(windowHandle))
                {
                    handles.Add(windowHandle);
                }

                return true;
            },
            0);
        return handles.ToArray();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumThreadWindowsProc(nint windowHandle, nint parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(
        uint threadId,
        EnumThreadWindowsProc callback,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

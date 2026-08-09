using System.Diagnostics;
using System.IO;
using System.Text;
using FireWill.App.Interop;

namespace FireWill.App.Services.Input;

public readonly record struct ScreenRectangle(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool Contains(ScreenPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public ScreenPoint PointFromClient(int clientX, int clientY) =>
        new(checked(X + clientX), checked(Y + clientY));
}

public sealed record War3WindowBinding(
    nint WindowHandle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle,
    string WindowClass,
    ScreenRectangle ClientBounds);

public sealed class War3WindowService
{
    private static readonly string[] DefaultProcessNames =
        ["War3", "Warcraft III", "Warcraft III Launcher"];

    private readonly object bindingLock = new();
    private readonly HashSet<string> processNames;
    private War3WindowBinding? binding;

    public War3WindowService(IEnumerable<string>? acceptedProcessNames = null)
    {
        processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in acceptedProcessNames ?? DefaultProcessNames)
        {
            var normalized = NormalizeProcessName(processName);
            if (normalized.Length != 0)
            {
                processNames.Add(normalized);
            }
        }

        if (processNames.Count == 0)
        {
            throw new ArgumentException("At least one Warcraft III process name is required.", nameof(acceptedProcessNames));
        }
    }

    public IReadOnlyCollection<string> AcceptedProcessNames => processNames;

    public bool IsBound
    {
        get
        {
            lock (bindingLock)
            {
                return binding is not null && IsSameWindowAndProcess(binding);
            }
        }
    }

    public bool IsBoundWindowForeground
    {
        get
        {
            War3WindowBinding? current;
            lock (bindingLock)
            {
                current = binding;
            }

            return current is not null &&
                NativeMethods.GetForegroundWindow() == current.WindowHandle &&
                IsSameWindowAndProcess(current);
        }
    }

    public bool TryBindForeground(out War3WindowBinding result)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        return TryBindWindow(foregroundWindow, out result);
    }

    public bool TryBindForegroundWindow(uint excludedProcessId, out War3WindowBinding result)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (!TryReadWindow(foregroundWindow, requireAcceptedProcess: false, out result) ||
            !IsManualBindingCandidate(result, excludedProcessId))
        {
            result = null!;
            return false;
        }

        lock (bindingLock)
        {
            binding = result;
            return true;
        }
    }

    public bool TryGetForegroundWindowInfo(out War3WindowBinding result) =>
        TryReadWindow(NativeMethods.GetForegroundWindow(), requireAcceptedProcess: false, out result);

    public bool TryFindAndBind(out War3WindowBinding result)
    {
        if (TryBindForeground(out result))
        {
            return true;
        }

        War3WindowBinding? bestCandidate = null;
        NativeMethods.EnumWindowsProc callback = (windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle) ||
                !TryReadWindow(windowHandle, requireAcceptedProcess: true, out var candidate) ||
                candidate.ClientBounds.Width <= 0 || candidate.ClientBounds.Height <= 0)
            {
                return true;
            }

            var candidateArea = (long)candidate.ClientBounds.Width * candidate.ClientBounds.Height;
            var bestArea = bestCandidate is null
                ? -1
                : (long)bestCandidate.ClientBounds.Width * bestCandidate.ClientBounds.Height;
            if (candidateArea > bestArea)
            {
                bestCandidate = candidate;
            }

            return true;
        };

        _ = NativeMethods.EnumWindows(callback, 0);
        GC.KeepAlive(callback);

        if (bestCandidate is null)
        {
            result = null!;
            return false;
        }

        lock (bindingLock)
        {
            binding = bestCandidate;
            result = bestCandidate;
            return true;
        }
    }

    public bool TryBindWindow(nint windowHandle, out War3WindowBinding result)
    {
        if (!TryReadWindow(windowHandle, requireAcceptedProcess: true, out result))
        {
            return false;
        }

        lock (bindingLock)
        {
            binding = result;
            return true;
        }
    }

    public bool TryGetBinding(out War3WindowBinding result)
    {
        War3WindowBinding? current;
        lock (bindingLock)
        {
            current = binding;
        }

        if (current is null || !IsSameWindowAndProcess(current) ||
            !TryReadWindow(current.WindowHandle, requireAcceptedProcess: false, out var refreshed) ||
            refreshed.ProcessId != current.ProcessId)
        {
            result = null!;
            return false;
        }

        lock (bindingLock)
        {
            if (binding?.WindowHandle == current.WindowHandle && binding.ProcessId == current.ProcessId)
            {
                binding = refreshed;
            }

            result = refreshed;
            return true;
        }
    }

    public bool TryGetClientPointOnScreen(int clientX, int clientY, out ScreenPoint point)
    {
        if (!TryGetBinding(out var current) ||
            clientX < 0 || clientX >= current.ClientBounds.Width ||
            clientY < 0 || clientY >= current.ClientBounds.Height)
        {
            point = default;
            return false;
        }

        point = current.ClientBounds.PointFromClient(clientX, clientY);
        return true;
    }

    public void ClearBinding()
    {
        lock (bindingLock)
        {
            binding = null;
        }
    }

    internal static bool IsManualBindingCandidate(
        War3WindowBinding candidate,
        uint excludedProcessId)
    {
        if (candidate.WindowHandle == 0 || candidate.ProcessId == 0 ||
            candidate.ProcessId == excludedProcessId)
        {
            return false;
        }

        var processName = candidate.ProcessName;
        return !processName.Equals("Fire Will", StringComparison.OrdinalIgnoreCase) &&
            !processName.Equals("war3_macro_gui", StringComparison.OrdinalIgnoreCase) &&
            !processName.Equals("AutoHotkey", StringComparison.OrdinalIgnoreCase) &&
            !processName.Equals("AutoHotkey64", StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrWhiteSpace(candidate.WindowTitle) ||
                !string.IsNullOrWhiteSpace(processName) ||
                !string.IsNullOrWhiteSpace(candidate.WindowClass));
    }

    private bool TryReadWindow(
        nint windowHandle,
        bool requireAcceptedProcess,
        out War3WindowBinding result)
    {
        result = null!;
        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle) ||
            NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId) == 0 ||
            processId == 0 ||
            !TryGetClientBounds(windowHandle, out var clientBounds))
        {
            return false;
        }

        var processName = GetProcessName(processId);
        if (requireAcceptedProcess && !processNames.Contains(processName))
        {
            return false;
        }

        result = new War3WindowBinding(
            windowHandle,
            processId,
            processName,
            GetWindowTitle(windowHandle),
            GetWindowClass(windowHandle),
            clientBounds);
        return true;
    }

    private static bool IsSameWindowAndProcess(War3WindowBinding candidate) =>
        NativeMethods.IsWindow(candidate.WindowHandle) &&
        NativeMethods.GetWindowThreadProcessId(candidate.WindowHandle, out var currentProcessId) != 0 &&
        currentProcessId == candidate.ProcessId;

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return NormalizeProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static bool TryGetClientBounds(nint windowHandle, out ScreenRectangle bounds)
    {
        bounds = default;
        if (!NativeMethods.GetClientRect(windowHandle, out var clientRectangle))
        {
            return false;
        }

        var topLeft = new NativeMethods.NativePoint(clientRectangle.Left, clientRectangle.Top);
        if (!NativeMethods.ClientToScreen(windowHandle, ref topLeft))
        {
            return false;
        }

        var width = clientRectangle.Right - clientRectangle.Left;
        var height = clientRectangle.Bottom - clientRectangle.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bounds = new ScreenRectangle(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    private static string GetWindowTitle(nint windowHandle)
    {
        var length = NativeMethods.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(windowHandle, title, title.Capacity) > 0
            ? title.ToString()
            : string.Empty;
    }

    private static string GetWindowClass(nint windowHandle)
    {
        var className = new StringBuilder(256);
        return NativeMethods.GetClassName(windowHandle, className, className.Capacity) > 0
            ? className.ToString()
            : string.Empty;
    }

    private static string NormalizeProcessName(string processName) =>
        Path.GetFileNameWithoutExtension(processName.Trim());
}

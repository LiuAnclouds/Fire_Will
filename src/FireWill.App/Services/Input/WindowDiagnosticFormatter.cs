namespace FireWill.App.Services.Input;

public static class WindowDiagnosticFormatter
{
    public static string Format(War3WindowBinding window) =>
        string.Join(
            Environment.NewLine,
            $"HWND: {window.WindowHandle}",
            $"Title: {window.WindowTitle}",
            $"Exe: {FormatExecutableName(window.ProcessName)}",
            $"Class: {window.WindowClass}",
            $"PID: {window.ProcessId}");

    private static string FormatExecutableName(string processName) =>
        string.IsNullOrWhiteSpace(processName) ? string.Empty : $"{processName}.exe";
}

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace FireWill.App;

public partial class App : Application
{
    private const string MutexName = "Local\\FireWill.Wpf.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Fire Will 已经在运行。",
                "Fire Will",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // This process never acquired the mutex.
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireWill",
            "logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "crash.log");
        var message = new StringBuilder()
            .AppendLine($"[{DateTimeOffset.Now:O}]")
            .AppendLine(e.Exception.ToString())
            .AppendLine()
            .ToString();
        File.AppendAllText(logPath, message, Encoding.UTF8);

        MessageBox.Show(
            $"程序发生未处理错误，日志已写入：\n{logPath}\n\n{e.Exception.Message}",
            "Fire Will",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}

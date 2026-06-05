using System.IO;
using System.Windows;

namespace FluxBrowser;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FluxBrowser", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("Unhandled: " + args.ExceptionObject);
        DispatcherUnhandledException += (_, args) =>
        { Log("Dispatcher: " + args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        { Log("Task: " + args.Exception); args.SetObserved(); };

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        base.OnStartup(e);
    }

    private static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }
}

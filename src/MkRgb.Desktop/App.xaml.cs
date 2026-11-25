using System.IO;
using System.Windows;

namespace MkRgb.Desktop;

public partial class App : System.Windows.Application
{
    /// <summary>"0.1.3" style version from the build (Directory.Build.props), shown in the UI.</summary>
    public static string VersionLabel { get; } = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";
    Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(false, @"Local\MkRgb.Desktop.v1");
        var acquired = false;
        try
        {
            acquired = instance.WaitOne(0);
        }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            System.Windows.MessageBox.Show("MK RGB is already running. Open it from the system tray.", "MK RGB");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, a) => { System.Windows.MessageBox.Show(a.Exception.Message, "MK RGB", MessageBoxButton.OK, MessageBoxImage.Error); a.Handled = true; };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            instance?.ReleaseMutex();
        }
        catch (ApplicationException) { }
        instance?.Dispose();
        base.OnExit(e);
    }
}

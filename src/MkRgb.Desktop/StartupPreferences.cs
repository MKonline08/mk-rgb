using Microsoft.Win32;
using System.IO;

namespace MkRgb.Desktop;

public static class StartupPreferences
{
    public static string Command(string executable, bool background) =>
        "\"" + Path.GetFullPath(executable) + "\"" + (background ? " --background" : "");

    public static void Apply(bool enabled, bool background)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("MK RGB", Command(Environment.ProcessPath!, background));
        else key.DeleteValue("MK RGB", false);
    }
}

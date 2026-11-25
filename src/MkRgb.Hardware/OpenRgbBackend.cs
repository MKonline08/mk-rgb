using System.Diagnostics;
using System.Net.Sockets;

namespace MkRgb.Hardware;

/// <summary>Owns only the bundled process; readiness never issues lighting commands.</summary>
public sealed class OpenRgbBackend(string executable, string configuration) : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1);
    Process? process;

    /// <summary>Starts the bundled SDK server. Returns warnings that may explain missing devices; they never block startup.</summary>
    public async Task<List<string>> StartAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            List<string> warnings = [];
            // Other RGB apps can hold USB devices exclusively, which hides them from OpenRGB.
            // Warn instead of refusing to start: most devices still work, and fan-only tools never conflict.
            var conflicts = Sensors.Conflicts().Where(name => !name.Equals("FanControl", StringComparison.OrdinalIgnoreCase)).ToList();
            if (conflicts.Count > 0)
                warnings.Add("Other RGB software is running (" + string.Join(", ", conflicts) + "). It can hide devices from MK RGB; close it and press Find devices again if something is missing.");
            if (!PawnIoInstalled())
                warnings.Add("The PawnIO driver is not installed, so RAM and some motherboard RGB cannot be detected. Install it from pawnio.eu, then restart your PC.");
            if (await Listening(token)) return warnings;
            if (process is null || process.HasExited)
            {
                process?.Dispose();
                if (Process.GetProcessesByName("OpenRGB").Any())
                    throw new InvalidOperationException("OpenRGB is already running without an SDK server on 127.0.0.1:6743. Enable its SDK server on that port or close it and reconnect.");
                if (!File.Exists(executable))
                    throw new FileNotFoundException("Bundled OpenRGB is missing. Repair the MK RGB installation.", executable);
                // OpenRGB rejects --config unless this directory already exists.
                Directory.CreateDirectory(configuration);
                var info = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
                };
                foreach (var arg in new[] { "--server", "--server-host", "127.0.0.1", "--server-port", "6743", "--noautoconnect", "--config", configuration })
                    info.ArgumentList.Add(arg);
                process = Process.Start(info) ?? throw new IOException("OpenRGB could not start.");
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                while (!await Listening(deadline.Token))
                {
                    if (process.HasExited)
                        throw new IOException($"OpenRGB exited before its SDK server was ready (exit code {process.ExitCode}). Check {configuration} and repair the installation if backend files are missing.");
                    await Task.Delay(250, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("OpenRGB is still scanning hardware after 2 minutes. Press Find devices again; close other RGB software if this keeps happening.");
            }
            return warnings;
        }
        finally { gate.Release(); }
    }

    /// <summary>True when PawnIO is installed: its driver service or its uninstall entry is registered.</summary>
    public static bool PawnIoInstalled()
    {
        if (DriverInstalled("PawnIO"))
            return true;
        try
        {
            using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var entry = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return entry != null;
        }
        catch (Exception) { return true; }
    }
    /// <summary>True when a kernel driver service with this name is registered.</summary>
    public static bool DriverInstalled(string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            return key != null;
        }
        catch (Exception) { return true; } // Unknown: don't show a misleading warning.
    }

    static async Task<bool> Listening(CancellationToken token)
    {
        using var client = new TcpClient();
        try { await client.ConnectAsync("127.0.0.1", 6743, token); return true; }
        catch (SocketException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(true);
                await process.WaitForExitAsync();
            }
            process?.Dispose();
            process = null;
        }
        finally { gate.Release(); }
    }
}

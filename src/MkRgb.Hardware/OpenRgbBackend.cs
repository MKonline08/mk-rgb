using System.Diagnostics;
using System.Net.Sockets;

namespace MkRgb.Hardware;

/// <summary>Owns only the bundled process; readiness never issues lighting commands.</summary>
public sealed class OpenRgbBackend(string executable, string configuration) : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1);
    Process? process;

    public async Task StartAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (await Listening(token)) return;
            if (process is null || process.HasExited)
            {
                process?.Dispose();
                var conflicts = Sensors.Conflicts();
                if (conflicts.Count > 0)
                    throw new InvalidOperationException("Close competing hardware controllers first: " + string.Join(", ", conflicts));
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
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
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
                throw new TimeoutException("OpenRGB is still scanning hardware after 60 seconds. Retry Connect RGB; check for competing controllers or unavailable device drivers.");
            }
        }
        finally { gate.Release(); }
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

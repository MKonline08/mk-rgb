using LibreHardwareMonitor.Hardware;
using MkRgb.Core;
using System.Diagnostics;

namespace MkRgb.Hardware;

public sealed class Sensors : ISensorProvider, IFanController, IDisposable
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsMemoryEnabled = true, IsControllerEnabled = true, IsStorageEnabled = true };
    readonly Dictionary<string, IControl> controls = []; readonly Dictionary<string, IControl> owned = [];
    readonly object sync = new(); bool opened; string? openError;
    public Sensors()
    {
        try
        {
            computer.Open();
            opened = true;
        }
        catch (Exception e) { openError = e.Message; }
    }
    public HardwareSnapshot Read()
    {
        lock (sync)
        {
            List<Device> devices = [];
            List<SensorSample> sensors = [];
            List<FanChannel> fans = [];
            List<string> warnings = [];
            var now = DateTimeOffset.UtcNow;
            controls.Clear();
            if (openError != null)
                warnings.Add("Hardware access: " + openError);
            void Visit(IHardware h)
            {
                try
                {
                    h.Update();
                    var id = h.Identifier.ToString();
                    devices.Add(new(id, h.Name, h.HardwareType.ToString(), Capability.Monitor, 0, [], "Monitoring", false));
                    foreach (var s in h.Sensors)
                    {
                        var (kind, unit) = s.SensorType switch
                        {
                            SensorType.Temperature => (SensorKind.Temperature, "°C"),
                            SensorType.Fan => (SensorKind.Fan, "RPM"),
                            SensorType.Load => (SensorKind.Load, "%"),
                            SensorType.Clock => (SensorKind.Clock, "MHz"),
                            SensorType.Power => (SensorKind.Power, "W"),
                            SensorType.Voltage => (SensorKind.Voltage, "V"),
                            SensorType.Control => (SensorKind.Control, "%"),
                            _ => (SensorKind.Other, s.SensorType.ToString())
                        };
                        double? reading = s.Value;
                        if (kind == SensorKind.Temperature && (reading <= 0 || reading > 150))
                            reading = null;
                        sensors.Add(new(s.Identifier.ToString(), s.Name, kind, reading, unit, now, id));
                        if (s.Control is { } control)
                        {
                            var cid = s.Identifier.ToString();
                            controls[cid] = control;
                            fans.Add(new(cid, s.Name, id, control.MinSoftwareValue, control.MaxSoftwareValue, true, true, s.Name.Contains("pump", StringComparison.OrdinalIgnoreCase), "Upstream control exposed; local commissioning required"));
                        }
                    }
                    foreach (var sub in h.SubHardware)
                        Visit(sub);
                }
                catch (Exception e) { warnings.Add(h.Name + ": " + e.Message); }
            }
            if (opened)
                foreach (var h in computer.Hardware)
                    Visit(h);
            if (!sensors.Any(s => s.Kind == SensorKind.Temperature))
                warnings.Add("No temperature sensors available. Install/start the MK RGB hardware service; driver access may be unavailable.");
            return new(devices, sensors, fans, warnings, now);
        }
    }
    public void SetDuty(string id, double duty)
    {
        lock (sync)
        {
            if (!controls.TryGetValue(id, out var c))
                throw new InvalidOperationException("Control channel disconnected.");
            if (!double.IsFinite(duty) || duty < c.MinSoftwareValue || duty > c.MaxSoftwareValue)
                throw new InvalidDataException("Duty is outside the device's supported range.");
            owned[id] = c;
            c.SetSoftware((float)duty);
        }
    }
    public void Restore(string id)
    {
        lock (sync)
        {
            if (owned.TryGetValue(id, out var c))
            {
                c.SetDefault();
                owned.Remove(id);
            }
        }
    }
    public void RestoreAll()
    {
        lock (sync)
        {
            foreach (var id in owned.Keys.ToList())
                try
                {
                    Restore(id);
                }
                catch { /* Attempt every owned channel even if one is disconnected. */ }
        }
    }
    public void Dispose()
    {
        lock (sync)
        {
            RestoreAll();
            computer.Close();
        }
    }
    public static List<string> Conflicts()
    {
        string[] names = ["SignalRgb", "SignalRGB", "iCUE", "LightingService", "Mystic_Light_Service", "RGBFusion", "FanControl"];
        return Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } finally { p.Dispose(); } }).Where(p => names.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

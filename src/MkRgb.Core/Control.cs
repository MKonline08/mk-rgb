namespace MkRgb.Core;

public sealed class CoolingEngine
{
    readonly Dictionary<string, (double Temp, double Duty, DateTimeOffset At)> state = [];
    public static double Interpolate(IReadOnlyList<CurvePoint> points, double temperature)
    {
        if (points.Count < 2 || points.Any(p => !double.IsFinite(p.Temperature) || !double.IsFinite(p.Duty) || p.Duty < 0 || p.Duty > 100))
            throw new InvalidDataException("A curve needs at least two valid points with duty between 0 and 100.");
        var p = points.OrderBy(p => p.Temperature).ToArray();
        if (p.Select(p => p.Temperature).Distinct().Count() != p.Length)
            throw new InvalidDataException("Curve temperatures must be unique.");
        if (temperature <= p[0].Temperature)
            return p[0].Duty;
        for (var i = 1; i < p.Length; i++)
            if (temperature <= p[i].Temperature)
                return p[i - 1].Duty + (p[i].Duty - p[i - 1].Duty) * (temperature - p[i - 1].Temperature) / (p[i].Temperature - p[i - 1].Temperature);
        return p[^1].Duty;
    }
    public double Evaluate(FanPolicy p, IReadOnlyList<SensorSample> sensors, DateTimeOffset now)
    {
        var readings = p.Sources.Select(s => sensors.FirstOrDefault(r => r.Id == s.SensorId)).ToArray();
        // Manual operation also needs mapped temperature guards. Stale data bypasses all ramp limits.
        if (readings.Length == 0 || readings.Any(s => s is null || !s.IsFresh(now)))
            return Remember(100, 100);
        var temp = readings.Max(s => s!.Value!.Value);
        var curve = p.Sources.Select((s, i) => Interpolate(s.Points, readings[i]!.Value!.Value)).ToArray();
        if (curve.Any(d => d >= 100))
            return Remember(temp, 100);
        var target = p.Manual ? Math.Clamp(p.ManualDuty, 0, 100) : p.MixTemperatures ? Interpolate(p.Sources[0].Points, p.Mix == MixMode.Maximum ? temp : readings.Average(s => s!.Value!.Value)) : p.Mix == MixMode.Maximum ? curve.Max() : curve.Average();
        if (p.Pump || !p.AllowStop || !p.Calibrated || target > 0)
            target = Math.Max(p.Minimum, target);
        var stopping = target == 0;
        if (state.TryGetValue(p.ChannelId, out var old))
        {
            if (target < old.Duty && Math.Abs(temp - old.Temp) < p.Hysteresis)
                target = old.Duty;
            var elapsed = Math.Clamp((now - old.At).TotalSeconds, 0, 5);
            target = Math.Clamp(target, old.Duty - Math.Max(0, p.FallPerSecond) * elapsed, old.Duty + Math.Max(0, p.RisePerSecond) * elapsed);
            if (old.Duty == 0 && target > 0)
                target = Math.Max(p.StartDuty, target);
        }
        else if (target > 0)
            target = Math.Max(p.StartDuty, target);
        if (target > 0 && target < p.Minimum)
            target = stopping ? 0 : p.Minimum;
        return Remember(temp, Math.Clamp(target, 0, 100));
        double Remember(double t, double duty)
        {
            state[p.ChannelId] = (t, duty, now);
            return duty;
        }
    }
    public static List<CurvePoint> Preset(string name) => name switch
    {
        "Quiet" => [new(30, 30), new(55, 35), new(70, 65), new(85, 100)],
        "Gaming" => [new(30, 40), new(50, 55), new(70, 85), new(85, 100)],
        "Maximum Cooling" => [new(20, 100), new(85, 100)],
        _ => [new(30, 30), new(50, 45), new(70, 75), new(85, 100)]
    };
}
public record AutomationContext(DateTimeOffset Now, HashSet<string> Processes, Dictionary<string, double> Sensors, double Battery, bool AcPower, Dictionary<string, double> Events);
public static class Automations
{
    public static string? Select(IEnumerable<AutomationRule> rules, AutomationContext c, DateTimeOffset? manualUntil = null)
    {
        if (manualUntil > c.Now)
            return null;
        return rules.Where(r => r.Enabled && Match(r, c)).OrderByDescending(r => r.Priority).ThenBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault()?.ProfileId;
    }
    public static bool Match(AutomationRule r, AutomationContext c) => r.Trigger switch
    {
        TriggerKind.Application => c.Processes.Contains(Path.GetFileNameWithoutExtension(r.Source)),
        TriggerKind.Temperature => c.Sensors.TryGetValue(r.Source, out var t) && t >= r.Threshold,
        TriggerKind.Battery => c.Battery >= 0 && c.Battery <= r.Threshold,
        TriggerKind.AcPower => c.AcPower == (r.Threshold != 0),
        TriggerKind.Event => c.Events.TryGetValue(r.Source, out var e) && e >= r.Threshold,
        TriggerKind.Schedule => InTime(r.Start, r.End, c.Now.LocalDateTime.TimeOfDay),
        _ => false
    };
    static bool InTime(string start, string end, TimeSpan now)
    {
        if (!TimeSpan.TryParse(start, out var a) || !TimeSpan.TryParse(end, out var b))
            return false;
        return a <= b ? now >= a && now < b : now >= a || now < b;
    }
}
public sealed class UndoHistory<T>
{
    readonly Stack<string> undo = []; readonly Stack<string> redo = [];
    public void Clear() { undo.Clear(); redo.Clear(); }
    public void Record(T value)
    {
        undo.Push(Json.Write(value));
        redo.Clear();
    }
    public T Undo(T current)
    {
        if (undo.Count == 0)
            return current;
        redo.Push(Json.Write(current));
        return Json.Read<T>(undo.Pop());
    }
    public T Redo(T current)
    {
        if (redo.Count == 0)
            return current;
        undo.Push(Json.Write(current));
        return Json.Read<T>(redo.Pop());
    }
}

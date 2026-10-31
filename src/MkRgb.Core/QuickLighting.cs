namespace MkRgb.Core;

/// <summary>Everyday lighting changes affect whole selected devices, including formerly painted LEDs.</summary>
public static class QuickLighting
{
    public static readonly EffectKind[] Kinds = [EffectKind.Static, EffectKind.Breathing, EffectKind.Rainbow, EffectKind.Wave];
    public static int Apply(Profile profile, IEnumerable<string> ids, EffectKind kind, string color, double brightness, double speed)
    {
        if (!Kinds.Contains(kind) || !double.IsFinite(brightness) || brightness is < 0 or > 1 || !double.IsFinite(speed) || speed is < .05 or > 2)
            throw new InvalidDataException("Choose a supported effect, brightness, and speed.");
        var hex = Rgb.Parse(color).Hex;
        var targets = ids.ToHashSet();
        var placements = profile.Layout.Where(d => targets.Contains(d.DeviceId)).ToList();
        foreach (var d in placements)
        {
            d.LedOverrides.Clear();
            d.Lighting = new() { Name = "Quick lighting", Kind = kind, Color = hex, Color2 = Rgb.Parse(hex).Scale(.08).Hex, Brightness = brightness, Speed = speed };
        }
        return placements.Count;
    }

    public static int ComponentLeds(int ledsPerComponent, int quantity, Zone zone)
    {
        if (!zone.Resizable || ledsPerComponent < 1 || quantity < 1 || quantity > 100)
            throw new InvalidDataException("Choose the connected component and quantity.");
        var total = checked(ledsPerComponent * quantity);
        if (total < zone.Minimum || total > Math.Min(10000, zone.Maximum))
            throw new InvalidDataException($"This header supports {zone.Minimum}–{Math.Min(10000, zone.Maximum)} LEDs in total.");
        return total;
    }
}

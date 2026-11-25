using MkRgb.Core;

namespace MkRgb.Hardware;

public static class RgbSetup
{
    public static bool NeedsSetup(string device, Zone zone, Dictionary<string, Dictionary<string, int>> saved) =>
        zone.Resizable && zone.Count == 0 && !(saved.TryGetValue(device, out var counts) && counts.TryGetValue(zone.Id, out var count) && count == 0);
    /// <summary>Re-applies saved header sizes. A header that fails is reported in <paramref name="problems"/> instead of aborting discovery.</summary>
    public static async Task<List<Device>> RestoreAsync(IResizableRgbController controller, Dictionary<string, Dictionary<string, int>> saved, CancellationToken token, List<string>? problems = null)
    {
        var devices = await controller.DiscoverAsync(token);
        foreach (var device in saved)
            foreach (var setting in device.Value)
            {
                var zone = devices.FirstOrDefault(d => d.Id == device.Key)?.Zones.FirstOrDefault(z => z.Id == setting.Key);
                // Keep saved disconnected hardware; never apply old values to a different controller or fixed zone.
                if (zone is null || !zone.Resizable || zone.Count == setting.Value || setting.Value < zone.Minimum || setting.Value > Math.Min(10000, zone.Maximum)) continue;
                var name = devices.First(d => d.Id == device.Key).Name + " / " + zone.Name;
                try
                {
                    await controller.ResizeAsync(device.Key, int.Parse(setting.Key), setting.Value, token);
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                    problems?.Add($"{name}: saved LED count could not be restored ({e.Message}).");
                }
                devices = await controller.DiscoverAsync(token);
                if (devices.FirstOrDefault(d => d.Id == device.Key)?.Zones.FirstOrDefault(z => z.Id == setting.Key)?.Count != setting.Value)
                    problems?.Add($"{name}: the controller did not keep the saved LED count. Set it up again in Devices.");
            }
        return devices;
    }
}

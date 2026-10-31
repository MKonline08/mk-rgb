using MkRgb.Core;

namespace MkRgb.Hardware;

public static class RgbSetup
{
    public static bool NeedsSetup(string device, Zone zone, Dictionary<string, Dictionary<string, int>> saved) =>
        zone.Resizable && zone.Count == 0 && !(saved.TryGetValue(device, out var counts) && counts.TryGetValue(zone.Id, out var count) && count == 0);
    public static async Task<List<Device>> RestoreAsync(IResizableRgbController controller, Dictionary<string, Dictionary<string, int>> saved, CancellationToken token)
    {
        var devices = await controller.DiscoverAsync(token);
        foreach (var device in saved)
            foreach (var setting in device.Value)
            {
                var zone = devices.FirstOrDefault(d => d.Id == device.Key)?.Zones.FirstOrDefault(z => z.Id == setting.Key);
                // Keep saved disconnected hardware; never apply old values to a different controller or fixed zone.
                if (zone is null || !zone.Resizable || zone.Count == setting.Value || setting.Value < zone.Minimum || setting.Value > Math.Min(10000, zone.Maximum)) continue;
                await controller.ResizeAsync(device.Key, int.Parse(setting.Key), setting.Value, token);
                devices = await controller.DiscoverAsync(token);
                if (devices.FirstOrDefault(d => d.Id == device.Key)?.Zones.FirstOrDefault(z => z.Id == setting.Key)?.Count != setting.Value)
                    throw new IOException("The RGB controller did not accept the saved component count. Open Devices to configure it again.");
            }
        return devices;
    }
}

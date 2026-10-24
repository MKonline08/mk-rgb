using System.Reflection;
using System.Runtime.Loader;
using MkRgb.Core;

namespace MkRgb.Hardware;

/// <summary>Only administrator-installed code beneath the application's protected directory is loaded.</summary>
public sealed class Extensions : IDisposable
{
    public List<ISensorProvider> SensorProviders { get; } = [];
    public List<IFanController> FanControllers { get; } = [];
    public List<IRgbController> RgbControllers { get; } = [];
    public List<IEventSource> EventSources { get; } = [];
    public List<string> Errors { get; } = [];
    readonly List<object> instances = [];
    public Extensions(string applicationDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(applicationDirectory, "extensions"));
        if (!Directory.Exists(root))
            return;
        foreach (var file in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = Json.Read<ExtensionManifest>(File.ReadAllText(file));
                if (Path.GetFileName(manifest.Assembly) != manifest.Assembly)
                    throw new InvalidDataException("Extension assembly must be a file in its manifest directory.");
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, manifest.Assembly));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Extension escapes installation directory.");
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                foreach (var t in assembly.GetExportedTypes().Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
                {
                    if (!typeof(ISensorProvider).IsAssignableFrom(t) && !typeof(IFanController).IsAssignableFrom(t) && !typeof(IRgbController).IsAssignableFrom(t) && !typeof(IEventSource).IsAssignableFrom(t))
                        continue;
                    var instance = Activator.CreateInstance(t)!;
                    instances.Add(instance);
                    if (instance is ISensorProvider s && manifest.Capabilities.Contains("sensors"))
                        SensorProviders.Add(s);
                    if (instance is IFanController f && manifest.Capabilities.Contains("fans"))
                        FanControllers.Add(f);
                    if (instance is IRgbController r && manifest.Capabilities.Contains("rgb"))
                        RgbControllers.Add(r);
                    if (instance is IEventSource events && manifest.Capabilities.Contains("events"))
                        EventSources.Add(events);
                }
            }
            catch (Exception e) { Errors.Add(Path.GetFileName(Path.GetDirectoryName(file)) + ": " + e.Message); }
        }
    }
    public void Dispose()
    {
        foreach (var instance in instances.OfType<IDisposable>())
            instance.Dispose();
    }
}

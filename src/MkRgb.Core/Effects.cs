namespace MkRgb.Core;

public static class Effects
{
    public static readonly string[] NodeTypes = ["Color", "Gradient", "Time", "Noise", "Audio", "Sensor", "Event", "Colorize", "Transform", "Mask", "Blend"];
    public static bool IsScalar(string type) => type is "Time" or "Noise" or "Audio" or "Sensor" or "Event";
    public static Rgb[] Render(Profile profile, Placement device, EffectContext context)
    {
        var colors = new Rgb[Math.Clamp(device.LedCount, 1, 10000)];
        for (var i = 0; i < colors.Length; i++)
        {
            var local = colors.Length == 1 ? 0.5 : i / (double)(colors.Length - 1);
            var radians = device.Rotation * Math.PI / 180;
            var x = (device.X + Math.Cos(radians) * local * device.Width) / 1000;
            var y = (device.Y + Math.Sin(radians) * local * device.Width) / 600;
            var color = Rgb.Black;
            foreach (var layer in profile.Layers.Where(l => l.Group == "All" || l.Group == device.Group))
            {
                if (local < layer.MaskStart || local > layer.MaskEnd)
                    continue;
                var next = Evaluate(layer, x, y, context).Scale(Math.Clamp(layer.Brightness, 0, 1));
                color = Blend(color, next, layer.Blend, Math.Clamp(layer.Opacity, 0, 1));
            }
            if (device.LedOverrides.TryGetValue(i, out var fixedColor))
                color = Rgb.Parse(fixedColor);
            colors[i] = Order(color, device.ColorOrder);
        }
        return colors;
    }
    public static Rgb Evaluate(EffectLayer e, double x, double y, EffectContext c)
    {
        var a = Rgb.Parse(e.Color);
        var b = Rgb.Parse(e.Color2);
        var t = c.Time * e.Speed;
        var axis = x * Math.Cos(e.Direction * Math.PI / 180) + y * Math.Sin(e.Direction * Math.PI / 180);
        var wave = (Math.Sin((axis - t) * Math.PI * 2) + 1) / 2;
        var sensor = c.Sensors.GetValueOrDefault(e.SensorId, double.NaN);
        return e.Kind switch
        {
            EffectKind.Static => a,
            EffectKind.Breathing => a.Scale(0.1 + 0.9 * (Math.Sin(t * Math.PI * 2) + 1) / 2),
            EffectKind.Pulse => a.Scale(Math.Pow((Math.Sin(t * Math.PI * 2) + 1) / 2, 5)),
            EffectKind.Rainbow => Rgb.Hsv(axis - t),
            EffectKind.Wave => Rgb.Lerp(a, b, wave),
            EffectKind.Chase => a.Scale(((axis - t) % 1 + 1) % 1 < 0.2 ? 1 : 0.04),
            EffectKind.Gradient => Rgb.Lerp(a, b, (Math.Sin(axis * Math.PI) + 1) / 2),
            EffectKind.Sparkle => a.Scale(Noise(Math.Floor(x * 200), Math.Floor(t * 20)) > 0.92 ? 1 : 0.03),
            EffectKind.Ripple => Rgb.Lerp(a, b, (Math.Sin((Math.Sqrt(Math.Pow(x - .5, 2) + Math.Pow(y - .5, 2)) - t) * 24) + 1) / 2),
            EffectKind.Temperature => double.IsFinite(sensor) ? Rgb.Lerp(a, b, (sensor - 25) / 60) : Rgb.Black,
            EffectKind.FanSpeed => double.IsFinite(sensor) ? Rgb.Lerp(a, b, sensor / 2000) : Rgb.Black,
            EffectKind.Audio => a.Scale(c.Audio.Length == 0 ? 0 : c.Audio[Math.Clamp((int)(Math.Abs(x) % 1 * c.Audio.Length), 0, c.Audio.Length - 1)]),
            EffectKind.Screen => c.Screen,
            EffectKind.Game => Rgb.Lerp(a, b, Math.Clamp(c.Events.GetValueOrDefault(e.SensorId, "health" == e.SensorId ? 100 : 0) / 100, 0, 1)),
            EffectKind.Timeline => Timeline(e.Keyframes, c.Time),
            EffectKind.Graph => Graph(e, x, y, c),
            _ => a
        };
    }
    public static Rgb Timeline(List<Keyframe> frames, double time)
    {
        if (frames.Count == 0)
            return Rgb.Black;
        var sorted = frames.OrderBy(f => f.Time).ToArray();
        var duration = sorted[^1].Time;
        var t = duration > 0 ? (time % duration + duration) % duration : 0;
        var left = sorted.LastOrDefault(f => f.Time <= t) ?? sorted[0];
        var right = sorted.FirstOrDefault(f => f.Time > t) ?? left;
        return left.Hold || left == right ? Rgb.Parse(left.Color) : Rgb.Lerp(Rgb.Parse(left.Color), Rgb.Parse(right.Color), (t - left.Time) / (right.Time - left.Time));
    }
    public static void ValidateGraph(EffectLayer layer)
    {
        if (layer.Nodes.Count > 128)
            throw new InvalidDataException("Graphs are limited to 128 nodes.");
        if (layer.Nodes.Select(n => n.Id).Distinct().Count() != layer.Nodes.Count)
            throw new InvalidDataException("Node IDs must be unique.");
        var nodes = layer.Nodes.ToDictionary(n => n.Id);
        var visited = new HashSet<string>();
        var path = new HashSet<string>();
        void Visit(string id)
        {
            if (!nodes.TryGetValue(id, out var n))
                throw new InvalidDataException($"Missing node: {id}");
            if (!path.Add(id))
                throw new InvalidDataException("Graph contains a cycle.");
            if (!NodeTypes.Contains(n.Type))
                throw new InvalidDataException($"Unknown node type: {n.Type}");
            if (!double.IsFinite(n.Value) || !double.IsFinite(n.X) || !double.IsFinite(n.Y))
                throw new InvalidDataException("Node parameters must be finite.");
            _ = Rgb.Parse(n.Color);
            var required = n.Type switch
            {
                "Blend" => 2,
                "Transform" or "Mask" or "Colorize" => 1,
                _ => 0
            };
            if (n.Inputs.Count != required && !(n.Type is "Blend" or "Mask" && n.Inputs.Count == required + 1))
                throw new InvalidDataException($"{n.Type} needs {required} input(s).");
            for (var index = 0; index < n.Inputs.Count; index++)
            {
                if (!nodes.TryGetValue(n.Inputs[index], out var inputNode))
                    throw new InvalidDataException("Missing node: " + n.Inputs[index]);
                var needsScalar = n.Type == "Colorize" || n.Type == "Blend" && index == 2 || n.Type == "Mask" && index == 1;
                if (IsScalar(inputNode.Type) != needsScalar)
                    throw new InvalidDataException($"{n.Type} input {index + 1} requires a {(needsScalar ? "scalar" : "color")} node.");
            }
            foreach (var input in n.Inputs)
                if (!visited.Contains(input))
                    Visit(input);
            path.Remove(id);
            visited.Add(id);
        }
        Visit(layer.OutputNode);
        if (IsScalar(nodes[layer.OutputNode].Type))
            throw new InvalidDataException("Output must be color. Connect scalar signals through Colorize, Blend, or Mask.");
        foreach (var id in nodes.Keys)
            if (!visited.Contains(id))
                Visit(id);
    }
    static Rgb Graph(EffectLayer layer, double x, double y, EffectContext c)
    {
        var nodes = layer.Nodes.ToDictionary(n => n.Id);
        var budget = 2048;
        Rgb Run(string id, double px, int depth)
        {
            if (--budget < 0)
                throw new InvalidDataException("Graph evaluation budget exceeded. Reduce branch complexity.");
            if (depth > 128 || !nodes.TryGetValue(id, out var n))
                return Rgb.Black;
            Rgb Input(int index, double at) => index < n.Inputs.Count ? Run(n.Inputs[index], at, depth + 1) : Rgb.Black;
            double Signal(int index) => index < n.Inputs.Count && nodes.TryGetValue(n.Inputs[index], out var signal) ? Scalar(signal, px, c) : n.Value;
            var color = Rgb.Parse(n.Color);
            return n.Type switch
            {
                "Color" => color,
                "Colorize" => color.Scale(Signal(0)),
                "Gradient" => Rgb.Lerp(color, Rgb.Black, px),
                "Time" => color.Scale((Math.Sin(c.Time * n.Value) + 1) / 2),
                "Noise" => color.Scale(Noise(px * 100, c.Time * n.Value)),
                "Audio" => color.Scale(c.Audio.Length > 0 ? c.Audio[Math.Clamp((int)(px * c.Audio.Length), 0, c.Audio.Length - 1)] : 0),
                "Sensor" => color.Scale(c.Sensors.GetValueOrDefault(n.Source) / Math.Max(1, n.Value)),
                "Event" => color.Scale(c.Events.GetValueOrDefault(n.Source) / Math.Max(1, n.Value)),
                "Transform" => Input(0, ((px + c.Time * n.Value) % 1 + 1) % 1),
                "Mask" => px <= Signal(1) ? Input(0, px) : Rgb.Black,
                "Blend" => Rgb.Lerp(Input(0, px), Input(1, px), Signal(2)),
                _ => Rgb.Black
            };
        }
        return Run(layer.OutputNode, x, 0);
    }
    static double Scalar(EffectNode n, double x, EffectContext c) => n.Type switch
    {
        "Time" => (Math.Sin(c.Time * n.Value) + 1) / 2,
        "Noise" => Noise(x * 100, c.Time * n.Value),
        "Audio" => c.Audio.Length == 0 ? 0 : c.Audio[Math.Clamp((int)(x * c.Audio.Length), 0, c.Audio.Length - 1)],
        "Sensor" => c.Sensors.GetValueOrDefault(n.Source) / Math.Max(1, n.Value),
        "Event" => c.Events.GetValueOrDefault(n.Source) / Math.Max(1, n.Value),
        _ => throw new InvalidDataException("Expected a scalar signal.")
    };
    static double Noise(double x, double y)
    {
        var n = Math.Sin(x * 12.9898 + y * 78.233) * 43758.5453;
        return n - Math.Floor(n);
    }
    static Rgb Order(Rgb c, string order) => order switch { "GRB" => new(c.G, c.R, c.B), "BGR" => new(c.B, c.G, c.R), "BRG" => new(c.B, c.R, c.G), "RBG" => new(c.R, c.B, c.G), "GBR" => new(c.G, c.B, c.R), _ => c };
    static Rgb Blend(Rgb a, Rgb b, BlendMode mode, double opacity)
    {
        byte Channel(byte x, byte y) => mode switch
        {
            BlendMode.Add => (byte)Math.Min(255, x + y),
            BlendMode.Multiply => (byte)(x * y / 255),
            BlendMode.Screen => (byte)(255 - (255 - x) * (255 - y) / 255),
            _ => y
        };
        return Rgb.Lerp(a, new(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B)), opacity);
    }
}

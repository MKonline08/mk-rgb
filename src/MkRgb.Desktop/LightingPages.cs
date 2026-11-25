using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MkRgb.Core;
using MkRgb.Hardware;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MkRgb.Desktop;

public sealed partial class Pages
{
    record Target(string Id, string Name);
    record EffectChoice(EffectKind Kind, string Name, string Description);
    record ComponentChoice(int Leds, string Name);

    /// <summary>Unapplied Lighting page choices. Kept across page rebuilds (device scans, profile refreshes).</summary>
    sealed class LightingDraft
    {
        public string ProfileId = "";
        public string[] Colors = ["#67E8C5", "#707BFF"];
        public int Slot;
        public EffectKind Kind = EffectKind.Static;
        public double Brightness = 1;
        public double Speed = .5;
        public string Target = "";
        public bool Live = true;
    }
    LightingDraft? draft;

    static readonly EffectChoice[] QuickEffects =
    [
        new(EffectKind.Static, "Solid", "One steady color"),
        new(EffectKind.Breathing, "Breathe", "Slow fade in and out"),
        new(EffectKind.Pulse, "Pulse", "Short heartbeat flash"),
        new(EffectKind.Rainbow, "Rainbow", "Every color, flowing"),
        new(EffectKind.Wave, "Wave", "Two colors flowing"),
        new(EffectKind.Gradient, "Gradient", "Two colors blended"),
        new(EffectKind.Chase, "Chase", "A light running around"),
        new(EffectKind.Sparkle, "Sparkle", "Random twinkles"),
        new(EffectKind.Ripple, "Ripple", "Two colors rippling"),
    ];
    static readonly (string Name, string Hex)[] Palette =
    [
        ("Red", "#FF1A1A"), ("Orange", "#FF6A00"), ("Amber", "#FFB000"), ("Yellow", "#FFE600"), ("Lime", "#9DFF00"),
        ("Green", "#00FF4C"), ("Mint", "#00FFA8"), ("Cyan", "#00E5FF"), ("Sky", "#0099FF"), ("Blue", "#0033FF"),
        ("Indigo", "#4B00FF"), ("Violet", "#8A00FF"), ("Purple", "#C000FF"), ("Magenta", "#FF00D4"), ("Pink", "#FF3DA5"),
        ("Rose", "#FF4F70"), ("Coral", "#FF7F50"), ("Peach", "#FFAA80"), ("Lavender", "#B69CFF"), ("Ice", "#9FE8FF"),
    ];
    static readonly (string Name, string Hex)[] Whites = [("Cool white", "#FFFFFF"), ("Daylight", "#FFF1DE"), ("Warm white", "#FFCF96"), ("Candle", "#FF9A40")];

    static TextBlock T(string value, double size = 13, string color = "#ECEFF1", bool bold = false) =>
        new() { Text = value, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    static TextBlock Label(string value) =>
        new() { Text = value.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("#8E99A2"), Margin = new(0, 4, 0, 10) };
    static Border Chip(string text, string foreground, string background) =>
        new() { CornerRadius = new(10), Padding = new(9, 3, 9, 3), Background = Brush(background), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = T(text, 11, foreground, true) };
    static TextBlock Icon(string glyph, double size = 16, string color = "#ECEFF1") =>
        new() { Text = glyph, FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("Icons"), FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Top };
    static Style Resource(string key) => (Style)System.Windows.Application.Current.FindResource(key);
    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>A highlighted message strip with an icon, a title, a body, and optional actions.</summary>
    static Border Notice(string glyph, string title, string body, bool warning, params UIElement[] actions)
    {
        var accent = warning ? "#FFC86B" : "#78E8C2";
        var text = new StackPanel();
        text.Children.Add(T(title, 14, "#ECEFF1", true));
        if (body.Length > 0)
        {
            var b = T(body, 12.5, "#B4BEC5");
            b.Margin = new(0, 4, 0, 0);
            text.Children.Add(b);
        }
        if (actions.Length > 0)
        {
            var row = Row(actions);
            row.Margin = new(0, 12, 0, -8);
            text.Children.Add(row);
        }
        var icon = Icon(glyph, 18, accent);
        icon.Margin = new(0, 1, 14, 0);
        if (glyph == Spinner)
        {
            // Spinning "sync" icon while a scan runs.
            var spin = new RotateTransform();
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = spin;
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(text);
        return new Border { Child = dock, Background = Brush(warning ? "#221D13" : "#15241F"), BorderBrush = Brush(warning ? "#4A3C20" : "#25463B"), BorderThickness = new(1), CornerRadius = new(9), Padding = new(16, 14, 16, 14), Margin = new(0, 0, 0, 18) };
    }
    const string Spinner = "\uE895";
    static Button SwatchButton(string hex, string name, Action<string> pick)
    {
        var b = new Button { Style = Resource("Swatch"), Background = Brush(hex), ToolTip = $"{name}  {hex}" };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => pick(hex);
        return b;
    }

    UIElement Lighting()
    {
        if (draft is null || draft.ProfileId != vm.Profile.Id)
        {
            draft = new() { ProfileId = vm.Profile.Id };
            if (vm.Profile.Layout.FirstOrDefault(p => p.Lighting != null)?.Lighting is { } saved)
            {
                draft.Colors = [saved.Color, saved.Color2];
                draft.Kind = QuickLighting.Kinds.Contains(saved.Kind) ? saved.Kind : EffectKind.Static;
                draft.Brightness = Math.Clamp(saved.Brightness, 0, 1);
                draft.Speed = Math.Clamp(saved.Speed, .05, 2);
            }
            else if (vm.Settings.RecentColors.FirstOrDefault() is { } lastUsed)
                draft.Colors[0] = lastUsed;
        }
        var d = draft;
        var updating = false;
        var initialized = false;
        System.Action changed = () => { };
        var rgbDevices = vm.Devices.Where(x => x.Capabilities.HasFlag(Capability.Rgb)).ToList();

        // ---- Color editor ----
        var wheel = new ColorWheel { Width = 250, Height = 250, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 2, 0, 16) };
        var hex = new TextBox { Width = 112, MaxLength = 7, FontFamily = new FontFamily("Consolas"), FontSize = 14, Margin = new(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(hex, "Hex color");
        var sliders = new Slider[3];
        var values = new TextBlock[3];
        var channels = new StackPanel { Margin = new(0, 10, 0, 8) };
        string[] channelNames = ["Red", "Green", "Blue"];
        for (var i = 0; i < 3; i++)
        {
            sliders[i] = new Slider { Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 16, Style = Resource("GradientSlider") };
            System.Windows.Automation.AutomationProperties.SetName(sliders[i], channelNames[i]);
            values[i] = T("0", 12, "#8E99A2");
            values[i].TextAlignment = TextAlignment.Right;
            values[i].VerticalAlignment = VerticalAlignment.Center;
            var label = T(channelNames[i][..1], 12, "#8E99A2", true);
            label.VerticalAlignment = VerticalAlignment.Center;
            var line = new Grid { Margin = new(0, 2, 0, 2) };
            line.ColumnDefinitions.Add(new() { Width = new(22) });
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = new(40) });
            line.Children.Add(label);
            System.Windows.Controls.Grid.SetColumn(sliders[i], 1);
            line.Children.Add(sliders[i]);
            System.Windows.Controls.Grid.SetColumn(values[i], 2);
            line.Children.Add(values[i]);
            channels.Children.Add(line);
        }

        // Main / second color slots. The second slot only shows for effects that blend two colors.
        var slotGroup = "slot" + Guid.NewGuid().ToString("N");
        var slotDots = new Ellipse[2];
        var slots = new RadioButton[2];
        for (var s = 0; s < 2; s++)
        {
            var index = s;
            slotDots[s] = new Ellipse { Width = 18, Height = 18, Stroke = Brush("#40000000"), StrokeThickness = 1, Margin = new(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(slotDots[s]);
            content.Children.Add(T(s == 0 ? "Main color" : "Second color", 13));
            slots[s] = new RadioButton { Style = Resource("Tile"), GroupName = slotGroup, Content = content, Padding = new(10, 7, 14, 7), IsChecked = d.Slot == s };
            slots[s].Checked += (_, _) => { d.Slot = index; ShowColor(); };
        }
        var swap = new Button { Content = "Swap", Style = Resource("Quiet"), ToolTip = "Swap main and second colors", VerticalAlignment = VerticalAlignment.Center };
        swap.Click += (_, _) => { (d.Colors[0], d.Colors[1]) = (d.Colors[1], d.Colors[0]); ShowColor(); changed(); };

        var palette = new WrapPanel();
        foreach (var (name, color) in Palette)
            palette.Children.Add(SwatchButton(color, name, SetColor));
        var whites = new WrapPanel();
        foreach (var (name, color) in Whites)
            whites.Children.Add(SwatchButton(color, name, SetColor));
        var recentLabel = Label("Recent");
        var recent = new WrapPanel();
        void RefreshRecent()
        {
            recent.Children.Clear();
            foreach (var color in vm.Settings.RecentColors)
                recent.Children.Add(SwatchButton(color, "Recent color", SetColor));
            recentLabel.Visibility = recent.Visibility = vm.Settings.RecentColors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshRecent();

        void ShowColor()
        {
            updating = true;
            var c = Rgb.Parse(d.Colors[d.Slot]);
            wheel.Select(c.Hex);
            if (!hex.Text.Equals(c.Hex, StringComparison.OrdinalIgnoreCase))
                hex.Text = c.Hex;
            byte[] parts = [c.R, c.G, c.B];
            for (var i = 0; i < 3; i++)
            {
                sliders[i].Value = parts[i];
                values[i].Text = parts[i].ToString();
                var from = i == 0 ? c with { R = 0 } : i == 1 ? c with { G = 0 } : c with { B = 0 };
                var to = i == 0 ? c with { R = 255 } : i == 1 ? c with { G = 255 } : c with { B = 255 };
                sliders[i].Background = new LinearGradientBrush(ToColor(from), ToColor(to), 0);
            }
            for (var s = 0; s < 2; s++)
                slotDots[s].Fill = Brush(d.Colors[s]);
            updating = false;
        }
        void SetColor(string value)
        {
            d.Colors[d.Slot] = Rgb.Parse(value).Hex;
            ShowColor();
            changed();
        }
        void TryHex()
        {
            try { SetColor(hex.Text); }
            catch (FormatException) { vm.Status = "Type a color like #FF5500, or pick one from the wheel."; }
        }
        wheel.ColorChanged += c => { if (!updating) SetColor(c); };
        foreach (var slider in sliders)
            slider.ValueChanged += (_, _) => { if (!updating) SetColor(new Rgb((byte)sliders[0].Value, (byte)sliders[1].Value, (byte)sliders[2].Value).Hex); };
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryHex(); };
        hex.LostFocus += (_, _) => TryHex();
        hex.TextChanged += (_, _) =>
        {
            if (updating || hex.Text.Trim().TrimStart('#').Length != 6)
                return;
            try
            {
                d.Colors[d.Slot] = Rgb.Parse(hex.Text).Hex;
                updating = true;
                wheel.Select(d.Colors[d.Slot]);
                updating = false;
                ShowColor();
                changed();
            }
            catch (FormatException) { }
        };

        var hexRow = new DockPanel { Margin = new(0, 0, 0, 0) };
        var hexLabel = T("Hex", 12, "#8E99A2");
        hexLabel.VerticalAlignment = VerticalAlignment.Center;
        hexLabel.Margin = new(0, 0, 10, 0);
        hexRow.Children.Add(hexLabel);
        hexRow.Children.Add(hex);
        hexRow.Children.Add(new Border());
        var slotRow = Row(slots[0], slots[1], swap);
        var rainbowNote = T("Rainbow uses every color, so there's nothing to pick here. Choose another effect to use your own colors.", 12.5, "#8E99A2");
        rainbowNote.Margin = new(0, 0, 0, 12);
        var editor = new StackPanel();
        foreach (var e in new UIElement[] { wheel, hexRow, channels, Label("Palette"), palette, Label("Whites"), whites, recentLabel, recent })
            editor.Children.Add(e);
        var colorCard = Card(Stack(Label("Color"), slotRow, rainbowNote, editor));

        // ---- Effect ----
        var preview = new EffectPreview { ReducedMotion = vm.Settings.ReducedMotion, Margin = new(0, 0, 0, 18) };
        var effectGroup = "effect" + Guid.NewGuid().ToString("N");
        var effects = new WrapPanel();
        foreach (var choice in QuickEffects)
        {
            var body = new StackPanel();
            body.Children.Add(T(choice.Name, 13.5, "#ECEFF1", true));
            var description = T(choice.Description, 11, "#8E99A2");
            description.Margin = new(0, 2, 0, 0);
            body.Children.Add(description);
            var tile = new RadioButton { Style = Resource("Tile"), GroupName = effectGroup, Content = body, Width = 148, IsChecked = choice.Kind == d.Kind };
            System.Windows.Automation.AutomationProperties.SetName(tile, choice.Name + " effect");
            tile.Checked += (_, _) => { d.Kind = choice.Kind; changed(); };
            effects.Children.Add(tile);
        }
        var brightness = new Slider { Minimum = 0, Maximum = 100, Value = d.Brightness * 100, SmallChange = 1, LargeChange = 10, Margin = new(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(brightness, "Brightness percent");
        var brightnessValue = T($"{brightness.Value:0}%", 12, "#8E99A2");
        brightness.ValueChanged += (_, _) => { d.Brightness = brightness.Value / 100; brightnessValue.Text = $"{brightness.Value:0}%"; changed(); };
        var speed = new Slider { Minimum = .05, Maximum = 2, Value = d.Speed, SmallChange = .05, LargeChange = .25 };
        System.Windows.Automation.AutomationProperties.SetName(speed, "Effect speed");
        speed.ValueChanged += (_, _) => { d.Speed = speed.Value; changed(); };
        DockPanel LabelRow(string title, UIElement right)
        {
            var row = new DockPanel { Margin = new(0, 6, 0, 2) };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(T(title, 13));
            return row;
        }
        var speedPanel = Stack(LabelRow("Speed", T("slow → fast", 12, "#8E99A2")), speed);
        var effectCard = Card(Stack(Label("Preview"), preview, Label("Effect"), effects, LabelRow("Brightness", brightnessValue), brightness, speedPanel));

        // ---- Apply ----
        var targets = new List<Target> { new("", "All devices") };
        targets.AddRange(rgbDevices.Select(x => new Target(x.Id, x.LedCount == 0 ? x.Name + "  (needs setup)" : x.Name)));
        var target = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Name", SelectedItem = targets.FirstOrDefault(t => t.Id == d.Target) ?? targets[0], Margin = new(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(target, "Apply lighting to");
        var live = new CheckBox { Style = Resource("Switch"), Content = "Live: update my devices as I adjust", IsChecked = d.Live, Margin = new(0, 0, 0, 14) };
        live.Checked += (_, _) => { d.Live = true; changed(); };
        live.Unchecked += (_, _) => d.Live = false;
        async Task Apply()
        {
            await vm.ApplyQuickLighting(d.Target, d.Kind, d.Colors[0], d.Brightness, d.Speed, QuickLighting.UsesSecondColor(d.Kind) ? d.Colors[1] : null);
            RefreshRecent();
        }
        var apply = Button(d.Target == "" ? "Apply to all devices" : "Apply to this device", Apply, true);
        apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        apply.Padding = new(14, 12, 14, 12);
        apply.FontSize = 14;
        target.SelectionChanged += (_, _) =>
        {
            d.Target = (target.SelectedItem as Target)?.Id ?? "";
            apply.Content = d.Target == "" ? "Apply to all devices" : "Apply to this device";
        };
        var applyHint = T("Apply turns lighting on and saves it to your profile.", 12, "#8E99A2");
        applyHint.Margin = new(0, 4, 0, 0);
        var applyCard = Card(Stack(Label("Apply to"), target, live, apply, applyHint));

        // Live preview: push changes to the hardware after a short pause, so dragging stays smooth.
        var liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        liveTimer.Tick += async (_, _) =>
        {
            liveTimer.Stop();
            var ready = vm.Devices.Any(x => x.Capabilities.HasFlag(Capability.Rgb) && x.LedCount > 0 && (d.Target == "" || x.Id == d.Target));
            if (d.Live && ready && !vm.Scanning)
                await vm.Run(() => vm.ApplyQuickLighting(d.Target, d.Kind, d.Colors[0], d.Brightness, d.Speed, QuickLighting.UsesSecondColor(d.Kind) ? d.Colors[1] : null, live: true));
        };

        changed = () =>
        {
            var second = QuickLighting.UsesSecondColor(d.Kind);
            var usesColor = QuickLighting.UsesColor(d.Kind);
            preview.Layer = new EffectLayer { Name = "Preview", Kind = d.Kind, Color = d.Colors[0], Color2 = second ? d.Colors[1] : Rgb.Parse(d.Colors[0]).Scale(.08).Hex, Brightness = d.Brightness, Speed = d.Speed };
            preview.InvalidateVisual();
            // The preview box glows in the chosen color (a cycling glow for Rainbow).
            var glow = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 0, Opacity = vm.Settings.ReducedMotion ? 0.35 : 0.55, Color = ToColor(Rgb.Parse(d.Colors[0])) };
            if (!usesColor && !vm.Settings.ReducedMotion)
            {
                var cycle = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(6) };
                foreach (var (hex, at) in new[] { ("#FF2A55", 0.0), ("#FFD000", 1.5), ("#00FF88", 3.0), ("#2F7BFF", 4.5), ("#FF2A55", 6.0) })
                    cycle.KeyFrames.Add(new LinearColorKeyFrame((Color)ColorConverter.ConvertFromString(hex), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
                glow.BeginAnimation(DropShadowEffect.ColorProperty, cycle);
            }
            preview.Effect = glow;
            slots[1].Visibility = swap.Visibility = second ? Visibility.Visible : Visibility.Collapsed;
            if (!second && d.Slot == 1)
                slots[0].IsChecked = true;
            slotRow.Visibility = editor.Visibility = usesColor ? Visibility.Visible : Visibility.Collapsed;
            rainbowNote.Visibility = usesColor ? Visibility.Collapsed : Visibility.Visible;
            speedPanel.Visibility = d.Kind == EffectKind.Static ? Visibility.Collapsed : Visibility.Visible;
            if (initialized)
            {
                liveTimer.Stop();
                liveTimer.Start();
            }
        };
        ShowColor();
        changed();
        initialized = true;

        var root = new StackPanel();
        root.Unloaded += (_, _) => liveTimer.Stop();
        var needsSetup = rgbDevices.Count(x => x.Zones.Any(z => vm.NeedsComponentSetup(x, z)));
        if (vm.Scanning)
            root.Children.Add(Notice(Spinner, "Looking for your RGB devices…", "This can take up to a minute the first time while every controller is checked.", false));
        else if (rgbDevices.Count == 0)
            root.Children.Add(Notice("", "No RGB devices found yet", "Press Find devices to scan. If something stays missing, the Devices page explains the usual causes.", true,
                Button("Find devices", vm.ConnectRgb, true), Action("Open Devices", () => window.Go("Devices"))));
        else if (needsSetup > 0)
            root.Children.Add(Notice("", $"{needsSetup} controller{(needsSetup == 1 ? "" : "s")} need{(needsSetup == 1 ? "s" : "")} fans or strips set up", "Fans and strips plugged into RGB headers can't report themselves, so they stay dark until you tell MK RGB what's connected.", true,
                Action("Set up fans & strips", () => window.Go("Devices"), true)));
        else
            root.Children.Add(Notice("", $"{rgbDevices.Count} RGB device{(rgbDevices.Count == 1 ? "" : "s")} ready", "", false));
        root.Children.Add(Columns(colorCard, Stack(effectCard, applyCard), 1, 1.2));
        return root;
    }

    UIElement DeviceSetup()
    {
        var root = new StackPanel();
        var rgb = vm.Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb)).OrderByDescending(d => d.Zones.Any(z => vm.NeedsComponentSetup(d, z))).ThenBy(d => d.Name).ToList();
        var other = vm.Devices.Where(d => !d.Capabilities.HasFlag(Capability.Rgb)).ToList();

        var find = Button(vm.Scanning ? "Scanning…" : "Find devices", vm.ConnectRgb, true);
        find.IsEnabled = !vm.Scanning;
        var summary = T(vm.Scanning ? "Checking every controller; this can take up to a minute." : rgb.Count == 0 ? "No RGB devices found yet." : $"{rgb.Count} RGB device{(rgb.Count == 1 ? "" : "s")} found.", 13, "#B4BEC5");
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new(6, 0, 0, 8);
        root.Children.Add(Row(find, summary));

        if (vm.Scanning)
            root.Children.Add(Notice(Spinner, "Scanning for RGB devices…", "OpenRGB is checking every controller. Devices appear here as soon as the scan settles.", false));
        if (vm.DetectionNotes.Count > 0)
            root.Children.Add(Notice("", "Things that may hide devices", string.Join("\n\n", vm.DetectionNotes), true));

        foreach (var device in rgb)
            root.Children.Add(DeviceCard(device));

        if (other.Count > 0)
        {
            var list = new StackPanel();
            foreach (var group in other.GroupBy(d => d.Vendor).OrderBy(g => g.Key))
            {
                var heading = T(group.Key, 11, "#8E99A2", true);
                heading.Margin = new(0, 8, 0, 4);
                list.Children.Add(heading);
                foreach (var d in group)
                {
                    var line = T("•  " + d.Name, 13, "#C4CCD1");
                    line.Margin = new(0, 0, 0, 3);
                    list.Children.Add(line);
                }
            }
            var expander = new Expander { Header = T($"Other detected hardware ({other.Count}) — sensors and monitoring only", 13.5, "#ECEFF1", true), Content = list, Margin = new(0, 0, 0, 0) };
            root.Children.Add(Card(expander));
        }

        root.Children.Add(MissingDeviceHelp());
        return root;
    }

    UIElement DeviceCard(Device device)
    {
        var needs = device.Zones.Any(z => vm.NeedsComponentSetup(device, z));
        var chip = needs ? Chip("Needs setup", "#FFC86B", "#3A2F18") : device.LedCount == 0 ? Chip("No LEDs configured", "#8E99A2", "#262B30") : Chip("Ready", "#78E8C2", "#1C3A31");
        var title = new StackPanel();
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        var nameText = T(device.Name, 17, "#ECEFF1", true);
        nameText.Margin = new(0, 0, 12, 0);
        name.Children.Add(nameText);
        name.Children.Add(chip);
        title.Children.Add(name);
        var details = T($"{device.Vendor}  ·  {device.LedCount} LED{(device.LedCount == 1 ? "" : "s")}  ·  {device.Zones.Count} zone{(device.Zones.Count == 1 ? "" : "s")}", 12, "#8E99A2");
        details.Margin = new(0, 4, 0, 0);
        title.Children.Add(details);

        var identify = Button("Identify", () => vm.IdentifyDevice(device.Id));
        identify.ToolTip = "Flash this device white for a few seconds so you can see which one it is.";
        identify.IsEnabled = device.LedCount > 0;
        identify.Margin = new(0);
        var header = new DockPanel { Margin = new(0, 0, 0, 4) };
        DockPanel.SetDock(identify, Dock.Right);
        header.Children.Add(identify);
        header.Children.Add(title);

        var body = Stack(header);
        var resizable = device.Zones.Where(z => z.Resizable).ToList();
        if (resizable.Count > 0)
        {
            var intro = T("Fans and strips plugged into these headers can't identify themselves. Pick what's connected to each one, or mark it unused.", 12.5, "#B4BEC5");
            intro.Margin = new(0, 12, 0, 12);
            body.Children.Add(intro);
            var zones = new WrapPanel();
            foreach (var zone in resizable)
                zones.Children.Add(ComponentSetup(device, zone));
            body.Children.Add(zones);
        }
        var placement = vm.Profile.Layout.FirstOrDefault(p => p.DeviceId == device.Id);
        if (placement != null)
        {
            var order = Choice("Color order", placement, nameof(placement.ColorOrder), new[] { "RGB", "GRB", "BGR", "BRG", "RBG", "GBR" });
            var help = T("If red shows as green or blue on this device, try another order, save, then apply a color again.", 12, "#8E99A2");
            help.Margin = new(0, 0, 0, 8);
            var expander = new Expander { Header = T("Colors look wrong?", 12.5, "#B4BEC5"), Content = Stack(help, Row(order, Action("Save color order", vm.Save))), Margin = new(0, 10, 0, 0) };
            body.Children.Add(expander);
        }
        return Card(body);
    }

    UIElement ComponentSetup(Device device, Zone zone)
    {
        var choices = new[] { new ComponentChoice(0, "Choose what's connected…"), new(8, "Fan · 8 LEDs"), new(12, "Fan · 12 LEDs"), new(16, "Fan · 16 LEDs"), new(18, "Fan · 18 LEDs"), new(24, "Fan · 24 LEDs"), new(30, "Strip · 30 LEDs"), new(60, "Strip · 60 LEDs"), new(-1, "Custom LED count") };
        var component = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 200, Margin = new(0, 4, 10, 8) };
        var quantity = new TextBox { Text = "1", Width = 56 };
        var custom = new TextBox { Text = Math.Max(1, zone.Count).ToString(), Width = 70 };
        var customField = Stack(T("LEDs each", 11, "#8E99A2"), custom);
        customField.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(component, zone.Name + " component");
        System.Windows.Automation.AutomationProperties.SetName(quantity, "How many");
        System.Windows.Automation.AutomationProperties.SetName(custom, "LEDs per component");
        var needs = vm.NeedsComponentSetup(device, zone);
        var state = zone.Count > 0 ? Chip($"{zone.Count} LEDs", "#78E8C2", "#1C3A31") : needs ? Chip("Not set up", "#FFC86B", "#3A2F18") : Chip("Unused", "#8E99A2", "#262B30");
        var total = T("Check the LED count on the product page or box.", 11.5, "#8E99A2");
        total.Margin = new(0, 2, 0, 10);
        var save = Button("Save", async () =>
        {
            var selection = (ComponentChoice)component.SelectedItem;
            var leds = selection.Leds == -1 ? int.Parse(custom.Text) : selection.Leds;
            var count = QuickLighting.ComponentLeds(leds, int.Parse(quantity.Text), zone);
            await vm.ConfigureRgbZone(device.Id, zone, count);
        }, true);
        save.IsEnabled = false;
        void Update()
        {
            var selection = (ComponentChoice)component.SelectedItem;
            customField.Visibility = selection.Leds == -1 ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                var leds = selection.Leds == -1 ? int.Parse(custom.Text) : selection.Leds;
                total.Text = $"Total: {QuickLighting.ComponentLeds(leds, int.Parse(quantity.Text), zone)} LEDs";
                save.IsEnabled = true;
            }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException)
            {
                total.Text = selection.Leds == 0 ? "Choose what's connected to continue." : e.Message;
                save.IsEnabled = false;
            }
        }
        component.SelectionChanged += (_, _) => Update();
        quantity.TextChanged += (_, _) => Update();
        custom.TextChanged += (_, _) => Update();

        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(state, Dock.Right);
        header.Children.Add(state);
        header.Children.Add(T(zone.Name, 14, "#ECEFF1", true));
        var unused = Button("Mark unused", () => vm.ConfigureRgbZone(device.Id, zone, 0));
        return new Border
        {
            Width = 340,
            Margin = new(0, 0, 12, 12),
            Padding = new(14, 12, 14, 6),
            CornerRadius = new(8),
            Background = Brush("#1B2024"),
            BorderBrush = Brush(needs ? "#4A3C20" : "#2B3137"),
            BorderThickness = new(1),
            Child = Stack(header, Row(component, Stack(T("How many", 11, "#8E99A2"), quantity), customField), total, Row(save, unused))
        };
    }

    UIElement MissingDeviceHelp()
    {
        var service = vm.ServiceConnected;
        var pawnIo = OpenRgbBackend.PawnIoInstalled();
        var conflicts = Sensors.Conflicts().Where(n => !n.Equals("FanControl", StringComparison.OrdinalIgnoreCase)).ToList();
        UIElement Step(int number, string title, string body, Border status, params UIElement[] actions)
        {
            var badge = new Border { Width = 26, Height = 26, CornerRadius = new(13), Background = Brush("#21262B"), Margin = new(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = number.ToString(), Foreground = Brush("#ECEFF1"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var t = T(title, 14, "#ECEFF1", true);
            t.Margin = new(0, 0, 10, 0);
            head.Children.Add(t);
            head.Children.Add(status);
            var text = Stack(head);
            var b = T(body, 12.5, "#B4BEC5");
            b.Margin = new(0, 4, 0, 0);
            text.Children.Add(b);
            if (actions.Length > 0)
            {
                var row = Row(actions);
                row.Margin = new(0, 10, 0, -8);
                text.Children.Add(row);
            }
            var dock = new DockPanel { Margin = new(0, 0, 0, 18) };
            DockPanel.SetDock(badge, Dock.Left);
            dock.Children.Add(badge);
            dock.Children.Add(text);
            return dock;
        }
        Border Good(string text) => Chip(text, "#78E8C2", "#1C3A31");
        Border Bad(string text) => Chip(text, "#FFC86B", "#3A2F18");
        Border Info(string text) => Chip(text, "#8E99A2", "#262B30");
        var title = T("Device missing?", 18, "#ECEFF1", true);
        title.Margin = new(0, 0, 0, 16);
        return Card(Stack(title,
            Step(1, "Hardware service", "Motherboard lighting, RAM lighting and fan control need the MK RGB service, which the Setup EXE installs. The portable ZIP can only use USB devices through an OpenRGB you start yourself.", service ? Good("Running") : Bad("Not running"),
                Action("Download installer", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb/releases/latest"))),
            Step(2, "Other RGB software", "iCUE, Armoury Crate, SignalRGB, Mystic Light and RGB Fusion can lock devices so nothing else can see them. Quit them from their tray icons, then scan again.", conflicts.Count == 0 ? Good("None running") : Bad(string.Join(", ", conflicts)),
                Button("Ask them to close", vm.CloseRgbApps)),
            Step(3, "PawnIO driver (RAM and motherboard RGB)", "OpenRGB needs this signed driver to talk to RAM sticks and many motherboard lighting chips. The MK RGB installer adds it automatically; if it is missing, install it here, restart your PC, then scan again.", pawnIo ? Good("Installed") : Bad("Not installed"),
                Button("Install PawnIO", vm.InstallPawnIo, true)),
            Step(4, "Fans and strips on RGB headers", "These can't report themselves. Find their controller above (usually your motherboard or a fan hub) and choose what's plugged into each header.", Info("Set up above")),
            Step(5, "Still missing?", "Unplug and reconnect USB devices, then scan again. Some devices aren't supported by OpenRGB yet; check the compatibility guide.", Info("Last step"),
                Button("Find devices again", vm.ConnectRgb, true), Action("Compatibility guide", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb/blob/main/docs/compatibility.md")))));
    }
}

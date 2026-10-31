using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MkRgb.Core;

namespace MkRgb.Desktop;

public sealed partial class Pages
{
    record Target(string Id, string Name);
    record EffectChoice(EffectKind Kind, string Name);
    record ComponentChoice(int Leds, string Name);
    UIElement AdvancedTools() => Stack(Text("Advanced tools", 30), Text("Optional editors for custom setups. Everyday colors and effects are in Lighting.", 13, "#959FA7"),
        Card(Stack(Text("Layout", 20), Text("Arrange devices in space, paint individual LEDs, and change hardware assignments.", 13, "#959FA7"), Action("Open layout editor", () => window.Go("Layout")))),
        Card(Stack(Text("Effect studio", 20), Text("Build layered effects, timelines, and node graphs.", 13, "#959FA7"), Action("Open studio", () => window.Go("Studio")))),
        Card(Stack(Text("Automation", 20), Text("Switch profiles for applications, schedules, and sensor conditions.", 13, "#959FA7"), Action("Open automations", () => window.Go("Automations")))),
        Row(Action("System overview", () => window.Go("Overview")), Action("Back to Lighting", () => window.Go("Lighting"))));

    UIElement Lighting()
    {
        var current = vm.Profile.Layout.FirstOrDefault(d => d.Lighting != null)?.Lighting ?? new EffectLayer { Kind = EffectKind.Static, Brightness = 1, Color = vm.Profile.Layers.FirstOrDefault()?.Color ?? "#67E8C5" };
        var selectedColor = current.Color;
        var wheel = new ColorWheel(); wheel.Select(selectedColor);
        var hex = new TextBox { Text = selectedColor, Width = 125, MaxLength = 7 };
        System.Windows.Automation.AutomationProperties.SetName(hex, "Hex color");
        var preview = new Border { Background = Brush(selectedColor), Height = 40, CornerRadius = new(9), Margin = new(0, 4, 0, 18) };
        var rgbDevices = vm.Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb)).ToList();
        var targets = new List<Target> { new("", "All connected devices") };
        targets.AddRange(rgbDevices.Select(d => new Target(d.Id, d.Name)));
        var target = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 230 };
        System.Windows.Automation.AutomationProperties.SetName(target, "Apply lighting to");
        var effects = new[] { new EffectChoice(EffectKind.Static, "Solid color"), new(EffectKind.Breathing, "Breathe"), new(EffectKind.Rainbow, "Rainbow"), new(EffectKind.Wave, "Color wave") };
        var effect = new ComboBox { ItemsSource = effects, DisplayMemberPath = "Name", SelectedItem = effects.FirstOrDefault(e => e.Kind == current.Kind) ?? effects[0] };
        var brightness = new Slider { Minimum = 0, Maximum = 100, Value = current.Brightness * 100, Margin = new(0, 8, 0, 15), TickFrequency = 1, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(brightness, "Brightness percent");
        var amount = Text($"Brightness  {brightness.Value:0}%", 13);
        brightness.ValueChanged += (_, _) => amount.Text = $"Brightness  {brightness.Value:0}%";
        var speed = new Slider { Minimum = .05, Maximum = 2, Value = Math.Clamp(current.Speed, .05, 2), Margin = new(0, 6, 0, 18) };
        System.Windows.Automation.AutomationProperties.SetName(speed, "Effect speed");
        var speedControls = Stack(Text("Speed  ·  slow → fast", 12, "#959FA7"), speed);
        speedControls.Visibility = ((EffectChoice)effect.SelectedItem).Kind == EffectKind.Static ? Visibility.Collapsed : Visibility.Visible;
        effect.SelectionChanged += (_, _) => speedControls.Visibility = ((EffectChoice)effect.SelectedItem).Kind == EffectKind.Static ? Visibility.Collapsed : Visibility.Visible;
        void SelectColor(string color)
        {
            selectedColor = Rgb.Parse(color).Hex;
            hex.Text = selectedColor; wheel.Select(selectedColor); preview.Background = Brush(selectedColor);
        }
        wheel.ColorChanged += SelectColor;
        hex.LostFocus += (_, _) => { try { SelectColor(hex.Text); } catch (FormatException) { vm.Status = "Use a color such as #FF5500, or pick one from the wheel."; } };
        var swatches = new WrapPanel();
        foreach (var (name, color) in new[] { ("Red", "#FF304F"), ("Orange", "#FF7A20"), ("Yellow", "#FFD84D"), ("Green", "#32E896"), ("Cyan", "#20D9FF"), ("Blue", "#367CFF"), ("Purple", "#A15CFF"), ("Pink", "#FF5AC8"), ("White", "#FFFFFF") })
        {
            var swatch = Action(name, () => SelectColor(color));
            swatch.Content = new Border { Width = 22, Height = 22, CornerRadius = new(11), Background = Brush(color) };
            swatch.Padding = new(7); swatch.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(swatch, name);
            swatches.Children.Add(swatch);
        }
        async Task Apply()
        {
            SelectColor(hex.Text);
            await vm.ApplyQuickLighting(((Target)target.SelectedItem).Id, ((EffectChoice)effect.SelectedItem).Kind, selectedColor, brightness.Value / 100, speed.Value);
        }
        var apply = Button("Apply to all devices", Apply, true);
        target.SelectionChanged += (_, _) => apply.Content = target.SelectedIndex == 0 ? "Apply to all devices" : "Apply to selected device";
        var colorCard = Card(Stack(Text("1   Pick a color", 19), Text("Use the wheel or choose a swatch.", 12, "#959FA7"), wheel, swatches, Row(Text("HEX", 11, "#959FA7"), hex)));
        var controls = Card(Stack(Text("2   Make it yours", 19), preview, Text("EFFECT", 10, "#959FA7"), effect, amount, brightness, Text("APPLY TO", 10, "#959FA7"), target, apply, Text("Apply turns lighting on and saves your choice.", 12, "#959FA7"), speedControls, Action("Pause lighting", vm.PauseLighting)));
        var summary = Text("", 13, "#78E8C2"); summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.RgbSummary)) { Source = vm });
        var root = Stack(Text("Your lights. Your color.", 30), Text("Choose a color and press Apply. No layers or LED indexes needed.", 13, "#959FA7"), Row(Button("Find / refresh devices", vm.ConnectRgb), Action("Device setup", () => window.Go("Devices"))), summary, Columns(colorCard, controls, 1, 1));
        var missing = rgbDevices.Where(d => d.Zones.Any(z => vm.NeedsComponentSetup(d, z))).ToList();
        if (missing.Count > 0)
            root.Children.Insert(4, Row(Text("Some PC lighting needs component setup.", 13, "#FFD487"), Action("Set up fans & strips", () => window.Go("Devices"), true)));
        if (rgbDevices.Count == 0)
            root.Children.Add(Card(Stack(Text("Start with your devices", 18), Text("Find devices first, or press Apply to connect automatically. Unplugged and unsupported devices are never shown as connected.", 13, "#AEB8BD"))));
        return root;
    }

    UIElement DeviceSetup()
    {
        var root = Stack(Text("Your RGB devices", 30), Text("Connected devices appear here. Set up any fans or strips attached to a controller, then return to Lighting.", 13, "#959FA7"), Row(Button("Find / refresh devices", vm.ConnectRgb, true), Action("Choose colors", () => window.Go("Lighting"))));
        var devices = vm.Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb)).OrderByDescending(d => d.Zones.Any(z => vm.NeedsComponentSetup(d, z))).ToList();
        if (devices.Count == 0)
            root.Children.Add(Card(Stack(Text("No RGB controllers connected yet", 20), Text("Press Find / refresh devices. If a device stays missing, close other RGB software and check its USB or motherboard connection. A detected controller is required to control its attached lights.", 13, "#AEB8BD"))));
        foreach (var device in devices)
        {
            var body = Stack(Text(device.Name, 20), Text($"{device.Vendor}  ·  {device.LedCount} configured LEDs", 12, "#959FA7"));
            if (!device.Zones.Any(z => z.Resizable))
                body.Children.Add(Text("No component setup needed. Choose All connected devices in Lighting to include this device.", 13, "#78E8C2"));
            foreach (var zone in device.Zones.Where(z => z.Resizable))
                body.Children.Add(ComponentSetup(device, zone));
            var advanced = new Expander { Header = "Troubleshoot colors", Margin = new(0, 12, 0, 0) };
            var placement = vm.Profile.Layout.FirstOrDefault(d => d.DeviceId == device.Id);
            if (placement != null)
                advanced.Content = Stack(Text("If red appears green or blue, change color order here, then apply a color again.", 12, "#959FA7"), Choice("Color order", placement, nameof(placement.ColorOrder), new[] { "RGB", "GRB", "BGR", "BRG", "RBG", "GBR" }), Action("Save color order", vm.Save));
            body.Children.Add(advanced);
            root.Children.Add(Card(body));
        }
        root.Children.Add(Card(Stack(Text("How detection works", 17), Text("USB and motherboard controllers can identify themselves. Passive ARGB fans and strips cannot report their own model or LED count. Configure connected components once; leave unused headers empty. Devices not supported by the backend need a compatible adapter or controller.", 13, "#959FA7"))));
        return root;
    }

    UIElement ComponentSetup(Device device, Zone zone)
    {
        var choices = new[] { new ComponentChoice(0, "Choose a component…"), new(8, "Fan · 8 LEDs"), new(12, "Fan · 12 LEDs"), new(16, "Fan · 16 LEDs"), new(24, "Fan · 24 LEDs"), new(30, "Strip · 30 LEDs"), new(60, "Strip · 60 LEDs"), new(-1, "Custom LED count") };
        var component = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 210 };
        var quantity = new TextBox { Text = "1", Width = 70 };
        var custom = new TextBox { Text = Math.Max(1, zone.Count).ToString(), Width = 90 };
        var customField = Stack(Text("LEDs each", 11, "#959FA7"), custom); customField.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(component, zone.Name + " component");
        System.Windows.Automation.AutomationProperties.SetName(quantity, "Component quantity");
        System.Windows.Automation.AutomationProperties.SetName(custom, "LEDs per component");
        var details = Text(zone.Count == 0 ? vm.NeedsComponentSetup(device, zone) ? "No components configured · add attached lights below, or leave this header unused." : "Marked unused · no setup needed." : $"Configured for {zone.Count} LEDs. Save components below to remember this setup.", 12, vm.NeedsComponentSetup(device, zone) ? "#FFD487" : "#78E8C2");
        var total = Text("Choose the LED count from the component's specifications.", 12, "#959FA7");
        var save = Button("Save components", async () =>
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
            { total.Text = selection.Leds == 0 ? "Choose the connected component to continue." : e.Message; save.IsEnabled = false; }
        }
        component.SelectionChanged += (_, _) => Update(); quantity.TextChanged += (_, _) => Update(); custom.TextChanged += (_, _) => Update();
        return new Border { BorderBrush = Brush("#34413D"), BorderThickness = new(0, 1, 0, 0), Padding = new(0, 16, 0, 6), Margin = new(0, 12, 0, 0), Child = Stack(Text(zone.Name, 15), details, Row(component, Stack(Text("Quantity", 11, "#959FA7"), quantity), customField), total, Row(save, Button("Mark unused", () => vm.ConfigureRgbZone(device.Id, zone, 0)))) };
    }
}

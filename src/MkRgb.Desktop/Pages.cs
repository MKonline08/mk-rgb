using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using MkRgb.Core;
using MkRgb.Hardware;

namespace MkRgb.Desktop;

public sealed class Pages(MainViewModel vm, MainWindow window)
{
    static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    static TextBlock Text(string value, double size = 13, string color = "#EDF0F1") => new() { Text = value, FontSize = size, Foreground = Brush(color), Margin = new(0, 0, 0, 8) };
    static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }
    static WrapPanel Row(params UIElement[] children)
    {
        var p = new WrapPanel();
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }
    static Border Card(UIElement child) => new() { Child = child, Background = Brush("#191C1F"), BorderBrush = Brush("#30363A"), BorderThickness = new(1), CornerRadius = new(7), Padding = new(20), Margin = new(0, 0, 0, 18) };
    static FrameworkElement Columns(UIElement left, UIElement right, double leftWeight = 2, double rightWeight = 1)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new()
        {
            Width = new(leftWeight, GridUnitType.Star)
        });
        g.ColumnDefinitions.Add(new()
        {
            Width = new(18)
        });
        g.ColumnDefinitions.Add(new()
        {
            Width = new(rightWeight, GridUnitType.Star)
        });
        g.Children.Add(left);
        System.Windows.Controls.Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }
    Button Button(string title, Func<Task> action, bool primary = false)
    {
        var b = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (primary)
            b.Style = (Style)System.Windows.Application.Current.FindResource("Primary");
        b.Click += async (_, _) => { b.IsEnabled = false; await vm.Run(action); b.IsEnabled = true; };
        return b;
    }
    Button Action(string title, System.Action action, bool primary = false) => Button(title, () => { action(); return Task.CompletedTask; }, primary);
    static Binding Bind(object source, string path) => new(path) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true };
    FrameworkElement Field(string title, object source, string path, double width = 190)
    {
        var box = new TextBox();
        box.SetBinding(TextBox.TextProperty, Bind(source, path));
        box.GotKeyboardFocus += (_, _) => vm.Record();
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        return new StackPanel { Width = width, Children = { Text(title, 11, "#959FA7"), box } };
    }
    FrameworkElement Choice(string title, object source, string path, IEnumerable items, string? display = null, string? value = null, double width = 190)
    {
        var box = new ComboBox { ItemsSource = items, DisplayMemberPath = display ?? "", SelectedValuePath = value ?? "" };
        box.SetBinding(value is null ? Selector.SelectedItemProperty : Selector.SelectedValueProperty, Bind(source, path));
        box.PreviewMouseLeftButtonDown += (_, _) => vm.Record();
        box.PreviewKeyDown += (_, _) => vm.Record();
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        return new StackPanel { Width = width, Children = { Text(title, 11, "#959FA7"), box } };
    }
    CheckBox Check(string title, object source, string path)
    {
        var b = new CheckBox { Content = title };
        b.SetBinding(ToggleButton.IsCheckedProperty, Bind(source, path));
        b.PreviewMouseLeftButtonDown += (_, _) => vm.Record();
        return b;
    }
    static DataGrid Grid(IEnumerable items, params (string Header, string Path)[] columns)
    {
        var g = new DataGrid { ItemsSource = items, MinHeight = 90, MaxHeight = 360, IsReadOnly = true };
        foreach (var (header, path) in columns)
            g.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path) { StringFormat = path is "Min" or "Max" or "Average" ? "0.#" : null, TargetNullValue = "—" }, Width = new(1, DataGridLengthUnitType.Star) });
        return g;
    }
    static void EditColumn(DataGrid grid, string title, string path, double width = 1) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { Mode = BindingMode.TwoWay, ValidatesOnExceptions = true }, Width = new(width, DataGridLengthUnitType.Star) });
    string? SavePath(string name, string filter)
    {
        var d = new SaveFileDialog { FileName = name, Filter = filter };
        return d.ShowDialog(window) == true ? d.FileName : null;
    }
    public UIElement Build(string page) => page switch { "Lighting" => Lighting(), "Studio" => Studio(), "Cooling" => Cooling(), "Monitoring" => Monitoring(), "Automations" => Automation(), "Library" => Library(), "Settings" => Settings(), _ => Overview() };
    FrameworkElement Metric(string label, string property, string detail)
    {
        var value = Text("", 28);
        value.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = vm });
        return Card(Stack(Text(label, 11, "#959FA7"), value, Text(detail, 10, "#7E8C93")));
    }
    UIElement Overview()
    {
        var metrics = new UniformGrid { Columns = 4 };
        foreach (var c in new[] { Metric("CPU TEMPERATURE", "Cpu", "Live sensor reading"), Metric("GPU TEMPERATURE", "Gpu", "Live sensor reading"), Metric("DEVICES", "DeviceCount", "Discovered hardware"), Metric("SENSORS", "SensorCount", "Available telemetry") })
        {
            c.Margin = new(0, 0, 12, 18);
            metrics.Children.Add(c);
        }
        var canvas = Card(Stack(Row(Text("Your lighting space", 17)), new SetupCanvas(vm), Row(Button("Connect RGB", vm.ConnectRgb, true), Action("Add preview device", vm.AddVirtual))));
        var active = Card(Stack(Text("Active composition", 17), Text(vm.Profile.Name, 24, "#78E8C2"), Text($"{vm.Profile.Layers.Count} layers · {vm.Profile.Layout.Count} mapped devices", 12, "#959FA7"), Text("Color moves across your entire layout. Open Studio to shape the motion, timing, and response.", 13, "#AEB8BD"), Action("Open Studio", () => window.Go("Studio")), Text("OUTPUT", 10, "#959FA7"), Check("Send lighting to connected devices", vm, nameof(vm.LightingEnabled)), Text("Preview devices are virtual and send no hardware commands.", 11, "#959FA7"), Action("Open cooling", () => window.Go("Cooling"))));
        var devices = Grid(vm.Devices, ("DEVICE", "Name"), ("BACKEND / TYPE", "Vendor"), ("CAPABILITIES", "Capabilities"), ("STATUS", "Status"));
        var dashboard = new WrapPanel();
        foreach (var id in vm.Settings.PinnedSensors)
        {
            var sensor = vm.Sensors.FirstOrDefault(s => s.Id == id);
            var value = Text("Unavailable", 25);
            if (sensor != null)
                value.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = sensor });
            var panel = Card(Stack(Text(sensor?.Name ?? id, 12), value, Row(Action("Move left", () =>
            {
                var index = vm.Settings.PinnedSensors.IndexOf(id);
                if (index > 0)
                    (vm.Settings.PinnedSensors[index - 1], vm.Settings.PinnedSensors[index]) = (vm.Settings.PinnedSensors[index], vm.Settings.PinnedSensors[index - 1]);
                vm.Save();
                window.Go("Overview");
            }), Action("Unpin", () => { vm.Settings.PinnedSensors.Remove(id); vm.Save(); window.Go("Overview"); }))));
            panel.Width = 270;
            panel.Margin = new(0, 0, 12, 18);
            dashboard.Children.Add(panel);
        }

        return Stack(Text("System overview", 28), Text("Lighting, thermals, and control for your setup.", 13, "#959FA7"), metrics, dashboard, Columns(canvas, active), Card(Stack(Text("Connected hardware", 17), devices)), Card(Stack(Text("Setup checklist", 17), Text("1   Install the hardware service for privileged sensors and cooling.\n2   Connect RGB and identify each physical device.\n3   Map fan RPM and temperature sources before commissioning.\n4   Save a profile and enable the outputs you want.", 13, "#AEB8BD"), Row(Action("Settings & diagnostics", () => window.Go("Settings")), Action("View monitoring", () => window.Go("Monitoring"))))));
    }
    UIElement LedEditor(Placement placement)
    {
        var hardware = new ComboBox { ItemsSource = vm.Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb) && !d.Capabilities.HasFlag(Capability.Virtual)).ToList(), DisplayMemberPath = "Name", Width = 250 };
        var first = new TextBox { Text = "0", Width = 60 };
        var last = new TextBox { Text = "0", Width = 60 };
        var color = new TextBox { Text = "#67E8C5", Width = 100 };
        var zone = new ComboBox { ItemsSource = vm.Devices.FirstOrDefault(d => d.Id == placement.DeviceId)?.Zones, DisplayMemberPath = "Name", Width = 180 };
        zone.SelectionChanged += (_, _) => { if (zone.SelectedItem is Zone z) { first.Text = z.Start.ToString(); last.Text = (z.Start + z.Count - 1).ToString(); } };
        void Apply(bool clear)
        {
            var start = int.Parse(first.Text);
            var end = int.Parse(last.Text);
            if (start < 0 || end < start || end >= placement.LedCount)
                throw new InvalidDataException("LED range is outside this device.");
            var rgb = Rgb.Parse(color.Text);
            vm.Record();
            for (var i = start; i <= end; i++)
            if (clear)
                placement.LedOverrides.Remove(i);
            else
                placement.LedOverrides[i] = rgb.Hex;
            vm.Status = clear ? "Selected LEDs use the layered effect again." : "Color assigned to selected LEDs.";
        }
        return Stack(Text("Hardware assignment", 15), hardware, Action("Assign selected device", () =>
        {
            if (hardware.SelectedItem is not Device device) throw new InvalidOperationException("Connect RGB and select a detected device first.");
            vm.Record();
            vm.Profile.Layout.RemoveAll(p => p != placement && p.DeviceId == device.Id);
            placement.DeviceId = device.Id; placement.LedCount = Math.Max(1, device.LedCount);
            foreach (var i in placement.LedOverrides.Keys.Where(i => i >= placement.LedCount).ToList()) placement.LedOverrides.Remove(i);
            vm.RefreshProfile();
        }), Text("LED / zone colors", 15), Text("Use inclusive LED indexes starting at 0, or select a zone.", 11, "#959FA7"), zone,
            Row(first, last, color), Row(Action("Choose color", () =>
            {
                using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true };
                if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    color.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
            }), Action("Set color", () => Apply(false)), Action("Clear override", () => Apply(true))));
    }
    UIElement Lighting()
    {
        var inspector = new ContentControl();
        void Inspect(Placement p)
        {
            inspector.Content = Card(Stack(Text(p.Name, 17), Row(Field("Name", p, nameof(p.Name)), Field("Group", p, nameof(p.Group))), Row(Field("X", p, nameof(p.X), 90), Field("Y", p, nameof(p.Y), 90), Field("Rotation", p, nameof(p.Rotation), 100)), Row(Field("Width", p, nameof(p.Width), 90), Field("Height", p, nameof(p.Height), 90), Field("LEDs", p, nameof(p.LedCount), 90)), Choice("Color order", p, nameof(p.ColorOrder), new[] { "RGB", "GRB", "BGR", "BRG", "RBG", "GBR" }), Text(p.DeviceId.StartsWith("virtual:") ? "Virtual preview device" : "Hardware LED count must match the detected controller.", 11, "#959FA7"), LedEditor(p), Action("Remove from layout", () => { vm.Record(); vm.Profile.Layout.Remove(p); vm.RefreshProfile(); })));
        }
        var canvas = new SetupCanvas(vm, true) { Height = 430 };
        canvas.Selected += Inspect;
        if (vm.Profile.Layout.FirstOrDefault() is { } first)
            Inspect(first);
        else
            inspector.Content = Card(Text("Select a device on the canvas to edit its placement and color order.", 13, "#959FA7"));
        var zones = new StackPanel();
        foreach (var d in vm.Devices.Where(d => d.Capabilities.HasFlag(Capability.Rgb)))
            foreach (var z in d.Zones)
            {
                var count = new TextBox { Text = z.Count.ToString(), Width = 90 };
                zones.Children.Add(Row(Text(d.Name + " / " + z.Name, 12), count, Button("Resize channel", async () => { if (!z.Resizable) throw new InvalidOperationException("This zone has a fixed size."); await vm.Rgb.ResizeAsync(d.Id, int.Parse(z.Id), int.Parse(count.Text), CancellationToken.None); await vm.ConnectRgb(); })));
            }
        return Stack(Text("Device layout", 28), Text("Drag devices into position. Effects follow their spatial coordinates.", 13, "#959FA7"), Row(Button("Connect / refresh RGB", vm.ConnectRgb, true), Action("Add preview device", vm.AddVirtual), Action("Undo", vm.Undo), Action("Redo", vm.Redo), Check("Lighting output", vm, nameof(vm.LightingEnabled))), Columns(Card(canvas), inspector), Card(Stack(Text("RGB channels", 17), zones, Text("ARGB headers showing 0 LEDs will stay dark. Set each attached header to its actual LED count, then select Resize channel. Leave unused headers at 0.", 12, "#959FA7"))));
    }
    UIElement Studio()
    {
        var root = new StackPanel();
        root.Children.Add(Text("Effect studio", 28));
        root.Children.Add(Text("Layers, keyframes, and signal-driven effects in one workspace.", 13, "#959FA7"));
        var selector = new ComboBox { ItemsSource = vm.Profile.Layers, DisplayMemberPath = "Name", SelectedIndex = 0, Width = 210 };
        var editor = new ContentControl();
        void Layer()
        {
            if (selector.SelectedItem is not EffectLayer e)
                return;
            var frameGrid = new DataGrid { ItemsSource = new ObservableCollection<Keyframe>(e.Keyframes), IsReadOnly = false, Height = 180 };
            frameGrid.BeginningEdit += (_, _) => vm.Record();
            EditColumn(frameGrid, "TIME (s)", "Time");
            EditColumn(frameGrid, "COLOR", "Color");
            frameGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "HOLD FRAME", Binding = new Binding("Hold") });
            var nodes = new ObservableCollection<NodeEditorRow>(e.Nodes.Select(n => new NodeEditorRow(n)));
            var graphGrid = new DataGrid { ItemsSource = nodes, IsReadOnly = false, Height = 230 };
            graphGrid.BeginningEdit += (_, _) => vm.Record();
            EditColumn(graphGrid, "ID", "Id");
            graphGrid.Columns.Add(new DataGridComboBoxColumn { Header = "TYPE", ItemsSource = Effects.NodeTypes, SelectedItemBinding = new Binding("Type"), Width = new(1, DataGridLengthUnitType.Star) });
            EditColumn(graphGrid, "INPUT IDs", "InputText");
            EditColumn(graphGrid, "VALUE", "Value");
            EditColumn(graphGrid, "COLOR", "Color");
            EditColumn(graphGrid, "SOURCE", "Source");
            var form = Card(Stack(Text("Layer controls", 17), Row(Field("Layer name", e, "Name"), Choice("Effect", e, "Kind", Enum.GetValues<EffectKind>()), Choice("Blend", e, "Blend", Enum.GetValues<BlendMode>())), Row(Field("Color A", e, "Color", 150), Field("Color B", e, "Color2", 150), Field("Speed", e, "Speed", 100), Field("Brightness 0–1", e, "Brightness", 120), Field("Opacity 0–1", e, "Opacity", 110), Field("Direction °", e, "Direction", 110)), Row(Field("Group / All", e, "Group"), Field("Mask start 0–1", e, "MaskStart", 140), Field("Mask end 0–1", e, "MaskEnd", 140)), Choice("Sensor source", e, "SensorId", vm.Sensors, "Name", "Id", 320), Field("Event source (health, damage, ammo)", e, "SensorId", 320), Row(Action("Validate & save", () => { Effects.ValidateGraph(e); vm.Save(); }, true), Action("Move layer earlier", () => { var i = vm.Profile.Layers.IndexOf(e); if (i > 0) { vm.Record(); vm.Profile.Layers.RemoveAt(i); vm.Profile.Layers.Insert(i - 1, e); vm.RefreshProfile(); } }), Action("Move layer later", () => { var i = vm.Profile.Layers.IndexOf(e); if (i < vm.Profile.Layers.Count - 1) { vm.Record(); vm.Profile.Layers.RemoveAt(i); vm.Profile.Layers.Insert(i + 1, e); vm.RefreshProfile(); } }), Action("Duplicate layer", () => { vm.Record(); var copy = Json.Clone(e); copy.Name += " copy"; vm.Profile.Layers.Add(copy); vm.RefreshProfile(); }), Action("Delete layer", () => { vm.Record(); vm.Profile.Layers.Remove(e); vm.RefreshProfile(); }))));
            var timeline = new TimelineCanvas(e, vm);
            var timelineCard = Card(Stack(Text("Timeline", 17), Text("Set the layer effect to Timeline. Drag keyframes at 30 fps; arrow keys move one frame. Hold keeps the color until the next keyframe.", 12, "#959FA7"), timeline, frameGrid, Row(Action("Add keyframe", () => { vm.Record(); var k = new Keyframe { Time = e.Keyframes.Select(k => k.Time).DefaultIfEmpty(0).Max() + 1 }; e.Keyframes.Add(k); ((ObservableCollection<Keyframe>)frameGrid.ItemsSource).Add(k); }), Action("Remove keyframe", () => { if (frameGrid.SelectedItem is Keyframe k) { vm.Record(); e.Keyframes.Remove(k); ((ObservableCollection<Keyframe>)frameGrid.ItemsSource).Remove(k); } }))));
            var graphCard = Card(Stack(Text("Node graph", 17), new NodeCanvas(e, vm), Text("Drag nodes to arrange. Shift-click a source, then Shift-click a destination to connect.", 12, "#959FA7"), Text("Color ports: Color, Gradient, Colorize, Transform, Mask, Blend. Scalar signals: Time, Noise, Audio, Sensor, Event. Colorize turns a signal into brightness. Blend accepts two colors and an optional scalar; Mask accepts a color and optional scalar. Cycles and type mismatches are rejected.", 12, "#959FA7"), graphGrid, Field("Output node ID", e, "OutputNode"), Row(Action("Add node", () => { vm.Record(); var n = new EffectNode(); e.Nodes.Add(n); nodes.Add(new(n)); }), Action("Delete node", () => { if (graphGrid.SelectedItem is NodeEditorRow n) { vm.Record(); e.Nodes.Remove(n.Node); nodes.Remove(n); } }), Action("Validate graph", () => { graphGrid.CommitEdit(); Effects.ValidateGraph(e); vm.Status = "Graph validated. Select Graph as the layer effect to render it."; }))));
            editor.Content = Stack(form, timelineCard, graphCard);
        }
        selector.SelectionChanged += (_, _) => Layer();
        root.Children.Add(Row(selector, Action("Add layer", () => { vm.Record(); vm.Profile.Layers.Add(new() { Name = "Layer " + (vm.Profile.Layers.Count + 1) }); vm.RefreshProfile(); }), Action("Undo", vm.Undo), Action("Redo", vm.Redo)));
        root.Children.Add(Card(new SetupCanvas(vm) { Height = 220 }));
        root.Children.Add(editor);
        Layer();
        root.Children.Add(Card(Stack(Text("Reactive inputs", 17), Row(Button("Start system audio", () => { vm.Inputs.StartAudio(); vm.Status = vm.Inputs.AudioState; return Task.CompletedTask; }), Action("Stop audio", vm.Inputs.StopAudio), Button("Choose screen / window", async () => { await vm.Inputs.StartScreen(new System.Windows.Interop.WindowInteropHelper(window).Handle); vm.Status = vm.Inputs.ScreenState; }), Action("Stop capture", vm.Inputs.StopScreen)), Text("Audio and screen content are processed locally. Start capture explicitly to feed reactive effects.", 12, "#959FA7"))));
        return root;
    }
    UIElement Cooling()
    {
        var body = new ContentControl();
        var selector = new ComboBox { ItemsSource = vm.Fans, DisplayMemberPath = "Name", Width = 320 };
        void Fan()
        {
            if (selector.SelectedItem is not FanChannel channel)
                return;
            var policy = vm.Profile.Fans.FirstOrDefault(f => f.ChannelId == channel.Id);
            if (policy is null)
            {
                policy = new()
                {
                    ChannelId = channel.Id,
                    Pump = channel.Pump
                };
                vm.Profile.Fans.Add(policy);
            }
            var p = policy; var templates = new ComboBox { ItemsSource = vm.Profile.Fans.Where(f => string.IsNullOrEmpty(f.ChannelId)).ToList(), DisplayMemberPath = "Mix", Width = 180 };
            if (p.Sources.Count == 0)
                p.Sources.Add(new());
            var source = p.Sources[0];
            var stopTest = new CheckBox { Content = "Also validate stop and restart (fans only)", IsEnabled = !channel.Pump && channel.Min == 0 };
            var selectedRpm = new ComboBox { ItemsSource = vm.Sensors.Where(s => s.Sample.Kind == SensorKind.Fan).ToList(), DisplayMemberPath = "Name", SelectedValuePath = "Id", Width = 250 };
            DataGrid? points = null; var curve = new PlotCanvas { BeginEdit = vm.Record, Points = () => source.Points.Select(p => (p.Temperature, p.Duty)).ToList(), Edit = (i, x, y) => { if (i == source.Points.Count - 1) y = 100; var low = i == 0 ? 0 : source.Points[i - 1].Temperature + 1; var high = i == source.Points.Count - 1 ? 85 : source.Points[i + 1].Temperature - 1; if (low > high) return; source.Points[i] = new(Math.Clamp(x, low, high), y); if (points != null) points.ItemsSource = new ObservableCollection<EditablePoint>(source.Points.Select(p => new EditablePoint(p))); } };
            points = new DataGrid { ItemsSource = new ObservableCollection<EditablePoint>(source.Points.Select(p => new EditablePoint(p))), Height = 160, IsReadOnly = false };
            EditColumn(points, "TEMPERATURE °C", "Temperature");
            EditColumn(points, "DUTY %", "Duty");
            var presets = new ComboBox { ItemsSource = new[] { "Quiet", "Balanced", "Gaming", "Maximum Cooling" }, SelectedIndex = 1, Width = 190 };
            presets.SelectionChanged += (_, _) => { source.Points = CoolingEngine.Preset((string)presets.SelectedItem); points.ItemsSource = new ObservableCollection<EditablePoint>(source.Points.Select(p => new EditablePoint(p))); curve.InvalidateVisual(); };
            var sourceGrid = new DataGrid { ItemsSource = new ObservableCollection<CurveSource>(p.Sources), Height = 130, IsReadOnly = false };
            sourceGrid.Columns.Add(new DataGridComboBoxColumn { Header = "TEMPERATURE SOURCE", ItemsSource = vm.Sensors.Where(s => s.Sample.Kind == SensorKind.Temperature).ToList(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValueBinding = new Binding("SensorId"), Width = new(1, DataGridLengthUnitType.Star) });
            sourceGrid.SelectionChanged += (_, _) => { if (sourceGrid.SelectedItem is CurveSource selected) { source = selected; points.ItemsSource = new ObservableCollection<EditablePoint>(source.Points.Select(p => new EditablePoint(p))); curve.InvalidateVisual(); } };
            body.Content = Stack(Card(Stack(Text(channel.Name, 22), Row(Text("Imported cooling template", 12), templates, Action("Map imported settings", () => { if (templates.SelectedItem is not FanPolicy template) throw new InvalidOperationException("Import a profile with cooling settings first."); vm.Record(); var mapped = Json.Clone(template); mapped.ChannelId = channel.Id; mapped.Enabled = false; mapped.Calibrated = false; mapped.AllowStop = false; mapped.Pump = channel.Pump; vm.Profile.Fans.Remove(p); vm.Profile.Fans.Add(mapped); Fan(); })), Text(channel.Evidence, 12, "#959FA7"), Text("Confirm the physical fan and RPM mapping before commissioning. The sweep stays between 30% and 100%; pumps stay at or above 70%. Firmware control is restored when the sweep ends.", 13, "#AEB8BD"), Row(Choice("Guard temperature", source, "SensorId", vm.Sensors.Where(s => s.Sample.Kind == SensorKind.Temperature).ToList(), "Name", "Id", 290), Stack(Text("RPM feedback", 11, "#959FA7"), selectedRpm), Check("This is a pump", p, "Pump"), stopTest), Button("Commission this channel", async () =>
            {
                if (System.Windows.MessageBox.Show(window, "This will change this channel's speed for about 20 seconds. Confirm you have identified the physical fan/pump, RPM sensor, and temperature guard. Continue?", "Commission cooling channel", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                var result = await PipeClient.Call<CalibrationResult>("calibrate", new CalibrationRequest(channel.Id, selectedRpm.SelectedValue as string ?? "", source.SensorId, p.Pump, true, stopTest.IsChecked == true), 120000);
                p.Minimum = result.Minimum;
                p.StartDuty = result.StartDuty;
                p.Calibrated = true;
                p.AllowStop = result.CanStop;
                vm.Status = $"Commissioned. Minimum {result.Minimum}% · start {result.StartDuty}%.";
            }, true))), Columns(Card(Stack(Text("Fan response", 17), presets, curve, points, Row(Action("Use edited points", () => { points.CommitEdit(); source.Points = ((ObservableCollection<EditablePoint>)points.ItemsSource).Select(p => new CurvePoint(p.Temperature, p.Duty)).OrderBy(p => p.Temperature).ToList(); _ = CoolingEngine.Interpolate(source.Points, 50); curve.InvalidateVisual(); }), Action("Add point", () => ((ObservableCollection<EditablePoint>)points.ItemsSource).Add(new(new(60, 60))))))), Card(Stack(Text("Response behavior", 17), Check("Enable this policy", p, "Enabled"), Check("Manual duty (temperature guard stays active)", p, "Manual"), Field("Manual duty %", p, "ManualDuty"), Field("Minimum duty %", p, "Minimum"), Field("Starting duty %", p, "StartDuty"), Field("Rise % / second", p, "RisePerSecond"), Field("Fall % / second", p, "FallPerSecond"), Field("Hysteresis °C", p, "Hysteresis"), Check("Allow validated fan stop", p, "AllowStop")))), Card(Stack(Text("Mix temperature sources", 17), sourceGrid, Row(Choice("Mix function", p, "Mix", Enum.GetValues<MixMode>()), Check("Mix temperatures before applying first curve", p, "MixTemperatures"), Action("Add source", () => { var s = new CurveSource { Points = source.Points.ToList() }; p.Sources.Add(s); ((ObservableCollection<CurveSource>)sourceGrid.ItemsSource).Add(s); })), Action("Remove selected source", () => { if (sourceGrid.SelectedItem is CurveSource selected && p.Sources.Count > 1) { p.Sources.Remove(selected); ((ObservableCollection<CurveSource>)sourceGrid.ItemsSource).Remove(selected); sourceGrid.SelectedIndex = 0; } }), Text("Select a source row to edit its independent response curve. Composite mode uses the first curve after mixing temperatures.", 12, "#959FA7"))), Row(Button("Apply cooling policies", vm.ApplyCooling, true), Button("Restore firmware control", vm.RestoreCooling)));
        }
        selector.SelectionChanged += (_, _) => Fan();
        if (vm.Fans.Count > 0)
            selector.SelectedIndex = 0;
        if (vm.Fans.Count == 0)
            body.Content = Card(Stack(Text("No controllable fan channels available", 19), Text("The hardware service must be installed and able to access a supported controller. Sensor readings alone do not imply fan-write support.", 13, "#959FA7"), Action("Open service setup", () => window.Go("Settings"))));
        return Stack(Text("Cooling control", 28), Text("Tune the relationship between temperature, airflow, and noise.", 13, "#959FA7"), Row(selector, Action("Undo", vm.Undo), Action("Redo", vm.Redo)), body);
    }
    UIElement Monitoring()
    {
        var search = new TextBox { Width = 300, ToolTip = "Filter sensors by name or type" };
        var view = new ListCollectionView(vm.Sensors);
        search.TextChanged += (_, _) => { view.Filter = o => o is SensorRow r && (r.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || r.Kind.Contains(search.Text, StringComparison.OrdinalIgnoreCase)); };
        var grid = Grid(view, ("SENSOR", "Name"), ("TYPE", "Kind"), ("CURRENT", "Value"), ("MIN¹", "Min"), ("MAX¹", "Max"), ("AVERAGE¹", "Average"));
        grid.MaxHeight = 440;
        var plot = new PlotCanvas { MaxX = 60, MaxY = 100, XLabel = "Minutes in selected window", YLabel = "Sensor value · native unit" };
        var hours = new ComboBox { ItemsSource = new[] { 1, 6, 24, 168 }, SelectedIndex = 0, Width = 110 };
        var inspector = new ContentControl();
        void Draw()
        {
            if (grid.SelectedItem is not SensorRow sensor)
                return;
            var duration = (int)(hours.SelectedItem ?? 1);
            var data = vm.History(sensor.Id, duration);
            var start = DateTimeOffset.UtcNow.AddHours(-duration);
            plot.MaxX = duration * 60;
            plot.MaxY = Math.Max(1, data.Select(p => p.Value).DefaultIfEmpty(100).Max() * 1.15);
            plot.Points = () => data.Select(p => ((p.At - start).TotalMinutes, p.Value)).ToList();
            plot.InvalidateVisual();
        }
        grid.SelectionChanged += (_, _) =>
        {
            Draw();
            if (grid.SelectedItem is not SensorRow sensor)
                return;
            var alias = new TextBox { Text = sensor.Name, Width = 250 };
            var rule = vm.Settings.Alerts.FirstOrDefault(r => r.SensorId == sensor.Id) ?? new AlertRule { SensorId = sensor.Id };
            inspector.Content = Row(alias, Action("Rename", () => { vm.Settings.SensorNames[sensor.Id] = alias.Text; vm.Save(); }), Action("Pin / unpin", () => { if (!vm.Settings.PinnedSensors.Remove(sensor.Id)) vm.Settings.PinnedSensors.Add(sensor.Id); vm.Save(); }), Field("Alert above", rule, "Above", 130), Field("Alert below", rule, "Below", 130), Action("Save alert", () => { if (!vm.Settings.Alerts.Contains(rule)) vm.Settings.Alerts.Add(rule); vm.Save(); }));
        };
        hours.SelectionChanged += (_, _) => Draw();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Draw();
        timer.Start();
        plot.Unloaded += (_, _) => timer.Stop();
        return Stack(Text("System monitoring", 28), Text("Live readings and seven-day history. Missing sensors remain explicitly unavailable.", 13, "#959FA7"), Row(search, Button("Export history CSV", () => { if (SavePath("mk-rgb-telemetry.csv", "CSV|*.csv") is { } path) vm.ExportTelemetry(path); return Task.CompletedTask; }), Action("Toggle desktop overlay", window.ToggleOverlay)), Card(Stack(grid, Text("¹ Statistics use native sensor units; temperature statistics remain in °C. Values reset when MK RGB starts.", 11, "#959FA7"))), Card(Stack(Row(Text("History", 17), hours, Text("hours", 12, "#959FA7")), plot, inspector)));
    }
    UIElement Automation()
    {
        var rules = new ObservableCollection<AutomationRule>(vm.Settings.Rules);
        var grid = new DataGrid { ItemsSource = rules, IsReadOnly = false, Height = 320 };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "ON", Binding = new Binding("Enabled") });
        EditColumn(grid, "NAME", "Name");
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "TRIGGER", ItemsSource = Enum.GetValues<TriggerKind>(), SelectedItemBinding = new Binding("Trigger") });
        EditColumn(grid, "SOURCE", "Source");
        EditColumn(grid, "THRESHOLD", "Threshold");
        EditColumn(grid, "START", "Start");
        EditColumn(grid, "END", "End");
        EditColumn(grid, "PRIORITY", "Priority");
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "PROFILE", ItemsSource = vm.Settings.Profiles, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValueBinding = new Binding("ProfileId") });
        var playlist = new ListBox { Background = Brush("#171C1F"), Foreground = Brush("#EDF0F1"), SelectionMode = SelectionMode.Multiple, ItemsSource = vm.Settings.Profiles, DisplayMemberPath = "Name", Height = 150 };
        foreach (var p in vm.Settings.Profiles.Where(p => vm.Settings.Playlist.Contains(p.Id)))
            playlist.SelectedItems.Add(p);
        return Stack(Text("Profile automations", 28), Text("Rules activate complete profiles. Higher priorities win; manual switching pauses rules for 15 minutes.", 13, "#959FA7"), Card(Stack(grid, Row(Action("Add rule", () => { var r = new AutomationRule { ProfileId = vm.Profile.Id }; rules.Add(r); vm.Settings.Rules.Add(r); }), Action("Remove rule", () => { if (grid.SelectedItem is AutomationRule r) { rules.Remove(r); vm.Settings.Rules.Remove(r); } }), Action("Save rules", () => { grid.CommitEdit(); vm.Save(); }), Action("Resume automatic switching", () => { vm.ManualUntil = null; vm.Status = "Automation resumed."; })), Text("Application: executable name. Temperature: sensor ID and threshold in °C. Battery: percentage at or below threshold. AC power: 1 plugged in / 0 unplugged. Schedule: local 24-hour time, including overnight intervals.", 12, "#959FA7"))), Columns(Card(Stack(Text("Profile playlist", 17), playlist, Check("Enable playlist when no rule matches", vm.Settings, "PlaylistEnabled"), Field("Seconds per profile", vm.Settings, "PlaylistSeconds"), Action("Save playlist", () => { vm.Settings.Playlist = playlist.SelectedItems.Cast<Profile>().Select(p => p.Id).ToList(); vm.Save(); vm.ManualUntil = null; }))), Card(Stack(Text("CS2 & local events", 17), Text("Health, damage, ammo, and round state become lighting inputs. Configuration is installed only in the folder you select.", 13, "#959FA7"), Check("Enable local event API", vm.Settings, "ApiEnabled"), Button("Apply API setting", vm.RestartApi), Action("Save CS2 integration file", () => { if (SavePath("gamestate_integration_mkrgb.cfg", "CS2 config|*.cfg") is { } path) { File.WriteAllText(path, EventServer.Cs2Config(vm.Settings)); vm.Status = "Save this file in CS2's game/csgo/cfg folder and restart the game."; } }), Action("Copy API token", () => { System.Windows.Clipboard.SetText(vm.Settings.ApiToken); vm.Status = "API token copied. Keep it private."; })))));
    }
    static UIElement CatalogPreview(CatalogItem item)
    {
        var gradient = new LinearGradientBrush();
        var colors = item.PreviewColors ?? ["#67E8C5", "#707BFF"];
        for (var i = 0; i < colors.Length; i++)
        {
            try
            {
                var c = Rgb.Parse(colors[i]);
                gradient.GradientStops.Add(new(Color.FromRgb(c.R, c.G, c.B), i / (double)Math.Max(1, colors.Length - 1)));
            }
            catch (FormatException) { }
        }
        return new Border { Height = 28, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left, Width = 300, CornerRadius = new(4), Background = gradient, Margin = new(0, 2, 0, 14) };
    }
    UIElement Library()
    {
        var list = new StackPanel();
        var search = new TextBox { Width = 300 };
        void Populate()
        {
            list.Children.Clear();
            foreach (var item in vm.Catalog.Where(i => i.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || i.Tags.Any(t => t.Contains(search.Text, StringComparison.OrdinalIgnoreCase))))
                list.Children.Add(Card(Columns(Stack(Text(item.Name, 20), CatalogPreview(item), Text(item.Compatibility, 11, "#959FA7"), Text(item.Description, 13, "#A5B1B8"), Text($"{item.Kind} · v{item.Version} · " + string.Join(" / ", item.Tags), 11, "#78E8C2")), Button(vm.Settings.InstalledCatalogVersions.TryGetValue(item.Id, out var installed) ? installed == item.Version ? "Add another copy" : "Add updated version" : "Add to my profiles", () => vm.InstallCatalog(item), true), 3, 1)));
        }
        search.TextChanged += (_, _) => Populate();
        System.Collections.Specialized.NotifyCollectionChangedEventHandler update = (_, _) => Populate();
        vm.Catalog.CollectionChanged += update;
        list.Unloaded += (_, _) => vm.Catalog.CollectionChanged -= update;
        Populate();
        return Stack(Text("Community library", 28), Text("Reviewed, portable creations. Your downloaded profiles work offline.", 13, "#959FA7"), Row(search, Button("Refresh catalog", vm.LoadCatalog), Action("Import profile", () => { var d = new OpenFileDialog { Filter = "MK RGB profile|*.json" }; if (d.ShowDialog(window) == true) vm.Import(d.FileName); }), Action("Export current profile", () => { if (SavePath(vm.Profile.Name + ".json", "MK RGB profile|*.json") is { } path) vm.Export(path); })), list, Card(Stack(Text("Share your work", 17), Text("Submit effects and profiles through the repository's catalog contribution workflow. Hardware extensions are reviewed code and require an explicit administrator installation.", 13, "#959FA7"), Action("Open contribution guide", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb/blob/main/CONTRIBUTING.md")))));
    }
    UIElement Settings()
    {
        var conflicts = Sensors.Conflicts();
        return Stack(Text("Application settings", 28), Text("Preferences, recovery, and a clear view of what your hardware can do.", 13, "#959FA7"), Columns(Card(Stack(Text("Startup & background", 17), Check("Start MK RGB with Windows", vm.Settings, "StartWithWindows"), Check("Launch hidden in the system tray", vm.Settings, "StartInTray"), Check("Connect RGB when MK RGB starts", vm.Settings, "ConnectRgbOnLaunch"), Check("Enable lighting when MK RGB starts", vm.Settings, "EnableLightingOnLaunch"), Check("Ask competing RGB apps to close when connecting", vm.Settings, "CloseRgbAppsOnConnect"), Button("Close competing RGB apps now", vm.CloseRgbApps), Text("Closing the window keeps MK RGB running in the tray. Fan controllers and background services are never terminated automatically.", 12, "#959FA7"), Text("Appearance & performance", 17), Check("Display temperatures in Fahrenheit", vm.Settings, "Fahrenheit"), Check("Reduce interface motion", vm.Settings, "ReducedMotion"), Choice("Lighting frame rate", vm.Settings, "Fps", new[] { 15, 30, 60 }), Field("Telemetry retention days", vm.Settings, "RetentionDays"), Action("Save preferences", vm.SavePreferences, true), Field("Current profile name", vm.Profile, "Name"), Action("Create a profile", vm.NewProfile))), Card(Stack(Text("Hardware service", 17), Text(vm.Connection, 13, "#78E8C2"), Text("Install the packaged application to register the hardware service. Closing this window leaves cooling policies active in the service. Exiting the tray app stops reactive lighting inputs.", 13, "#A5B1B8"), Text("Conflict scan: " + (conflicts.Count == 0 ? "no known competing controllers found" : string.Join(", ", conflicts)), 12, "#959FA7"), Button("Restore firmware fan control", vm.RestoreCooling), Action("Export diagnostic report", () => { if (SavePath("mk-rgb-diagnostics.json", "JSON|*.json") is { } path) vm.Diagnostics(path); }), Action("Compatibility guide", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb/blob/main/docs/compatibility.md"))))), Card(Stack(Text("Profiles & recovery", 17), Row(Action("Back up settings", () => { if (SavePath("mk-rgb-backup.json", "JSON|*.json") is { } path) File.WriteAllText(path, Json.Write(vm.Settings)); }), Action("Restore backup", () => { var d = new OpenFileDialog { Filter = "Settings backup|*.json" }; if (d.ShowDialog(window) != true) return; var incoming = Json.Read<Core.Settings>(File.ReadAllText(d.FileName)); SettingsStore.Validate(incoming); foreach (var p in incoming.Profiles) foreach (var f in p.Fans) { f.Enabled = false; f.Calibrated = false; } new SettingsStore(Paths.UserData).Save(incoming); vm.Status = "Backup restored with fan policies disabled. Exit and restart MK RGB to load it."; }), Action("Export current profile", () => { if (SavePath(vm.Profile.Name + ".json", "Profile|*.json") is { } path) vm.Export(path); })), Text("Imported cooling settings must be mapped to this machine before activation. The previous settings file is kept as a .bak file.", 12, "#959FA7"))), Card(Stack(Text("Updates & open source", 17), Text("MK RGB 0.1.1 · GPLv3\nOpenRGB and LibreHardwareMonitor retain their own licenses. No account is required for core operation.", 13, "#A5B1B8"), Row(Button("Check for updates", vm.CheckUpdates), Action("Open Releases", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb/releases")), Action("Source code", () => window.OpenUrl("https://github.com/MKonline08/mk-rgb")), Action("Exit MK RGB", window.ExitApp)))));
    }
}
public sealed class EditablePoint(CurvePoint p)
{
    public double Temperature { get; set; } = p.Temperature; public double Duty { get; set; } = p.Duty;
}
public sealed class NodeEditorRow(EffectNode node)
{
    public EffectNode Node => node; public string Id
    {
        get => node.Id; set => node.Id = value;
    }
    public string Type
    {
        get => node.Type; set => node.Type = value;
    }
    public double Value
    {
        get => node.Value; set => node.Value = value;
    }
    public string Color
    {
        get => node.Color; set => node.Color = value;
    }
    public string Source
    {
        get => node.Source; set => node.Source = value;
    }
    public string InputText
    {
        get => string.Join(",", node.Inputs); set => node.Inputs = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}

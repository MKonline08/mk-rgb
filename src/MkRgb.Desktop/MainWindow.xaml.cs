using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MkRgb.Core;

namespace MkRgb.Desktop;

public partial class MainWindow : Window
{
    MainViewModel? vm; Pages? pages; System.Windows.Forms.NotifyIcon? tray; Window? overlay; DispatcherTimer? overlayTimer; bool exiting; string current = "Lighting"; string? animatedPage;
    public MainWindow()
    {
        InitializeComponent();
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/mk-rgb.ico"));
        vm = new();
        DataContext = vm;
        pages = new(vm, this);
        vm.ProfileChanged += () => { Profiles.Items.Refresh(); Render(); };
        vm.Alert += message => { tray?.ShowBalloonTip(5000, "MK RGB", message, System.Windows.Forms.ToolTipIcon.Warning); vm.Status = message; };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is "" or null or nameof(MainViewModel.RgbStatus) or nameof(MainViewModel.ServiceStatus) or nameof(MainViewModel.Scanning)) UpdateIndicators(); };
        Loaded += (_, _) => { Render(); vm.Start(); SetupTray(); var handle = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)?.AddHook(Hook); RegisterHotKey(handle, 1, 0x0002 | 0x0001, 0x4D); if (vm.Settings.StartInTray || Environment.GetCommandLineArgs().Contains("--background")) Hide(); };
        Closing += OnClosing;
        VersionText.Text = $"v{App.VersionLabel} \u00B7 preview";
        SourceInitialized += (_, _) => DarkFrame();
        StateChanged += (_, _) => FitMaximized();
        Loaded += (_, _) => StartAmbientAnimations();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Status)) Flash(StatusText); };
    }
    void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseClick(object sender, RoutedEventArgs e) => Close();
    /// <summary>A maximized custom-chrome window overhangs the screen by its resize border; pull the content back in.</summary>
    void FitMaximized()
    {
        var maximized = WindowState == WindowState.Maximized;
        var border = SystemParameters.WindowResizeBorderThickness;
        Frame.Margin = maximized ? new Thickness(border.Left + 4, border.Top + 4, border.Right + 4, border.Bottom + 4) : new Thickness(0);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }
    /// <summary>Windows 11: dark frame, rounded corners, and a border that matches the app instead of the white system default.</summary>
    void DarkFrame()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int on = 1, round = 2, border = 0x0037312B; // COLORREF is 0x00BBGGRR: #2B3137
        DwmSetWindowAttribute(handle, 20, ref on, sizeof(int));     // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
    }
    bool Calm => vm?.Settings.ReducedMotion == true;
    /// <summary>Loads the fire logo; unless motion is reduced, its flame flickers and its glow breathes.</summary>
    void StartAmbientAnimations()
    {
        var logo = (FrameworkElement)System.Windows.Application.LoadComponent(new Uri("Assets/mk-logo.xaml", UriKind.Relative));
        // At title-bar size the letters blur, and "MK RGB" is written right next to it; show the flame alone.
        if (logo.FindName("Letters") is UIElement letters)
            letters.Visibility = Visibility.Collapsed;
        LogoHost.Content = logo;
        if (Calm)
            return;
        // Each layer flickers on its own rhythm so the flame never looks like a loop.
        void Flicker(string name, double low, double high, double seconds)
        {
            if (logo.FindName(name) is not ScaleTransform scale)
                return;
            var wave = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(seconds) };
            var steps = new[] { 1, high, low + (high - low) * .4, high * .99, low, 1 };
            for (var i = 0; i < steps.Length; i++)
                wave.KeyFrames.Add(new EasingDoubleKeyFrame(steps[i], KeyTime.FromPercent(i / (double)(steps.Length - 1)), new SineEase()));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, wave);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1 + (1 - low) * .5, TimeSpan.FromSeconds(seconds / 2.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        Flicker("OuterScale", .95, 1.05, 1.7);
        Flicker("MidScale", .93, 1.06, 1.3);
        Flicker("CoreScale", .9, 1.08, 0.9);
        Flicker("TongueScale", .7, 1.15, 1.1);
        LogoGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 0.8, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        var ember = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4) };
        foreach (var (hex, at) in new[] { ("#FF6A00", 0.0), ("#FF3B1F", 1.6), ("#FFB020", 2.8), ("#FF6A00", 4.0) })
            ember.KeyFrames.Add(new LinearColorKeyFrame((Color)ColorConverter.ConvertFromString(hex), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
        LogoGlow.BeginAnimation(DropShadowEffect.ColorProperty, ember);
    }
    static void Flash(UIElement element) => element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(350)));
    /// <summary>Cards slide up and fade in one after another when a page opens.</summary>
    void EnterPage(UIElement page)
    {
        if (Calm)
            return;
        var heading = (TranslateTransform)Heading.RenderTransform;
        Heading.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        heading.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var parts = new List<UIElement>();
        foreach (var child in page is Panel panel ? panel.Children.Cast<UIElement>() : [page])
            if (child is Grid { ColumnDefinitions.Count: > 1 } columns)
                parts.AddRange(columns.Children.Cast<UIElement>());
            else
                parts.Add(child);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (var i = 0; i < parts.Count && i < 14; i++)
        {
            var delay = TimeSpan.FromMilliseconds(40 + i * 55);
            var slide = new TranslateTransform(0, 22);
            parts[i].RenderTransform = slide;
            parts[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = delay, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(460)) { BeginTime = delay, EasingFunction = ease });
        }
    }
    void SetupTray()
    {
        tray = new()
        {
            Text = "MK RGB",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Visible = true
        };
        tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open MK RGB", null, (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
        menu.Items.Add("Next profile", null, (_, _) => NextProfile());
        menu.Items.Add("Toggle overlay", null, (_, _) => ToggleOverlay());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { if (tray != null && vm != null) tray.Text = ("MK RGB · CPU " + vm.Cpu + " · GPU " + vm.Gpu)[..Math.Min(63, ("MK RGB · CPU " + vm.Cpu + " · GPU " + vm.Gpu).Length)]; };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }
    static readonly Dictionary<string, (string Title, string Subtitle)> headings = new()
    {
        ["Lighting"] = ("Lighting", "Pick a color and an effect, then apply it to your devices."),
        ["Devices"] = ("Devices", "Find your RGB hardware, set up fans and strips, and check which device is which."),
        ["Presets"] = ("Presets", "Ready-made looks you can add to your profiles."),
        ["Overview"] = ("Overview", "Temperatures, devices, and your lighting at a glance."),
        ["Monitoring"] = ("Monitoring", "Live sensor readings and history."),
        ["Cooling"] = ("Cooling", "Fan curves and channel commissioning (needs the hardware service)."),
        ["Settings"] = ("Settings", "Startup, appearance, recovery, and updates."),
        ["Layout"] = ("Layout", "Arrange devices in space and paint individual LEDs."),
        ["Studio"] = ("Effect studio", "Layered effects, timelines, and node graphs."),
        ["Automations"] = ("Automations", "Switch profiles automatically by app, time, or sensor."),
    };
    void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is ListBoxItem { Tag: string page })
        {
            current = page;
            Render();
        }
    }
    void Render()
    {
        if (pages is null)
            return;
        var (title, subtitle) = headings.GetValueOrDefault(current, (current, ""));
        PageHeading.Text = title;
        PageSubtitle.Text = subtitle;
        ProfileTools.Visibility = current is "Devices" or "Settings" or "Monitoring" or "Overview" ? Visibility.Collapsed : Visibility.Visible;
        var page = pages.Build(current);
        PageContent.Content = page;
        // Only animate real navigation, not the rebuilds that happen after scans or edits on the same page.
        if (animatedPage != current)
        {
            animatedPage = current;
            PageScroll.ScrollToTop();
            EnterPage(page);
        }
    }
    void UpdateIndicators()
    {
        if (vm is null)
            return;
        var good = (System.Windows.Media.Brush)FindResource("Accent");
        var idle = (System.Windows.Media.Brush)FindResource("Muted");
        var warn = (System.Windows.Media.Brush)FindResource("Warn");
        RgbDot.Fill = vm.Scanning ? warn : vm.RgbDeviceCount > 0 ? good : idle;
        ServiceDot.Fill = vm.ServiceConnected ? good : warn;
        // A ripple radiates from the dot while a scan is running.
        if (vm.Scanning && !Calm)
        {
            var span = TimeSpan.FromSeconds(1.2);
            RgbPulse.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, span) { RepeatBehavior = RepeatBehavior.Forever });
            RgbPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 2.8, span) { RepeatBehavior = RepeatBehavior.Forever });
            RgbPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 2.8, span) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            RgbPulse.BeginAnimation(OpacityProperty, null);
            RgbPulse.Opacity = 0;
        }
    }
    public void Go(string page)
    {
        if (current == page)
        {
            Render();
            return;
        }
        foreach (ListBoxItem item in Navigation.Items)
            if (item.Tag as string == page)
            {
                Navigation.SelectedItem = item;
                break;
            }
        if (current != page)
        {
            current = page;
            Render();
        }
    }
    void SaveProfile(object sender, RoutedEventArgs e)
    {
        if (vm != null)
            _ = vm.Run(() => { vm.Save(); Profiles.Items.Refresh(); return Task.CompletedTask; });
    }
    void NextProfile()
    {
        if (vm is null)
            return;
        var index = vm.Settings.Profiles.IndexOf(vm.Profile);
        vm.Profile = vm.Settings.Profiles[(index + 1) % vm.Settings.Profiles.Count];
    }
    public void ToggleOverlay()
    {
        if (overlay != null)
        {
            overlay.Close();
            overlay = null;
            overlayTimer?.Stop();
            return;
        }
        var text = new TextBlock { Margin = new(18), Foreground = System.Windows.Media.Brushes.White, FontSize = 14 };
        overlay = new Window { Title = "MK RGB · Telemetry", Width = 300, Height = 160, Topmost = true, ResizeMode = ResizeMode.CanResizeWithGrip, Content = text, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 28, 31)) };
        overlay.Show();
        overlayTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        overlayTimer.Tick += (_, _) => { if (vm != null) text.Text = $"CPU  {vm.Cpu}\nGPU  {vm.Gpu}\n" + string.Join("\n", vm.Sensors.Where(s => vm.Settings.PinnedSensors.Contains(s.Id)).Take(4).Select(s => s.Name + "  " + s.Value)); };
        overlayTimer.Start();
        overlay.Closed += (_, _) => { overlay = null; overlayTimer.Stop(); };
    }
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exiting)
        {
            e.Cancel = true;
            Hide();
            vm!.Status = "Running in the system tray.";
        }
    }
    public async void ExitApp()
    {
        if (exiting)
            return;
        exiting = true;
        tray?.Dispose();
        overlayTimer?.Stop();
        overlay?.Close();
        UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
        if (vm != null)
            await vm.DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }
    nint Hook(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x0312 && w == 1)
        {
            NextProfile();
            handled = true;
        }
        return 0;
    }
    [DllImport("user32.dll")] static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

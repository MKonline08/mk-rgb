using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MkRgb.Core;

namespace MkRgb.Desktop;

public partial class MainWindow : Window
{
    MainViewModel? vm; Pages? pages; System.Windows.Forms.NotifyIcon? tray; Window? overlay; DispatcherTimer? overlayTimer; bool exiting; string current = "Overview";
    public MainWindow()
    {
        InitializeComponent();
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/mk-rgb.ico"));
        vm = new();
        DataContext = vm;
        pages = new(vm, this);
        vm.ProfileChanged += () => { Profiles.Items.Refresh(); Render(); };
        vm.Alert += message => { tray?.ShowBalloonTip(5000, "MK RGB", message, System.Windows.Forms.ToolTipIcon.Warning); vm.Status = message; };
        Loaded += (_, _) => { Render(); vm.Start(); SetupTray(); var handle = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)?.AddHook(Hook); RegisterHotKey(handle, 1, 0x0002 | 0x0001, 0x4D); if (vm.Settings.StartInTray || Environment.GetCommandLineArgs().Contains("--background")) Hide(); };
        Closing += OnClosing;
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
    void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is ListBoxItem item)
        {
            current = item.Content.ToString()!;
            Render();
        }
    }
    void Render()
    {
        if (pages is null)
            return;
        PageHeading.Text = current;
        PageContent.Content = pages.Build(current);
    }
    public void Go(string page)
    {
        if (current == page)
        {
            Render();
            return;
        }
        foreach (ListBoxItem item in Navigation.Items)
            if (item.Content.ToString() == page)
            {
                Navigation.SelectedItem = item;
                break;
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
}

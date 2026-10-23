using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrbitProbe;

public partial class MainWindow : Window
{
    private readonly ProbeController _controller = new();
    private readonly DispatcherTimer _logPump = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        LogPathText.Text = $"Log: {ProbeLog.FilePath}";
        _controller.StatsChanged += s => StatsText.Text = s;
        _logPump.Tick += (_, _) => PumpLog();
        _logPump.Start();
        Loaded += (_, _) => { _loaded = true; _controller.Start(); };
        Closed += (_, _) => { _controller.Dispose(); ProbeLog.Close(); };
    }

    private void PumpLog()
    {
        bool any = false;
        foreach (var line in ProbeLog.Drain())
        {
            var item = new System.Windows.Controls.ListBoxItem { Content = line, Padding = new Thickness(2, 0, 2, 0) };
            if (line.Contains(" FAIL ")) item.Foreground = Brushes.Firebrick;
            else if (line.Contains(" PASS ")) item.Foreground = Brushes.SeaGreen;
            else if (line.Contains(" WARN ")) item.Foreground = Brushes.DarkOrange;
            else if (line.Contains(" SIG ")) item.Foreground = Brushes.SteelBlue;
            LogList.Items.Add(item);
            any = true;
        }
        while (LogList.Items.Count > 5000) LogList.Items.RemoveAt(0);
        if (any) LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var s = _controller.Settings;
        s.NativeDropTarget = VariantNative.IsChecked == true;
        s.UseDropTargetHelper = UseHelper.IsChecked == true;
        s.UseDropDescriptions = UseDescriptions.IsChecked == true;
        s.PlaceBelowDragImage = BelowDragImage.IsChecked == true;
        s.Animate = Animate.IsChecked == true;
        s.ArmOnOleEvent = ArmOle.IsChecked == true;
        s.ArmOnDragImage = ArmImage.IsChecked == true;
        s.ArmOnMouseHook = ArmHook.IsChecked == true;
        s.DiagnosticArmOnButton = ArmButton.IsChecked == true;
        _controller.ApplySettings();
    }

    private void OnCreateFixtures(object sender, RoutedEventArgs e)
    {
        try
        {
            var created = Fixtures.Create(Fixtures.DefaultFolder);
            ProbeLog.Info(created.Count == 0 ? $"Test files already present in {Fixtures.DefaultFolder}"
                                             : $"Created {created.Count} test file(s) in {Fixtures.DefaultFolder}");
            foreach (var f in Directory.EnumerateFiles(Fixtures.DefaultFolder))
                ProbeLog.Info($"  {Path.GetFileName(f),-24} sha256 {SafeOutput.Sha256(f)[..12]}…");
        }
        catch (Exception ex) { ProbeLog.Fail($"Creating test files failed: {ex.Message}"); }
    }

    private void OnOpenFixtures(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(Fixtures.DefaultFolder)) Process.Start("explorer.exe", Fixtures.DefaultFolder);
        else ProbeLog.Warn("C:\\OrbitTest does not exist yet: click 'Create test files' first");
    }

    private void OnPreview(object sender, RoutedEventArgs e) => _controller.Preview();
    private void OnResetStats(object sender, RoutedEventArgs e) => _controller.ResetStats();

    private void OnCopySummary(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_controller.Summary());
        ProbeLog.Info("Statistics copied to clipboard");
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (ProbeLog.FilePath is { } p) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
    }
}

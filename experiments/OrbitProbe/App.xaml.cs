using System.Windows;

namespace OrbitProbe;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ProbeLog.Open();

        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string dir = e.Args.Length > 1 ? e.Args[1]
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OrbitProbeSelfTest-{DateTime.Now:yyyyMMdd-HHmmss}");
            int failures = await SelfTest.RunAsync(dir);
            ProbeLog.Close();
            Shutdown(failures == 0 ? 0 : 1);
            return;
        }

        if (e.Args.Length > 1 && e.Args[0] == "--render")
        {
            RenderSamples(e.Args[1]);
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }

    /// <summary>Offscreen design-review renders of the wheel in each state (no screen capture involved).</summary>
    private static void RenderSamples(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var jpg = new[] { "photo.jpg" };
        var cases = new (string Name, WheelMode Mode, string[] Files, int Highlight, string Hub)[]
        {
            ("convert-1file", WheelMode.Convert, jpg, 1, "BMP"),
            ("convert-mixed", WheelMode.Convert, ["a.jpg", "b.png"], -2, "2 files"),
            ("tools", WheelMode.Tools, jpg, 0, "Rotate ↻"),
            ("actions", WheelMode.Actions, ["c.pdf"], 2, "Reveal"),
            ("waiting", WheelMode.Convert, [], -2, "Move here"),
        };
        foreach (bool dark in new[] { false, true })
        foreach (var c in cases)
        {
            var wheel = new WheelWindow(wpfDropTarget: false, darkOverride: dark);
            wheel.SetContent(c.Mode, Operations.ItemsFor(c.Mode, c.Files), c.Hub);
            wheel.Highlight(c.Highlight);
            foreach (double scale in new[] { 1.0, 1.5 })
                wheel.RenderToPng(System.IO.Path.Combine(dir, $"{c.Name}-{(dark ? "dark" : "light")}-{scale * 100:F0}.png"), scale);
            wheel.Close();
        }
    }
}

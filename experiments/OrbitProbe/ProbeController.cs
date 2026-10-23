using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrbitProbe.Native;

namespace OrbitProbe;

public sealed class ProbeSettings
{
    public bool NativeDropTarget { get; set; } = true;   // variant B (false = variant A, WPF AllowDrop)
    public bool UseDropTargetHelper { get; set; } = true;
    public bool UseDropDescriptions { get; set; } = true;
    public bool PlaceBelowDragImage { get; set; }
    public bool ArmOnOleEvent { get; set; } = true;
    public bool ArmOnDragImage { get; set; } = true;
    public bool ArmOnMouseHook { get; set; } = true;
    /// <summary>Diagnostic: ignore drag signals and open on Shift whenever the drag button is held (isolates H-1 from H-3).</summary>
    public bool DiagnosticArmOnButton { get; set; }
    public bool Animate { get; set; } = true;
}

/// <summary>
/// The probe's state machine: drag signals → Shift tap → wheel → OLE callbacks → operation → integrity check.
/// Everything runs on the UI thread; image work runs on a worker thread.
/// </summary>
public sealed class ProbeController : IDropSink, IDisposable
{
    private sealed class Session
    {
        public int Id;
        public double OpenedMs;
        public HashSet<DragSignal> SignalsAtOpen = [];
        public double? EnterMs;
        public string[] Files = [];
        public List<WheelItem> Items = [];
        public WheelMode Mode = WheelMode.Convert;
        public bool OverWheel, Dropped, FormatsLogged;
        public Task<Dictionary<string, string>>? HashesBefore;
    }

    private readonly DragSignalMonitor _signals = new();
    private readonly DispatcherTimer _poll;
    private readonly ProbeStats _stats = new();
    private WheelWindow? _wheel;
    private NativeDropTarget? _nativeTarget;
    private Session? _session;
    private int _sessionCounter;
    private bool _prevShift, _prevEsc, _prevButton;
    private DispatcherTimer? _deferredDismiss;

    public ProbeSettings Settings { get; } = new();
    public event Action<string>? StatsChanged;

    public ProbeController()
    {
        _poll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _poll.Tick += (_, _) => Poll();
        _signals.SignalChanged += OnSignalChanged;
        _signals.LeftButton += (down, injected) =>
        {
            if (injected) _stats.InjectedInputSeen = true;
            if (down) StartPolling();
        };
    }

    public void Start()
    {
        bool elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        ProbeLog.Info($"OrbitProbe {typeof(ProbeController).Assembly.GetName().Version} | {Environment.OSVersion} | " +
                      $".NET {Environment.Version} | elevated={elevated}");
        if (elevated) ProbeLog.Warn("Running ELEVATED: UIPI will block drops from Explorer. Restart as a normal user.");
        ProbeLog.Info($"SM_CXDRAG={Win32.GetSystemMetrics(Win32.SM_CXDRAG)} swapButtons={SwapButtons}");
        _signals.Start(Settings.ArmOnMouseHook);
        RebuildWheel();
        PublishStats();
    }

    // ---------- configuration ----------

    public void ApplySettings()
    {
        _signals.SetMouseHook(Settings.ArmOnMouseHook);
        if (Settings.DiagnosticArmOnButton) StartPolling();
        RebuildWheel();
        ProbeLog.Info($"Settings: target={(Settings.NativeDropTarget ? "B native IDropTarget" : "A WPF AllowDrop")}, " +
                      $"helper={Settings.UseDropTargetHelper}, descriptions={Settings.UseDropDescriptions}, " +
                      $"belowDragImage={Settings.PlaceBelowDragImage}, arm[ole={Settings.ArmOnOleEvent}, image={Settings.ArmOnDragImage}, " +
                      $"hook={Settings.ArmOnMouseHook}, diagnosticButton={Settings.DiagnosticArmOnButton}], animate={Settings.Animate}");
    }

    private void RebuildWheel()
    {
        Dismiss("rebuilding wheel", force: true);
        _nativeTarget?.Revoke();
        _nativeTarget = null;
        _wheel?.Close();

        _wheel = new WheelWindow(wpfDropTarget: !Settings.NativeDropTarget);
        if (Settings.NativeDropTarget)
        {
            _nativeTarget = new NativeDropTarget(_wheel.Handle, this, Settings.UseDropTargetHelper, Settings.UseDropDescriptions);
            int hr = _nativeTarget.Register();
            ProbeLog.Info($"RegisterDragDrop(native) hr=0x{hr:X8}");
        }
        else
        {
            _wheel.WpfDragEnter += (data, pt) =>
            {
                string[] files = data.GetDataPresent(DataFormats.FileDrop) ? (string[])data.GetData(DataFormats.FileDrop) : [];
                OnDragEnter(files, data.GetFormats(), pt, 0);
            };
            _wheel.WpfDragOver += pt => OnDragOver(pt, 0).Accept;
            _wheel.WpfDragLeave += OnDragLeave;
            _wheel.WpfDrop += (data, pt) =>
                OnDrop(data.GetDataPresent(DataFormats.FileDrop) ? (string[])data.GetData(DataFormats.FileDrop) : [], pt);
        }
    }

    // ---------- signals & polling ----------

    private static bool SwapButtons => Win32.GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0;
    private static bool DragButtonDown => Win32.IsDown(SwapButtons ? 0x02 /* VK_RBUTTON */ : Win32.VK_LBUTTON);

    private void OnSignalChanged(DragSignal s, bool active, string detail)
    {
        ProbeLog.Signal($"{s} → {(active ? "ACTIVE" : "inactive")} ({detail})");
        if (active) StartPolling();
        else if (_session is { } session && !AnyArmingSignal() && !DragButtonDown)
            RequestDismiss($"{s} ended", session);
    }

    private void StartPolling()
    {
        if (_poll.IsEnabled) return;
        _prevShift = Win32.IsDown(Win32.VK_SHIFT);
        _prevEsc = Win32.IsDown(Win32.VK_ESCAPE);
        _prevButton = DragButtonDown;
        _poll.Start();
    }

    /// <summary>Runs at ~60 Hz only while a drag may be in progress or the wheel is open.</summary>
    private void Poll()
    {
        bool button = DragButtonDown, shift = Win32.IsDown(Win32.VK_SHIFT), esc = Win32.IsDown(Win32.VK_ESCAPE);

        if (shift && !_prevShift) OnShiftPressed(button);
        if (esc && !_prevEsc && _session is not null) Dismiss("Escape pressed");
        if (!button && _prevButton && _session is { } s) RequestDismiss("drag button released", s);

        _prevShift = shift; _prevEsc = esc; _prevButton = button;

        bool keepPolling = button || _session is not null || Settings.DiagnosticArmOnButton || AnySignalActive();
        if (!keepPolling) _poll.Stop();
    }

    private bool AnySignalActive() =>
        _signals.IsActive(DragSignal.OleDragDropEvent) || _signals.IsActive(DragSignal.ShellDragImage) ||
        _signals.IsActive(DragSignal.MouseHookHeuristic);

    private bool AnyArmingSignal() =>
        (Settings.ArmOnOleEvent && _signals.IsActive(DragSignal.OleDragDropEvent)) ||
        (Settings.ArmOnDragImage && _signals.IsActive(DragSignal.ShellDragImage)) ||
        (Settings.ArmOnMouseHook && _signals.IsActive(DragSignal.MouseHookHeuristic));

    private string SignalSnapshot() =>
        $"ole={Flag(DragSignal.OleDragDropEvent)} image={Flag(DragSignal.ShellDragImage)} hook={Flag(DragSignal.MouseHookHeuristic)}";

    private string Flag(DragSignal s) => _signals.IsActive(s) ? "Y" : "n";

    // ---------- wheel lifecycle ----------

    private void OnShiftPressed(bool buttonDown)
    {
        if (_session is not null) { CycleMode(); return; }
        if (!buttonDown) return;

        if (!AnyArmingSignal() && !Settings.DiagnosticArmOnButton)
        {
            _stats.ShiftWithoutSignal++;
            ProbeLog.Warn($"Shift tapped with button held but no enabled drag signal active → wheel NOT shown [{SignalSnapshot()}]");
            PublishStats();
            return;
        }
        OpenWheel();
    }

    private void OpenWheel()
    {
        double t0 = ProbeLog.NowMs;
        Win32.GetCursorPos(out var cursor);
        _deferredDismiss?.Stop();
        _session = new Session { Id = ++_sessionCounter, OpenedMs = t0 };
        foreach (DragSignal sig in Enum.GetValues<DragSignal>())
            if (_signals.IsActive(sig)) _session.SignalsAtOpen.Add(sig);

        _wheel!.SetContent(WheelMode.Convert, [], "Move here");
        uint dpi = _wheel.ShowAt(cursor, Settings.Animate);
        string zorder = "";
        if (Settings.PlaceBelowDragImage) zorder = _wheel.PlaceBelowDragImage() ? " z-order: below SysDragImage" : " z-order: SysDragImage not found";

        double ms = ProbeLog.NowMs - t0;
        _stats.OpenLatencies.Add(ms);
        _stats.WheelOpens++;
        ProbeLog.Info($"#{_session.Id} WHEEL OPEN in {ms:F1} ms at ({cursor.X},{cursor.Y}) dpi={dpi} [{SignalSnapshot()}]{zorder}");
        PublishStats();
    }

    private void CycleMode()
    {
        if (_session is null) return;
        _session.Mode = (WheelMode)(((int)_session.Mode + 1) % 3);
        _stats.ModeSwitches++;
        ProbeLog.Info($"#{_session.Id} mode → {_session.Mode}");
        RefreshWheel();
        PublishStats();
    }

    private void RefreshWheel()
    {
        if (_session is null || _wheel is null) return;
        _session.Items = Operations.ItemsFor(_session.Mode, _session.Files);
        _wheel.SetContent(_session.Mode, _session.Items, HubSummary());
    }

    private string HubSummary()
    {
        var s = _session!;
        if (s.EnterMs is null) return "Move here";
        if (s.Files.Length == 0) return "No files";
        if (s.Items.Count == 0) return "Not supported";
        if (s.Files.Length > 1) return $"{s.Files.Length} files";
        try { return HumanSize(new FileInfo(s.Files[0]).Length); } catch { return "1 file"; }
    }

    /// <summary>If the cursor is over the wheel, a Drop may still be in flight: wait briefly for it.</summary>
    private void RequestDismiss(string reason, Session session)
    {
        if (session.OverWheel && !session.Dropped)
        {
            _deferredDismiss?.Stop();
            _deferredDismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _deferredDismiss.Tick += (_, _) =>
            {
                _deferredDismiss?.Stop();
                if (_session == session) Dismiss($"{reason} (no Drop within 500 ms)");
            };
            _deferredDismiss.Start();
        }
        else Dismiss(reason);
    }

    private void Dismiss(string reason, bool force = false)
    {
        var s = _session;
        if (s is null && !force) return;
        _deferredDismiss?.Stop();
        _session = null;
        _wheel?.Hide();
        if (s is null) return;

        if (s.EnterMs is null)
        {
            _stats.UnconfirmedOpens++;
            ProbeLog.Warn($"#{s.Id} closed ({reason}) WITHOUT DragEnter: no OLE drag reached the wheel");
        }
        else ProbeLog.Info($"#{s.Id} closed ({reason})");

        if (!s.Dropped && s.Files.Length > 0)
        {
            var missing = s.Files.Where(f => !File.Exists(f)).ToList();
            ProbeLog.Info(missing.Count == 0
                ? $"#{s.Id} no drop on wheel; all {s.Files.Length} source(s) still in place"
                : $"#{s.Id} no drop on wheel; {missing.Count} source(s) no longer at original path (moved by the drop target you released over?)");
        }
        PublishStats();
    }

    // ---------- OLE drop target (IDropSink) ----------

    public void OnDragEnter(string[] files, IReadOnlyList<string> formats, Win32.POINT pt, int keyState)
    {
        var s = _session;
        if (s is null) { ProbeLog.Warn("DragEnter with no open wheel session (ignored)"); return; }
        s.OverWheel = true;

        if (s.EnterMs is null)
        {
            s.EnterMs = ProbeLog.NowMs;
            double latency = s.EnterMs.Value - s.OpenedMs;
            _stats.EnterLatencies.Add(latency);
            _stats.ConfirmedDrags++;
            foreach (DragSignal sig in Enum.GetValues<DragSignal>())
                if (s.SignalsAtOpen.Contains(sig)) _stats.SignalHits[sig]++; // measured at Shift time: the shell hides SysDragImage once over our target
            ProbeLog.Pass($"#{s.Id} DragEnter {latency:F1} ms after open: {files.Length} file(s) BEFORE drop " +
                          $"[{SignalSnapshot()}] keys=0x{keyState:X}");
        }
        else ProbeLog.Ole($"#{s.Id} DragEnter (re-entered wheel)");

        if (!s.FormatsLogged)
        {
            s.FormatsLogged = true;
            ProbeLog.Ole($"#{s.Id} formats: {string.Join(", ", formats)}");
            foreach (var f in files.Take(10)) ProbeLog.Ole($"#{s.Id}   file: {f}");
            if (files.Length > 10) ProbeLog.Ole($"#{s.Id}   … and {files.Length - 10} more");
        }

        if (s.Files.Length == 0 && files.Length > 0)
        {
            s.Files = files;
            var snapshot = files.ToArray();
            s.HashesBefore = Task.Run(() => snapshot.Where(File.Exists).ToDictionary(f => f, SafeOutput.Sha256));
        }
        RefreshWheel();
        OnDragOver(pt, keyState);
        PublishStats();
    }

    public DragFeedback OnDragOver(Win32.POINT pt, int keyState)
    {
        var s = _session;
        if (s is null || _wheel is null) return new(false, null, null);
        int hit = _wheel.HitTest(pt);
        _wheel.Highlight(hit);

        if (hit >= 0 && hit < s.Items.Count)
        {
            var item = s.Items[hit];
            _wheel.SetHubText(item.Label);
            return new(true, item.Verb, item.Insert);
        }
        _wheel.SetHubText(HubSummary());
        return hit == WheelWindow.HitHub ? new(false, "Release to cancel", null) : new(false, null, null);
    }

    public void OnDragLeave()
    {
        if (_session is not { } s) return;
        s.OverWheel = false;
        _wheel?.Highlight(WheelWindow.HitOutside);
        _wheel?.SetHubText(HubSummary());
        ProbeLog.Ole($"#{s.Id} DragLeave (wheel stays anchored until the drag ends)");
    }

    public void OnDrop(string[] files, Win32.POINT pt)
    {
        var s = _session;
        if (s is null || _wheel is null) { ProbeLog.Warn("Drop with no open wheel session (ignored)"); return; }
        int hit = _wheel.IsVisible ? _wheel.HitTest(pt) : _wheel.HighlightIndex;
        s.Dropped = true;
        s.OverWheel = false;
        _stats.Drops++;
        Dismiss("dropped");

        if (hit < 0 || hit >= s.Items.Count)
        {
            ProbeLog.Info($"#{s.Id} dropped on hub/outside segment → nothing to do");
            return;
        }
        if (files.Length == 0) files = s.Files;
        _ = RunOperationAsync(s, s.Items[hit], files);
    }

    private async Task RunOperationAsync(Session s, WheelItem item, string[] files)
    {
        ProbeLog.Info($"#{s.Id} DROP on '{item.Label}' ({item.Id}) for {files.Length} file(s)");
        var results = await Operations.ExecuteAsync(item, files);
        foreach (var (src, output, error) in results)
        {
            if (error?.StartsWith("already ", StringComparison.Ordinal) == true) { _stats.OpsSkipped++; ProbeLog.Info($"#{s.Id} SKIP {Path.GetFileName(src)}: {error}"); }
            else if (error is not null) { _stats.OpsFailed++; ProbeLog.Fail($"#{s.Id} {Path.GetFileName(src)}: {error}"); }
            else { _stats.OpsSucceeded++; ProbeLog.Pass($"#{s.Id} {Path.GetFileName(src)} → {(output is null ? item.Label : Path.GetFileName(output))}"); }
        }

        foreach (var dir in files.Select(Path.GetDirectoryName).Distinct())
            if (dir is not null && SafeOutput.LeftoverTemps(dir).Any())
                ProbeLog.Fail($"#{s.Id} leftover temp files in {dir}");

        await VerifyIntegrityAsync(s, files);
        PublishStats();
    }

    /// <summary>H-4: after the drag has fully ended, every source must still exist with an identical SHA-256.</summary>
    private async Task VerifyIntegrityAsync(Session s, string[] files)
    {
        while (DragButtonDown) await Task.Delay(100);
        await Task.Delay(750); // give Explorer time to act on the returned effect, if it ever would
        Dictionary<string, string> before;
        try { before = s.HashesBefore is null ? [] : await s.HashesBefore; }
        catch (Exception ex) { ProbeLog.Warn($"#{s.Id} could not hash sources before drop: {ex.Message}"); return; }

        foreach (var f in files)
        {
            if (!File.Exists(f)) { _stats.IntegrityFail++; ProbeLog.Fail($"#{s.Id} SOURCE MISSING after drop: {f}"); continue; }
            if (!before.TryGetValue(f, out var h0)) { ProbeLog.Warn($"#{s.Id} no 'before' hash for {f}"); continue; }
            string h1 = await Task.Run(() => SafeOutput.Sha256(f));
            if (h0 == h1) { _stats.IntegrityPass++; ProbeLog.Pass($"#{s.Id} source intact (sha256 {h1[..12]}…): {Path.GetFileName(f)}"); }
            else { _stats.IntegrityFail++; ProbeLog.Fail($"#{s.Id} SOURCE CHANGED: {f}"); }
        }
    }

    // ---------- misc ----------

    /// <summary>Shows the wheel at the cursor with sample content for 4 s (rendering/DPI check; no drag involved).</summary>
    public void Preview()
    {
        Dismiss("preview", force: true);
        Win32.GetCursorPos(out var cursor);
        var items = Operations.ItemsFor(WheelMode.Convert, ["sample.jpg"]);
        _wheel!.SetContent(WheelMode.Convert, items, "1.2 MB");
        uint dpi = _wheel.ShowAt(cursor, Settings.Animate);
        _wheel.Highlight(1);
        _wheel.SetHubText(items[1].Label);
        ProbeLog.Info($"Preview wheel at ({cursor.X},{cursor.Y}) dpi={dpi}");
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) => { t.Stop(); if (_session is null) _wheel?.Hide(); };
        t.Start();
    }

    public void ResetStats()
    {
        _stats.Reset();
        ProbeLog.Info("---- statistics reset ----");
        PublishStats();
    }

    public string Summary() => _stats.Format();

    private void PublishStats() => StatsChanged?.Invoke(_stats.Format());

    private static string HumanSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F1} GB",
    };

    public void Dispose()
    {
        _poll.Stop();
        _nativeTarget?.Revoke();
        _signals.Dispose();
        _wheel?.Close();
    }
}

public sealed class ProbeStats
{
    public int WheelOpens, ShiftWithoutSignal, ConfirmedDrags, UnconfirmedOpens, Drops, OpsSucceeded, OpsFailed, OpsSkipped,
        IntegrityPass, IntegrityFail, ModeSwitches;
    public bool InjectedInputSeen;
    public List<double> OpenLatencies { get; } = [];
    public List<double> EnterLatencies { get; } = [];
    public Dictionary<DragSignal, int> SignalHits { get; } = Enum.GetValues<DragSignal>().ToDictionary(s => s, _ => 0);

    public void Reset()
    {
        WheelOpens = ShiftWithoutSignal = ConfirmedDrags = UnconfirmedOpens = Drops = OpsSucceeded = OpsFailed = OpsSkipped =
            IntegrityPass = IntegrityFail = ModeSwitches = 0;
        InjectedInputSeen = false;
        OpenLatencies.Clear();
        EnterLatencies.Clear();
        foreach (var k in SignalHits.Keys.ToList()) SignalHits[k] = 0;
    }

    public string Format()
    {
        static string Lat(List<double> l) => l.Count == 0 ? "n/a"
            : $"median {l.Order().ElementAt(l.Count / 2):F0} ms · max {l.Max():F0} ms (n={l.Count})";
        string Hit(DragSignal s) => $"{SignalHits[s]}/{ConfirmedDrags}";

        return $"""
            Wheel opens ............ {WheelOpens}   (Shift+button but no signal: {ShiftWithoutSignal})
            Confirmed drags ........ {ConfirmedDrags}   (opens without DragEnter: {UnconfirmedOpens})
            Shift → wheel visible .. {Lat(OpenLatencies)}
            Wheel → DragEnter ...... {Lat(EnterLatencies)}
            Signal active at open . OLE event {Hit(DragSignal.OleDragDropEvent)} · SysDragImage {Hit(DragSignal.ShellDragImage)} · mouse hook {Hit(DragSignal.MouseHookHeuristic)}
            Drops on wheel ......... {Drops}   mode switches: {ModeSwitches}
            Operations ............. {OpsSucceeded} ok · {OpsSkipped} skipped · {OpsFailed} failed
            Source integrity ....... {IntegrityPass} pass · {IntegrityFail} FAIL
            Physical input only .... {(InjectedInputSeen ? "NO (injected input seen)" : "yes")}
            """;
    }
}

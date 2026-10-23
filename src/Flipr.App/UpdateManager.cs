using System.Diagnostics;
using System.Windows.Threading;
using Flipr.Infrastructure;
using Flipr.Infrastructure.Updates;
using Flipr.UI.Toasts;
using Flipr.Windows.Shell;

namespace Flipr.App;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Installing, Failed }

/// <summary>
/// Finds, downloads and installs new Flipr releases.
/// <list type="bullet">
/// <item>Automatic checks run every few hours while "Check for updates automatically" is on; manual checks always run.</item>
/// <item>With "Install updates automatically" on, an update found by an automatic check is downloaded quietly,
/// verified, and installed the next time Flipr is idle (no wheel, no jobs, no Flipr windows). Otherwise the
/// user gets a card with an Install button.</item>
/// <item>Installed copies update in place via the silent installer, which relaunches Flipr; portable copies open
/// the release page instead.</item>
/// </list>
/// </summary>
public sealed class UpdateManager
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(30);

    private readonly UpdateService _service = new();
    private readonly ToastManager _toasts;
    private readonly Func<AppSettings> _settings;
    private readonly Action<Action<AppSettings>> _update;
    private readonly Action _quit;
    private readonly Action _releaseInstance;
    private readonly Func<bool> _busy;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private readonly DispatcherTimer _idleWait = new() { Interval = IdlePoll };
    private string? _notified;
    private string? _downloaded; // verified installer waiting for an idle moment

    /// <param name="busy">True while Flipr is in use (wheel open, jobs running, windows open); automatic installs wait.</param>
    public UpdateManager(ToastManager toasts, Func<AppSettings> settings, Action<Action<AppSettings>> update,
        Action releaseInstance, Action quit, Func<bool> busy)
    {
        _toasts = toasts;
        _settings = settings;
        _update = update;
        _quit = quit;
        _releaseInstance = releaseInstance;
        _busy = busy;
        _timer.Tick += (_, _) => _ = CheckIfDueAsync();
        _idleWait.Tick += (_, _) => TryInstallWhenIdle();
    }

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public ReleaseInfo? Available { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public static SemVersion Current => UpdateService.Current;

    public bool Enabled => _service.Enabled;

    /// <summary>Installed by Flipr-Setup (the uninstaller sits next to the exe) rather than unzipped.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>First automatic check shortly after start-up (so it never slows launch), then hourly "is it due?" ticks.</summary>
    public void Start()
    {
        if (!_service.Enabled) return;
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        first.Tick += (_, _) => { first.Stop(); _ = CheckIfDueAsync(); _timer.Start(); };
        first.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _idleWait.Stop();
    }

    private Task CheckIfDueAsync()
    {
        var s = _settings();
        if (!s.CheckForUpdates) return Task.CompletedTask;
        if (s.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < CheckInterval) return Task.CompletedTask;
        return CheckAsync(manual: false);
    }

    private bool AutoInstall => IsInstalled && _settings().AutoInstallUpdates;

    /// <summary>Asks GitHub for a newer release. Manual checks report every outcome; automatic ones only a new version.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready or UpdateState.Installing) return;
        Set(UpdateState.Checking);
        try
        {
            var release = await _service.CheckAsync(Current);
            _update(s => s.LastUpdateCheck = DateTimeOffset.UtcNow);
            Available = release;
            Error = null;
            Set(release is null ? UpdateState.UpToDate : UpdateState.Available);
            Log.Info(release is null ? $"Update check: {Current} is current" : $"Update check: {release.Version} available");

            if (release is null)
            {
                if (manual) _toasts.ShowMessage(ToastKind.Success, "You're up to date", $"Flipr {Current} is the latest version.", ScreenInfo.CursorPosition);
            }
            else if (!manual && AutoInstall) await DownloadInBackgroundAsync(release);
            else if (manual || _notified != release.Tag)
            {
                _notified = release.Tag;
                OfferUpdate(release);
            }
        }
        catch (Exception ex)
        {
            Error = Plain(ex);
            Set(UpdateState.Failed);
            Log.Error("Update check failed", ex);
            if (manual) _toasts.ShowMessage(ToastKind.Warning, "Couldn't check for updates", Error, ScreenInfo.CursorPosition);
        }
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        string detail = IsInstalled
            ? "Takes a few seconds. Flipr restarts and keeps your settings."
            : "This is the portable copy: download the new version from GitHub.";
        _toasts.ShowAction(ToastKind.Info, $"Flipr {release.Version} is available", detail,
            IsInstalled ? "Install" : "Download", () => _ = InstallAsync(), ScreenInfo.CursorPosition, TimeSpan.FromSeconds(20));
    }

    // ------------------------------------------------------------------ automatic

    /// <summary>Quiet download and verification; installation then waits for an idle moment.</summary>
    private async Task DownloadInBackgroundAsync(ReleaseInfo release)
    {
        Set(UpdateState.Downloading);
        try
        {
            _downloaded = await _service.DownloadAsync(release);
            Set(UpdateState.Ready);
            Log.Info($"Update {release.Version} downloaded and verified; installing when Flipr is idle");
            TryInstallWhenIdle();
        }
        catch (Exception ex)
        {
            // Fall back to asking: the card's Install button retries the download with progress shown.
            Error = Plain(ex);
            Set(UpdateState.Available);
            Log.Error("Background update download failed", ex);
            if (_notified != release.Tag) { _notified = release.Tag; OfferUpdate(release); }
        }
    }

    /// <summary>Installs the waiting update now if Flipr isn't in use; otherwise looks again shortly.</summary>
    private void TryInstallWhenIdle()
    {
        if (State != UpdateState.Ready || _downloaded is null || Available is null) { _idleWait.Stop(); return; }
        if (!_settings().AutoInstallUpdates) { _idleWait.Stop(); OfferUpdate(Available); return; }
        if (_busy()) { _idleWait.Start(); return; }
        _idleWait.Stop();
        var toast = _toasts.ShowProgress(ScreenInfo.CursorPosition);
        RunInstaller(Available, _downloaded, toast);
    }

    // ------------------------------------------------------------------ manual

    /// <summary>Downloads (with progress) and verifies the installer, then hands over to it and quits so it can replace the app.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } release || State is UpdateState.Downloading or UpdateState.Installing) return;
        if (!IsInstalled)
        {
            OpenUrl(release.PageUrl);
            return;
        }

        var toast = _toasts.ShowProgress(ScreenInfo.CursorPosition);
        if (State == UpdateState.Ready && _downloaded is not null && File.Exists(_downloaded))
        {
            _idleWait.Stop();
            RunInstaller(release, _downloaded, toast);
            return;
        }

        Set(UpdateState.Downloading);
        toast.SetRunning($"Downloading Flipr {release.Version}", "Verifying as it arrives", 0, indeterminate: true);
        var progress = new Progress<double>(f => toast.SetRunning($"Downloading Flipr {release.Version}", $"{f:P0}", f, indeterminate: false));
        string setup;
        try
        {
            setup = await _service.DownloadAsync(release, progress);
        }
        catch (Exception ex)
        {
            Error = Plain(ex);
            Set(UpdateState.Available);
            Log.Error("Update download failed", ex);
            toast.SetResult(ToastKind.Error, "The update didn't download", Error, canShow: false, TimeSpan.FromSeconds(12));
            return;
        }
        RunInstaller(release, setup, toast);
    }

    private async void RunInstaller(ReleaseInfo release, string setup, ToastWindow toast)
    {
        Set(UpdateState.Installing);
        toast.SetRunning($"Updating Flipr to {release.Version}", "Flipr will reopen in a moment", 1, indeterminate: true);
        Log.Info($"Installing update {release.Version} from {setup}");
        // The installer refuses to run while the single-instance mutex is held (Inno AppMutex), so let go of it first.
        _releaseInstance();
        try
        {
            // /RELAUNCH=1 makes the installer start the new Flipr once it has finished (see Flipr.iss). Its own
            // log goes next to Flipr's, so a failed update can always be explained.
            string setupLog = Path.Combine(Log.Folder ?? Path.GetTempPath(), $"setup-{release.Version}.log");
            Process.Start(new ProcessStartInfo(setup, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1 \"/LOG={setupLog}\"")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(setup)!,
            });
        }
        catch (Exception ex)
        {
            Error = Plain(ex);
            Set(UpdateState.Available);
            Log.Error("Could not start the installer", ex);
            toast.SetResult(ToastKind.Error, "The update couldn't start", Error, canShow: false, TimeSpan.FromSeconds(12));
            return;
        }
        await Task.Delay(1200); // let the card be read before the app goes away
        _quit();
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static string Plain(Exception ex) => ex switch
    {
        // A status code means GitHub answered: it's busy or limiting requests, not that the PC is offline.
        System.Net.Http.HttpRequestException { StatusCode: not null } => "GitHub didn't answer the update check just now. Flipr will try again later.",
        System.Net.Http.HttpRequestException or TaskCanceledException => "Flipr couldn't reach GitHub. Check your connection and try again.",
        _ => ex.Message,
    };

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"Could not open {url}", ex); }
    }
}

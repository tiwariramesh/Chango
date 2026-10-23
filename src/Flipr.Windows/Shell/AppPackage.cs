using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Flipr.Windows.Shell;

/// <summary>
/// Whether Flipr runs as the Microsoft Store (MSIX) package. The same executable ships both ways; packaged, the
/// Store handles updates and "start at sign-in" uses the package's startup task instead of the Run key.
/// </summary>
public static class AppPackage
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
    public const string StartupTaskId = "FliprStartup";

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch { return false; }
    }

    /// <summary>Started by the package's startup task at sign-in (the packaged equivalent of --background).</summary>
    public static bool LaunchedAtSignIn
    {
        get
        {
            if (!IsPackaged) return false;
            try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
            catch { return false; }
        }
    }

    public static bool StartupEnabled
    {
        get
        {
            var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
    }

    /// <summary>Flipr's Microsoft Store product ID; empty until Flipr has a Store listing.</summary>
    public const string StoreId = "";

    /// <summary>
    /// The Store's own "rate and review" dialog (Store version only). False if it couldn't be shown, so the caller can
    /// open the Store's review page instead.
    /// </summary>
    public static async Task<bool> RequestRatingAsync(IntPtr owner)
    {
        if (!IsPackaged) return false;
        try
        {
            var context = global::Windows.Services.Store.StoreContext.GetDefault();
            if (owner != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(context, owner);
            var result = await context.RequestRateAndReviewAppAsync();
            return result.Status != global::Windows.Services.Store.StoreRateAndReviewStatus.Error;
        }
        catch { return false; }
    }

    /// <summary>How many Store updates are waiting for this app (0 if none, or if the Store couldn't be reached).</summary>
    public static async Task<int> PendingStoreUpdatesAsync()
    {
        if (!IsPackaged) return 0;
        var context = global::Windows.Services.Store.StoreContext.GetDefault();
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        return updates.Count;
    }

    /// <summary>
    /// Downloads and installs the waiting Store updates with no dialog. Windows closes the app to install; it is
    /// registered for restart first, so Windows starts it again (with --updated) once the new version is in place.
    /// Returns the Store's final state, for the log.
    /// </summary>
    public static async Task<string> InstallStoreUpdatesSilentlyAsync()
    {
        if (!IsPackaged) return "not packaged";
        var context = global::Windows.Services.Store.StoreContext.GetDefault();
        if (!context.CanSilentlyDownloadStorePackageUpdates) return "silent updates aren't allowed on this PC";
        var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0) return "no updates";
        // RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT: restart only after an update, never after a crash.
        RegisterApplicationRestart("--updated", 1 | 2 | 8);
        var result = await context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates);
        return result.OverallState.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string commandLine, int flags);

    public static void SetStartup(bool enabled)
    {
        var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        if (enabled) task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
        else task.Disable();
    }

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref int length, IntPtr name);
}

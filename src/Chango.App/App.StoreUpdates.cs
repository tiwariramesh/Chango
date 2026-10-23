using System.Windows.Threading;
using Chango.Infrastructure;
using Chango.Windows.Shell;

namespace Chango.App;

/// <summary>
/// The Store version updates itself right away instead of waiting for the next restart: it asks the Store for updates
/// a few minutes after starting and every 6 hours, and when one is waiting it installs it silently the moment
/// Chango isn't in use (no wheel open, nothing converting, no Chango windows). Windows then starts the new
/// version, which says "Chango updated".
/// </summary>
public partial class App
{
    private static readonly TimeSpan StoreFirstCheck = TimeSpan.FromMinutes(3), StoreCheckEvery = TimeSpan.FromHours(6);
    private DispatcherTimer? _storeCheck, _storeWaitIdle;
    private bool _storeInstalling;

    private void StartStoreUpdates()
    {
        if (!AppPackage.IsPackaged) return;
        _storeCheck = new DispatcherTimer { Interval = StoreFirstCheck };
        _storeCheck.Tick += async (_, _) => { _storeCheck.Interval = StoreCheckEvery; await CheckStoreAsync(); };
        _storeCheck.Start();
    }

    private async Task CheckStoreAsync()
    {
        if (_storeInstalling || _storeWaitIdle is not null) return;
        int count;
        try { count = await AppPackage.PendingStoreUpdatesAsync(); }
        catch (Exception ex) { Log.Info($"Store update check failed: {ex.Message}"); return; }
        if (count == 0) { Log.Info("Store update check: up to date"); return; }

        Log.Info("Store update available: installing as soon as Chango isn't in use");
        _storeWaitIdle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _storeWaitIdle.Tick += async (_, _) =>
        {
            if (IsInUse() || _storeInstalling) return;
            _storeWaitIdle?.Stop();
            _storeWaitIdle = null;
            await InstallStoreUpdateAsync();
        };
        _storeWaitIdle.Start();
    }

    private async Task InstallStoreUpdateAsync()
    {
        _storeInstalling = true;
        try
        {
            Log.Info("Installing the Store update (Chango restarts by itself)");
            string state = await AppPackage.InstallStoreUpdatesSilentlyAsync();
            Log.Info($"Store update finished: {state}"); // still running here only if it didn't install
        }
        catch (Exception ex) { Log.Info($"Store update install failed: {ex.Message}"); }
        finally { _storeInstalling = false; }
    }
}

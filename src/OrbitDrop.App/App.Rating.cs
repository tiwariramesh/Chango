using System.Windows;
using System.Windows.Threading;
using OrbitDrop.Infrastructure;
using OrbitDrop.UI.Toasts;
using OrbitDrop.Windows.Interop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.App;

/// <summary>
/// Asks for a rating once, ever: after the 10th job that produced a result, a few seconds after its notification.
/// The Store version opens the Store's own rating dialog; the GitHub version asks for a star instead.
/// </summary>
public partial class App
{
    private const int RatingAfterJobs = 10;

    private void OnSuccessfulJob() => Dispatcher.BeginInvoke(() =>
    {
        Update(s => s.SuccessfulJobs++);
        if (_settings.RatingAsked || _settings.SuccessfulJobs < RatingAfterJobs || _onboarding is not null) return;
        Update(s => s.RatingAsked = true);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Log.Info("Asking for a rating");
            if (AppPackage.IsPackaged)
                _toasts.ShowAction(ToastKind.Info, "Enjoying OrbitDrop?", "A quick rating helps other people find it.",
                    "Rate", () => _ = RateAsync(), ScreenInfo.CursorPosition, TimeSpan.FromSeconds(15));
            else
                _toasts.ShowAction(ToastKind.Info, "Enjoying OrbitDrop?", "A star on GitHub helps other people find it.",
                    "Star", () => UpdateManager.OpenUrl("https://github.com/i3sam/OrbitDrop"), ScreenInfo.CursorPosition, TimeSpan.FromSeconds(15));
        };
        timer.Start();
    });

    public string RateButtonText => AppPackage.IsPackaged ? "Rate" : "Star on GitHub";
    public string RateHint => AppPackage.IsPackaged
        ? "A quick rating in the Microsoft Store helps other people find OrbitDrop."
        : "A star on GitHub helps other people find OrbitDrop.";

    public void RateClicked()
    {
        Update(s => s.RatingAsked = true); // they found it themselves: no need to ask later
        if (AppPackage.IsPackaged) _ = RateAsync();
        else UpdateManager.OpenUrl("https://github.com/i3sam/OrbitDrop");
    }

    private async Task RateAsync()
    {
        IntPtr owner = _messages?.Handle ?? IntPtr.Zero;
        if (!await AppPackage.RequestRatingAsync(owner))
            UpdateManager.OpenUrl($"ms-windows-store://review/?ProductId={AppPackage.StoreId}");
    }
}

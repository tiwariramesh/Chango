using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;

namespace Flipr.Windows.Shell;

/// <summary>
/// The Windows share window (Nearby Share, Mail, Phone Link, apps …) for desktop apps, via IDataTransferManagerInterop.
/// It needs a window to anchor to; Flipr has none on screen, so a tiny invisible one is opened at the pointer.
/// </summary>
public static class ShareSheet
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <summary>Must be called on the UI thread.</summary>
    public static void Show(IReadOnlyList<string> paths, Interop.Win32.POINT anchor)
    {
        var host = new Window
        {
            Width = 1, Height = 1, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Topmost = true, Title = "Flipr Share",
        };
        host.Show();
        IntPtr hwnd = new WindowInteropHelper(host).Handle;
        Interop.Win32.SetWindowPos(hwnd, Interop.Win32.HWND_TOPMOST, anchor.X, anchor.Y, 1, 1, 0);
        host.Activate();

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DataTransferManagerIid;
        var manager = MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(hwnd, ref iid));
        manager.DataRequested += async (_, e) =>
        {
            var deferral = e.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var p in paths)
                {
                    if (File.Exists(p)) items.Add(await StorageFile.GetFileFromPathAsync(p));
                    else if (Directory.Exists(p)) items.Add(await StorageFolder.GetFolderFromPathAsync(p));
                }
                e.Request.Data.Properties.Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} items";
                e.Request.Data.Properties.Description = "Shared from Flipr";
                e.Request.Data.SetStorageItems(items);
            }
            catch (Exception ex)
            {
                e.Request.FailWithDisplayText($"These files couldn't be shared ({ex.Message})");
            }
            finally { deferral.Complete(); }
        };
        manager.TargetApplicationChosen += (_, _) => host.Dispatcher.BeginInvoke(host.Close);
        interop.ShowShareUIForWindow(hwnd);

        // The share window has no "dismissed" event: tidy up the anchor window after a while either way.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        timer.Tick += (_, _) => { timer.Stop(); if (host.IsLoaded) host.Close(); };
        timer.Start();
    }
}

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flipr.Windows.Shell;

namespace Flipr.UI.Shelf;

/// <summary>A file parked on the shelf.</summary>
public sealed record ShelfItem(string Path, string Name, string Detail, ImageSource? Icon);

/// <summary>
/// "Float": a small always-on-top shelf holding files (references, not copies) until you drag them where they're
/// needed. Drag one out, or "Drag all"; drop more files on it to add them. Closing it never touches the files.
/// </summary>
public partial class ShelfWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".ico" };

    private readonly ObservableCollection<ShelfItem> _items = [];
    private Point _pressAt;
    private bool _placed;

    public ShelfWindow()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        Shadow.Opacity = ThemeManager.Number("ToastShadowOpacity");
        Drop += OnDropFiles;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
    }

    public IReadOnlyList<string> Paths => _items.Select(i => i.Path).ToList();

    /// <summary>Adds files (skipping ones already there) and shows the shelf near the right edge of the screen.</summary>
    public void Add(IEnumerable<string> paths, bool show = true)
    {
        foreach (var p in paths)
        {
            if (_items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            if (!File.Exists(p) && !Directory.Exists(p)) continue;
            _items.Add(Describe(p));
        }
        TitleText.Text = _items.Count == 1 ? "Float · 1 item" : $"Float · {_items.Count} items";
        DragAll.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return; // design review renders offscreen
        if (!IsVisible) Show();
        if (!_placed) { PlaceAtRightEdge(); _placed = true; }
    }

    private void PlaceAtRightEdge()
    {
        UpdateLayout();
        var monitor = ScreenInfo.MonitorAt(ScreenInfo.CursorPosition);
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? monitor.Scale;
        int w = (int)Math.Round(ActualWidth * scale), h = (int)Math.Round(ActualHeight * scale);
        var work = monitor.WorkArea;
        Flipr.Windows.Interop.Win32.SetWindowPos(new WindowInteropHelper(this).Handle, Flipr.Windows.Interop.Win32.HWND_TOPMOST,
            work.Right - w - (int)(8 * scale), work.Top + (work.Bottom - work.Top - h) / 3, 0, 0, Flipr.Windows.Interop.Win32.SWP_NOSIZE);
    }

    private static ShelfItem Describe(string path)
    {
        string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        string detail;
        if (Directory.Exists(path)) detail = "Folder";
        else
        {
            long size = new FileInfo(path).Length;
            detail = size switch
            {
                < 1024 => $"{size} bytes",
                < 1024 * 1024 => $"{size / 1024.0:0} KB",
                < 1024L * 1024 * 1024 => $"{size / 1024.0 / 1024:0.0} MB",
                _ => $"{size / 1024.0 / 1024 / 1024:0.00} GB",
            };
        }
        return new ShelfItem(path, name, detail, Thumbnail(path) ?? ShellIcon(path));
    }

    /// <summary>A small decoded preview for images (fast: decodes at thumbnail size).</summary>
    private static ImageSource? Thumbnail(string path)
    {
        if (!ImageExtensions.Contains(System.IO.Path.GetExtension(path))) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 64;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>The icon Explorer shows for the file or folder.</summary>
    private static ImageSource? ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    // ------------------------------------------------------------------ dragging files out

    private void OnItemDown(object sender, MouseButtonEventArgs e)
    {
        _pressAt = e.GetPosition(this);
        e.Handled = sender == DragAll;
    }

    private void OnItemMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { Tag: string tag } element) return;
        var d = e.GetPosition(this) - _pressAt;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var paths = (tag == "*" ? Paths : [tag]).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return;
        var data = new DataObject(DataFormats.FileDrop, paths);
        Flipr.UI.Onboarding.InternalDrag.Starting?.Invoke(); // so tapping the wheel key works from the shelf too
        // Copy, move or link: whatever the target prefers. The shelf only holds references.
        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        // A move elsewhere means the file left its old place: drop it from the shelf.
        foreach (var gone in _items.Where(i => !File.Exists(i.Path) && !Directory.Exists(i.Path)).ToList()) _items.Remove(gone);
        if (_items.Count == 0) Close();
        else Add([]);
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Add(files);
        e.Handled = true;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && _items.FirstOrDefault(i => i.Path == path) is { } item) _items.Remove(item);
        if (_items.Count == 0) Close();
        else Add([]);
    }

    private void OnHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInside<System.Windows.Controls.Button>(d)) return;
        if (e.ClickCount == 1) DragMove();
    }

    private static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var x = d; x is not null; x = VisualTreeHelper.GetParent(x)) if (x is T) return true;
        return false;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ shell icon interop

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

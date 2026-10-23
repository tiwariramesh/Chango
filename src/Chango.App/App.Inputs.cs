using System.Windows;
using Chango.Core;
using Chango.Infrastructure;
using Chango.Tools;
using Chango.Tools.Metadata;
using Chango.UI.Metadata;
using Chango.Windows.Interop;
using Chango.Windows.Shell;

namespace Chango.App;

/// <summary>The windows and pickers that collect choices for options that need them (see <see cref="InputBroker"/>).</summary>
public partial class App
{
    private void RegisterInputs()
    {
        _inputs.Register(MetadataProvider.InfoId, AskMetadataAsync);
        _inputs.Register(ImageToolsProvider.EditId, AskPhotoEditAsync);
        _inputs.Register(Chango.Conversion.Media.MediaFoundationProvider.TrimId, AskTrimAsync);
        _inputs.Register(FileActionsProvider.CopyToPrefix + FileActionsProvider.ChooseSuffix, (op, files, _) => AskFolderAsync("Copy to", files));
        _inputs.Register(FileActionsProvider.MoveToPrefix + FileActionsProvider.ChooseSuffix, (op, files, _) => AskFolderAsync("Move to", files));
    }

    /// <summary>Opens the photo editor; the edited full-resolution bitmap becomes the job's input.</summary>
    private async Task<(bool, object?)> AskPhotoEditAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        var image = Chango.Conversion.Imaging.WicImageCodec.Load(files[0]); // upright, BGRA32
        var editor = new Chango.UI.Editor.PhotoEditorWindow(image, Path.GetFileName(files[0]));
        ShowInFront(editor);
        var edited = await editor.Result;
        return edited is null ? (false, null) : (true, edited);
    }

    /// <summary>Opens the Trim window; the chosen range becomes the job's input.</summary>
    private async Task<(bool, object?)> AskTrimAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        string file = files[0];
        var length = await Chango.Conversion.Media.MediaFoundationProvider.DurationAsync(file);
        var window = new Chango.UI.Trim.TrimWindow(file, length, Chango.Conversion.Media.MediaFoundationProvider.IsVideo(file));
        ShowInFront(window);
        var range = await window.Result;
        return range is null ? (false, null) : (true, range);
    }

    /// <summary>A folder picker in front of everything; the chosen folder becomes the job's input.</summary>
    private Task<(bool, object?)> AskFolderAsync(string verb, FileSet files)
    {
        // An invisible, activated owner makes the dialog open in front (Chango has no window of its own).
        var owner = new Window
        {
            Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = true,
            AllowsTransparency = true, Opacity = 0, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        owner.Show();
        owner.Activate();
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = files.Count == 1 ? $"{verb}: choose a folder for {Path.GetFileName(Path.TrimEndingDirectorySeparator(files[0]))}" : $"{verb}: choose a folder for {files.Count} items",
                InitialDirectory = _settings.RecentFolders.FirstOrDefault(Directory.Exists) ?? Path.GetDirectoryName(files[0]),
            };
            return Task.FromResult<(bool, object?)>(dialog.ShowDialog(owner) == true ? (true, dialog.FolderName) : (false, null));
        }
        finally { owner.Close(); }
    }

    // ------------------------------------------------------------------ Float shelf

    private Chango.UI.Shelf.ShelfWindow? _shelf;

    /// <summary>One shelf at a time: floating more files adds them to the open one.</summary>
    private void ShowShelf(IReadOnlyList<string> paths)
    {
        if (_shelf is null)
        {
            _shelf = new Chango.UI.Shelf.ShelfWindow();
            _shelf.Closed += (_, _) => _shelf = null;
        }
        _shelf.Add(paths);
        Log.Info($"Floated {paths.Count} item(s)");
    }

    // ------------------------------------------------------------------ keyboard flow

    /// <summary>Claims (or releases) Ctrl+Alt+Space according to the setting.</summary>
    private void ApplyHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (!_settings.KeyboardShortcut || _messages is null) { Log.Info("Keyboard shortcut Ctrl+Alt+Space off"); return; }
        _hotkey = new GlobalHotkey(_messages, GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT, GlobalHotkey.VK_SPACE, OnHotkey);
        Log.Info(_hotkey.Registered ? "Keyboard shortcut Ctrl+Alt+Space ready" : "Ctrl+Alt+Space is taken by another app");
    }

    public bool KeyboardShortcutAvailable => _hotkey?.Registered ?? true;

    private bool _readingSelection;

    private async void OnHotkey()
    {
        if (Paused || _readingSelection) return;
        if (_interaction?.CycleIfOpenFromKeyboard() == true) return; // pressed again: next mode
        IReadOnlyList<string> files = ExplorerSelection.FromForegroundWindow();
        Log.Info($"Ctrl+Alt+Space: {ExplorerSelection.LastResult}");
        bool vsCode = files.Count == 0 && _messages is not null && VsCodeSelection.IsForeground(out _);
        if (vsCode)
        {
            _readingSelection = true;
            try { files = await VsCodeSelection.ReadAsync(_messages!.Handle); }
            catch (Exception ex) { Log.Warn($"Reading the VS Code selection failed: {ex.Message}"); }
            finally { _readingSelection = false; }
            Log.Info($"Ctrl+Alt+Space in VS Code: {files.Count} item(s)");
        }
        if (files.Count == 0 && !vsCode && _settings.UseClipboard && FromClipboard(ExplorerSelection.LastFolder) is { } clip)
        {
            Log.Info($"Ctrl+Alt+Space: nothing selected, using the clipboard ({clip.Files.Count} item(s))");
            _interaction?.OpenForSelection(clip.Files, clip.OutputFolder, clip.Label);
            return;
        }
        if (files.Count == 0)
        {
            _toasts.ShowMessage(Chango.UI.Toasts.ToastKind.Info, "Select files first",
                vsCode ? "Select files in VS Code's Explorer (or open a file), then press Ctrl+Alt+Space."
                : _settings.UseClipboard ? "Select files in File Explorer, on the desktop or in VS Code, or copy an image, then press Ctrl+Alt+Space."
                : "Select files in File Explorer, on the desktop or in VS Code, then press Ctrl+Alt+Space.",
                ScreenInfo.CursorPosition);
            return;
        }
        _interaction?.OpenForSelection(new FileSet(files));
    }

    /// <summary>"Chango" in File Explorer's right-click menu (Settings → Shortcuts): registered or removed to match.</summary>
    private void ApplyContextMenu()
    {
        string version = $"{UpdateManager.Current.Major}.{UpdateManager.Current.Minor}.{UpdateManager.Current.Patch}";
        bool ok = ContextMenu.Apply(_settings.ContextMenu, AppPackage.IsPackaged, AppContext.BaseDirectory, version);
        Log.Info($"Right-click menu {(_settings.ContextMenu ? "on" : "off")}{(ok ? "" : " (couldn't update the registration)")}");
    }

    /// <summary>The right-click menu sent files (WM_COPYDATA, checked by <see cref="ContextMenu.TryReadFiles"/>): open the wheel for them.</summary>
    private bool OnShellMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (ContextMenu.TryReadFiles(msg, lParam) is not { } files) return false;
        Log.Info($"Right-click menu: {files.Count} item(s)");
        if (files.Count > 0 && !Paused) Dispatcher.BeginInvoke(() => _interaction?.OpenForSelection(new FileSet(files)));
        return true;
    }

    /// <summary>Puts a Copy to / Move to destination at the top of the recent folders.</summary>
    private void RememberFolder(string folder) =>
        Dispatcher.BeginInvoke(() => Update(s => s.RecentFolders = [folder, .. s.RecentFolders]));

    private async Task<(bool, object?)> AskMetadataAsync(OperationDescriptor op, FileSet files, Win32.POINT anchor)
    {
        var snapshot = await FileProperties.ReadAsync(files[0]);
        var window = new MetadataWindow(snapshot);
        ShowInFront(window);
        var edit = await window.Result;
        return edit is { IsEmpty: false } ? (true, edit) : (false, null);
    }

    /// <summary>
    /// Shows a window centred on the screen and brings it forward. Chango runs in the background, so Windows
    /// may otherwise open it behind File Explorer.
    /// </summary>
    private static void ShowInFront(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        Log.Info($"Opened {window.GetType().Name}");
    }
}

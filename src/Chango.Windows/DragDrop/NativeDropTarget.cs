using System.Runtime.InteropServices;
using Chango.Windows.Interop;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Chango.Windows.DragDrop;

/// <summary>What the wheel wants OLE to show while hovering.</summary>
public readonly record struct DragFeedback(bool Accept, string? Message, string? Insert);

/// <summary>Receives OLE callbacks from <see cref="NativeDropTarget"/> on the UI thread.</summary>
public interface IDropSink
{
    void OnDragEnter(string[] files, Win32.POINT pt);
    DragFeedback OnDragOver(Win32.POINT pt);
    void OnDragLeave();
    void OnDrop(string[] files, Win32.POINT pt);
}

/// <summary>
/// Raw COM IDropTarget registered on the wheel HWND (validated in Orbit Probe, feasibility R1/R2/R10).
/// Uses IDropTargetHelper so the shell drag image behaves correctly over our window, and DROPDESCRIPTION for the
/// native "Convert to PNG" drag tooltip. Answers only DROPEFFECT_COPY or NONE, <b>never MOVE</b>, so the drag source can
/// never delete or move the original file.
/// </summary>
[ComVisible(true)]
public sealed class NativeDropTarget : Ole.IDropTarget
{
    private readonly IntPtr _hwnd;
    private readonly IDropSink _sink;
    private readonly Ole.IDropTargetHelper? _helper;
    private ComIDataObject? _current;
    private string? _lastDescription;

    public NativeDropTarget(IntPtr hwnd, IDropSink sink)
    {
        _hwnd = hwnd;
        _sink = sink;
        try { _helper = Ole.CreateDropTargetHelper(); }
        catch (Exception ex) { Diagnostics.Write($"IDropTargetHelper unavailable: {ex.Message}"); }
    }

    /// <summary>The current drag carries no real files, only "virtual" ones (dragged out of a ZIP, a phone or an email).</summary>
    public bool LastDragFromArchive { get; private set; }

    public int Register()
    {
        Win32.RevokeDragDrop(_hwnd); // WPF may have registered its own target on this HWND
        return Win32.RegisterDragDrop(_hwnd, this);
    }

    public void Revoke() => Win32.RevokeDragDrop(_hwnd);

    public int DragEnter(ComIDataObject pDataObj, int grfKeyState, Ole.POINTL pt, ref int pdwEffect)
    {
        try
        {
            _current = pDataObj;
            _lastDescription = null;
            int allowed = pdwEffect;
            string[] files = [];
            try { files = DataObjectReader.ReadFileDrop(pDataObj); }
            catch (Exception ex) { Diagnostics.Write($"File list read failed: {ex.Message}"); }
            LastDragFromArchive = false;
            if (files.Length == 0)
            {
                var formats = DataObjectReader.ListFormats(pDataObj);
                Diagnostics.Write($"Drag has no files; formats: {string.Join(", ", formats)}");
                // Files inside a ZIP (or a phone, or an email) exist only as a description and a stream, not on disk.
                LastDragFromArchive = formats.Contains("FileGroupDescriptorW") || formats.Contains("FileGroupDescriptor");
            }

            var p = new Win32.POINT(pt.X, pt.Y);
            _sink.OnDragEnter(files, p);
            pdwEffect = files.Length > 0 && (allowed & Ole.DROPEFFECT_COPY) != 0 ? Ole.DROPEFFECT_COPY : Ole.DROPEFFECT_NONE;
            _helper?.DragEnter(_hwnd, pDataObj, ref p, pdwEffect);
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"DragEnter failed: {ex}");
            pdwEffect = Ole.DROPEFFECT_NONE;
        }
        return Ole.S_OK;
    }

    public int DragOver(int grfKeyState, Ole.POINTL pt, ref int pdwEffect)
    {
        try
        {
            var p = new Win32.POINT(pt.X, pt.Y);
            var feedback = _sink.OnDragOver(p);
            pdwEffect = feedback.Accept && (pdwEffect & Ole.DROPEFFECT_COPY) != 0 ? Ole.DROPEFFECT_COPY : Ole.DROPEFFECT_NONE;
            UpdateDescription(feedback);
            _helper?.DragOver(ref p, pdwEffect);
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"DragOver failed: {ex.Message}");
            pdwEffect = Ole.DROPEFFECT_NONE;
        }
        return Ole.S_OK;
    }

    public int DragLeave()
    {
        try
        {
            ClearDescription();
            _helper?.DragLeave();
            _sink.OnDragLeave();
        }
        catch (Exception ex) { Diagnostics.Write($"DragLeave failed: {ex.Message}"); }
        _current = null;
        return Ole.S_OK;
    }

    public int Drop(ComIDataObject pDataObj, int grfKeyState, Ole.POINTL pt, ref int pdwEffect)
    {
        var p = new Win32.POINT(pt.X, pt.Y);
        try
        {
            string[] files = DataObjectReader.ReadFileDrop(pDataObj);
            ClearDescription();
            pdwEffect = files.Length > 0 ? Ole.DROPEFFECT_COPY : Ole.DROPEFFECT_NONE; // never MOVE
            _helper?.Drop(pDataObj, ref p, pdwEffect);
            _sink.OnDrop(files, p);
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"Drop failed: {ex}");
            pdwEffect = Ole.DROPEFFECT_NONE;
        }
        _current = null;
        return Ole.S_OK;
    }

    private void UpdateDescription(DragFeedback fb)
    {
        if (_current is null) return;
        string key = $"{fb.Accept}|{fb.Message}|{fb.Insert}";
        if (key == _lastDescription) return;
        _lastDescription = key;
        if (fb.Message is null) DataObjectReader.TrySetDropDescription(_current, Ole.DROPIMAGE_NONE, "", "");
        else DataObjectReader.TrySetDropDescription(_current, fb.Accept ? Ole.DROPIMAGE_COPY : Ole.DROPIMAGE_NONE, fb.Message, fb.Insert ?? "");
    }

    /// <summary>Hand the drag tooltip back to the next target (DROPIMAGE_INVALID = "not set").</summary>
    private void ClearDescription()
    {
        if (_current is not null && _lastDescription is not null)
            DataObjectReader.TrySetDropDescription(_current, Ole.DROPIMAGE_INVALID, "", "");
        _lastDescription = null;
    }
}

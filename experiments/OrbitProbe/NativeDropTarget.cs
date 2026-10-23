using System.Runtime.InteropServices;
using OrbitProbe.Native;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace OrbitProbe;

/// <summary>What the controller wants OLE to show while hovering.</summary>
public readonly record struct DragFeedback(bool Accept, string? Message, string? Insert);

public interface IDropSink
{
    void OnDragEnter(string[] files, IReadOnlyList<string> formats, Win32.POINT pt, int keyState);
    DragFeedback OnDragOver(Win32.POINT pt, int keyState);
    void OnDragLeave();
    void OnDrop(string[] files, Win32.POINT pt);
}

/// <summary>
/// Variant B: raw COM IDropTarget registered on the wheel HWND, with IDropTargetHelper (keeps the shell drag
/// image correct over our window) and DROPDESCRIPTION (native "Convert to PNG" drag tooltip).
/// We only ever answer DROPEFFECT_COPY or NONE, never MOVE, so the source can never delete the original.
/// </summary>
[ComVisible(true)]
public sealed class NativeDropTarget : Ole.IDropTarget
{
    private readonly IntPtr _hwnd;
    private readonly IDropSink _sink;
    private readonly Ole.IDropTargetHelper? _helper;
    private readonly bool _useDescriptions;
    private ComIDataObject? _current;
    private string? _lastDescription;

    public NativeDropTarget(IntPtr hwnd, IDropSink sink, bool useHelper, bool useDescriptions)
    {
        _hwnd = hwnd;
        _sink = sink;
        _useDescriptions = useDescriptions;
        if (useHelper)
        {
            try { _helper = Ole.CreateDropTargetHelper(); }
            catch (Exception ex) { ProbeLog.Warn($"IDropTargetHelper unavailable: {ex.Message}"); }
        }
    }

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
            catch (Exception ex) { ProbeLog.Warn($"CF_HDROP read failed: {ex.Message}"); }
            var formats = DataObjectReader.ListFormats(pDataObj);

            var p = new Win32.POINT(pt.X, pt.Y);
            _sink.OnDragEnter(files, formats, p, grfKeyState);
            pdwEffect = files.Length > 0 && (allowed & Ole.DROPEFFECT_COPY) != 0 ? Ole.DROPEFFECT_COPY : Ole.DROPEFFECT_NONE;
            ProbeLog.Ole($"allowed effects from source: {EffectNames(allowed)}");
            _helper?.DragEnter(_hwnd, pDataObj, ref p, pdwEffect);
        }
        catch (Exception ex)
        {
            ProbeLog.Fail($"DragEnter threw: {ex}");
            pdwEffect = Ole.DROPEFFECT_NONE;
        }
        return Ole.S_OK;
    }

    public int DragOver(int grfKeyState, Ole.POINTL pt, ref int pdwEffect)
    {
        try
        {
            var p = new Win32.POINT(pt.X, pt.Y);
            var fb = _sink.OnDragOver(p, grfKeyState);
            pdwEffect = fb.Accept && (pdwEffect & Ole.DROPEFFECT_COPY) != 0 ? Ole.DROPEFFECT_COPY : Ole.DROPEFFECT_NONE;
            UpdateDescription(fb);
            _helper?.DragOver(ref p, pdwEffect);
        }
        catch (Exception ex)
        {
            ProbeLog.Fail($"DragOver threw: {ex.Message}");
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
        catch (Exception ex) { ProbeLog.Fail($"DragLeave threw: {ex.Message}"); }
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
            ProbeLog.Fail($"Drop threw: {ex}");
            pdwEffect = Ole.DROPEFFECT_NONE;
        }
        _current = null;
        return Ole.S_OK;
    }

    private void UpdateDescription(DragFeedback fb)
    {
        if (!_useDescriptions || _current is null) return;
        string key = $"{fb.Accept}|{fb.Message}|{fb.Insert}";
        if (key == _lastDescription) return;
        _lastDescription = key;
        bool ok = fb.Message is null
            ? DataObjectReader.TrySetDropDescription(_current, Ole.DROPIMAGE_NONE, "", "")
            : DataObjectReader.TrySetDropDescription(_current, fb.Accept ? Ole.DROPIMAGE_COPY : Ole.DROPIMAGE_NONE, fb.Message, fb.Insert ?? "");
        if (!ok) ProbeLog.Warn("SetData(DropDescription) rejected by the data object");
    }

    /// <summary>Hand the drag tooltip back to whatever target is next (DROPIMAGE_INVALID = "not set").</summary>
    private void ClearDescription()
    {
        if (_useDescriptions && _current is not null && _lastDescription is not null)
            DataObjectReader.TrySetDropDescription(_current, Ole.DROPIMAGE_INVALID, "", "");
        _lastDescription = null;
    }

    public static string EffectNames(int e) => e == 0 ? "NONE" : string.Join("|",
        new[] { (Ole.DROPEFFECT_COPY, "COPY"), (Ole.DROPEFFECT_MOVE, "MOVE"), (Ole.DROPEFFECT_LINK, "LINK") }
            .Where(x => (e & x.Item1) != 0).Select(x => x.Item2));
}

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chango.Core;

/// <summary>
/// The files and folders <see cref="SafeOutput"/> published in this session, by identity (volume and file ID), so
/// <see cref="Undo"/> can tell Chango's own results apart from anything else even though a result carries the
/// original's (older) dates. A path counts only while it is still the very file Chango made, unchanged: a file put
/// there in its place has another identity, or (when Windows reuses the ID of a file just deleted) another size or
/// modified time.
/// </summary>
public static class OutputLedger
{
    private const int Capacity = 4096; // undo is offered for seconds; older entries only need to be forgotten
    private static readonly Dictionary<string, FileIdentity> Published = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    private readonly record struct FileIdentity(uint Volume, ulong Index, ulong Size, long Modified);

    public static void Record(string path)
    {
        if (IdentityOf(path) is not { } id) return;
        string key = Normalize(path);
        lock (Published)
        {
            if (!Published.ContainsKey(key)) Order.Enqueue(key);
            Published[key] = id;
            while (Order.Count > Capacity) Published.Remove(Order.Dequeue());
        }
    }

    /// <summary>True when <paramref name="path"/> is still the file or folder Chango published there.</summary>
    public static bool IsPublished(string path)
    {
        FileIdentity id;
        lock (Published)
            if (!Published.TryGetValue(Normalize(path), out id)) return false;
        return IdentityOf(path) == id;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static FileIdentity? IdentityOf(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // No access rights needed to read the identity; backup semantics lets the same call open folders.
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) return null;
        bool folder = (info.FileAttributes & (uint)FileAttributes.Directory) != 0;
        // A folder's modified time moves whenever anything inside changes, so a folder is known by its ID alone.
        return new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow,
            folder ? 0 : ((ulong)info.FileSizeHigh << 32) | info.FileSizeLow, folder ? 0 : info.LastWriteTime);
    }

    private const uint BackupSemantics = 0x02000000; // FILE_FLAG_BACKUP_SEMANTICS

    // FILETIMEs are two DWORDs in Windows, so they sit at 4-byte offsets: without Pack = 4 every later field would shift.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);
}

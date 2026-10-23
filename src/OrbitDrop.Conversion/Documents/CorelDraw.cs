using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitDrop.Conversion.Documents;

/// <summary>
/// CorelDRAW drawings (.cdr, and Corel's exchange format .cmx), read by OrbitDrop's own reader, OrbitDropCdr.exe
/// (src/OrbitDrop.CdrReader: libcdr, the reader Inkscape and LibreOffice use). Nothing else needs to be installed.
/// The reader runs as a separate process with a time limit, so a damaged or hostile file can only fail its own
/// conversion: it can't crash or freeze OrbitDrop.
/// </summary>
public static class CorelDraw
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>OrbitDropCdr.exe, shipped next to OrbitDrop.exe. Tests point this at the build output.</summary>
    public static string ReaderPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "OrbitDropCdr.exe");

    public static bool Available => File.Exists(ReaderPath);

    /// <summary>
    /// Each page of the drawing as an SVG file in <paramref name="workFolder"/> (a fresh, private temp folder).
    /// The drawing is copied in under a short name first: long download names would pass Windows' path limit.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ToSvgPagesAsync(string file, string workFolder, CancellationToken ct)
    {
        if (!Available) throw new NotSupportedException("OrbitDrop's CorelDRAW reader is missing. Reinstalling OrbitDrop brings it back.");
        Directory.CreateDirectory(workFolder);
        string input = Path.Combine(workFolder, "drawing" + Path.GetExtension(file).ToLowerInvariant());
        File.Copy(file, input, overwrite: true);

        var start = new ProcessStartInfo(ReaderPath) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(input);
        start.ArgumentList.Add(workFolder);
        using var process = Process.Start(start) ?? throw new IOException("The CorelDRAW reader didn't start.");
        using var job = Limits.Apply(process); // at most 2 GB of memory, and it ends if OrbitDrop does
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException("Reading the drawing took too long, so it was stopped.");
        }
        await Task.WhenAll(stdout, stderr);

        if (process.ExitCode != 0 && job?.HitMemoryLimit == true) throw new InvalidDataException("The drawing needed too much memory to read.");
        switch (process.ExitCode)
        {
            case 0: break;
            case 2: throw new InvalidDataException("This drawing is from a CorelDRAW version OrbitDrop can't read yet, or it's password-protected.");
            case 3: throw new InvalidDataException("The drawing couldn't be read. It may be damaged.");
            default: throw new IOException("The CorelDRAW reader couldn't open the file" + (stderr.Result.Trim() is { Length: > 0 } e ? ": " + e : "."));
        }
        var pages = Directory.GetFiles(workFolder, "page-*.svg")
            .OrderBy(p => int.TryParse(Path.GetFileNameWithoutExtension(p)["page-".Length..], out int n) ? n : int.MaxValue).ToList();
        if (pages.Count == 0) throw new InvalidDataException("The drawing has no pages.");
        return pages;
    }

    /// <summary>A Windows job object around the reader: a memory ceiling, and it's killed if OrbitDrop closes.</summary>
    private sealed class Limits : IDisposable
    {
        private const long MaxMemory = 2L * 1024 * 1024 * 1024;
        private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000, JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x400;
        private const int JobObjectExtendedLimitInformation = 9;
        private readonly IntPtr _job;

        private Limits(IntPtr job) => _job = job;

        /// <summary>The reader got close to its memory ceiling (so a failure was most likely that).</summary>
        public bool HitMemoryLimit => PeakMemory() >= MaxMemory * 9 / 10;

        public static Limits? Apply(Process process)
        {
            IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return null;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION,
                },
                ProcessMemoryLimit = (UIntPtr)MaxMemory,
            };
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>())
                || !AssignProcessToJobObject(job, process.Handle))
            {
                CloseHandle(job);
                return null;
            }
            return new Limits(job);
        }

        private long PeakMemory()
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            return QueryInformationJobObject(_job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), out _)
                ? (long)(ulong)info.PeakProcessMemoryUsed : 0;
        }

        public void Dispose() => CloseHandle(_job);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length, out int returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

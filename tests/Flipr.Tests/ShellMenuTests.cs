using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Flipr.Windows.Shell;

namespace Flipr.Tests;

/// <summary>Runs only after build/shell/build-shell.ps1 has built the DLL (not on CI's test step).</summary>
public sealed class ShellDllFactAttribute : FactAttribute
{
    public static string Dll => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\artifacts\shell\FliprShell.dll"));
    public ShellDllFactAttribute() { if (!File.Exists(Dll)) Skip = "FliprShell.dll isn't built"; }
}

/// <summary>
/// The right-click entry exactly as Explorer uses it: load the DLL, create the command through COM, ask its title and
/// state, invoke it on real files, and check the paths arrive at an Flipr message window. Nothing is registered.
/// </summary>
public class ShellMenuTests
{
    private static readonly Guid Clsid = new(ContextMenu.Clsid);

    [ShellDllFact]
    public void The_menu_entry_hands_the_chosen_files_to_Flipr()
    {
        using var t = new TempFolder();
        string a = t.File("first file.txt"), b = t.File("second & last.png");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        IReadOnlyList<string>? received = null;
        string? title = null;
        uint state = 99;

        RunSta(() =>
        {
            // A stand-in for Flipr's message window, answering like App.OnShellMessage does.
            using var window = new HwndSource(new HwndSourceParameters("Flipr.MessageWindow") { Width = 0, Height = 0, WindowStyle = 0 });
            window.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (ContextMenu.TryReadFiles(msg, l) is { } files) { received = files; handled = true; return 1; }
                return IntPtr.Zero;
            });

            var command = CreateCommand();
            var items = ShellItems([a, b]);
            Marshal.ThrowExceptionForHR(command.GetTitle(items, out IntPtr name));
            title = Marshal.PtrToStringUni(name);
            Marshal.FreeCoTaskMem(name);
            Marshal.ThrowExceptionForHR(command.GetState(items, false, out state));
            Marshal.ThrowExceptionForHR(command.Invoke(items, IntPtr.Zero));

            // The DLL hands over on its own thread with SendMessage: keep pumping until the files arrive.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (received is null && DateTime.UtcNow < deadline)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
                Thread.Sleep(20);
            }
        });

        Assert.Equal("Flipr", title);
        Assert.Equal(0u, state); // ECS_ENABLED
        Assert.NotNull(received);
        Assert.Equal([a, b], received!);
    }

    [ShellDllFact]
    public void Turning_the_menu_off_in_Settings_hides_the_entry()
    {
        using var t = new TempFolder();
        string a = t.File("x.txt");
        File.WriteAllText(a, "x");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Flipr");
        object? before = key.GetValue("ContextMenu");
        try
        {
            key.SetValue("ContextMenu", 0, RegistryValueKind.DWord);
            uint state = 0;
            RunSta(() => Marshal.ThrowExceptionForHR(CreateCommand().GetState(ShellItems([a]), false, out state)));
            Assert.Equal(2u, state); // ECS_HIDDEN
        }
        finally
        {
            if (before is null) key.DeleteValue("ContextMenu", throwOnMissingValue: false);
            else key.SetValue("ContextMenu", before, RegistryValueKind.DWord);
        }
    }

    // ------------------------------------------------------------------ COM plumbing, as Explorer does it

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception(error.Message, error);
    }

    private delegate int DllGetClassObjectFn(ref Guid clsid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IClassFactory factory);

    private static IExplorerCommand CreateCommand()
    {
        IntPtr module = LoadLibrary(ShellDllFactAttribute.Dll);
        if (module == IntPtr.Zero) throw new DllNotFoundException(ShellDllFactAttribute.Dll);
        var get = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(GetProcAddress(module, "DllGetClassObject"));
        Guid clsid = Clsid, iidFactory = typeof(IClassFactory).GUID, iidCommand = typeof(IExplorerCommand).GUID;
        Marshal.ThrowExceptionForHR(get(ref clsid, ref iidFactory, out var factory));
        factory.CreateInstance(null, ref iidCommand, out object command);
        return (IExplorerCommand)command;
    }

    private static IShellItemArray ShellItems(string[] paths)
    {
        var pidls = paths.Select(p => { Marshal.ThrowExceptionForHR(SHParseDisplayName(p, IntPtr.Zero, out IntPtr pidl, 0, out _)); return pidl; }).ToArray();
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array));
            return array;
        }
        finally { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); }
    }

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        void CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object? outer, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        void LockServer(bool lockIt);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IShellItemArray? items, out IntPtr name);
        [PreserveSig] int GetIcon(IShellItemArray? items, out IntPtr icon);
        [PreserveSig] int GetToolTip(IShellItemArray? items, out IntPtr tip);
        [PreserveSig] int GetCanonicalName(out Guid name);
        [PreserveSig] int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);
        [PreserveSig] int Invoke(IShellItemArray? items, IntPtr bindContext);
        [PreserveSig] int GetFlags(out uint flags);
        [PreserveSig] int EnumSubCommands(out IntPtr commands);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IShellItemArray array);
}

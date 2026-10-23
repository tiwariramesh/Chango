using System.Runtime.InteropServices;

namespace Chango.Conversion.Imaging;

/// <summary>
/// Asks Windows Imaging Component which image formats this PC can decode, including codecs installed from the
/// Microsoft Store (HEIF/AVIF, WebP, JPEG XL, RAW …), which aren't visible in the registry. Calls the COM vtables
/// directly: the classic interop marshaller fails to QueryInterface WIC's enumerator.
/// </summary>
public static unsafe class WicCodecs
{
    private static readonly Lazy<HashSet<string>> Cached = new(Enumerate);

    /// <summary>Lower-case extensions with a dot (".heic"). Empty if WIC can't be queried.</summary>
    public static IReadOnlySet<string> DecoderExtensions => Cached.Value;

    /// <summary>Why enumeration failed, if it did (diagnostics).</summary>
    public static Exception? LastError { get; private set; }

    // vtable slots: IUnknown has 3; IWICImagingFactory::CreateComponentEnumerator is its 21st method;
    // IWICBitmapCodecInfo::GetFileExtensions follows 8 IWICComponentInfo and 6 earlier codec-info methods.
    private const int CreateComponentEnumeratorSlot = 3 + 20, EnumNextSlot = 3, GetFileExtensionsSlot = 3 + 8 + 6;
    private const uint WICDecoder = 0x1, CLSCTX_INPROC_SERVER = 0x1;
    private static readonly Guid FactoryClsid = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid FactoryIid = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid CodecInfoIid = new("E87A44C4-B76E-4c47-8B09-298EB12A2714");

    private static HashSet<string> Enumerate()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr factory = IntPtr.Zero, enumerator = IntPtr.Zero;
        try
        {
            Guid clsid = FactoryClsid, iid = FactoryIid;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out factory));
            var fvt = *(IntPtr**)factory;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)fvt[CreateComponentEnumeratorSlot])(factory, WICDecoder, 0, &enumerator));
            var evt = *(IntPtr**)enumerator;
            var next = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)evt[EnumNextSlot];
            while (true)
            {
                IntPtr item; uint fetched;
                if (next(enumerator, 1, &item, &fetched) != 0 || fetched == 0) break;
                try
                {
                    if (Marshal.QueryInterface(item, in CodecInfoIid, out IntPtr codec) != 0) continue;
                    try
                    {
                        var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, char*, uint*, int>)(*(IntPtr**)codec)[GetFileExtensionsSlot];
                        uint length;
                        if (get(codec, 0, null, &length) != 0 || length == 0) continue;
                        var buffer = new char[length];
                        fixed (char* b = buffer) if (get(codec, length, b, &length) != 0) continue;
                        foreach (var ext in new string(buffer).TrimEnd('\0').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            result.Add(ext.ToLowerInvariant());
                    }
                    finally { Marshal.Release(codec); }
                }
                finally { Marshal.Release(item); }
            }
        }
        catch (Exception ex) { LastError = ex; /* fall back to the formats WPF always supports */ }
        finally
        {
            if (enumerator != IntPtr.Zero) Marshal.Release(enumerator);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
        return result;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
}

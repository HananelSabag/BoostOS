using System.Runtime.InteropServices;

namespace BoostOS.Core;

/// <summary>
/// Where the sound goes right now: the default playback device and the bus it hangs on. Bluetooth audio streams over
/// the air all the time, and on cards where Wi-Fi and Bluetooth share one antenna that air time is taken from Wi-Fi.
/// </summary>
internal static class AudioRoute
{
    public sealed record Output(string Name, bool Bluetooth);

    public static Output? DefaultOutput()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 0 /* console */, out var device) != 0) return null;
                try
                {
                    if (device.OpenPropertyStore(0, out var store) != 0) return null;
                    try
                    {
                        // The device's own name ("UGREEN-90748") reads better than the endpoint's ("Headphones (UGREEN-90748)").
                        var name = Read(store, DeviceName) ?? Read(store, FriendlyName) ?? "";
                        var bus = Read(store, EnumeratorName) ?? "";
                        return new Output(name, bus.Equals("BTHENUM", StringComparison.OrdinalIgnoreCase)
                                                || bus.Equals("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase));
                    }
                    finally { Marshal.ReleaseComObject(store); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    private static readonly PropertyKey FriendlyName = new() { Fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
    private static readonly PropertyKey DeviceName = new() { Fmtid = new("026e516e-b814-414b-83cd-856d6fef4822"), Pid = 2 };
    private static readonly PropertyKey EnumeratorName = new() { Fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 24 };

    private static string? Read(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return null;
        try { return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Fmtid; public int Pid; }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant { [FieldOffset(0)] public short Type; [FieldOffset(8)] public IntPtr Pointer; [FieldOffset(16)] public IntPtr Spare; }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}

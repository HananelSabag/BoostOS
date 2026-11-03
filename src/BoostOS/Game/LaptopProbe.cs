using System.Runtime.InteropServices;

namespace BoostOS.Game;

/// <summary>
/// Fans and CPU temperature on Clevo-built laptops (the Gigabyte G5 and many others), from InsydeDCHU.dll, the vendor
/// DLL that Control Center itself uses. Only its read call is made, with the read command 12: nothing is ever written
/// to the laptop's controller. On other PCs the DLL isn't there and there is simply no reading.
/// </summary>
internal static class LaptopProbe
{
    public sealed record Reading(int? CpuTemp, int? GpuTemp, int CpuFanDuty, int GpuFanDuty, int? CpuFanRpm, int? GpuFanRpm)
    {
        public int FanDuty => Math.Max(CpuFanDuty, GpuFanDuty);
    }

    private const int FanStatusCommand = 12;
    private static readonly object Gate = new();
    private static readonly Lazy<GetData?> Reader = new(Load);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetData(int command, [In, Out] byte[] buffer);

    public static Reading? Read()
    {
        if (Reader.Value is not { } read) return null;
        var buffer = new byte[256];
        try { lock (Gate) read(FanStatusCommand, buffer); }
        catch (Exception ex) when (ex is SEHException or AccessViolationException) { return null; }
        return Parse(buffer);
    }

    /// <summary>
    /// Command 12's buffer on the Clevo-built Gigabyte G5: fan 1 (CPU) tacho period at bytes 2-3, fan 2 (GPU) at 4-5
    /// (rpm = 2156220 / period), CPU fan duty at 16, CPU temperature at 18, GPU fan duty at 19, GPU temperature at 21.
    /// Checked against nvidia-smi and the fans' sound (DeviceHub).
    /// </summary>
    internal static Reading? Parse(byte[] buf)
    {
        if (buf.Length < 22 || buf.All(b => b == 0)) return null;
        int? Temp(byte b) => b is > 0 and < 120 ? b : null;
        int? Rpm(int hi, int lo) { var period = (buf[hi] << 8) | buf[lo]; return period is 0 or >= 0xFF00 ? 0 : (int)Math.Round(2156220.0 / period); }
        int Duty(byte b) => (int)Math.Round(b * 100 / 255.0);
        return new Reading(Temp(buf[18]), Temp(buf[21]), Duty(buf[16]), Duty(buf[19]), Rpm(2, 3), Rpm(4, 5));
    }

    private static GetData? Load()
    {
        if (!Environment.Is64BitProcess || FindDll() is not { } dll) return null;
        var library = LoadLibraryEx(dll, IntPtr.Zero, LoadWithAlteredSearchPath);
        if (library == IntPtr.Zero) return null;
        var function = GetProcAddress(library, "GetDCHU_Data_Buffer");
        return function == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<GetData>(function);
    }

    /// <summary>The DLL lives in the driver store, in the folder of Clevo's ACPI bridge driver package.</summary>
    private static string? FindDll()
    {
        var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
        try
        {
            return Directory.EnumerateDirectories(store, "acpibridge*")
                .Select(dir => Path.Combine(dir, "InsydeDCHU.dll"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    /// <summary>True on battery, false on the charger, null without a battery.</summary>
    public static bool? OnBattery() =>
        GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.ACLineStatus != 255 ? s.ACLineStatus == 0 : null;

    private const uint LoadWithAlteredSearchPath = 0x00000008;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)] private static extern IntPtr GetProcAddress(IntPtr library, string name);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

using BoostOS.Core;

namespace BoostOS.Diagnostics;

/// <summary>Facts about the machine that several checks need. Collected once per scan.</summary>
internal sealed class Facts
{
    public required string Machine { get; init; }
    public required bool IsLaptop { get; init; }
    public required IReadOnlyList<string> GpuNames { get; init; }
    /// <summary>Whole-machine CPU %, sampled before the scan starts so the scan's own work doesn't count.</summary>
    public required double CpuLoad { get; init; }
    public bool HasNvidia => GpuNames.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
    public bool HasTwoGpus => GpuNames.Count > 1;

    public static Facts Collect()
    {
        var cpuLoad = SampleCpuLoad(1000);
        var cs =Wmi.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
        var machine = cs is null ? Environment.MachineName : $"{cs.Str("Manufacturer")} {cs.Str("Model")}".Trim();

        // Chassis types 8-14 and 30-32 are portable/laptop/notebook/tablet variants.
        int[] portable = [8, 9, 10, 11, 12, 14, 30, 31, 32];
        var isLaptop = Wmi.Query("SELECT ChassisTypes FROM Win32_SystemEnclosure")
            .Any(e => e["ChassisTypes"] is ushort[] types && types.Any(t => portable.Contains(t)));
        isLaptop |= SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;

        var gpus = Wmi.Query("SELECT Name FROM Win32_VideoController").Select(g => g.Str("Name")).Where(n => n != "").ToList();

        return new Facts { Machine = machine, IsLaptop = isLaptop, GpuNames = gpus, CpuLoad = cpuLoad };
    }

    /// <summary>Whole-machine CPU usage over a short window, from kernel idle/busy counters.</summary>
    private static double SampleCpuLoad(int ms)
    {
        if (!Native.GetSystemTimes(out var idle1, out var kernel1, out var user1)) return 0;
        Thread.Sleep(ms);
        if (!Native.GetSystemTimes(out var idle2, out var kernel2, out var user2)) return 0;
        var total = (kernel2 - kernel1) + (user2 - user1); // kernel time includes idle time
        return total <= 0 ? 0 : Math.Clamp(100.0 * (total - (idle2 - idle1)) / total, 0, 100);
    }
}

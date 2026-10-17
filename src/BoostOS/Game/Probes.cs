using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using BoostOS.Core;
using Microsoft.Win32;

namespace BoostOS.Game;

/// <summary>
/// Light system probes used while a game runs. None of them reads the game: they ask Windows about the network,
/// the Wi-Fi link, the CPU clock, memory and the process list, exactly like Task Manager does.
/// </summary>
internal sealed class PingProbe : IDisposable
{
    public sealed record Result(double Seconds, string Target, double? Ms);

    private readonly List<Result> results = [];
    private readonly Func<double> elapsed;
    private readonly System.Threading.Timer timer;
    private readonly IPAddress? router;
    private readonly IPAddress internet = IPAddress.Parse("1.1.1.1");
    private int tick;
    private int busy;
    private volatile bool stopped;

    public double? LastRouter { get; private set; }
    public double? LastInternet { get; private set; }

    /// <param name="router">The gateway; one ping a second stays far below the rate home routers start to limit.</param>
    public PingProbe(IPAddress? router, Func<double> elapsed)
    {
        this.router = router;
        this.elapsed = elapsed;
        timer = new System.Threading.Timer(_ => _ = TickAsync(), null, 1000, 1000);
    }

    private async Task TickAsync()
    {
        if (stopped || Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var n = tick++;
            var jobs = new List<Task>();
            if (router is not null) jobs.Add(Send("router", router, ms => LastRouter = ms));
            if (n % 2 == 0) jobs.Add(Send("internet", internet, ms => LastInternet = ms));
            await Task.WhenAll(jobs).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    private async Task Send(string target, IPAddress address, Action<double?> last)
    {
        var at = elapsed();
        double? ms = null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 900).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success) ms = reply.RoundtripTime;
        }
        catch (PingException) { }
        catch (InvalidOperationException) { }
        if (stopped) return;
        last(ms);
        lock (results) results.Add(new Result(Math.Round(at, 2), target, ms));
    }

    public List<Result> Results() { lock (results) return [.. results]; }

    public void Dispose()
    {
        stopped = true;
        timer.Dispose();
    }
}

/// <summary>Signal and link speed of the connected Wi-Fi adapter (Native Wifi API, wlanapi.dll).</summary>
internal static class WifiProbe
{
    public sealed record Reading(int Quality, int? Rssi, double RxMbps, double TxMbps);

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid iface, uint opCode, IntPtr reserved, out uint size, out IntPtr data, IntPtr valueType);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);

    private const uint CurrentConnection = 7, Rssi = 0x10000102;
    private const int InterfaceInfoSize = 532; // GUID + WCHAR[256] + state

    public static Reading? Read()
    {
        IntPtr handle;
        try { if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return null; }
        catch (DllNotFoundException) { return null; } // no Wi-Fi service on this PC
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0) return null;
            try
            {
                var count = Marshal.ReadInt32(list);
                for (var i = 0; i < count; i++)
                {
                    var info = list + 8 + i * InterfaceInfoSize;
                    if (Marshal.ReadInt32(info, 528) != 1) continue; // wlan_interface_state_connected
                    var guid = Marshal.PtrToStructure<Guid>(info);
                    if (WlanQueryInterface(handle, ref guid, CurrentConnection, IntPtr.Zero, out _, out var conn, IntPtr.Zero) != 0) continue;
                    int quality; uint rx, tx;
                    try
                    {
                        // WLAN_CONNECTION_ATTRIBUTES: association attributes start at 520; signal 56, rx 60, tx 64 into them.
                        quality = Marshal.ReadInt32(conn, 576);
                        rx = (uint)Marshal.ReadInt32(conn, 580);
                        tx = (uint)Marshal.ReadInt32(conn, 584);
                    }
                    finally { WlanFreeMemory(conn); }
                    int? rssi = null;
                    if (WlanQueryInterface(handle, ref guid, Rssi, IntPtr.Zero, out _, out var r, IntPtr.Zero) == 0)
                    {
                        rssi = Marshal.ReadInt32(r);
                        WlanFreeMemory(r);
                    }
                    return new Reading(quality, rssi, rx / 1000d, tx / 1000d);
                }
                return null;
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }
}

/// <summary>The CPU's real clock right now: "% Processor Performance" × base clock, the number Task Manager calls Speed.</summary>
internal sealed class CpuClockProbe : IDisposable
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out FmtValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)] private struct FmtValue { public uint Status; public uint Pad; public double Value; }

    private readonly IntPtr query, counter;
    public double BaseMhz { get; }

    public CpuClockProbe()
    {
        BaseMhz = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", null) is int mhz ? mhz : 0;
        if (PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
        if (PdhAddEnglishCounterW(query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out counter) != 0) counter = IntPtr.Zero;
        PdhCollectQueryData(query); // rate counters need a first sample
    }

    /// <summary>Average effective clock across cores since the last call, in MHz.</summary>
    public double? Read()
    {
        if (query == IntPtr.Zero || counter == IntPtr.Zero || BaseMhz <= 0) return null;
        if (PdhCollectQueryData(query) != 0) return null;
        if (PdhGetFormattedCounterValue(counter, 0x200 | 0x8000, IntPtr.Zero, out var v) != 0 || v.Status > 1) return null; // DOUBLE | NOCAP100
        return v.Value > 0 ? Math.Round(BaseMhz * v.Value / 100) : null;
    }

    public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); }
}

internal static class MemoryProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>Used RAM in percent, and the total in GB.</summary>
    public static (double UsedPct, double TotalGb)? Read()
    {
        var s = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref s) || s.TotalPhys == 0) return null;
        return (Math.Round(100d * (s.TotalPhys - s.AvailPhys) / s.TotalPhys, 1), Math.Round(s.TotalPhys / 1073741824d, 1));
    }
}

/// <summary>
/// CPU time used by every other process during the session, from the kernel's process snapshot. It answers
/// "what else was busy while I played?" without opening a single process (the game's own entries are skipped).
/// </summary>
internal sealed class BackgroundCpu
{
    private readonly HashSet<string> skip;
    private Dictionary<(int, long), (string Name, long Cpu)> last = [];
    private readonly Dictionary<string, long> used = [];
    private readonly long startTicks = DateTime.UtcNow.Ticks;
    private bool first = true;

    public BackgroundCpu(IEnumerable<string> gameProcesses) =>
        skip = new HashSet<string>(gameProcesses.Concat(["idle", "system idle process", "memory compression", "secure system", "registry"]));

    public void Sample(IReadOnlyList<ProcessInfo.Entry>? snapshot = null)
    {
        snapshot ??= ProcessInfo.Snapshot();
        var now = new Dictionary<(int, long), (string, long)>(snapshot.Count);
        foreach (var e in snapshot)
        {
            if (skip.Contains(e.Name)) continue;
            var key = (e.Pid, e.CreateTime);
            now[key] = (e.Name, e.CpuTime);
            // Processes started during the session count from zero; the first snapshot is the baseline.
            var before = last.TryGetValue(key, out var b) ? b.Cpu : first ? e.CpuTime : 0;
            var delta = e.CpuTime - before;
            if (delta > 0) used[e.Name] = used.GetValueOrDefault(e.Name) + delta;
        }
        last = now;
        first = false;
    }

    /// <summary>The busiest processes as a share of the whole CPU (all cores) over the session.</summary>
    public List<BackgroundApp> Top(int count = 6)
    {
        var wall = (DateTime.UtcNow.Ticks - startTicks) * (double)Environment.ProcessorCount;
        if (wall <= 0) return [];
        return used.OrderByDescending(kv => kv.Value).Take(count)
            .Select(kv => new BackgroundApp(kv.Key, Math.Round(100d * kv.Value / wall, 2)))
            .Where(a => a.CpuPct >= 0.05).ToList();
    }
}

public sealed record BackgroundApp(string Name, double CpuPct);

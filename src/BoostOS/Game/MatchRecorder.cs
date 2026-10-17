using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BoostOS.Core;

namespace BoostOS.Game;

public sealed record MatchSample(double GpuUtil, double GpuTemp, double GpuClock, double GpuPower, bool GpuLimited, double Cpu, double BusiestCore);

public sealed record MatchReport(
    DateTime Start, double Minutes, bool GameMode, int Samples,
    double? GpuUtil, double? GpuTempAvg, double? GpuTempMax, double? GpuClock, double? GpuPower, double? GpuLimitedPct,
    double CpuAvg, double BusiestCoreAvg, string Bottleneck);

/// <summary>
/// Watches the PC, not the game, while a match runs: GPU numbers come from the driver (nvidia-smi), CPU numbers from
/// Windows' own per-core counters. Nothing reads or opens the game process, so Easy Anti-Cheat has nothing to see.
/// One sample every 2 seconds keeps the cost close to zero.
/// </summary>
internal sealed class MatchRecorder : IDisposable
{
    private readonly List<MatchSample> samples = [];
    private readonly object gate = new();
    private readonly DateTime start = DateTime.Now;
    private readonly bool gameMode;
    private readonly Process? smi;
    private readonly System.Threading.Timer cpuTimer;
    private (double Cpu, double Busiest) lastCpu;
    private long[]? prevCore;

    public MatchRecorder(bool gameMode)
    {
        this.gameMode = gameMode;
        cpuTimer = new System.Threading.Timer(_ => SampleCpu(), null, 0, 2000);

        var exe = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(exe)) return;
        try
        {
            smi = Process.Start(new ProcessStartInfo(exe,
                "--query-gpu=utilization.gpu,temperature.gpu,clocks.gr,power.draw,clocks_event_reasons.active --format=csv,noheader,nounits -lms 2000")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (smi is null) return;
            smi.OutputDataReceived += (_, e) => OnGpuLine(e.Data);
            smi.BeginOutputReadLine();
        }
        catch
        {
            smi = null; // no GPU numbers, CPU numbers still work
        }
    }

    private void OnGpuLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var v = line.Split(',').Select(s => s.Trim()).ToArray();
        if (v.Length < 5) return;
        double N(int i) => double.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        // Throttle reasons that mean "held back": SW power cap 0x4, HW slowdown 0x8, SW thermal 0x20, HW thermal 0x40, power brake 0x80.
        var reasons = ulong.TryParse(v[4].Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ? r : 0;
        var limited = (reasons & 0xEC) != 0;
        lock (gate)
            samples.Add(new MatchSample(N(0), N(1), N(2), N(3), limited, lastCpu.Cpu, lastCpu.Busiest));
    }

    private void SampleCpu()
    {
        var now = ReadCoreTimes();
        if (now is null) return;
        if (prevCore is not null)
        {
            double totalBusy = 0, totalAll = 0, busiest = 0;
            for (var c = 0; c < now.Length / 3; c++)
            {
                var idle = now[c * 3] - prevCore[c * 3];
                var all = (now[c * 3 + 1] - prevCore[c * 3 + 1]) + (now[c * 3 + 2] - prevCore[c * 3 + 2]); // kernel includes idle
                if (all <= 0) continue;
                var busy = all - idle;
                totalBusy += busy;
                totalAll += all;
                busiest = Math.Max(busiest, 100.0 * busy / all);
            }
            lastCpu = (totalAll > 0 ? 100.0 * totalBusy / totalAll : 0, busiest);
            if (smi is null) lock (gate) samples.Add(new MatchSample(double.NaN, double.NaN, double.NaN, double.NaN, false, lastCpu.Cpu, lastCpu.Busiest));
        }
        prevCore = now;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    /// <summary>Idle, kernel and user time for every logical core (SystemProcessorPerformanceInformation).</summary>
    private static long[]? ReadCoreTimes()
    {
        const int entry = 48; // 5 × LARGE_INTEGER + ULONG, padded
        var cores = Environment.ProcessorCount;
        var buf = Marshal.AllocHGlobal(entry * cores);
        try
        {
            if (NtQuerySystemInformation(8, buf, entry * cores, out _) != 0) return null;
            var result = new long[cores * 3];
            for (var c = 0; c < cores; c++)
            {
                result[c * 3] = Marshal.ReadInt64(buf, c * entry);          // IdleTime
                result[c * 3 + 1] = Marshal.ReadInt64(buf, c * entry + 8);  // KernelTime
                result[c * 3 + 2] = Marshal.ReadInt64(buf, c * entry + 16); // UserTime
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public MatchReport Finish()
    {
        Dispose();
        List<MatchSample> s;
        lock (gate) s = [.. samples];
        return Summarize(start, DateTime.Now, gameMode, s);
    }

    internal static MatchReport Summarize(DateTime start, DateTime end, bool gameMode, IReadOnlyList<MatchSample> s)
    {
        // Skip the first 20 seconds: loading screens are not gameplay.
        var play = s.Count > 15 ? s.Skip(10).ToList() : s.ToList();
        double? Avg(Func<MatchSample, double> f)
        {
            var vals = play.Select(f).Where(x => !double.IsNaN(x)).ToList();
            return vals.Count == 0 ? null : Math.Round(vals.Average(), 1);
        }
        double? Max(Func<MatchSample, double> f)
        {
            var vals = play.Select(f).Where(x => !double.IsNaN(x)).ToList();
            return vals.Count == 0 ? null : Math.Round(vals.Max(), 1);
        }
        var util = Avg(x => x.GpuUtil);
        var busiest = Avg(x => x.BusiestCore) ?? 0;
        var limited = play.Count(x => !double.IsNaN(x.GpuUtil)) is var n and > 0 ? Math.Round(100.0 * play.Count(x => x.GpuLimited) / n, 0) : (double?)null;

        // Rocket League at a high frame rate is usually held back by one CPU core, not the GPU.
        var bottleneck = util switch
        {
            null => "unknown",
            >= 90 => "gpu",
            _ when busiest >= 85 => "cpu",
            _ => "none",
        };

        return new MatchReport(start, Math.Round((end - start).TotalMinutes, 1), gameMode, play.Count,
            util, Avg(x => x.GpuTemp), Max(x => x.GpuTemp), Avg(x => x.GpuClock), Avg(x => x.GpuPower), limited,
            Avg(x => x.Cpu) ?? 0, busiest, bottleneck);
    }

    public void Dispose()
    {
        cpuTimer.Dispose();
        try { if (smi is { HasExited: false }) smi.Kill(); } catch { }
        smi?.Dispose();
    }
}

/// <summary>The last matches, newest first, so a report can be compared with the one before it.</summary>
internal static class MatchStore
{
    private static string FilePath => Path.Combine(AppSettings.DataDir, "matches.json");

    public static List<MatchReport> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<MatchReport>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch { return []; }
    }

    public static void Add(MatchReport report)
    {
        // Ignore launches that never reached gameplay.
        if (report.Minutes < 1) return;
        var list = Load();
        list.Insert(0, report);
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list.Take(20), new JsonSerializerOptions { WriteIndented = true }));
    }
}

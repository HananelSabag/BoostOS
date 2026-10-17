using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BoostOS.Core;

namespace BoostOS.Game;

/// <summary>
/// Samples system counters every two seconds while Rocket League is open, without reading the game process.
/// CPU collection remains independent of the optional driver stream. Detailed samples are archived at Finish.
/// </summary>
internal sealed class MatchRecorder : IDisposable
{
    private const int MaxSamples = 43_200; // cap live memory at roughly 24 hours
    private readonly List<MatchSample> samples = [];
    private readonly object gate = new();
    private readonly object samplingGate = new();
    private readonly object stopGate = new();
    private readonly DateTimeOffset start;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Func<double> elapsed;
    private readonly Func<long[]?> readCpu;
    private readonly bool gameMode;
    private readonly GpuSampleStream gpu = new();
    private Process? smi;
    private readonly System.Threading.Timer cpuTimer;
    private long[]? prevCore;
    private bool stopped;
    private bool truncated;
    private DateTimeOffset? ended;
    private MatchReport? finalReport;

    public MatchRecorder(bool gameMode) : this(gameMode, ReadCoreTimes)
    {
        var exe = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(exe)) exe = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe";
        if (File.Exists(exe)) try
        {
            smi = new Process
            {
                StartInfo = new ProcessStartInfo(exe,
                    "--query-gpu=uuid,utilization.gpu,temperature.gpu,clocks.gr,power.draw,clocks_event_reasons.active --format=csv,noheader,nounits -lms 2000")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            smi.OutputDataReceived += (_, e) => AcceptGpuLine(e.Data);
            smi.ErrorDataReceived += (_, _) => { }; // drain errors so the optional producer cannot block
            smi.Exited += (_, _) => { lock (gate) gpu.Invalidate(); };
            if (!smi.Start()) throw new InvalidOperationException("Could not start GPU telemetry");
            smi.BeginOutputReadLine();
            smi.BeginErrorReadLine();
        }
        catch
        {
            try { if (smi is { HasExited: false }) smi.Kill(entireProcessTree: true); } catch { }
            smi?.Dispose();
            smi = null;
        }
        cpuTimer.Change(0, 2000);
    }

    // Injected counters/clock keep lifecycle and missing-GPU tests independent of real hardware.
    internal MatchRecorder(bool gameMode, Func<long[]?> readCpu, Func<double>? elapsed = null,
        DateTimeOffset? started = null, bool startTimer = false)
    {
        this.gameMode = gameMode;
        this.readCpu = readCpu;
        this.elapsed = elapsed ?? (() => clock.Elapsed.TotalSeconds);
        start = started ?? DateTimeOffset.Now;
        cpuTimer = new System.Threading.Timer(_ => SampleNow(), null, startTimer ? 0 : Timeout.Infinite, 2000);
    }

    internal void AcceptGpuLine(string? line)
    {
        lock (gate) { if (!stopped) gpu.Accept(line, elapsed()); }
    }

    internal void SampleNow()
    {
        lock (samplingGate)
        {
            lock (gate) { if (stopped) return; }
            long[]? now;
            try { now = readCpu(); } catch { now = null; }
            double totalBusy = 0, totalAll = 0, busiest = 0;
            if (now is not null && prevCore is not null && now.Length == prevCore.Length)
            {
                for (var c = 0; c < now.Length / 3; c++)
                {
                    var idle = now[c * 3] - prevCore[c * 3];
                    var all = (now[c * 3 + 1] - prevCore[c * 3 + 1]) + (now[c * 3 + 2] - prevCore[c * 3 + 2]);
                    if (all <= 0 || idle < 0 || idle > all) continue;
                    var busy = all - idle;
                    totalBusy += busy;
                    totalAll += all;
                    busiest = Math.Max(busiest, 100d * busy / all);
                }
            }
            prevCore = now;
            var seconds = elapsed();
            lock (gate)
            {
                if (stopped) return;
                var latest = gpu.Current(seconds);
                if (samples.Count >= MaxSamples) { samples.RemoveAt(0); truncated = true; }
                samples.Add(new MatchSample(latest?.Util ?? double.NaN, latest?.Temp ?? double.NaN,
                    latest?.Clock ?? double.NaN, latest?.Power ?? double.NaN,
                    latest?.Reasons is { } reasons && (reasons & SessionSummary.LimitedMask) != 0,
                    totalAll > 0 ? 100d * totalBusy / totalAll : double.NaN,
                    totalAll > 0 ? busiest : double.NaN)
                {
                    At = start.AddSeconds(seconds), Seconds = seconds, ClockReasons = latest?.Reasons,
                    GpuObservedAt = latest is null ? null : start.AddSeconds(latest.ObservedSeconds),
                    GpuId = gpu.SelectedId, CpuAvailable = totalAll > 0,
                });
            }
        }
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
        => FinishTo(Path.Combine(AppSettings.DataDir, "sessions"));

    internal MatchReport FinishTo(string archiveDirectory)
    {
        lock (stopGate)
        {
            if (finalReport is not null) return finalReport;
            Dispose();
            List<MatchSample> saved;
            lock (gate) saved = [.. samples];
            var end = ended ?? start;
            var report = Summarize(start.LocalDateTime, end.LocalDateTime, gameMode, saved) with
            {
                Minutes = Math.Round(Math.Max(0, (end - start).TotalMinutes), 1),
                TelemetryTruncated = truncated,
            };
            try
            {
                var name = SessionArchive.Save(SessionSummary.Recording(start, end, gameMode, truncated, saved), archiveDirectory);
                report = report with { TelemetryFile = name };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine(ex);
                report = report with { TelemetrySaveFailed = true };
            }
            return finalReport = report;
        }
    }

    internal static MatchReport Summarize(DateTime start, DateTime end, bool gameMode, IReadOnlyList<MatchSample> s)
        => SessionSummary.Build(start, end, gameMode, s);

    public void Dispose()
    {
        lock (stopGate)
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                ended = start.AddSeconds(elapsed());
            }
            // Timer disposal waits for callbacks; no callback holds stopGate or waits for the UI.
            cpuTimer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            lock (samplingGate) { } // drain an explicitly requested sample too
            try
            {
                if (smi is { HasExited: false }) { smi.Kill(entireProcessTree: true); smi.WaitForExit(2000); }
                try { smi?.CancelOutputRead(); } catch (InvalidOperationException) { }
                try { smi?.CancelErrorRead(); } catch (InvalidOperationException) { }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { smi?.Dispose(); }
            // Late GPU callbacks cannot add samples after stopped was set under gate.
        }
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
        SessionArchive.WriteAtomic(FilePath, list.Take(20).ToList());
    }
}

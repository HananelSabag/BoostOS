using System.Text.Json;
using System.Globalization;

namespace BoostOS.Game;

public sealed record MatchSample(double GpuUtil, double GpuTemp, double GpuClock, double GpuPower, bool GpuLimited, double Cpu, double BusiestCore)
{
    public DateTimeOffset? At { get; init; }
    public double? Seconds { get; init; }
    public ulong? ClockReasons { get; init; }
    public string? GpuId { get; init; }
    public DateTimeOffset? GpuObservedAt { get; init; }
    public bool CpuAvailable { get; init; } = true;
}

public sealed record MatchReport(
    DateTime Start, double Minutes, bool GameMode, int Samples,
    double? GpuUtil, double? GpuTempAvg, double? GpuTempMax, double? GpuClock, double? GpuPower, double? GpuLimitedPct,
    double CpuAvg, double BusiestCoreAvg, string Bottleneck)
{
    // Zero means an old summary: the original reason masks and timestamps were not retained.
    public int SchemaVersion { get; init; }
    public double? GpuPowerLimitedPct { get; init; }
    public double? GpuThermalLimitedPct { get; init; }
    public double? GpuHardwareSlowdownPct { get; init; }
    public int ClockReasonSamples { get; init; }
    public int GpuSamples { get; init; }
    public int CpuSamples { get; init; }
    public string? GpuId { get; init; }
    public string? TelemetryFile { get; init; }
    public bool TelemetrySaveFailed { get; init; }
    public bool TelemetryTruncated { get; init; }
    public IReadOnlyList<SessionPoint>? Timeline { get; init; }
}

public sealed record SessionPoint(double Seconds, double? GpuTemp, double? GpuUtil, ulong? ClockReasons);

public sealed record RecordedSample(DateTimeOffset At, double Seconds, string? GpuId,
    double? GpuUtil, double? GpuTemp, double? GpuClock, double? GpuPower, ulong? ClockReasons,
    double? Cpu, double? BusiestCore, DateTimeOffset? GpuObservedAt);

public sealed record SessionRecording(int SchemaVersion, DateTimeOffset Start, DateTimeOffset End,
    bool GameMode, string? GpuId, bool Truncated, IReadOnlyList<RecordedSample> Samples);

internal static class SessionSummary
{
    internal const ulong LimitedMask = 0xEC;
    internal const ulong PowerMask = 0x04;
    internal const ulong ThermalMask = 0x60;
    internal const ulong HardwareMask = 0x88;

    internal static double? Finite(double value) => double.IsFinite(value) ? value : null;

    internal static MatchReport Build(DateTime start, DateTime end, bool gameMode, IReadOnlyList<MatchSample> samples)
    {
        // New recordings use elapsed monotonic time. Keep the historical behavior for old synthetic samples.
        var timed = samples.Any(s => s.Seconds is not null);
        var play = timed ? samples.Where(s => s.Seconds is >= 20).ToList()
            : samples.Count > 15 ? samples.Skip(10).ToList() : samples.ToList();
        double? Avg(Func<MatchSample, double> f, IEnumerable<MatchSample>? source = null)
        {
            var values = (source ?? play).Select(f).Where(double.IsFinite).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }
        double? Max(Func<MatchSample, double> f)
        {
            var values = play.Select(f).Where(double.IsFinite).ToList();
            return values.Count == 0 ? null : Math.Round(values.Max(), 1);
        }
        var cpu = play.Where(s => s.CpuAvailable && double.IsFinite(s.Cpu) && double.IsFinite(s.BusiestCore)).ToList();
        var gpu = play.Where(s => double.IsFinite(s.GpuUtil)).ToList();
        var reasons = play.Where(s => s.ClockReasons is not null).ToList();
        double? ReasonPct(ulong mask) => reasons.Count == 0 ? null
            : Math.Round(100d * reasons.Count(s => (s.ClockReasons!.Value & mask) != 0) / reasons.Count, 1);
        var limited = timed || reasons.Count > 0 ? ReasonPct(LimitedMask)
            : gpu.Count == 0 ? null : Math.Round(100d * gpu.Count(s => s.GpuLimited) / gpu.Count, 0);
        var util = Avg(s => s.GpuUtil);
        var busiest = Avg(s => s.BusiestCore, cpu) ?? 0;
        var bottleneck = util switch
        {
            null => "unknown",
            >= 90 => "gpu",
            _ when cpu.Count > 0 && busiest >= 85 => "cpu",
            _ => "none",
        };
        return new MatchReport(start, Math.Round(Math.Max(0, (end - start).TotalMinutes), 1), gameMode, play.Count,
            util, Avg(s => s.GpuTemp), Max(s => s.GpuTemp), Avg(s => s.GpuClock), Avg(s => s.GpuPower), limited,
            Avg(s => s.Cpu, cpu) ?? 0, busiest, bottleneck)
        {
            SchemaVersion = 2,
            GpuPowerLimitedPct = ReasonPct(PowerMask),
            GpuThermalLimitedPct = ReasonPct(ThermalMask),
            GpuHardwareSlowdownPct = ReasonPct(HardwareMask),
            ClockReasonSamples = reasons.Count,
            GpuSamples = gpu.Count,
            CpuSamples = cpu.Count,
            GpuId = samples.Select(s => s.GpuId).FirstOrDefault(id => id is not null),
            Timeline = timed ? MakeTimeline(play) : null,
        };
    }

    private static IReadOnlyList<SessionPoint> MakeTimeline(IReadOnlyList<MatchSample> samples)
    {
        var valid = samples.Where(s => s.Seconds is { } t && double.IsFinite(t)).OrderBy(s => s.Seconds).ToList();
        var size = Math.Max(1, (int)Math.Ceiling(valid.Count / 120d));
        return valid.Chunk(size).Select(bucket =>
        {
            var temperatures = bucket.Select(s => s.GpuTemp).Where(double.IsFinite).ToList();
            var utilization = bucket.Select(s => s.GpuUtil).Where(double.IsFinite).ToList();
            var reasons = bucket.Where(s => s.ClockReasons is not null).Select(s => s.ClockReasons!.Value).ToList();
            return new SessionPoint(Math.Round(bucket[0].Seconds!.Value, 2),
                temperatures.Count > 0 ? temperatures.Max() : null,
                utilization.Count > 0 ? Math.Round(utilization.Average(), 1) : null,
                reasons.Count > 0 ? reasons.Aggregate(0UL, (value, mask) => value | mask) : null);
        }).ToList();
    }

    internal static SessionRecording Recording(DateTimeOffset start, DateTimeOffset end, bool gameMode,
        bool truncated, IReadOnlyList<MatchSample> samples) => new(2, start, end, gameMode,
            samples.Select(s => s.GpuId).FirstOrDefault(id => id is not null), truncated,
            samples.Select(s => new RecordedSample(s.At ?? start.AddSeconds(s.Seconds ?? 0), s.Seconds ?? 0,
                s.GpuId, Finite(s.GpuUtil), Finite(s.GpuTemp), Finite(s.GpuClock), Finite(s.GpuPower), s.ClockReasons,
                s.CpuAvailable ? Finite(s.Cpu) : null, s.CpuAvailable ? Finite(s.BusiestCore) : null,
                s.GpuObservedAt)).ToList());
}

internal sealed record GpuReading(double Util, double Temp, double Clock, double Power, ulong? Reasons, double ObservedSeconds);

/// <summary>One GPU for the entire session; a missing/stale value never means zero utilization or no limiting.</summary>
internal sealed class GpuSampleStream
{
    private GpuReading? latest;
    private bool ended;
    internal string? SelectedId { get; private set; }

    internal bool Accept(string? line, double seconds)
    {
        if (ended || string.IsNullOrWhiteSpace(line)) return false;
        var values = line.Split(',').Select(s => s.Trim()).ToArray();
        if (values.Length != 6 || !(values[0].StartsWith("GPU-", StringComparison.Ordinal)
            || values[0].StartsWith("MIG-", StringComparison.Ordinal))) return false;
        SelectedId ??= values[0];
        if (!string.Equals(SelectedId, values[0], StringComparison.Ordinal)) return false;
        double Number(int i) => double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value >= 0 ? value : double.NaN;
        var text = values[5];
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var known = ulong.TryParse(hex ? text[2..] : text, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var reasons);
        latest = new GpuReading(Number(1) is var util && util <= 100 ? util : double.NaN,
            Number(2), Number(3), Number(4), known ? reasons : null, seconds);
        return true;
    }

    internal GpuReading? Current(double seconds) => !ended && latest is not null
        && seconds >= latest.ObservedSeconds && seconds - latest.ObservedSeconds <= 5 ? latest : null;

    internal void Invalidate() { ended = true; latest = null; }
}

internal static class SessionArchive
{
    internal static string Save(SessionRecording recording, string directory)
    {
        Directory.CreateDirectory(directory);
        var name = $"session-{recording.Start.UtcDateTime:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(directory, name);
        WriteAtomic(path, recording);
        // Only our own archive names are eligible for retention, never unrelated user files.
        foreach (var old in new DirectoryInfo(directory).EnumerateFiles("session-*.json")
                     .Where(f => f.Name != name && IsArchiveName(f.Name))
                     .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(19))
        {
            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return name;
    }

    private static bool IsArchiveName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, @"^session-\d{8}T\d{9}-[a-f0-9]{32}\.json$");

    internal static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

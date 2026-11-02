using System.Text.Json;
using BoostOS.Game;

namespace BoostOS.Tests;

/// <summary>Schema 4: what heat really cost, a clock held down by something else, and sessions brought back from their archives.</summary>
public class SessionRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 20, 0, 0, TimeSpan.FromHours(2));

    private static MatchSample Sample(double seconds, double util, double clock, double temp = 80, ulong reasons = 0,
        double busiest = 50, double? cpuMhz = 3900) =>
        new(util, temp, clock, 60, (reasons & 0xEC) != 0, 25, busiest)
        { At = Start.AddSeconds(seconds), Seconds = seconds, ClockReasons = reasons, GpuId = "GPU-A", CpuMhz = cpuMhz };

    [Fact]
    public void Heat_at_the_target_with_power_to_spare_costs_nothing()
    {
        // The card sits at 87°C trimming its boost, but it is only 45% busy: the frame cap is reached anyway.
        var samples = Enumerable.Range(0, 40).Select(i => Sample(20 + i * 2, util: 45, clock: 1500, temp: 87, reasons: 0x20)).ToList();
        var report = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(2).LocalDateTime, true, samples, 2100);
        Assert.Equal(100d, report.GpuThermalLimitedPct);
        Assert.Equal(0d, report.GpuThermalBusyPct);
        Assert.Equal(0d, report.GpuBusyPct);
        Assert.Equal(0d, report.GpuLowClockPct);
    }

    [Fact]
    public void Heat_while_maxed_out_is_what_costs_frames()
    {
        var samples = new List<MatchSample>
        {
            Sample(20, util: 95, clock: 1400, temp: 87, reasons: 0x20),
            Sample(22, util: 96, clock: 1400, temp: 87, reasons: 0x20),
            Sample(24, util: 50, clock: 1500, temp: 87, reasons: 0x20),
            Sample(26, util: 95, clock: 1900, temp: 84, reasons: 0x00),
        };
        var report = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples, 2100);
        Assert.Equal(75d, report.GpuThermalLimitedPct);
        Assert.Equal(50d, report.GpuThermalBusyPct);
        Assert.Equal(75d, report.GpuBusyPct);
    }

    [Fact]
    public void A_card_working_hard_at_a_low_clock_is_flagged_only_when_its_top_clock_is_known()
    {
        var samples = Enumerable.Range(0, 30).Select(i => Sample(20 + i * 2, util: 80, clock: 780, temp: 86)).ToList();
        var known = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples, 2100);
        Assert.Equal(100d, known.GpuLowClockPct);
        Assert.Equal(2100d, known.GpuMaxClockMhz);
        var unknown = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples);
        Assert.Null(unknown.GpuLowClockPct);
    }

    [Fact]
    public void Time_to_85_counts_from_the_game_start_including_the_warmup()
    {
        var samples = new[] { 60.0, 75, 85, 87, 87 }.Select((temp, i) => Sample(i * 5, util: 40, clock: 1800, temp: temp)).ToList();
        var report = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples, 2100);
        Assert.Equal(10d, report.GpuHotAfterSec);
    }

    [Fact]
    public void Cpu_slow_moments_count_only_while_the_game_uses_the_cpu()
    {
        // A resting CPU may clock down to 2.2 GHz; while the game keeps a core busy it holds 3.9 GHz.
        var samples = Enumerable.Range(0, 40)
            .Select(i => i % 4 == 0 ? Sample(20 + i * 2, 40, 1500, busiest: 10, cpuMhz: 2200) : Sample(20 + i * 2, 40, 1500, busiest: 60, cpuMhz: 3900))
            .ToList();
        var report = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(2).LocalDateTime, true, samples, 2100);
        Assert.Equal(3900d, report.CpuMhzLow);
        Assert.True(report.CpuMhzAvg < 3900);
    }

    [Fact]
    public void The_driver_stream_carries_the_power_limit_and_top_clock()
    {
        var stream = new GpuSampleStream();
        Assert.True(stream.Accept("GPU-A, 80, 86, 780, 57.5, 0x1, 2900, 6144, 80.00, 2100", 20));
        var reading = stream.Current(21)!;
        Assert.Equal(80d, reading.PowerLimit);
        Assert.Equal(2100d, reading.MaxClock);
        Assert.Equal(2900d, reading.VramMb);
        Assert.True(stream.Accept("GPU-A, 80, 86, 780, 57.5, 0x1, 2900, 6144, [N/A], 2100", 22));
        Assert.Null(stream.Current(22)!.PowerLimit);
    }

    [Fact]
    public void The_archive_carries_its_summary()
    {
        InDirectory(directory =>
        {
            double seconds = 0;
            using var recorder = new MatchRecorder(true, () => [0L, 1L, 1L], () => seconds, Start);
            seconds = 20;
            recorder.AcceptGpuLine("GPU-A, 80, 86, 780, 57.5, 0x1, 2900, 6144, 80.00, 2100");
            recorder.SampleNow();
            seconds = 90;
            var report = recorder.FinishTo(directory);
            var archive = JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(Path.Combine(directory, report.TelemetryFile!)))!;
            Assert.Equal(4, archive.SchemaVersion);
            Assert.NotNull(archive.Report);
            Assert.Equal(report.Minutes, archive.Report!.Minutes);
            Assert.Equal(2100d, archive.Report.GpuMaxClockMhz);
            Assert.Equal(80d, archive.Samples[0].GpuPowerLimit);
        });
    }

    [Fact]
    public void Recover_brings_back_missing_sessions_once_and_skips_short_or_known_ones()
    {
        InDirectory(directory =>
        {
            var archives = Path.Combine(directory, "sessions");
            var list = Path.Combine(directory, "matches.json");
            var held = Enumerable.Range(0, 30).Select(i => Sample(20 + i * 2, util: 80, clock: 780, temp: 86)).ToList();

            // Already in the list.
            var knownRecording = SessionSummary.Recording(Start, Start.AddMinutes(5), true, false, held);
            var knownName = SessionArchive.Save(knownRecording, archives);
            MatchStore.Add(SessionSummary.Rebuild(knownRecording, 2100) with { TelemetryFile = knownName }, list);
            // An archive from before schema 4: no summary inside, so it is rebuilt from the samples.
            var oldStart = Start.AddHours(1);
            var old = SessionSummary.Recording(oldStart, oldStart.AddMinutes(40), true, false,
                held.Select(s => s with { At = oldStart.AddSeconds(s.Seconds!.Value) }).ToList());
            var oldName = SessionArchive.Save(old, archives);
            // The same session recorded by a second copy a few seconds later: one entry, not two.
            SessionArchive.Save(old with { Start = oldStart.AddSeconds(4) }, archives);
            // A new archive with its summary inside.
            var newStart = Start.AddHours(2);
            var inside = SessionSummary.Build(newStart.LocalDateTime, newStart.AddMinutes(12).LocalDateTime, false, held, 2100) with { GameName = "Rocket League" };
            SessionArchive.Save(SessionSummary.Recording(newStart, newStart.AddMinutes(12), false, false, held) with { Report = inside }, archives);
            // A launch that never reached gameplay.
            var shortStart = Start.AddHours(3);
            SessionArchive.Save(SessionSummary.Recording(shortStart, shortStart.AddSeconds(30), true, false, held.Take(2).ToList()), archives);

            var asked = 0;
            Assert.Equal(2, MatchStore.Recover(list, archives, () => { asked++; return 2100; }));
            Assert.Equal(1, asked); // the top clock is asked once, only for the rebuilt archive

            var sessions = MatchStore.Load(list);
            Assert.Equal(3, sessions.Count);
            Assert.True(sessions.Select(m => m.Start).SequenceEqual(sessions.Select(m => m.Start).OrderByDescending(x => x)));
            Assert.Equal("Rocket League", sessions[0].GameName);
            Assert.True(sessions[0].Recovered);
            var rebuilt = Assert.Single(sessions, m => m.Start == oldStart.LocalDateTime || m.Start == oldStart.AddSeconds(4).LocalDateTime);
            Assert.InRange(rebuilt.Minutes, 39.9, 40); // either copy of the duplicated session
            Assert.Equal(100d, rebuilt.GpuLowClockPct);
            Assert.StartsWith("session-", rebuilt.TelemetryFile);
            Assert.Equal(0, MatchStore.Recover(list, archives, () => 2100));
            Assert.Contains(sessions, m => m.TelemetryFile == knownName);
            Assert.NotEqual(oldName, knownName);
        });
    }

    [Fact]
    public void Adding_the_same_session_twice_keeps_one_entry()
    {
        InDirectory(directory =>
        {
            var list = Path.Combine(directory, "matches.json");
            var samples = Enumerable.Range(0, 30).Select(i => Sample(20 + i * 2, 40, 1500)).ToList();
            var report = SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(3).LocalDateTime, true, samples, 2100) with { TelemetryFile = "session-a.json" };
            MatchStore.Add(report, list);
            MatchStore.Add(report, list);
            Assert.Single(MatchStore.Load(list));
        });
    }

    [Fact]
    public void A_damaged_list_is_kept_aside_and_rebuilt()
    {
        InDirectory(directory =>
        {
            var list = Path.Combine(directory, "matches.json");
            File.WriteAllText(list, "{ not json");
            var samples = Enumerable.Range(0, 30).Select(i => Sample(20 + i * 2, 40, 1500)).ToList();
            MatchStore.Add(SessionSummary.Build(Start.LocalDateTime, Start.AddMinutes(3).LocalDateTime, true, samples), list);
            Assert.Single(MatchStore.Load(list));
            Assert.Equal("{ not json", File.ReadAllText(Path.Combine(directory, "matches.damaged.json")));
        });
    }

    private static void InDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boostos-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

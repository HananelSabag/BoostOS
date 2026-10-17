using System.Text.Json;
using BoostOS.Game;

namespace BoostOS.Tests;

public class SessionTelemetryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(3));

    private static MatchSample Sample(double seconds, ulong? reasons, double util = 40, double temp = 80) =>
        new(util, temp, 1500, 65, reasons is { } r && (r & 0xEC) != 0, 25, 50)
        { At = Start.AddSeconds(seconds), Seconds = seconds, ClockReasons = reasons, GpuId = "GPU-A" };

    [Fact]
    public void Raw_reason_masks_preserve_overlapping_power_and_thermal_signals()
    {
        var stream = new GpuSampleStream();
        Assert.True(stream.Accept("GPU-A, 46, 88, 1523, 66.5, 0x0000000000000024", 20));
        var value = Assert.IsType<GpuReading>(stream.Current(22));
        Assert.Equal(0x24UL, value.Reasons);
        Assert.Equal(66.5, value.Power);
    }

    [Fact]
    public void Unavailable_reasons_are_different_from_known_no_limiting()
    {
        var stream = new GpuSampleStream();
        stream.Accept("GPU-A, 46, 88, 1523, 66.5, N/A", 20);
        Assert.Null(stream.Current(20)!.Reasons);
        stream.Accept("GPU-A, 46, 88, 1523, 66.5, 0x0", 22);
        Assert.Equal(0UL, stream.Current(22)!.Reasons);
    }

    [Fact]
    public void A_second_GPU_never_enters_the_selected_series()
    {
        var stream = new GpuSampleStream();
        stream.Accept("GPU-A, 40, 70, 1500, 65, 0x4", 20);
        Assert.False(stream.Accept("GPU-B, 99, 99, 2500, 200, 0x60", 21));
        Assert.Equal("GPU-A", stream.SelectedId);
        Assert.Equal(40, stream.Current(22)!.Util);
        Assert.Equal(70, stream.Current(22)!.Temp);
    }

    [Fact]
    public void Stale_or_ended_GPU_streams_become_unavailable()
    {
        var stream = new GpuSampleStream();
        stream.Accept("GPU-A, 40, 70, 1500, 65, 0x4", 20);
        Assert.Null(stream.Current(26));
        stream.Accept("GPU-A, 50, 75, 1550, 70, 0x0", 27);
        Assert.NotNull(stream.Current(28));
        stream.Invalidate();
        Assert.False(stream.Accept("GPU-A, 99, 99, 2500, 200, 0x60", 29));
        Assert.Null(stream.Current(29));
    }

    [Fact]
    public void Warmup_uses_elapsed_time_rather_than_the_number_of_driver_rows()
    {
        var samples = new[] { Sample(0, 0, 1), Sample(19.9, 0, 2), Sample(20, 0, 70), Sample(400, 0, 90) };
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(10).LocalDateTime, true, samples);
        Assert.Equal(2, report.Samples);
        Assert.Equal(80, report.GpuUtil);
        Assert.Equal(20, report.Timeline![0].Seconds);
    }

    [Fact]
    public void Reason_percentages_use_known_masks_and_do_not_treat_unknown_as_zero()
    {
        var samples = new[] { Sample(20, 4), Sample(22, 0x20), Sample(24, null), Sample(26, 0) };
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples);
        Assert.Equal(3, report.ClockReasonSamples);
        Assert.Equal(4, report.GpuSamples);
        Assert.Equal(66.7, report.GpuLimitedPct);
        Assert.Equal(33.3, report.GpuPowerLimitedPct);
        Assert.Equal(33.3, report.GpuThermalLimitedPct);
        Assert.Equal(0d, report.GpuHardwareSlowdownPct);
    }

    [Fact]
    public void New_unknown_reason_data_cannot_be_reported_as_zero_percent()
    {
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, [Sample(20, null)]);
        Assert.Equal(0, report.ClockReasonSamples);
        Assert.Null(report.GpuLimitedPct);
        Assert.Null(report.GpuThermalLimitedPct);
    }

    [Fact]
    public void Missing_CPU_samples_are_not_treated_as_measured_idle_time()
    {
        var samples = new[] { Sample(20, 0) with { Cpu = 0, BusiestCore = 0, CpuAvailable = false }, Sample(22, 0) with { Cpu = 80, BusiestCore = 90 } };
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples);
        Assert.Equal(1, report.CpuSamples);
        Assert.Equal(80, report.CpuAvg);
        Assert.Equal(90, report.BusiestCoreAvg);
    }

    [Fact]
    public void Non_finite_metrics_are_filtered_from_summary_and_saved_as_JSON_null()
    {
        var samples = new[] { Sample(20, null, double.PositiveInfinity, double.NaN) with { Cpu = double.NaN, BusiestCore = double.NegativeInfinity } };
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true, samples);
        var recording = SessionSummary.Recording(Start, Start.AddMinutes(1), true, false, samples);
        Assert.Null(report.GpuUtil);
        Assert.Null(report.GpuTempMax);
        Assert.Equal(0, report.CpuSamples);
        Assert.DoesNotContain("Infinity", JsonSerializer.Serialize(report));
        var json = JsonSerializer.Serialize(recording);
        Assert.DoesNotContain("NaN", json);
        Assert.Contains("\"GpuUtil\":null", json);
    }

    [Fact]
    public void Old_summary_keeps_its_aggregate_and_marks_raw_reasons_unavailable()
    {
        const string json = """
            {"Start":"2026-01-01T12:00:00+03:00","Minutes":26.4,"GameMode":true,"Samples":779,
             "GpuUtil":46.1,"GpuTempAvg":86,"GpuTempMax":88,"GpuClock":1523.4,"GpuPower":66.5,
             "GpuLimitedPct":92,"CpuAvg":27.6,"BusiestCoreAvg":54,"Bottleneck":"none"}
            """;
        var old = JsonSerializer.Deserialize<MatchReport>(json)!;
        Assert.Equal(92d, old.GpuLimitedPct);
        Assert.Equal(0, old.SchemaVersion);
        Assert.Null(old.GpuThermalLimitedPct);
        Assert.Null(old.TelemetryFile);
        Assert.Null(old.Timeline);
    }

    [Fact]
    public void Timeline_is_bounded_and_keeps_brief_temperature_peaks()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => Sample(20 + 2 * i, i == 500 ? 0x20UL : 0, 40, i == 500 ? 88 : 70)).ToList();
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddHours(1).LocalDateTime, true, samples);
        Assert.InRange(report.Timeline!.Count, 1, 120);
        Assert.Contains(report.Timeline, p => p.GpuTemp == 88 && (p.ClockReasons & 0x20) != 0);
    }

    [Fact]
    public void CPU_collection_and_raw_archive_work_without_valid_GPU_output()
    {
        InDirectory(directory =>
        {
            double seconds = 0;
            var n = 0;
            using var recorder = new MatchRecorder(true, () => { n++; return [50L * n, 100L * n, 100L * n]; }, () => seconds, Start);
            recorder.SampleNow();
            recorder.AcceptGpuLine("driver output unavailable");
            seconds = 22;
            recorder.SampleNow();
            var report = recorder.FinishTo(directory);
            Assert.Equal(75, report.CpuAvg);
            Assert.Equal(1, report.CpuSamples);
            Assert.Equal(0, report.GpuSamples);
            Assert.Null(report.GpuLimitedPct);
            Assert.Equal("unknown", report.Bottleneck);
            var raw = JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(Path.Combine(directory, report.TelemetryFile!)))!;
            Assert.Equal(2, raw.Samples.Count);
            Assert.Null(raw.Samples[0].Cpu);
            Assert.Equal(75d, raw.Samples[1].Cpu);
            Assert.Null(raw.Samples[1].GpuUtil);
            Assert.Equal(Start.AddSeconds(22), raw.Samples[1].At);
        });
    }

    [Fact]
    public void GPU_updates_do_not_change_CPU_sample_count_or_mix_devices()
    {
        InDirectory(directory =>
        {
            double seconds = 0;
            var n = 0;
            using var recorder = new MatchRecorder(false, () => { n++; return [50L * n, 100L * n, 100L * n]; }, () => seconds, Start);
            recorder.SampleNow();
            seconds = 20;
            recorder.AcceptGpuLine("GPU-A, 40, 70, 1500, 65, 0x24");
            recorder.AcceptGpuLine("GPU-B, 99, 99, 2500, 200, 0x0");
            recorder.SampleNow();
            seconds = 28;
            recorder.SampleNow(); // GPU source has stalled; CPU still samples normally.
            var report = recorder.FinishTo(directory);
            Assert.Equal(2, report.CpuSamples);
            Assert.Equal(1, report.GpuSamples);
            Assert.Equal(40, report.GpuUtil);
            Assert.Equal("GPU-A", report.GpuId);
            Assert.Equal(100d, report.GpuThermalLimitedPct);
            var raw = JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(Path.Combine(directory, report.TelemetryFile!)))!;
            Assert.Equal(Start.AddSeconds(20), raw.Samples[1].GpuObservedAt);
            Assert.Null(raw.Samples[2].ClockReasons);
        });
    }

    [Fact]
    public async Task Dispose_drains_an_in_flight_sample_and_blocks_late_updates()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var recorder = new MatchRecorder(true, () => { entered.Set(); release.Wait(); return [0L, 1L, 1L]; }, () => 25, Start);
        var sampling = Task.Run(recorder.SampleNow);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        var disposal = Task.Run(recorder.Dispose);
        try { Assert.NotSame(disposal, await Task.WhenAny(disposal, Task.Delay(100))); }
        finally { release.Set(); }
        await Task.WhenAll(sampling, disposal).WaitAsync(TimeSpan.FromSeconds(3));
        recorder.AcceptGpuLine("GPU-A, 99, 99, 2500, 200, 0x60");
        recorder.SampleNow();
        InDirectory(directory =>
        {
            var report = recorder.FinishTo(directory);
            Assert.Equal(0, report.Samples);
            Assert.Equal(report.TelemetryFile, recorder.FinishTo(directory).TelemetryFile);
            Assert.Single(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public void An_archive_write_failure_keeps_the_summary_and_is_explicit()
    {
        InDirectory(directory =>
        {
            var blocked = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocked, "keep");
            using var recorder = new MatchRecorder(true, () => null, () => 25, Start);
            recorder.SampleNow();
            var report = recorder.FinishTo(blocked);
            Assert.True(report.TelemetrySaveFailed);
            Assert.Null(report.TelemetryFile);
            Assert.Equal("keep", File.ReadAllText(blocked));
        });
    }

    [Fact]
    public void Archive_retention_keeps_current_and_twenty_owned_files_only()
    {
        InDirectory(directory =>
        {
            var unrelated = Path.Combine(directory, "session-user-notes.json");
            File.WriteAllText(unrelated, "keep");
            var recording = SessionSummary.Recording(Start, Start.AddMinutes(1), true, false, [Sample(20, 0x24)]);
            string? last = null;
            for (var i = 0; i < 23; i++) last = SessionArchive.Save(recording, directory);
            Assert.Equal(21, Directory.GetFiles(directory).Length);
            Assert.True(File.Exists(Path.Combine(directory, last!)));
            Assert.Equal("keep", File.ReadAllText(unrelated));
            Assert.Equal(recording.Samples[0], JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(Path.Combine(directory, last!)))!.Samples[0]);
        });
    }

    [Fact]
    public void Report_bridge_serialization_retains_reason_percentages_and_timestamped_points()
    {
        var report = MatchRecorder.Summarize(Start.LocalDateTime, Start.AddMinutes(1).LocalDateTime, true,
            [Sample(20, 0x24), Sample(22, 0)]) with { TelemetryFile = "session-example.json" };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, BoostOS.Program.Json));
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(50d, json.RootElement.GetProperty("gpuThermalLimitedPct").GetDouble());
        Assert.Equal(50d, json.RootElement.GetProperty("gpuPowerLimitedPct").GetDouble());
        Assert.Equal(2, json.RootElement.GetProperty("clockReasonSamples").GetInt32());
        Assert.Equal(20d, json.RootElement.GetProperty("timeline")[0].GetProperty("seconds").GetDouble());
        Assert.Equal("session-example.json", json.RootElement.GetProperty("telemetryFile").GetString());
    }

    private static void InDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boostos-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}

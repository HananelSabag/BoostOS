using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Game;

namespace BoostOS.Tests;

public class ReportTests
{
    private static List<MatchSample> Match(int n, double gpuUtil, double busiestCore, double temp = 70) =>
        Enumerable.Range(0, n).Select(_ => new MatchSample(gpuUtil, temp, 1800, 75, false, 30, busiestCore)).ToList();

    [Fact]
    public void A_maxed_GPU_is_the_bottleneck()
    {
        var r = MatchRecorder.Summarize(DateTime.Now.AddMinutes(-10), DateTime.Now, true, Match(300, 98, 60));
        Assert.Equal("gpu", r.Bottleneck);
        Assert.Equal(10, r.Minutes, 0);
    }

    [Fact]
    public void One_saturated_core_with_a_relaxed_GPU_is_a_CPU_bottleneck()
    {
        var r = MatchRecorder.Summarize(DateTime.Now.AddMinutes(-5), DateTime.Now, false, Match(150, 55, 95));
        Assert.Equal("cpu", r.Bottleneck);
    }

    [Fact]
    public void Loading_screens_are_left_out_of_the_averages()
    {
        // The first 10 samples (~20 seconds) are the menu and loading: very different numbers that must not count.
        var samples = Match(10, 5, 20, temp: 40).Concat(Match(100, 80, 70, temp: 75)).ToList();
        var r = MatchRecorder.Summarize(DateTime.Now.AddMinutes(-4), DateTime.Now, true, samples);
        Assert.Equal(80, r.GpuUtil);
        Assert.Equal(75, r.GpuTempAvg);
    }

    [Fact]
    public void Score_takes_more_for_problems_than_for_improvements()
    {
        Section S(params CheckStatus[] statuses) => new("s", "s", "chip", statuses.Select(x => new Check("t", "v", x)).ToList());
        Assert.Equal(100, Scanner.Score([S(CheckStatus.Ok, CheckStatus.Info)]));
        Assert.Equal(95, Scanner.Score([S(CheckStatus.Warn)]));
        Assert.Equal(83, Scanner.Score([S(CheckStatus.Warn, CheckStatus.Bad)]));
        Assert.Equal(0, Scanner.Score([S(Enumerable.Repeat(CheckStatus.Bad, 20).ToArray())]));
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_a_broken_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"boostos-settings-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { Language = "en", Sounds = false }.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal("en", loaded.Language);
            Assert.False(loaded.Sounds);
            Assert.True(loaded.TrackMatches);

            File.WriteAllText(path, "{ not json");
            Assert.Equal("he", AppSettings.Load(path).Language);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

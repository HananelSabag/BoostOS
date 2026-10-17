using BoostOS.Game;

namespace BoostOS.Tests;

public class ReportRegressionTests
{
    [Fact]
    public void Limited_percentage_uses_the_same_valid_samples_in_both_counts()
    {
        MatchSample Sample(double util, bool limited) => new(util, 70, 1800, 75, limited, 30, 60);
        var samples = new[] { Sample(80, true), Sample(70, false), Sample(double.NaN, true), Sample(double.PositiveInfinity, true) };

        var report = MatchRecorder.Summarize(DateTime.UnixEpoch, DateTime.UnixEpoch.AddMinutes(5), true, samples);

        Assert.Equal(50d, report.GpuLimitedPct);
    }

    [Fact]
    public void Missing_gpu_utilization_does_not_create_a_limiting_percentage()
    {
        var samples = new[] { new MatchSample(double.NaN, 70, 1800, 75, true, 30, 60) };

        var report = MatchRecorder.Summarize(DateTime.UnixEpoch, DateTime.UnixEpoch.AddMinutes(5), true, samples);

        Assert.Null(report.GpuLimitedPct);
    }
}

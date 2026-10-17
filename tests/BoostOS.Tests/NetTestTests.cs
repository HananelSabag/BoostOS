using System.Net;
using BoostOS.Core;
using BoostOS.Network;

namespace BoostOS.Tests;

public class NetTestTests
{
    private static readonly NetTest.Target Router = new("router", "Router", IPAddress.Loopback, 400);
    private static readonly NetTest.Target Internet = new("internet", "Internet", IPAddress.Loopback, 400);

    /// <summary>A steady link at ~3ms, sampled every 0.4s, with optional spikes and losses.</summary>
    private static List<(double T, double? Ms)> Samples(int seconds, Func<double, double?>? at = null)
    {
        var list = new List<(double, double?)>();
        for (var i = 0; i * 0.4 < seconds; i++)
        {
            var t = Math.Round(i * 0.4, 2);
            list.Add((t, at?.Invoke(t) ?? 3.0 + (i * 0.37 % 1)));
        }
        return list;
    }

    [Fact]
    public void Spikes_on_a_fixed_beat_are_found_and_timed()
    {
        // A 60ms spike every 20 seconds: the background-scan pattern.
        var stats = NetTest.Analyze(Router, Samples(120, t => t % 20 < 0.4 && t > 1 ? 60 : null));
        Assert.True(stats.SpikeEvents >= 4);
        Assert.NotNull(stats.PeriodSeconds);
        Assert.InRange(stats.PeriodSeconds!.Value, 19, 21);
    }

    [Fact]
    public void A_clean_link_has_no_spikes_or_loss()
    {
        var stats = NetTest.Analyze(Router, Samples(60));
        Assert.Equal(0, stats.SpikeEvents);
        Assert.Equal(0, stats.LossPct);
        Assert.Null(stats.PeriodSeconds);
        Assert.InRange(stats.Median!.Value, 3, 4);
    }

    [Fact]
    public void Lost_replies_count_as_loss_not_spikes()
    {
        var stats = NetTest.Analyze(Router, Samples(60, t => t is > 10 and < 12 ? double.NaN : null)
            .Select(s => (s.T, double.IsNaN(s.Ms ?? 0) ? (double?)null : s.Ms)).ToList());
        Assert.True(stats.Lost > 0);
        Assert.Equal(0, stats.SpikeEvents);
    }

    [Fact]
    public void Even_spaced_late_losses_look_like_a_router_rate_limit()
    {
        // One lost ping a second, starting after a 17 second burst: what the home router here does.
        var losses = Enumerable.Range(0, 40).Select(i => 17 + i * 1.0).ToList();
        Assert.True(NetTest.LooksRateLimited(losses));
    }

    [Fact]
    public void Bursty_losses_are_real_loss()
    {
        var losses = new List<double> { 6.0, 6.4, 6.8, 30.2, 30.6, 55.0, 55.4, 55.8 };
        Assert.False(NetTest.LooksRateLimited(losses));
    }

    [Fact]
    public void Router_only_loss_is_reported_as_ping_limiting_not_a_problem()
    {
        var router = NetTest.Analyze(Router, Samples(60, t => (int)(t * 10) % 10 == 0 ? double.NaN : null)
            .Select(s => (s.T, double.IsNaN(s.Ms ?? 0) ? (double?)null : s.Ms)).ToList());
        var internet = NetTest.Analyze(Internet, Samples(60));
        var result = NetTest.Judge(60, [router, internet], pacedLoss: false);
        Assert.True(router.LossPct > 0.5);
        Assert.DoesNotContain(result.Findings, f => f.Status == CheckStatus.Bad);
    }
}

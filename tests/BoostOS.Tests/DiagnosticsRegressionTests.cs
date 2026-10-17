using System.Diagnostics;
using System.Net;
using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Network;

namespace BoostOS.Tests;

public class DiagnosticsRegressionTests
{
    private static TargetStats Stats(string id, int lost, int sent = 100)
    {
        var target = new NetTest.Target(id, id, IPAddress.Loopback, 400);
        var samples = Enumerable.Range(0, sent)
            .Select(i => (T: i * 0.4, Ms: i < lost ? (double?)null : 3.0)).ToList();
        return NetTest.Analyze(target, samples);
    }

    [Fact]
    public void Internet_loss_is_not_hidden_by_greater_gateway_loss()
    {
        // This used to classify the gateway as rate-limited and return a healthy overall result.
        var result = NetTest.Judge(60, [Stats("router", 60), Stats("internet", 10)], pacedLoss: false);
        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.DoesNotContain(result.Findings, f => f.Status == CheckStatus.Ok);
    }

    [Fact]
    public void Periodic_gateway_losses_do_not_suppress_internet_loss()
    {
        var result = NetTest.Judge(60, [Stats("router", 10), Stats("internet", 10)], pacedLoss: true);
        Assert.Equal(CheckStatus.Bad, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(100)]
    public void No_internet_replies_can_never_be_reported_as_healthy(int routerLost)
    {
        var result = NetTest.Judge(60, [Stats("router", routerLost), Stats("internet", 100)], pacedLoss: true);
        Assert.Equal(CheckStatus.Bad, result.Status);
    }

    [Fact]
    public void A_test_cancelled_before_sampling_is_inconclusive()
    {
        var result = NetTest.Judge(60, [Stats("router", 0, 0), Stats("internet", 0, 0)], pacedLoss: false);
        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.DoesNotContain(result.Findings, f => f.Status == CheckStatus.Ok);
    }

    [Fact]
    public void Gateway_only_loss_with_successful_internet_probes_remains_inconclusive()
    {
        var result = NetTest.Judge(60, [Stats("router", 100), Stats("internet", 0)], pacedLoss: false);
        Assert.DoesNotContain(result.Findings, f => f.Status == CheckStatus.Bad);
        Assert.DoesNotContain(result.Findings, f => f.Status == CheckStatus.Ok);
    }

    [Fact]
    public void Internet_spikes_without_a_gateway_target_are_not_reported_as_steady()
    {
        var samples = Enumerable.Range(0, 150)
            .Select(i => (T: i * 0.4, Ms: (double?)(i is 20 or 50 or 100 ? 100 : 30))).ToList();
        var internet = NetTest.Analyze(new NetTest.Target("internet", "internet", IPAddress.Loopback, 400), samples);
        Assert.Equal(3, internet.SpikeEvents);
        Assert.True(internet.Jitter < 10); // The spike warning must work independently of the jitter warning.

        var result = NetTest.Judge(60, [internet], pacedLoss: false);
        Assert.Equal(CheckStatus.Warn, result.Status);
    }

    [Fact]
    public void Internet_jitter_is_warned_even_when_gateway_probes_are_steady()
    {
        var samples = Enumerable.Range(0, 150)
            .Select(i => (T: i * 0.4, Ms: (double?)(i % 2 == 0 ? 30 : 45))).ToList();
        var internet = NetTest.Analyze(new NetTest.Target("internet", "internet", IPAddress.Loopback, 400), samples);
        Assert.Equal(0, internet.SpikeEvents);
        Assert.True(internet.Jitter > 10); // Frequent variation can remain below the spike threshold.

        var result = NetTest.Judge(60, [Stats("router", 0), internet], pacedLoss: false);
        Assert.Equal(CheckStatus.Warn, result.Status);
    }

    [Fact]
    public void Steady_gateway_and_internet_probes_remain_healthy()
    {
        var result = NetTest.Judge(60, [Stats("router", 0), Stats("internet", 0)], pacedLoss: false);
        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.DoesNotContain(result.Findings, f => f.Status is CheckStatus.Warn or CheckStatus.Bad);
    }

    [Fact]
    public void Unknown_storage_media_is_not_presented_as_a_healthy_SSD()
    {
        var check = StorageCheck.DescribeDisk("Test drive", 0, "SATA", 512);
        Assert.Equal(CheckStatus.Info, check.Status);
        Assert.DoesNotContain("SSD", check.Value);
    }

    private static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    [Fact]
    public void A_tool_with_a_full_error_pipe_does_not_deadlock()
    {
        var output = Shell.Run(PowerShell,
            "-NoProfile -NonInteractive -Command \"[Console]::Error.Write(('e' * 100000)); [Console]::Out.Write('ready')\"", 10000);
        Assert.Equal("ready", output);
    }

    [Fact]
    public void Output_from_a_failed_tool_is_not_treated_as_success()
    {
        var output = Shell.Run(PowerShell,
            "-NoProfile -NonInteractive -Command \"[Console]::Out.Write('partial'); exit 7\"", 10000);
        Assert.Null(output);
    }

    [Fact]
    public void A_timed_out_tool_returns_within_a_bounded_time()
    {
        var clock = Stopwatch.StartNew();
        var output = Shell.Run(PowerShell,
            "-NoProfile -NonInteractive -Command \"[Threading.Thread]::Sleep(10000)\"", 200);
        Assert.Null(output);
        Assert.InRange(clock.Elapsed.TotalSeconds, 0, 5);
    }
}

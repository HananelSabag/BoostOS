using System.Net;
using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Game;
using BoostOS.Network;

namespace BoostOS.Tests;

/// <summary>Advice comes from measured facts: the Wi-Fi link, Bluetooth, the GPU's power limit, fans, the power plan.</summary>
public class EvidenceTests
{
    private const string Netsh = """
        There is 1 interface on the system:

            Name                   : Wi-Fi
            Description            : Intel(R) Wi-Fi 6 AX201 160MHz
            State                  : connected
            SSID                   : home
            AP BSSID               : 62:d8:a4:a5:db:39
            Band                   : 5 GHz
            Channel                : 52
            Radio type             : 802.11ax
            Receive rate (Mbps)    : 2402
            Transmit rate (Mbps)   : 2402
            Signal                 : 93%
            Rssi                   : -41
        """;

    private static readonly WifiLink StrongDfsWide = WifiLink.Parse(Netsh)!;

    [Fact]
    public void The_link_is_read_from_netsh()
    {
        Assert.Equal("5 GHz", StrongDfsWide.Band);
        Assert.Equal(52, StrongDfsWide.Channel);
        Assert.Equal(-41, StrongDfsWide.Rssi);
        Assert.Equal(93, StrongDfsWide.SignalPct);
        Assert.True(StrongDfsWide.Dfs);
        Assert.True(StrongDfsWide.Wide);
        Assert.True(StrongDfsWide.Intel);
        Assert.True(StrongDfsWide.Strong);
        Assert.False(StrongDfsWide.Is24);
        Assert.Null(WifiLink.Parse(Netsh.Replace("connected", "disconnected")));
    }

    [Fact]
    public void A_strong_5GHz_link_gets_no_advice_without_evidence_of_trouble()
    {
        var facts = new NetFacts(true, StrongDfsWide, EthernetPort: true, BluetoothAudio: "Headphones (UGREEN-90748)", BluetoothInput: false);
        Assert.Empty(WifiAdvice.For(facts, trouble: false));
        // With spikes: Bluetooth sound first (it takes air time all the time), then the wide channel, then the cable.
        Assert.Equal([WifiAdvice.Tip.BluetoothAudio, WifiAdvice.Tip.Wide, WifiAdvice.Tip.Cable], WifiAdvice.For(facts, trouble: true));
        // The DFS channel only matters when the link actually dropped.
        Assert.Equal([WifiAdvice.Tip.BluetoothAudio, WifiAdvice.Tip.Wide, WifiAdvice.Tip.Dfs, WifiAdvice.Tip.Cable], WifiAdvice.For(facts, trouble: true, drops: true));
        Assert.Contains("UGREEN", WifiAdvice.Text(WifiAdvice.Tip.BluetoothAudio, facts));
    }

    [Fact]
    public void Never_says_move_to_5GHz_to_someone_already_there_but_always_to_someone_on_24()
    {
        var on24 = StrongDfsWide with { Band = "2.4 GHz", Channel = 6, RxMbps = 286, TxMbps = 286 };
        Assert.Equal([WifiAdvice.Tip.Band24], WifiAdvice.For(new NetFacts(true, on24, false, null, false), trouble: false));
        var clean = StrongDfsWide with { Channel = 36, RxMbps = 1201, TxMbps = 1201 };
        Assert.Equal([WifiAdvice.Tip.Clean], WifiAdvice.For(new NetFacts(true, clean, false, null, false), trouble: true));
        Assert.Empty(WifiAdvice.For(new NetFacts(false, null, true, null, false), trouble: true)); // a cable needs no Wi-Fi advice
    }

    [Theory]
    [InlineData(80, 80, 105, true)]   // held to the base: a quiet mode
    [InlineData(105, 80, 105, false)] // Dynamic Boost on: Performance
    [InlineData(95, 80, 105, false)]  // boost shared with a busy CPU
    [InlineData(80, 80, 80, null)]    // no Dynamic Boost on this card: nothing to tell
    public void The_power_limit_tells_the_laptop_mode(double enforced, double basePower, double max, bool? held) =>
        Assert.Equal(held, PowerCheck.GpuBelowFullPower(enforced, basePower, max));

    [Fact]
    public void The_driver_stream_carries_base_and_max_power()
    {
        var stream = new GpuSampleStream();
        Assert.True(stream.Accept("GPU-A, 80, 86, 780, 57.5, 0x1, 2900, 6144, 80.00, 2100, 80.00, 105.00", 20));
        var reading = stream.Current(21)!;
        Assert.Equal(80d, reading.PowerDefault);
        Assert.Equal(105d, reading.PowerMax);
        Assert.Equal(80d, reading.PowerLimit);
    }

    [Fact]
    public void The_laptop_buffer_gives_fans_and_temperatures()
    {
        var buffer = new byte[256];
        buffer[2] = 0x01; buffer[3] = 0x90; // period 400 → 5391 rpm
        buffer[16] = 250; buffer[18] = 74;  // CPU fan 98%, CPU 74°C
        buffer[19] = 209; buffer[21] = 62;  // GPU fan 82%, GPU 62°C
        var r = LaptopProbe.Parse(buffer)!;
        Assert.Equal(74, r.CpuTemp);
        Assert.Equal(62, r.GpuTemp);
        Assert.Equal(98, r.CpuFanDuty);
        Assert.Equal(82, r.GpuFanDuty);
        Assert.Equal(98, r.FanDuty);
        Assert.Equal(5391, r.CpuFanRpm);
        Assert.Null(LaptopProbe.Parse(new byte[256])); // an all-zero answer is no answer
    }

    [Fact]
    public void Game_Mode_never_quiets_sound_voice_controllers_or_the_game()
    {
        var game = new HashSet<string> { "rocketleague" };
        Assert.True(BackgroundThrottle.MayQuietWhenBusy("someupdater", game));
        foreach (var name in new[] { "rocketleague", "discord", "spotify", "obs64", "devicehub", "easyanticheat", "bakkesmod", "explorer", "audiodg", "boostos" })
            Assert.False(BackgroundThrottle.MayQuietWhenBusy(name, game), name);
    }

    // ---- Session findings ----

    private static MatchReport Session(double minutes = 30) =>
        new(new DateTime(2026, 3, 1, 20, 0, 0), minutes, true, 900, 45, 84, 87, 1550, 66, 70, 22, 58, "none")
        {
            SchemaVersion = 5, GpuThermalLimitedPct = 70, GpuThermalBusyPct = 0, GpuBusyPct = 0, GpuLowClockPct = 0,
            GpuHotAfterSec = 95, CpuMhzAvg = 3900, CpuMhzLow = 3800, CpuSamples = 900, PowerPlan = "boostos",
            GpuPowerLimitAvg = 100, GpuPowerDefault = 80, GpuPowerMax = 105, FanDutyAvg = 94, Background = [],
        };

    [Fact]
    public void Heat_at_the_limit_with_power_to_spare_is_fine()
    {
        var findings = SessionFindings.For(Session());
        Assert.DoesNotContain(findings, f => f.Status is "bad" or "warn");
        Assert.Contains(findings, f => f.Status == "ok" && f.Text.Contains("85"));
    }

    [Fact]
    public void A_held_clock_with_a_base_power_limit_points_at_the_laptop_mode()
    {
        var held = Session() with { GpuClock = 780, GpuUtil = 80, GpuLowClockPct = 95, GpuPowerLimitAvg = 80 };
        var line = Assert.Single(SessionFindings.For(held), f => f.Status == "warn" && f.Text.Contains("780"));
        Assert.Contains("Control Center", line.Tip);
        Assert.Contains("80", line.Tip);
        var summary = SessionFindings.Summarize([held, Session()]);
        Assert.Equal("warn", summary.Main.Status);
        Assert.Contains("Control Center", summary.Main.Tip);
    }

    [Fact]
    public void Wifi_spikes_get_advice_from_that_session_s_own_link()
    {
        var facts = new NetFacts(true, StrongDfsWide, false, "Headphones (UGREEN-90748)", false);
        var spiky = Session(60) with { Router = new PingStats(2, 5, 137, 0, 14, 3600), Net = facts };
        var line = Assert.Single(SessionFindings.For(spiky), f => f.Status == "warn" && f.Text.Contains("137"));
        Assert.Contains("UGREEN", line.Tip);
        var steady = Session(60) with { Router = new PingStats(2, 4, 9, 0, 0, 3600), Net = facts };
        Assert.DoesNotContain(SessionFindings.For(steady), f => f.Tip?.Contains("UGREEN") == true);
    }

    [Fact]
    public void A_busy_cpu_with_a_quiet_background_and_full_power_blames_the_cpu_not_the_apps()
    {
        var cpuBound = Session() with { BusiestCoreAvg = 92 };
        var line = Assert.Single(SessionFindings.For(cpuBound), f => f.Status == "warn" && f.Text.Contains("92"));
        Assert.DoesNotContain("Close", line.Tip!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("סגור", line.Tip!);
    }

    [Fact]
    public void The_scan_area_counts_what_happened_in_games()
    {
        var held = Session() with { GpuClock = 780, GpuUtil = 80, GpuLowClockPct = 95 };
        var section = SessionFindings.PlaySection([Session(), held, Session()], null);
        Assert.Equal("play", section.Id);
        Assert.Contains(section.Checks, c => c.Status == CheckStatus.Warn && c.Value.Contains("780"));
        Assert.Contains(section.Checks, c => c.Status == CheckStatus.Ok && c.Value.Contains("87"));
        Assert.Equal("play", SessionFindings.PlaySection([], null).Id);
    }

    [Fact]
    public void Network_test_spike_advice_follows_the_link()
    {
        // Spikes with no fixed beat (a regular beat is judged as background scans instead).
        int[] spikes = [5, 40, 52, 90, 131];
        var router = NetTest.Analyze(new NetTest.Target("router", "Router", IPAddress.Loopback, 400),
            Enumerable.Range(0, 150).Select(i => (Math.Round(i * 0.4, 2), (double?)(spikes.Contains(i) ? 80 : 3))).ToList());
        Assert.Null(router.PeriodSeconds);
        var internet = NetTest.Analyze(new NetTest.Target("internet", "Internet", IPAddress.Loopback, 400),
            Enumerable.Range(0, 150).Select(i => (Math.Round(i * 0.4, 2), (double?)8)).ToList());
        var facts = new NetFacts(true, StrongDfsWide, false, "Headphones (UGREEN-90748)", false);
        var result = NetTest.Judge(60, [router, internet], pacedLoss: false, facts);
        Assert.Contains(result.Findings, f => f.Text.Contains("UGREEN"));
    }
}

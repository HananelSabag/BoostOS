using BoostOS.Core;
using BoostOS.Game;
using BoostOS.Tuning;
using Xunit;

namespace BoostOS.Tests;

public class LibraryAndProbeTests
{
    [Fact]
    public void Steam_manifest_keeps_installed_games_and_skips_tools()
    {
        const string game = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";
        var m = GameLibrary.ParseAppManifest(game);
        Assert.NotNull(m);
        Assert.Equal("730", m.AppId);
        Assert.Equal("Counter-Strike Global Offensive", m.InstallDir);

        Assert.Null(GameLibrary.ParseAppManifest(game.Replace("Counter-Strike 2", "Steamworks Common Redistributables")));
        Assert.Null(GameLibrary.ParseAppManifest(game.Replace("\"StateFlags\"\t\t\"4\"", "\"StateFlags\"\t\t\"2\""))); // still downloading
    }

    [Fact]
    public void Names_lose_trademark_signs_and_paths_match_only_inside_the_folder()
    {
        Assert.Equal("Rocket League", GameLibrary.CleanName("Rocket League®"));
        Assert.True(GameLibrary.IsInside(@"C:\Games\RL\Binaries\Win64\RocketLeague.exe", @"c:\games\rl"));
        Assert.False(GameLibrary.IsInside(@"C:\Games\RL2\x.exe", @"C:\Games\RL"));
    }

    [Fact]
    public void Process_path_is_read_from_the_snapshot_without_opening_the_process()
    {
        var path = ProcessInfo.ImagePath(Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
        Assert.Contains(ProcessInfo.Snapshot(), e => e.Pid == Environment.ProcessId);
        Assert.Equal(@"C:\x\y.exe", ProcessInfo.ToDosPath(@"\Device\HarddiskVolume3\x\y.exe", new Dictionary<string, string> { [@"\Device\HarddiskVolume3"] = "C:" }));
    }

    [Fact]
    public void Ping_counts_a_run_of_slow_replies_as_one_spike_and_skips_the_warmup()
    {
        var results = new List<PingProbe.Result> { new(5, "router", 200) }; // warm-up, ignored
        for (var i = 0; i < 40; i++) results.Add(new(20 + i, "router", i is 10 or 11 or 12 ? 90 : i == 30 ? null : 3));
        var stats = SessionSummary.Ping(results, "router");
        Assert.NotNull(stats);
        Assert.Equal(3, stats.MedianMs);
        Assert.Equal(1, stats.Spikes);
        Assert.Equal(2.5, stats.LossPct);
        Assert.Equal(40, stats.Count);
    }

    [Fact]
    public void Power_plan_saves_power_only_on_battery()
    {
        // PCI Express link state, USB suspend and Wi-Fi saving: off on the charger, on when running on battery.
        foreach (var setting in new[] { "ee12f906-d277-404b-b6da-e5fa1a576df5", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", "12bbebe6-58d6-4636-95bb-3217ef867c1a" })
        {
            var v = PowerPlan.Values.Single(x => x.Setting == setting);
            Assert.Equal(0u, v.Ac);
            Assert.True(v.Dc > 0);
        }
        var boost = PowerPlan.Values.Single(x => x.Setting == "be337238-0d82-4146-a960-4f3749d470c7");
        Assert.Equal((2u, 0u), (boost.Ac, boost.Dc)); // aggressive boost plugged in, none on battery
    }

    [Fact]
    public void Frame_cap_follows_the_screen_and_stays_under_it_with_gsync()
    {
        Assert.Equal(152u, Nvidia.RecommendedFrameCap(155, gsync: true));
        Assert.Equal(155u, Nvidia.RecommendedFrameCap(155, gsync: false));
        Assert.Equal(30u, Nvidia.RecommendedFrameCap(24, gsync: true)); // never a silly low cap
    }

    [Fact]
    public void Video_settings_map_to_the_games_own_keys()
    {
        Assert.Equal([("Fullscreen", "True"), ("Borderless", "False")], RlVideo.Keys("display", "fullscreen").ToArray());
        Assert.Equal([("DynamicShadows", "False")], RlVideo.Keys("dynamicShadows", "off").ToArray());
        Assert.Throws<ArgumentException>(() => RlVideo.Keys("worldDetail", "9").ToArray());

        var ini = new Dictionary<string, string> { ["Fullscreen"] = "True", ["Borderless"] = "True", ["UseVsync"] = "True", ["DetailMode"] = "2", ["DynamicShadows"] = "True" };
        var hot = RlVideo.Describe(ini, hot: true);
        Assert.Equal("borderless", hot.Single(s => s.Id == "display").Value);
        Assert.Equal("off", hot.Single(s => s.Id == "vsync").Recommended);
        Assert.Equal("off", hot.Single(s => s.Id == "dynamicShadows").Recommended);
        Assert.Null(RlVideo.Describe(ini, hot: false).Single(s => s.Id == "dynamicShadows").Recommended);
    }
}

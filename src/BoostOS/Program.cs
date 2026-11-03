using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Network;
using BoostOS.Tuning;

namespace BoostOS;

internal static class Program
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Hebrew readable in dumps
        WriteIndented = true,
    };

    /// <summary>
    /// BoostOS.exe                                → console-style app (fullscreen unless turned off in Settings)
    /// BoostOS.exe --windowed                     → same, in a normal window
    /// BoostOS.exe --scan-json out.json [en]      → run the diagnostics headless and save them
    /// BoostOS.exe --net-json out.json 60         → run the network test headless and save it
    /// BoostOS.exe --elevated-fix ids out.json    → internal: started elevated by BoostOS itself for admin fixes
    /// BoostOS.exe --elevated-undo ids out.json   → internal: same, to undo them
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--scan-json", var scanPath, ..])
        {
            I18n.English = args.Length > 2 && args[2] == "en";
            var report = Scanner.RunAsync().GetAwaiter().GetResult();
            File.WriteAllText(scanPath, JsonSerializer.Serialize(report, Json));
            return 0;
        }

        if (args is ["--net-json", var netPath, ..])
        {
            var seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 30;
            var samples = new System.Collections.Concurrent.ConcurrentQueue<PingSample>();
            var result = NetTest.RunAsync(seconds, samples.Enqueue, CancellationToken.None).GetAwaiter().GetResult();
            File.WriteAllText(netPath, JsonSerializer.Serialize(new { result, samples = samples.OrderBy(x => x.T) }, Json));
            return 0;
        }

        // What the recorded sessions mean, as the Game data tab shows it: checks the findings against real recordings.
        if (args is ["--findings-json", var findingsPath, ..])
        {
            I18n.English = args.Length > 2 && args[2] == "en";
            NetFacts.Recent(); // read the link once, as the window has by the time it shows sessions
            File.WriteAllText(findingsPath, JsonSerializer.Serialize(Game.SessionFindings.Payload(Game.MatchStore.Load()), Json));
            return 0;
        }

        if (args is ["--games-json", var gamesPath, ..])
        {
            var games = Game.GameLibrary.List(refresh: true);
            var running = Game.GameLibrary.Running();
            File.WriteAllText(gamesPath, JsonSerializer.Serialize(new
            {
                games = games.Select(g => new { g.Id, g.Name, g.Store, g.Launch, g.InstallDir, g.Wide, g.Logo, g.Icon, g.Processes }),
                running = running?.Id,
                video = RlVideo.Read(hot: false),
            }, Json));
            return 0;
        }

        // Which apps Game Mode would quiet as busy right now, measured over a few seconds; changes nothing.
        if (args is ["--busy-test", ..])
        {
            var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? Math.Max(10, s) : 20;
            var minShare = args.Length > 2 && double.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 2;
            Game.BackgroundThrottle.Mark();
            Thread.Sleep(seconds * 1000);
            var picked = Game.BackgroundThrottle.ApplyBusy(["rocketleague"], [], minShare,
                e => new Game.BackgroundThrottle.Throttled(e.Pid, e.CreateTime, e.Name, 0));
            Console.WriteLine($"would quiet {picked.Count}: {string.Join(", ", picked.Select(t => t.Name))}");
            return 0;
        }

        // Puts the heavy background apps into efficiency mode for a few seconds and back: checks Game Mode's throttle.
        if (args is ["--efficiency-test", ..])
        {
            var list = Game.BackgroundThrottle.Apply([], []);
            Console.WriteLine($"throttled {list.Count}: {string.Join(", ", list.Select(t => t.Name).Distinct())}");
            var idle = list.Count(t => { try { using var p = System.Diagnostics.Process.GetProcessById(t.Pid); return p.PriorityClass == System.Diagnostics.ProcessPriorityClass.Idle; } catch { return false; } });
            Console.WriteLine($"now idle priority: {idle}");
            Thread.Sleep(5000);
            Game.BackgroundThrottle.Restore(list);
            var back = list.Count(t => { try { using var p = System.Diagnostics.Process.GetProcessById(t.Pid); return (int)p.PriorityClass == t.OldPriority; } catch { return true; } });
            Console.WriteLine($"restored {back} of {list.Count}");
            return 0;
        }

        // Records the system the way a game session does, without a game: checks every probe on a new machine.
        if (args is ["--record-json", var recordPath, ..])
        {
            var seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? Math.Max(25, s) : 40;
            using var recorder = new Game.MatchRecorder(gameMode: false, Game.GameLibrary.List().FirstOrDefault());
            Thread.Sleep(seconds * 1000);
            var report = recorder.FinishTo(Path.Combine(Path.GetTempPath(), "boostos-record-test"));
            File.WriteAllText(recordPath, JsonSerializer.Serialize(report, Json));
            return 0;
        }

        if (args is ["--nvidia-json", var nvPath, ..])
        {
            File.WriteAllText(nvPath, JsonSerializer.Serialize(new
            {
                dump = Nvidia.Dump(),
                names = new[] { Nvidia.PowerManagementId, Nvidia.PrerenderLimitId, Nvidia.FrameRateLimitId, Nvidia.GsyncModeId }
                    .ToDictionary(id => $"0x{id:X8}", id => Nvidia.SettingName(id)),
            }, Json));
            return 0;
        }

        // Applies or undoes fixes from a script (admin fixes still ask for approval once).
        if (args is [("--fix" or "--undo") and var fixMode, var fixIds, ..])
        {
            I18n.English = true;
            var outcome = Fixes.Run(fixIds.Split(',', StringSplitOptions.RemoveEmptyEntries), undo: fixMode == "--undo");
            Console.WriteLine(JsonSerializer.Serialize(outcome, Json));
            if (args.Length > 2) File.WriteAllText(args[2], JsonSerializer.Serialize(outcome, Json));
            return outcome.Failed.Count == 0 ? 0 : 1;
        }

        if (args is [("--elevated-fix" or "--elevated-undo") and var mode, var ids, var outPath, ..])
        {
            I18n.English = args.Length > 3 && args[3] == "en";
            var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Fixes.NeedsAdmin).ToList();
            var outcome = mode == "--elevated-fix" ? Fixes.ApplyHere(list) : Fixes.UndoHere(list);
            File.WriteAllText(outPath, JsonSerializer.Serialize(outcome));
            return outcome.Failed.Count == 0 ? 0 : 1;
        }

        ApplicationConfiguration.Initialize();
        if (args is ["--agent", ..]) return Agent.Run();
        Log.Write($"window opened, version {Application.ProductVersion.Split('+')[0]}, {Environment.ProcessPath}");
        Application.Run(new MainForm(windowed: args.Contains("--windowed")));
        return 0;
    }
}

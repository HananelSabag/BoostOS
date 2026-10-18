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
            File.WriteAllText(nvPath, JsonSerializer.Serialize(Nvidia.Dump(), Json));
            return 0;
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
        Application.Run(new MainForm(windowed: args.Contains("--windowed")));
        return 0;
    }
}

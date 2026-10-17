using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BoostOS.Core;
using Microsoft.Win32;

namespace BoostOS.Game;

/// <summary>
/// One installed game. Art holds file names in the local art cache (served to the page from art.boostos.example):
/// Wide is a landscape picture, Logo a transparent title logo to lay over it, Icon the executable's icon.
/// </summary>
public sealed record GameInfo(string Id, string Name, string Store, string Launch, string InstallDir,
    string? Wide, string? Logo, string? Icon)
{
    /// <summary>Executable names (lower case, no extension) that mean "this game is running".</summary>
    [JsonIgnore] public IReadOnlyList<string> Processes { get; init; } = [];
    public bool IsRocketLeague => Id is "epic:Sugar" or "steam:252950";
}

/// <summary>
/// The games installed through Epic Games and Steam, found from the launchers' own files on disk.
/// Pictures come from what the launchers already keep locally; nothing is downloaded.
/// </summary>
internal static partial class GameLibrary
{
    public const string ArtHost = "art.boostos.example";
    public static string ArtDir => Path.Combine(AppSettings.DataDir, "art");

    private static readonly object Gate = new();
    private static List<GameInfo>? cache;

    /// <summary>The cached list; refresh walks the launchers' files again (on start and when the Games row opens).</summary>
    public static List<GameInfo> List(bool refresh = false)
    {
        lock (Gate)
        {
            if (cache is null || refresh)
            {
                var found = new List<GameInfo>();
                try { found.AddRange(Epic()); } catch { /* a broken launcher install must not hide the others */ }
                try { found.AddRange(Steam()); } catch { }
                cache = found.GroupBy(g => g.Id).Select(g => g.First())
                    .OrderByDescending(g => g.IsRocketLeague).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            return cache;
        }
    }

    public static GameInfo? Find(string? id) => id is null ? null : List().FirstOrDefault(g => g.Id == id);

    // ---- Epic Games ----

    private static IEnumerable<GameInfo> Epic()
    {
        var manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifests)) yield break;
        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            GameInfo? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                game = FromEpicManifest(doc.RootElement);
            }
            catch { /* a half-written manifest should not stop the list */ }
            if (game is not null) yield return game;
        }
    }

    internal static GameInfo? FromEpicManifest(JsonElement m, bool withArt = true)
    {
        string S(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (m.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) return null;
        if (m.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
            && !cats.EnumerateArray().Any(c => c.GetString() == "games")) return null;
        var app = S("AppName");
        var folder = S("InstallLocation");
        if (app == "" || folder == "") return null;
        var name = CleanName(S("DisplayName"));
        var id = $"epic:{app}";
        var launchExe = S("LaunchExecutable");
        var game = new GameInfo(id, name, "Epic Games", $"com.epicgames.launcher://apps/{Uri.EscapeDataString(app)}?action=launch&silent=true",
            folder, null, null, null);
        if (!withArt || !Directory.Exists(folder)) return game;

        // Easy Anti-Cheat games ship the splash picture shown while the game starts: real key art, already on disk.
        var splash = SafeFiles(folder, "SplashScreen.png", 4).FirstOrDefault();
        var exe = launchExe == "" ? null : Path.Combine(folder, launchExe.Replace('/', '\\'));
        return game with
        {
            Wide = Cache(splash, $"{Slug(id)}-wide"),
            Icon = IconOf(exe, $"{Slug(id)}-icon"),
            Processes = GameProcesses(folder),
        };
    }

    // ---- Steam ----

    private static IEnumerable<GameInfo> Steam()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam) yield break;
        steam = steam.Replace('/', '\\');
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(SteamLibraries(File.ReadAllText(vdf)));
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                GameInfo? game = null;
                try
                {
                    if (ParseAppManifest(File.ReadAllText(acf)) is { } m)
                    {
                        var folder = Path.Combine(apps, "common", m.InstallDir);
                        var id = $"steam:{m.AppId}";
                        var art = SteamArt(steam, m.AppId);
                        game = new GameInfo(id, CleanName(m.Name), "Steam", $"steam://rungameid/{m.AppId}", folder,
                            Cache(art.Hero, $"{Slug(id)}-wide"), Cache(art.Logo, $"{Slug(id)}-logo"), null)
                        { Processes = GameProcesses(folder) };
                    }
                }
                catch { }
                if (game is not null) yield return game;
            }
        }
    }

    internal sealed record AppManifest(string AppId, string Name, string InstallDir);

    /// <summary>A Steam appmanifest_*.acf, or null for tools, runtimes and games that aren't fully installed.</summary>
    internal static AppManifest? ParseAppManifest(string text)
    {
        string V(string key) => Regex.Match(text, $"\"{key}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        var id = V("appid");
        var name = V("name");
        var dir = V("installdir");
        if (id == "" || name == "" || dir == "") return null;
        if (!int.TryParse(V("StateFlags"), out var state) || (state & 4) == 0) return null; // 4 = fully installed
        if (NotAGame().IsMatch(name) || id is "228980" or "1070560" or "1391110" or "1628350" or "250820") return null;
        return new AppManifest(id, name, dir);
    }

    internal static IEnumerable<string> SteamLibraries(string vdf) =>
        SteamPathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    /// <summary>Steam's local picture cache: the hero banner and the title logo (both layouts Steam has used).</summary>
    private static (string? Hero, string? Logo) SteamArt(string steam, string appId)
    {
        var cacheDir = Path.Combine(steam, @"appcache\librarycache");
        var perApp = Path.Combine(cacheDir, appId);
        string? Find(params string[] names)
        {
            foreach (var n in names)
            {
                if (Directory.Exists(perApp) && SafeFiles(perApp, n, 2).FirstOrDefault() is { } hit) return hit;
                var flat = Path.Combine(cacheDir, $"{appId}_{n}");
                if (File.Exists(flat)) return flat;
            }
            return null;
        }
        return (Find("library_hero.jpg", "header.jpg", "library_header.jpg"), Find("logo.png"));
    }

    // ---- Shared helpers ----

    /// <summary>Executable names inside the install folder, minus installers, crash reporters and anti-cheat helpers.</summary>
    internal static IReadOnlyList<string> GameProcesses(string folder) =>
        SafeFiles(folder, "*.exe", 4).Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Select(n => n.ToLowerInvariant()).Where(n => !HelperExe().IsMatch(n)).Distinct().Take(40).ToList();

    private static IEnumerable<string> SafeFiles(string folder, string pattern, int depth)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
            }).ToList();
        }
        catch { return []; }
    }

    internal static string CleanName(string name) => TrademarkRegex().Replace(name, "").Trim();

    private static string Slug(string id) => id.Replace(':', '-').ToLowerInvariant();

    /// <summary>Copies a picture into the art cache (only when it changed) and returns its file name there.</summary>
    private static string? Cache(string? source, string baseName)
    {
        if (source is null || !File.Exists(source)) return null;
        try
        {
            Directory.CreateDirectory(ArtDir);
            var name = baseName + Path.GetExtension(source).ToLowerInvariant();
            var target = Path.Combine(ArtDir, name);
            var info = new FileInfo(source);
            if (!File.Exists(target) || new FileInfo(target).Length != info.Length || File.GetLastWriteTimeUtc(target) < info.LastWriteTimeUtc)
                File.Copy(source, target, overwrite: true);
            return name;
        }
        catch { return null; }
    }

    private static string? IconOf(string? exe, string baseName)
    {
        if (exe is null || !File.Exists(exe)) return null;
        try
        {
            Directory.CreateDirectory(ArtDir);
            var name = baseName + ".png";
            var target = Path.Combine(ArtDir, name);
            if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(exe)) return name;
            using var icon = Icon.ExtractIcon(exe, 0, 256) ?? Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            bitmap.Save(target, ImageFormat.Png);
            return name;
        }
        catch { return null; }
    }

    // ---- Is one of them running? ----

    private static readonly Dictionary<(int Pid, long Created), string?> Confirmed = [];

    /// <summary>
    /// The library game that is running right now, if any. Names come from the system snapshot; a matching name is
    /// confirmed by its path, also read from the snapshot. The game's process is never opened.
    /// </summary>
    public static GameInfo? Running(IReadOnlyList<ProcessInfo.Entry>? snapshot = null)
    {
        var games = List();
        var byName = new Dictionary<string, List<GameInfo>>();
        foreach (var g in games)
            foreach (var p in g.Processes)
                (byName.TryGetValue(p, out var l) ? l : byName[p] = []).Add(g);
        if (byName.Count == 0) return null;

        snapshot ??= ProcessInfo.Snapshot();
        lock (Confirmed)
        {
            var alive = new HashSet<(int, long)>();
            GameInfo? found = null;
            foreach (var e in snapshot)
            {
                if (!byName.TryGetValue(e.Name, out var candidates)) continue;
                var key = (e.Pid, e.CreateTime);
                alive.Add(key);
                if (!Confirmed.TryGetValue(key, out var id))
                {
                    var path = ProcessInfo.ImagePath(e.Pid);
                    id = path is null ? null : candidates.FirstOrDefault(g => IsInside(path, g.InstallDir))?.Id;
                    Confirmed[key] = id;
                }
                if (id is not null) found ??= games.FirstOrDefault(g => g.Id == id);
            }
            foreach (var stale in Confirmed.Keys.Where(k => !alive.Contains(k)).ToList()) Confirmed.Remove(stale);
            return found;
        }
    }

    internal static bool IsInside(string path, string folder)
    {
        var root = folder.TrimEnd('\\', '/') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")] private static partial Regex SteamPathRegex();
    [GeneratedRegex(@"redistributable|steamworks|proton|steam linux runtime|steamvr|soundtrack|dedicated server|\bsdk\b|benchmark", RegexOptions.IgnoreCase)] private static partial Regex NotAGame();
    [GeneratedRegex(@"^(unins.*|.*crash.*|.*setup.*|.*redist.*|dxsetup|ue\dprereq.*|easyanticheat.*|eac.*|start_protected_game|be(service|launcher).*|.*installer.*|.*updater?|.*helper|dotnet.*|vc_?redist.*|cefprocess|quicksfv|.*report.*)$", RegexOptions.IgnoreCase)] private static partial Regex HelperExe();
    [GeneratedRegex(@"[®™©]")] private static partial Regex TrademarkRegex();
}

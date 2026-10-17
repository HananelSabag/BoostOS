using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoostOS.Core;
using Microsoft.Win32;
using BoostOS.Tuning;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

/// <summary>
/// Rocket League install and settings. Since 28 April 2026 the game runs Easy Anti-Cheat online, so this class only
/// reads files on disk and the registry: it never opens a handle to the game process or touches its memory.
/// </summary>
internal static partial class RocketLeague
{
    public sealed record Install(string Store, string Folder, string Exe, string? EpicAppName);

    public static Install? Find()
    {
        var manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (Directory.Exists(manifests))
        {
            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("DisplayName", out var dn) || dn.GetString()?.Contains("Rocket League", StringComparison.OrdinalIgnoreCase) != true) continue;
                    var folder = root.GetProperty("InstallLocation").GetString() ?? "";
                    var app = root.TryGetProperty("AppName", out var an) ? an.GetString() : null;
                    return new Install("Epic Games", folder, Path.Combine(folder, @"Binaries\Win64\RocketLeague.exe"), app);
                }
                catch
                {
                    // A half-written manifest should not stop the scan.
                }
            }
        }

        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam)
        {
            var libraries = new List<string> { steam };
            var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                libraries.AddRange(SteamPathRegex().Matches(File.ReadAllText(vdf)).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(lib, @"steamapps\appmanifest_252950.acf"))) continue;
                var folder = Path.Combine(lib, @"steamapps\common\rocketleague");
                return new Install("Steam", folder, Path.Combine(folder, @"Binaries\Win64\RocketLeague.exe"), null);
            }
        }
        return null;
    }

    /// <summary>From the system process snapshot only: no handle is ever opened to the game.</summary>
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName("RocketLeague");
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    public static string ConfigFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Rocket League\TAGame\Config");

    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();
        var title = L("רוקט ליג", "Rocket League");
        var installTitle = L("התקנה", "Install");
        var install = Find();
        if (install is null)
        {
            checks.Add(new(installTitle, L("לא מצאתי את רוקט ליג", "Rocket League not found"), Warn, L("חיפשתי ב-Epic Games וב-Steam.", "Looked in Epic Games and Steam.")));
            return new Section("rocketleague", title, "car", checks);
        }

        checks.Add(new(installTitle, $"{install.Store} · {install.Folder}", File.Exists(install.Exe) ? Ok : Warn));

        if (IsRunning())
            checks.Add(new(L("מצב", "Status"), L("המשחק פתוח עכשיו", "The game is running"), Info,
                L("חלק מההגדרות נקראות מהקובץ ומתעדכנות רק אחרי שהמשחק נסגר.", "Some settings come from its file and update only after the game closes.")));

        if (Directory.Exists(Path.Combine(install.Folder, @"Binaries\Win64\EasyAntiCheat")))
            checks.Add(new("Easy Anti-Cheat", L("פעיל באונליין", "Active online"), Info,
                L("BoostOS לא נוגע בתהליך של המשחק ולא בזיכרון שלו, ולא מזריק לתוכו כלום. הוא משנה רק את ווינדוס, את הגדרות הדרייבר ואת קובץ ההגדרות, כמו שאתה היית עושה ידנית. לכן אין סיכון לבאן.",
                  "BoostOS never touches the game process or its memory and injects nothing. It only changes Windows, driver settings and the settings file, exactly what you could do by hand. So there's no ban risk.")));

        var prefs = WindowsCheck.ParseFlags(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", install.Exe, null) as string);
        var gpuTitle = L("כרטיס שהמשחק משתמש בו", "GPU the game uses");
        if (facts.HasTwoGpus)
            checks.Add(prefs.GetValueOrDefault("GpuPreference") == "2"
                ? new(gpuTitle, "NVIDIA (High performance)", Ok, null, Fixes.Ref("rl-gpu-pref"))
                : new(gpuTitle, L("לא מוגדר", "Not set"), Warn,
                    L("בלפטופ עם שני כרטיסים, משחק יכול בטעות לרוץ על הכרטיס החלש.", "On a laptop with two GPUs a game can end up on the weak one."), Fixes.Ref("rl-gpu-pref")));

        var ini = ReadIni(Path.Combine(ConfigFolder, "TASystemSettings.ini"), "SystemSettings");
        if (ini.Count == 0)
        {
            checks.Add(new(L("קובץ הגדרות", "Settings file"), L("לא נמצא", "Not found"), Info,
                L($"חיפשתי ב-{ConfigFolder}. הוא נוצר בפעם הראשונה שמשנים הגדרות במשחק.", $"Looked in {ConfigFolder}. The game creates it the first time you change a setting.")));
            return new Section("rocketleague", title, "car", checks);
        }

        bool Flag(string key) => ini.GetValueOrDefault(key, "").Equals("True", StringComparison.OrdinalIgnoreCase);

        var fullscreen = Flag("Fullscreen");
        var borderless = Flag("Borderless");
        var mode = L("מצב תצוגה", "Display mode");
        checks.Add(fullscreen switch
        {
            true when !borderless => new(mode, "Fullscreen", Ok),
            true => new(mode, "Borderless", Info,
                L("Borderless נוח לאלט-טאב. כשהאפשרות Optimizations for windowed games פעילה, ההשהיה שלו כמעט כמו Fullscreen.",
                  "Borderless makes alt-tab easy. With Optimizations for windowed games on, its latency is close to fullscreen.")),
            false => new(mode, L("חלון", "Windowed"), Warn,
                L("מצב חלון מוסיף השהיה. בהגדרות הווידאו במשחק עבור ל-Fullscreen או Borderless.", "Windowed mode adds latency. Switch to Fullscreen or Borderless in the game's video settings.")),
        });

        if (int.TryParse(ini.GetValueOrDefault("ResX"), out var resX) && int.TryParse(ini.GetValueOrDefault("ResY"), out var resY))
        {
            var primary = DisplayCheck.Enumerate().FirstOrDefault(d => d.IsPrimary);
            var resTitle = L("רזולוציה במשחק", "Game resolution");
            checks.Add(primary is null || (primary.Width == resX && primary.Height == resY)
                ? new(resTitle, $"{resX}×{resY}", Ok)
                : new(resTitle, $"{resX}×{resY} · {L("המסך", "display")} {primary.Width}×{primary.Height}", Info,
                    L("המשחק לא ברזולוציה של המסך. אם זה בכוונה בשביל FPS, זה בסדר. אחרת התמונה פחות חדה.", "The game isn't at the display's resolution. Fine if it's on purpose for FPS; otherwise the picture is softer.")));
        }

        checks.Add(Flag("UseVsync")
            ? new("VSync", L("פעיל", "On"), Warn,
                L("VSync מוסיף השהיה מורגשת. ברוקט ליג כמעט כולם מכבים אותו, ובמסך G-Sync/FreeSync הוא לא נחוץ. צריך שהמשחק יהיה סגור בזמן התיקון.",
                  "VSync adds noticeable latency. Almost everyone turns it off in Rocket League, and a G-Sync/FreeSync display doesn't need it. The game must be closed for the fix."),
                Fixes.Ref("rl-vsync"))
            : new("VSync", L("כבוי", "Off"), Ok, null, Fixes.Ref("rl-vsync")));

        checks.Add(new(L("הגבלת FPS", "Frame cap"), Flag("UncappedFramerate") ? L("ללא הגבלה", "Uncapped") : L("מוגבל (לפי מה שבחרת במשחק)", "Capped (as set in game)"), Info,
            L("FPS יציב עדיף על FPS גבוה שקופץ. דוח המשחק מראה אם המעבד או הכרטיס הם שמגבילים.", "Steady FPS beats high FPS that jumps around. The match report shows whether the CPU or the GPU holds you back.")));

        if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bakkesmod")))
            checks.Add(new("BakkesMod", L("מותקן", "Installed"), Info,
                L("מאז 28.4.2026, כשנכנס Easy Anti-Cheat, BakkesMod עובד רק כשמפעילים את המשחק בלי EAC: אימון, Free Play, ריפליים ומשחקים אופליין. באונליין הוא לא נטען.",
                  "Since Easy Anti-Cheat arrived on 28 April 2026, BakkesMod only works when the game runs without EAC: training, Free Play, replays and offline matches. It doesn't load online.")));

        return new Section("rocketleague", title, "car", checks);
    }

    /// <summary>Reads one [section] of an Unreal-style ini file. Missing file → empty.</summary>
    internal static Dictionary<string, string> ReadIni(string path, string section)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;
        var inSection = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var i = line.IndexOf('=');
            if (i > 0) map.TryAdd(line[..i].Trim(), line[(i + 1)..].Trim());
        }
        return map;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")] private static partial Regex SteamPathRegex();
}

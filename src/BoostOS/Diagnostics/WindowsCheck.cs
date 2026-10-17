using BoostOS.Core;
using BoostOS.Tuning;
using Microsoft.Win32;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class WindowsCheck
{
    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();

        var gameMode = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", null);
        checks.Add(gameMode is 0
            ? new("Game Mode", L("כבוי", "Off"), Warn,
                L("Game Mode נותן למשחק עדיפות על המעבד ומונע התקנת עדכונים באמצע משחק.", "Game Mode gives the game CPU priority and stops updates from installing mid-match."),
                Fixes.Ref("windows-game-mode"))
            : new("Game Mode", L("פעיל", "On"), Ok));

        var history = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", null);
        var recTitle = L("הקלטה ברקע (Game Bar)", "Background recording (Game Bar)");
        checks.Add(history is 1
            ? new(recTitle, L("פעילה", "On"), Warn,
                L("ווינדוס מקליט כל הזמן את הדקות האחרונות כדי שתוכל לשמור קליפ, וזה עולה בביצועים. אם אתה מקליט קליפים עם NVIDIA או OBS, אין בזה צורך.",
                  "Windows keeps recording the last minutes so you can save a clip, which costs performance. If you clip with NVIDIA or OBS, you don't need it."),
                Fixes.Ref("game-dvr-history"))
            : new(recTitle, L("כבויה", "Off"), Ok));

        var global = ParseFlags(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", null) as string);
        const string windowed = "Optimizations for windowed games";
        checks.Add(global.GetValueOrDefault("SwapEffectUpgradeEnable") switch
        {
            "1" => new(windowed, L("פעיל", "On"), Ok, L("משחק במצב Borderless מקבל כמעט את אותה השהיה כמו Fullscreen אמיתי.", "Borderless games get nearly the same latency as true fullscreen.")),
            "0" => new(windowed, L("כבוי", "Off"), Warn,
                L("בלי זה, משחק במצב Borderless מקבל יותר השהיה.", "Without it, borderless games get more latency."), Fixes.Ref("windowed-opt")),
            _ => new(windowed, L("ברירת מחדל", "Default"), Info),
        });

        var hags = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", null);
        var hagsAdvice = L("ההשפעה של זה על רוקט ליג קטנה ומשתנה ממחשב למחשב, אז לא נוגעים בזה בלי מדידה.",
                           "Its effect on Rocket League is small and varies by PC, so BoostOS leaves it alone without a measurement.");
        const string hagsTitle = "Hardware-accelerated GPU scheduling";
        checks.Add(hags switch
        {
            2 => new(hagsTitle, L("פעיל", "On"), Info, hagsAdvice),
            1 => new(hagsTitle, L("כבוי", "Off"), Info, hagsAdvice),
            _ => new(hagsTitle, L("ברירת מחדל של ווינדוס", "Windows default"), Info, hagsAdvice),
        });

        return new Section("windows", L("הגדרות ווינדוס למשחקים", "Windows gaming settings"), "windows", checks);
    }

    /// <summary>Parses DirectX preference strings such as "SwapEffectUpgradeEnable=1;GpuPreference=2;".</summary>
    public static Dictionary<string, string> ParseFlags(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) map[kv[0].Trim()] = kv[1].Trim();
        }
        return map;
    }
}

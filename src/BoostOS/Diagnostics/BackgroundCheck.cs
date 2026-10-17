using System.Diagnostics;
using BoostOS.Core;
using BoostOS.Game;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class BackgroundCheck
{
    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();

        // GetProcesses reads a system snapshot and opens no process handles, so it never touches
        // the game process (important with Easy Anti-Cheat).
        var procs = Process.GetProcesses();
        var groups = procs
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count(), Bytes: g.Sum(SafeWorkingSet)))
            .ToList();
        foreach (var p in procs) p.Dispose();

        var os = Wmi.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        var memText = "";
        if (os is not null)
        {
            var totalGb = os.Long("TotalVisibleMemorySize") / 1024d / 1024;
            var usedGb = totalGb - os.Long("FreePhysicalMemory") / 1024d / 1024;
            memText = $" · {L($"זיכרון בשימוש {usedGb:0.0} מתוך {totalGb:0} GB", $"{usedGb:0.0} of {totalGb:0} GB memory in use")}";
        }

        // Game Mode deals with the background before every match, so these lines inform rather than count against the score.
        var open = L("תהליכים פתוחים", "Running processes");
        checks.Add(procs.Length < 200
            ? new(open, $"{procs.Length}{memText}", Ok)
            : new(open, $"{procs.Length}{memText}", Info,
                L("הרבה תוכנות פתוחות ברקע. מצב משחק סוגר את אלה שמפריעות לפני כל משחק.", "Plenty running in the background. Game Mode closes the ones that get in the way before each match.")));

        var closing = GameMode.RunningLabels();
        var closeTitle = L("מצב משחק יסגור", "Game Mode will close");
        checks.Add(closing.Count == 0
            ? new(closeTitle, L("אין כרגע מה לסגור", "Nothing right now"), Ok)
            : new(closeTitle, string.Join(" · ", closing), Info,
                L("התוכנות האלה מסנכרנות, מעדכנות ושולחות התראות. מצב משחק סוגר אותן כשאתה לוחץ 'שחק' ומחזיר את מה שצריך בסוף.",
                  "These sync, update and send notifications. Game Mode closes them when you press Play and brings back what's needed afterwards.")));

        var top = groups.OrderByDescending(g => g.Bytes).Take(6)
            .Select(g => g.Count > 1 ? $"{g.Name} ×{g.Count} ({Gb(g.Bytes)})" : $"{g.Name} ({Gb(g.Bytes)})");
        checks.Add(new(L("הכי הרבה זיכרון", "Most memory"), string.Join(" · ", top), Info));

        return new Section("background", L("תהליכים ברקע", "Background apps"), "layers", checks);
    }

    private static long SafeWorkingSet(Process p)
    {
        try { return p.WorkingSet64; }
        catch { return 0; }
    }

    private static string Gb(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0}GB" : $"{bytes / (1L << 20)}MB";
}

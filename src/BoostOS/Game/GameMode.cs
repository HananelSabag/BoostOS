using System.Diagnostics;
using System.Text.Json;
using BoostOS.Core;
using BoostOS.Diagnostics;
using static BoostOS.Core.I18n;

namespace BoostOS.Game;

public sealed record PlanItem(string Title, string Detail, bool WillRun);

public sealed record StepResult(string Title, string Detail, string Result); // done | skipped | failed

public sealed record GameModeStatus(bool Active, DateTime? Since, IReadOnlyList<PlanItem> Plan, IReadOnlyList<StepResult> Steps);

/// <summary>
/// Game Mode: get the PC out of the game's way, remember exactly what was changed, and put it back afterwards.
/// The snapshot lives on disk, so even if BoostOS is closed or crashes, the next start can still restore.
/// Nothing here touches the Rocket League process.
/// </summary>
internal static class GameMode
{
    private sealed record AppRule(string Label, string[] Processes, string? CloseArgs, string? RelaunchArgs);

    // Apps that only get in the way during a match: background sync, updaters and phone/desktop extras that can
    // grab the network or CPU mid-game. Browsers, Discord, Spotify, the Epic launcher, controller and keyboard
    // software, and Claude are deliberately left alone.
    private static readonly AppRule[] Rules =
    [
        new("OneDrive", ["OneDrive"], "/shutdown", null), // the user unlinked OneDrive and doesn't want it back after a match
        new("Steam", ["steam"], "-shutdown", "-silent"),
        new("Phone Link", ["PhoneExperienceHost", "CrossDeviceService"], null, null),
        new("Widgets", ["Widgets"], null, null), // WidgetService is respawned by Windows at once, so leave it
        new("Teams", ["ms-teams"], null, null),
        new("Copilot", ["Copilot"], null, null),
        new("Adobe Creative Cloud", ["Creative Cloud", "Adobe Desktop Service", "CCXProcess", "AdobeIPCBroker"], null, null),
    ];

    public sealed class Snapshot
    {
        public DateTime Since { get; set; }
        public List<Relaunch> Relaunch { get; set; } = [];
        public Guid? Overlay { get; set; }
        public string? WifiScanStoppedOn { get; set; }
        public List<StepResult> Steps { get; set; } = [];
    }

    public sealed record Relaunch(string Label, string Exe, string Args);

    private static string StatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostOS", "gamemode.json");

    public static bool IsActive => File.Exists(StatePath);

    /// <param name="periodicWifiSpikes">From the last network test: true = spikes on a fixed beat were found, null = no test yet.</param>
    public static GameModeStatus Status(bool? periodicWifiSpikes)
    {
        var snap = Load();
        return new GameModeStatus(snap is not null, snap?.Since, BuildPlan(periodicWifiSpikes), snap?.Steps ?? []);
    }

    private static List<PlanItem> BuildPlan(bool? periodicWifiSpikes)
    {
        var plan = new List<PlanItem>();

        var running = RunningRules().Select(r => r.Label).ToList();
        plan.Add(running.Count > 0
            ? new(L("סגירת תוכנות רקע", "Close background apps"),
                L($"{string.Join(", ", running)}. הן מסנכרנות, מעדכנות ושולחות התראות באמצע משחק.",
                  $"{string.Join(", ", running)}. They sync, update and pop notifications mid-match."), true)
            : new(L("סגירת תוכנות רקע", "Close background apps"), L("אין כרגע מה לסגור.", "Nothing to close right now."), false));

        if (PowerCheck.ActiveScheme() == PowerCheck.Balanced)
            plan.Add(PowerCheck.Overlay() == PowerCheck.OverlayBestPerformance
                ? new(L("מצב חשמל Best performance", "Best performance power mode"), L("כבר מוגדר.", "Already set."), false)
                : new(L("מצב חשמל Best performance", "Best performance power mode"),
                    L("המעבד לא יוריד מהירות באמצע משחק. חוזר למצב הקודם בסוף.", "The CPU won't clock down mid-match. Goes back afterwards."), true));

        plan.Add(periodicWifiSpikes switch
        {
            true => new(L("עצירת סריקת Wi-Fi ברקע", "Pause Wi-Fi background scans"),
                L("בבדיקת הרשת נמצאו קפיצות בקצב קבוע. הסריקה חוזרת בסוף.", "The network test found spikes on a fixed beat. Scanning resumes afterwards."), true),
            false => new(L("סריקת Wi-Fi ברקע", "Wi-Fi background scans"),
                L("לא נוגעים: בבדיקת הרשת לא נמצאו קפיצות קבועות.", "Left alone: the network test found no spikes on a fixed beat."), false),
            null => new(L("סריקת Wi-Fi ברקע", "Wi-Fi background scans"),
                L("לא נוגעים: עוד לא הורצה בדיקת רשת.", "Left alone: no network test has been run yet."), false),
        });

        plan.Add(new(L("אחרי המשחק", "After the match"),
            L("כשרוקט ליג נסגר הכל חוזר לבד: Steam נפתח שוב ומצב החשמל חוזר. OneDrive נשאר סגור.",
              "When Rocket League closes, everything goes back by itself: Steam reopens and the power mode is restored. OneDrive stays closed."), true));
        return plan;
    }

    public static GameModeStatus Apply(bool? periodicWifiSpikes)
    {
        if (IsActive) return Status(periodicWifiSpikes);

        var snap = new Snapshot { Since = DateTime.Now };
        Save(snap); // first, so a crash half-way can still be restored

        foreach (var rule in RunningRules())
        {
            try
            {
                var relaunch = Close(rule);
                if (relaunch is not null) snap.Relaunch.Add(relaunch);
                snap.Steps.Add(new(rule.Label, relaunch is null ? L("נסגר", "Closed") : L("נסגר, ייפתח שוב בסוף", "Closed, reopens afterwards"), "done"));
            }
            catch (Exception ex)
            {
                snap.Steps.Add(new(rule.Label, L($"לא הצלחתי לסגור: {ex.Message}", $"Could not close: {ex.Message}"), "failed"));
            }
            Save(snap);
        }

        if (PowerCheck.ActiveScheme() == PowerCheck.Balanced && PowerCheck.Overlay() is { } overlay)
        {
            if (overlay == PowerCheck.OverlayBestPerformance)
                snap.Steps.Add(new(L("מצב חשמל", "Power mode"), L("כבר על Best performance", "Already on Best performance"), "skipped"));
            else if (Native.PowerSetActiveOverlayScheme(PowerCheck.OverlayBestPerformance) == 0)
            {
                snap.Overlay = overlay;
                snap.Steps.Add(new(L("מצב חשמל", "Power mode"), L("עבר ל-Best performance", "Switched to Best performance"), "done"));
            }
            else
                snap.Steps.Add(new(L("מצב חשמל", "Power mode"), L("ווינדוס לא אישר את השינוי", "Windows refused the change"), "failed"));
        }

        if (periodicWifiSpikes == true && NetworkCheck.FindActiveLink() is { IsWifi: true } link)
        {
            var output = Shell.Run("netsh", $"wlan set autoconfig enabled=no interface=\"{link.Nic.Name}\"") ?? "";
            if (output.Contains("elevation", StringComparison.OrdinalIgnoreCase) || output.Contains("denied", StringComparison.OrdinalIgnoreCase))
                snap.Steps.Add(new(L("סריקת Wi-Fi", "Wi-Fi scans"), L("צריך להריץ את BoostOS כמנהל בשביל זה", "Needs BoostOS to run as administrator"), "failed"));
            else
            {
                snap.WifiScanStoppedOn = link.Nic.Name;
                snap.Steps.Add(new(L("סריקת Wi-Fi", "Wi-Fi scans"), L("נעצרה עד סוף המשחק", "Paused until the match ends"), "done"));
            }
        }

        Save(snap);
        return Status(periodicWifiSpikes);
    }

    public static GameModeStatus Restore(bool? periodicWifiSpikes)
    {
        var snap = Load();
        if (snap is null)
        {
            if (IsActive) File.Delete(StatePath); // unreadable snapshot: nothing we can put back
            return Status(periodicWifiSpikes);
        }

        foreach (var r in snap.Relaunch)
        {
            try
            {
                Process.Start(new ProcessStartInfo(r.Exe, r.Args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(r.Exe) ?? "" })?.Dispose();
            }
            catch
            {
                // The app may have been uninstalled or updated to a new path; nothing to put back then.
            }
        }

        if (snap.Overlay is { } overlay) Native.PowerSetActiveOverlayScheme(overlay);
        if (snap.WifiScanStoppedOn is { } nic) Shell.Run("netsh", $"wlan set autoconfig enabled=yes interface=\"{nic}\"");

        File.Delete(StatePath);
        return Status(periodicWifiSpikes);
    }

    public static List<string> RunningLabels() => RunningRules().Select(r => r.Label).ToList();

    private static IEnumerable<AppRule> RunningRules()
    {
        var rlOnSteam = RocketLeague.Find()?.Store == "Steam";
        foreach (var rule in Rules)
        {
            if (rlOnSteam && rule.Label == "Steam") continue;
            var procs = rule.Processes.SelectMany(Process.GetProcessesByName).ToList();
            var any = procs.Count > 0;
            foreach (var p in procs) p.Dispose();
            if (any) yield return rule;
        }
    }

    /// <summary>Closes an app politely when it has a shutdown switch, otherwise by window, otherwise by force.</summary>
    private static Relaunch? Close(AppRule rule)
    {
        var main = Process.GetProcessesByName(rule.Processes[0]);
        var exe = main.Select(ExePath).FirstOrDefault(p => p is not null);
        foreach (var p in main) p.Dispose();

        if (rule.CloseArgs is not null && exe is not null)
        {
            Process.Start(new ProcessStartInfo(exe, rule.CloseArgs) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            var deadline = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < deadline && AnyRunning(rule)) Thread.Sleep(250);
        }

        foreach (var p in rule.Processes.SelectMany(Process.GetProcessesByName))
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(2000)) continue;
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch
                {
                    // Already gone, or a system-owned helper we are not allowed to stop.
                }
            }
        }

        return rule.RelaunchArgs is not null && exe is not null ? new Relaunch(rule.Label, exe, rule.RelaunchArgs) : null;
    }

    private static bool AnyRunning(AppRule rule)
    {
        var procs = rule.Processes.SelectMany(Process.GetProcessesByName).ToList();
        foreach (var p in procs) p.Dispose();
        return procs.Count > 0;
    }

    private static string? ExePath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch
        {
            // 32-bit apps such as Steam: ask WMI instead.
            return Wmi.Query($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {p.Id}").FirstOrDefault()?.Str("ExecutablePath") is { Length: > 0 } path ? path : null;
        }
    }

    private static Snapshot? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(StatePath)) : null; }
        catch { return null; }
    }

    private static void Save(Snapshot snap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
    }
}

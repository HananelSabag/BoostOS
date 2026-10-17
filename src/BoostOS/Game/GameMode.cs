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
    private static readonly object StateGate = new();
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

    public static bool IsActive
    {
        get { lock (StateGate) return File.Exists(StatePath); }
    }

    /// <param name="periodicWifiSpikes">From the last network test: true = spikes on a fixed beat were found, null = no test yet.</param>
    public static GameModeStatus Status(bool? periodicWifiSpikes)
    {
        lock (StateGate)
        {
            var snap = Load();
            var active = IsActive;
            IReadOnlyList<StepResult> steps = snap?.Steps ?? [];
            if (active && snap is null)
                steps = [new(L("שחזור מצב משחק", "Game Mode recovery"),
                    L("קובץ השחזור לא קריא. הוא נשמר ולא יוחלף, כדי לא לאבד את ההגדרות המקוריות.",
                      "The recovery file cannot be read. It has been kept to preserve the original settings."), "failed")];
            return new GameModeStatus(active, snap?.Since, BuildPlan(periodicWifiSpikes), steps);
        }
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
            true => new(L("סריקת Wi-Fi ברקע", "Wi-Fi background scans"),
                L("נמצאו קפיצות בקצב קבוע, אך זו אינה הוכחה לסריקות Wi-Fi. הגדרות החיבור נשארות כפי שהן.",
                  "Periodic spikes were found, but they do not prove Wi-Fi scanning is the cause. Connection settings stay as they are."), false),
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
        lock (StateGate) return ApplyCore(periodicWifiSpikes);
    }

    private static GameModeStatus ApplyCore(bool? periodicWifiSpikes)
    {
        if (IsActive) return Status(periodicWifiSpikes);

        var snap = new Snapshot { Since = DateTime.Now };
        Save(snap); // first, so a crash half-way can still be restored

        foreach (var rule in RunningRules())
        {
            try
            {
                var relaunch = Close(rule, pending =>
                {
                    snap.Relaunch.Add(pending);
                    Save(snap); // remember how to reopen the app before closing it
                });
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
            else
            {
                snap.Overlay = overlay;
                Save(snap); // persist the original mode before changing Windows
                snap.Steps.Add(Native.PowerSetActiveOverlayScheme(PowerCheck.OverlayBestPerformance) == 0
                    ? new(L("מצב חשמל", "Power mode"), L("עבר ל-Best performance", "Switched to Best performance"), "done")
                    : new(L("מצב חשמל", "Power mode"), L("ווינדוס לא אישר את השינוי", "Windows refused the change"), "failed"));
            }
        }

        Save(snap);
        return Status(periodicWifiSpikes);
    }

    public static GameModeStatus Restore(bool? periodicWifiSpikes)
    {
        lock (StateGate)
        {
            var snap = Load();
            if (snap is null) return Status(periodicWifiSpikes);

            var pending = RestorePending(snap,
                r =>
                {
                    using var process = Process.Start(new ProcessStartInfo(r.Exe, r.Args)
                    { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(r.Exe) ?? "" });
                    if (process is null) throw new InvalidOperationException(L("התוכנה לא נפתחה", "The app did not start"));
                },
                overlay => Native.PowerSetActiveOverlayScheme(overlay) == 0,
                nic => Shell.Run("netsh", $"wlan set autoconfig enabled=yes interface=\"{nic}\"") is not null);

            if (pending) Save(snap);
            else File.Delete(StatePath);
            return Status(periodicWifiSpikes);
        }
    }

    /// <summary>Attempt every independent recovery item; leave failed items available for another try.</summary>
    internal static bool RestorePending(Snapshot snap, Action<Relaunch> relaunch, Func<Guid, bool> restorePower, Func<string, bool> restoreWifi)
    {
        snap.Steps.Clear();
        void Attempt(string title, Action restore)
        {
            try
            {
                restore();
                snap.Steps.Add(new(title, L("הוחזר למצב הקודם", "Restored"), "done"));
            }
            catch (Exception ex)
            {
                snap.Steps.Add(new(title,
                    L($"השחזור נכשל: {ex.Message}. ההגדרות המקוריות נשמרו. אפשר לנסות שוב בלחצן השחזור.",
                      $"Recovery failed: {ex.Message}. The original settings were kept. Use the restore button to retry."), "failed"));
            }
        }

        if (snap.Overlay is { } overlay)
            Attempt(L("מצב חשמל", "Power mode"), () =>
            {
                if (!restorePower(overlay)) throw new InvalidOperationException(L("ווינדוס לא אישר את השינוי", "Windows refused the change"));
                snap.Overlay = null;
            });
        // Old snapshots may contain a paused Wi-Fi service. New sessions never disable it automatically.
        if (snap.WifiScanStoppedOn is { } nic)
            Attempt(L("סריקת Wi-Fi", "Wi-Fi scans"), () =>
            {
                if (!restoreWifi(nic)) throw new InvalidOperationException(L("ווינדוס לא אישר את השינוי; ייתכן שנדרשות הרשאות מנהל", "Windows refused the change; administrator rights may be needed"));
                snap.WifiScanStoppedOn = null;
            });

        foreach (var r in snap.Relaunch.ToArray())
            Attempt(r.Label, () =>
            {
                relaunch(r);
                snap.Relaunch.Remove(r);
            });

        return snap.Overlay is not null || snap.WifiScanStoppedOn is not null || snap.Relaunch.Count > 0;
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
    private static Relaunch? Close(AppRule rule, Action<Relaunch> recordRelaunch)
    {
        var main = Process.GetProcessesByName(rule.Processes[0]);
        var exe = main.Select(ExePath).FirstOrDefault(p => p is not null);
        foreach (var p in main) p.Dispose();
        var relaunch = rule.RelaunchArgs is not null && exe is not null ? new Relaunch(rule.Label, exe, rule.RelaunchArgs) : null;
        if (relaunch is not null) recordRelaunch(relaunch);

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

        return relaunch;
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
        try
        {
            var snap = File.Exists(StatePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(StatePath)) : null;
            return snap?.Relaunch is not null && snap.Steps is not null ? snap : null;
        }
        catch { return null; }
    }

    private static void Save(Snapshot snap) => SaveSnapshot(snap, StatePath);

    internal static void SaveSnapshot(Snapshot snap, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, snap, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using BoostOS.Core;
using BoostOS.Diagnostics;
using Microsoft.Win32;

namespace BoostOS.Tuning;

public sealed record FixRecord(string Id, DateTime At, string? Before);

public sealed record FixOutcome(List<string> Done, List<FixError> Failed);

public sealed record FixError(string Id, string Message);

/// <summary>
/// Everything BoostOS can change for good (as opposed to Game Mode, which is temporary). Each fix records the value
/// it replaced in fixes.json, so "Undo all fixes" can put the PC back exactly as it was.
/// Admin fixes run in a second copy of BoostOS started elevated: one UAC prompt for the whole batch.
/// </summary>
internal static class Fixes
{
    private sealed record Def(bool Admin, Func<string?> Apply, Action<string?> Undo);

    private const string DirectXKey = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GameDvrKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameBarKey = @"HKEY_CURRENT_USER\Software\Microsoft\GameBar";

    private static readonly Dictionary<string, Def> Defs = new()
    {
        ["power-overlay"] = new(false,
            () =>
            {
                var before = PowerCheck.Overlay();
                Ensure(Native.PowerSetActiveOverlayScheme(PowerCheck.OverlayBestPerformance) == 0, "power mode");
                return before?.ToString();
            },
            before => { if (Guid.TryParse(before, out var g)) Native.PowerSetActiveOverlayScheme(g); }),

        ["game-dvr-history"] = new(false,
            () => SetDword(GameDvrKey, "HistoricalCaptureEnabled", 0),
            before => RestoreDword(GameDvrKey, "HistoricalCaptureEnabled", before)),

        ["windows-game-mode"] = new(false,
            () => SetDword(GameBarKey, "AutoGameModeEnabled", 1),
            before => RestoreDword(GameBarKey, "AutoGameModeEnabled", before)),

        ["windowed-opt"] = new(false,
            () => SetFlag(DirectXKey, "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable", "1"),
            before => RestoreString(DirectXKey, "DirectXUserGlobalSettings", before)),

        ["rl-gpu-pref"] = new(false,
            () => SetFlag(DirectXKey, GameExe(), "GpuPreference", "2"),
            before => RestoreString(DirectXKey, GameExe(), before)),

        ["rl-vsync"] = new(false,
            () => SetIni("UseVsync", "False"),
            before => { if (before is not null) SetIni("UseVsync", before); }),

        ["wifi-power-ac"] = new(false,
            () =>
            {
                var (ac, _) = PowerCheck.ReadSetting(PowerCheck.WirelessSubgroup, PowerCheck.WirelessPowerSaving);
                WriteWirelessAc(0);
                return ac?.ToString();
            },
            before => { if (uint.TryParse(before, out var v)) WriteWirelessAc(v); }),

        ["wifi-roaming"] = new(true,
            () => SetAdapterProperty("RoamAggressiveness", "0"),
            before => { if (before is not null) SetAdapterProperty("RoamAggressiveness", before); }),

        ["nvidia-profile"] = new(true,
            () => JsonSerializer.Serialize(Nvidia.Apply()),
            before => { if (before is not null) Nvidia.Restore(JsonSerializer.Deserialize<Nvidia.Snapshot>(before)!); }),
    };

    public static bool NeedsAdmin(string id) => Defs.TryGetValue(id, out var d) && d.Admin;

    public static FixRef Ref(string id) => new(id, NeedsAdmin(id));

    private static string LogPath => Path.Combine(AppSettings.DataDir, "fixes.json");

    public static List<FixRecord> Applied()
    {
        try { return File.Exists(LogPath) ? JsonSerializer.Deserialize<List<FixRecord>>(File.ReadAllText(LogPath)) ?? [] : []; }
        catch { return []; }
    }

    private static void SaveLog(List<FixRecord> log)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(LogPath, JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Runs fixes in this process. The caller makes sure admin fixes only get here when elevated.</summary>
    public static FixOutcome ApplyHere(IEnumerable<string> ids)
    {
        var done = new List<string>();
        var failed = new List<FixError>();
        foreach (var id in ids.Distinct())
        {
            if (!Defs.TryGetValue(id, out var def)) { failed.Add(new(id, "unknown fix")); continue; }
            try
            {
                var before = def.Apply();
                var log = Applied();
                // Keep the oldest "before": undo must return to the state from before BoostOS touched it.
                if (log.All(r => r.Id != id)) log.Add(new FixRecord(id, DateTime.Now, before));
                SaveLog(log);
                done.Add(id);
            }
            catch (Exception ex)
            {
                failed.Add(new(id, ex.Message));
            }
        }
        return new(done, failed);
    }

    public static FixOutcome UndoHere(IEnumerable<string> ids)
    {
        var done = new List<string>();
        var failed = new List<FixError>();
        foreach (var id in ids.Distinct())
        {
            var log = Applied();
            var record = log.FirstOrDefault(r => r.Id == id);
            if (record is null || !Defs.TryGetValue(id, out var def)) continue;
            try
            {
                def.Undo(record.Before);
                log.RemoveAll(r => r.Id == id);
                SaveLog(log);
                done.Add(id);
            }
            catch (Exception ex)
            {
                failed.Add(new(id, ex.Message));
            }
        }
        return new(done, failed);
    }

    /// <summary>Applies (or undoes) a batch: user-level fixes here, admin fixes in one elevated helper.</summary>
    public static FixOutcome Run(IReadOnlyCollection<string> ids, bool undo)
    {
        var plain = ids.Where(i => !NeedsAdmin(i)).ToList();
        var admin = ids.Where(NeedsAdmin).ToList();
        var result = undo ? UndoHere(plain) : ApplyHere(plain);
        if (admin.Count == 0) return result;

        var elevated = RunElevated(admin, undo);
        result.Done.AddRange(elevated.Done);
        result.Failed.AddRange(elevated.Failed);
        return result;
    }

    private static FixOutcome RunElevated(List<string> ids, bool undo)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"boostos-fix-{Guid.NewGuid():N}.json");
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, $"{(undo ? "--elevated-undo" : "--elevated-fix")} {string.Join(',', ids)} \"{outFile}\" {(I18n.English ? "en" : "he")}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(120_000);
            return File.Exists(outFile)
                ? JsonSerializer.Deserialize<FixOutcome>(File.ReadAllText(outFile)) ?? new([], ids.Select(i => new FixError(i, "no result")).ToList())
                : new([], ids.Select(i => new FixError(i, "no result")).ToList());
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new([], ids.Select(i => new FixError(i, I18n.L("לא אושר בחלון ההרשאות (UAC)", "Not approved in the UAC prompt"))).ToList());
        }
        finally
        {
            try { File.Delete(outFile); } catch { }
        }
    }

    // ---- Helpers ----

    private static void Ensure(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException($"Windows refused to change {what}");
    }

    private static string? SetDword(string key, string name, int value)
    {
        var before = Registry.GetValue(key, name, null);
        Registry.SetValue(key, name, value, RegistryValueKind.DWord);
        return before?.ToString();
    }

    private static void RestoreDword(string key, string name, string? before)
    {
        if (int.TryParse(before, out var v)) Registry.SetValue(key, name, v, RegistryValueKind.DWord);
        else DeleteValue(key, name);
    }

    /// <summary>Sets one "Name=value;" flag inside a DirectX preference string, keeping the others.</summary>
    private static string? SetFlag(string key, string name, string flag, string value)
    {
        var before = Registry.GetValue(key, name, null) as string;
        var flags = WindowsCheck.ParseFlags(before);
        flags[flag] = value;
        Registry.SetValue(key, name, string.Concat(flags.Select(kv => $"{kv.Key}={kv.Value};")), RegistryValueKind.String);
        return before;
    }

    private static void RestoreString(string key, string name, string? before)
    {
        if (before is not null) Registry.SetValue(key, name, before, RegistryValueKind.String);
        else DeleteValue(key, name);
    }

    private static void DeleteValue(string key, string name)
    {
        var sub = key.Replace(@"HKEY_CURRENT_USER\", "");
        using var k = Registry.CurrentUser.OpenSubKey(sub, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string GameExe() => RocketLeague.Find()?.Exe ?? throw new InvalidOperationException("Rocket League not found");

    private static string? SetIni(string key, string value)
    {
        if (RocketLeague.IsRunning()) throw new InvalidOperationException(I18n.L("צריך לסגור את רוקט ליג קודם", "Close Rocket League first"));
        var path = Path.Combine(RocketLeague.ConfigFolder, "TASystemSettings.ini");
        var backupDir = Path.Combine(AppSettings.DataDir, "backups");
        Directory.CreateDirectory(backupDir);
        File.Copy(path, Path.Combine(backupDir, $"TASystemSettings-{DateTime.Now:yyyyMMdd-HHmmss}.ini"));
        var (text, before) = IniEdit.Set(File.ReadAllText(path), "SystemSettings", key, value);
        File.WriteAllText(path, text);
        return before;
    }

    private static void WriteWirelessAc(uint value)
    {
        if (PowerCheck.ActiveScheme() is not { } scheme) throw new InvalidOperationException("no active power plan");
        var sub = PowerCheck.WirelessSubgroup;
        var setting = PowerCheck.WirelessPowerSaving;
        Ensure(Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) == 0, "Wi-Fi power setting");
        Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme); // re-apply so the change takes effect now
    }

    /// <summary>Sets an advanced property of the Wi-Fi card (restarts it: a few seconds without Wi-Fi). Returns the old value.</summary>
    private static string? SetAdapterProperty(string keyword, string value)
    {
        var link = NetworkCheck.FindActiveLink();
        var name = (link is { IsWifi: true } ? link.Nic.Name : "Wi-Fi").Replace("'", "''");
        var before = Shell.Run("powershell", $"-NoProfile -Command \"(Get-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword {keyword}).RegistryValue[0]\"")?.Trim();
        var output = Shell.Run("powershell",
            $"-NoProfile -Command \"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword {keyword} -RegistryValue {value}; 'ok'\"", 60_000);
        if (output?.Contains("ok") != true) throw new InvalidOperationException($"could not set {keyword}");
        return string.IsNullOrEmpty(before) ? null : before;
    }
}

/// <summary>Minimal edits to Unreal-style ini files, keeping every other line exactly as it was.</summary>
internal static class IniEdit
{
    public static (string Text, string? Before) Set(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(newline).ToList();
        var inSection = false;
        var sectionEnd = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
            {
                if (inSection) { sectionEnd = i; break; }
                inSection = trimmed.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var eq = trimmed.IndexOf('=');
            if (eq > 0 && trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var before = trimmed[(eq + 1)..].Trim();
                lines[i] = $"{key}={value}";
                return (string.Join(newline, lines), before);
            }
        }
        if (!inSection && sectionEnd < 0) throw new InvalidOperationException($"[{section}] not found");
        lines.Insert(sectionEnd < 0 ? lines.Count : sectionEnd, $"{key}={value}");
        return (string.Join(newline, lines), null);
    }
}

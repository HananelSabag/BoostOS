using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            before => SetIni("UseVsync", before)),

        ["wifi-power-ac"] = new(false,
            ApplyWirelessAc,
            before => RestoreWirelessAc(before, WriteWirelessAc)),

        ["wifi-roaming"] = new(true,
            ApplyRoaming,
            before => RestoreRoaming(before, (id, value) => SetAdapterProperty(id, "RoamAggressiveness", value))),

        ["nvidia-profile"] = new(true,
            () => JsonSerializer.Serialize(Nvidia.Apply()),
            before => { if (before is not null) Nvidia.Restore(JsonSerializer.Deserialize<Nvidia.Snapshot>(before)!); }),

        ["nvidia-global-power"] = new(true,
            () => JsonSerializer.Serialize(Nvidia.ResetGlobalPower()),
            before => { if (before is not null) Nvidia.SetGlobalPower(JsonSerializer.Deserialize<Nvidia.SettingValue>(before)!); }),
    };

    /// <summary>Every fix BoostOS knows, in the order the Fixes tab lists them.</summary>
    public static IReadOnlyList<FixRef> All() => Defs.Keys.Select(Ref).ToList();

    public static bool NeedsAdmin(string id) => Defs.TryGetValue(id, out var d) && d.Admin;

    public static FixRef Ref(string id) => new(id, NeedsAdmin(id));

    private static string LogPath => Path.Combine(AppSettings.DataDir, "fixes.json");

    public static List<FixRecord> Applied()
    {
        try { return ReadLog(); }
        catch { return []; }
    }

    private static List<FixRecord> ReadLog() => File.Exists(LogPath)
        ? JsonSerializer.Deserialize<List<FixRecord>>(File.ReadAllText(LogPath)) ?? throw new InvalidOperationException("invalid fix backup")
        : [];

    private static void SaveLog(List<FixRecord> log)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        var temporary = LogPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, LogPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
                // Read before applying: a damaged or unreadable backup must not be overwritten by a new fix.
                var log = ReadLog();
                var before = def.Apply();
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
            try
            {
                var log = ReadLog();
                var record = log.FirstOrDefault(r => r.Id == id);
                if (record is null || !Defs.TryGetValue(id, out var def)) continue;
                UndoRecord(log, record, def.Undo);
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

    internal static void UndoRecord(List<FixRecord> log, FixRecord record, Action<string?> undo)
    {
        undo(record.Before);
        log.RemoveAll(r => r.Id == record.Id);
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

    private static string? SetIni(string key, string? value)
    {
        if (RocketLeague.IsRunning()) throw new InvalidOperationException(I18n.L("צריך לסגור את רוקט ליג קודם", "Close Rocket League first"));
        var path = Path.Combine(RocketLeague.ConfigFolder, "TASystemSettings.ini");
        var backupDir = Path.Combine(AppSettings.DataDir, "backups");
        Directory.CreateDirectory(backupDir);
        File.Copy(path, Path.Combine(backupDir, $"TASystemSettings-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ini"));
        var original = File.ReadAllText(path);
        var (text, before) = value is null
            ? IniEdit.Remove(original, "SystemSettings", key)
            : IniEdit.Set(original, "SystemSettings", key, value);
        File.WriteAllText(path, text);
        return before;
    }

    internal sealed record WirelessAcSnapshot([property: JsonRequired] Guid Scheme, [property: JsonRequired] uint Value);
    internal sealed record RoamingSnapshot([property: JsonRequired] Guid AdapterId, [property: JsonRequired] string Value);
    private sealed record AdapterPropertyResult(bool Applied, string? Before);

    private static string ApplyWirelessAc()
    {
        if (PowerCheck.ActiveScheme() is not { } scheme) throw new InvalidOperationException("no active power plan");
        EnsureTargetAvailable("wifi-power-ac", scheme, ReadLog());
        var sub = PowerCheck.WirelessSubgroup;
        var setting = PowerCheck.WirelessPowerSaving;
        Ensure(Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var before) == 0, "read Wi-Fi power setting");
        WriteWirelessAc(scheme, 0);
        return JsonSerializer.Serialize(new WirelessAcSnapshot(scheme, before));
    }

    internal static void RestoreWirelessAc(string? before, Action<Guid, uint> write)
    {
        var saved = ReadWirelessAcSnapshot(before);
        write(saved.Scheme, saved.Value);
    }

    private static WirelessAcSnapshot ReadWirelessAcSnapshot(string? before)
    {
        if (before?.TrimStart().StartsWith('{') == true)
        {
            var saved = JsonSerializer.Deserialize<WirelessAcSnapshot>(before);
            if (saved is null || saved.Scheme == Guid.Empty) throw new InvalidOperationException("invalid saved power plan");
            return saved;
        }
        // v0.2 records stored only a number. Never apply it to an unrelated current plan.
        throw new InvalidOperationException(I18n.L(
            "הגיבוי הישן לא מכיל מזהה תוכנית חשמל. יש לשחזר את הגדרת ה-Wi-Fi ידנית; הגיבוי נשמר.",
            "The old backup has no power plan ID. Restore the Wi-Fi power setting manually; the backup is retained."));
    }

    private static void WriteWirelessAc(Guid scheme, uint value)
    {
        var sub = PowerCheck.WirelessSubgroup;
        var setting = PowerCheck.WirelessPowerSaving;
        Ensure(Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) == 0, "Wi-Fi power setting");
        // Refresh only if this plan is still active. Undo must never select an old plan for the user.
        if (PowerCheck.ActiveScheme() == scheme)
            Ensure(Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0, "refresh Wi-Fi power setting");
    }

    private static Guid ActiveWifiId()
    {
        var link = NetworkCheck.FindActiveLink();
        if (link is not { IsWifi: true } || !Guid.TryParse(link.Nic.Id, out var id))
            throw new InvalidOperationException("no active Wi-Fi adapter");
        return id;
    }

    private static string ApplyRoaming()
    {
        var id = ActiveWifiId();
        EnsureTargetAvailable("wifi-roaming", id, ReadLog());
        var before = SetAdapterProperty(id, "RoamAggressiveness", "0");
        return JsonSerializer.Serialize(new RoamingSnapshot(id, before));
    }

    internal static void RestoreRoaming(string? before, Action<Guid, string> write)
    {
        var saved = ReadRoamingSnapshot(before);
        write(saved.AdapterId, saved.Value);
    }

    private static RoamingSnapshot ReadRoamingSnapshot(string? before)
    {
        if (before?.TrimStart().StartsWith('{') == true)
        {
            var saved = JsonSerializer.Deserialize<RoamingSnapshot>(before);
            if (saved is null || saved.AdapterId == Guid.Empty || !uint.TryParse(saved.Value, out _))
                throw new InvalidOperationException("invalid saved Wi-Fi adapter setting");
            return saved;
        }
        throw new InvalidOperationException(I18n.L(
            "הגיבוי הישן לא מכיל מזהה כרטיס רשת. יש לשחזר את הגדרת הנדידה ידנית; הגיבוי נשמר.",
            "The old backup has no network adapter ID. Restore the roaming setting manually; the backup is retained."));
    }

    internal static void EnsureTargetAvailable(string id, Guid target, IEnumerable<FixRecord> log)
    {
        foreach (var record in log.Where(r => r.Id == id))
        {
            var savedTarget = id switch
            {
                "wifi-power-ac" => ReadWirelessAcSnapshot(record.Before).Scheme,
                "wifi-roaming" => ReadRoamingSnapshot(record.Before).AdapterId,
                _ => throw new ArgumentException("fix has no saved target", nameof(id)),
            };
            if (savedTarget != target)
                throw new InvalidOperationException(I18n.L(
                    "יש לשחזר קודם את התיקון הקודם לפני החלתו על תוכנית חשמל או כרטיס רשת אחר. הגיבוי נשמר.",
                    "Undo the previous fix before applying it to a different power plan or network adapter. The backup is retained."));
        }
    }

    /// <summary>Sets an advanced property of the Wi-Fi card (restarts it: a few seconds without Wi-Fi). Returns the old value.</summary>
    private static string SetAdapterProperty(Guid adapterId, string keyword, string value) =>
        SetAdapterProperty(adapterId, keyword, value, (script, timeout) =>
            Shell.Run("powershell", $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}", timeout));

    internal static string SetAdapterProperty(Guid adapterId, string keyword, string value, Func<string, int, string?> run)
    {
        if (adapterId == Guid.Empty || !uint.TryParse(value, out _)) throw new ArgumentException("invalid Wi-Fi adapter setting");
        var escapedKeyword = keyword.Replace("'", "''");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $adapter = @(Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object { $_.InterfaceGuid -eq [guid]'{{adapterId}}' })
            if ($adapter.Count -ne 1) { throw 'Original Wi-Fi adapter not found' }
            $property = @(Get-NetAdapterAdvancedProperty -Name ([WildcardPattern]::Escape($adapter[0].Name)) -RegistryKeyword '{{escapedKeyword}}' -IncludeHidden -ErrorAction Stop | Where-Object { $_.Name -eq $adapter[0].Name -and $_.RegistryKeyword -eq '{{escapedKeyword}}' })
            if ($property.Count -ne 1 -or @($property[0].RegistryValue).Count -ne 1) { throw 'Wi-Fi property not found or ambiguous' }
            $before = [string]$property[0].RegistryValue[0]
            if ($before -notmatch '^\d+$') { throw 'Wi-Fi property value could not be read' }
            Set-NetAdapterAdvancedProperty -InputObject $property[0] -RegistryValue '{{value}}' -ErrorAction Stop | Out-Null
            ConvertTo-Json -Compress -InputObject @{ Applied = $true; Before = $before }
            """;
        var output = run(script, 60_000);
        try
        {
            var result = output is null ? null : JsonSerializer.Deserialize<AdapterPropertyResult>(output);
            if (result is { Applied: true } && uint.TryParse(result.Before, out _)) return result.Before;
        }
        catch (JsonException) { }
        throw new InvalidOperationException($"could not set {keyword}");
    }
}

/// <summary>Minimal edits to Unreal-style ini files, keeping every other line exactly as it was.</summary>
internal static class IniEdit
{
    public static (string Text, string? Before) Remove(string text, string section, string key)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(newline).ToList();
        var inSection = false;
        string? before = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
            {
                inSection = trimmed.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var eq = trimmed.IndexOf('=');
            if (!inSection || eq <= 0 || !trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            before ??= trimmed[(eq + 1)..].Trim();
            lines.RemoveAt(i--);
        }
        return (string.Join(newline, lines), before);
    }

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

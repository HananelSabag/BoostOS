using System.Text.Json;
using BoostOS.Core;
using BoostOS.Diagnostics;
using static BoostOS.Core.I18n;

namespace BoostOS.Tuning;

public sealed record VideoSetting(string Id, string Value, IReadOnlyList<string> Options, string? Recommended);

public sealed record VideoState(bool Found, bool Running, string? Resolution, bool Changed, bool Hot, IReadOnlyList<VideoSetting> Settings);

/// <summary>
/// Rocket League's video settings, read from and written to its own TASystemSettings.ini, the same file the game's
/// Video menu saves. Only options that appear in that menu by name are offered. Changes need the game closed
/// (it rewrites the file on exit), and every value replaced is kept so the whole set can be put back.
/// </summary>
internal static class RlVideo
{
    private const string Section = "SystemSettings";
    private static string IniPath => Path.Combine(RocketLeague.ConfigFolder, "TASystemSettings.ini");
    private static string BackupPath => Path.Combine(AppSettings.DataDir, "rlvideo.json");

    private static readonly string[] OnOff = ["on", "off"];

    // id → ini key, for the on/off options of the game's Video menu.
    internal static readonly Dictionary<string, string> Toggles = new()
    {
        ["vsync"] = "UseVsync",
        ["motionBlur"] = "MotionBlur",
        ["dof"] = "DepthOfField",
        ["bloom"] = "Bloom",
        ["lightShafts"] = "bAllowLightShafts",
        ["lensFlares"] = "LensFlares",
        ["ao"] = "AmbientOcclusion",
        ["dynamicShadows"] = "DynamicShadows",
        ["hqShaders"] = "bAllowHighQualityMaterials",
    };

    /// <param name="hot">The last session showed the graphics card held back by heat: suggest lighter settings.</param>
    public static VideoState Read(bool hot)
    {
        var ini = RocketLeague.ReadIni(IniPath, Section);
        var running = RocketLeague.IsRunning();
        if (ini.Count == 0) return new VideoState(false, running, null, false, hot, []);
        return new VideoState(true, running,
            ini.TryGetValue("ResX", out var x) && ini.TryGetValue("ResY", out var y) ? $"{x}×{y}" : null,
            File.Exists(BackupPath), hot, Describe(ini, hot));
    }

    internal static List<VideoSetting> Describe(IReadOnlyDictionary<string, string> ini, bool hot)
    {
        bool Flag(string key) => ini.TryGetValue(key, out var v) && v.Equals("True", StringComparison.OrdinalIgnoreCase);
        string Toggle(string id) => Flag(Toggles[id]) ? "on" : "off";
        var display = !Flag("Fullscreen") ? "windowed" : Flag("Borderless") ? "borderless" : "fullscreen";
        var detail = ini.TryGetValue("DetailMode", out var d) && d is "0" or "1" or "2" ? d : "2";
        return
        [
            new("display", display, ["fullscreen", "borderless", "windowed"], display == "windowed" ? "fullscreen" : null),
            new("vsync", Toggle("vsync"), OnOff, "off"),
            new("worldDetail", detail, ["0", "1", "2"], hot ? "1" : null),
            new("dynamicShadows", Toggle("dynamicShadows"), OnOff, hot ? "off" : null),
            new("hqShaders", Toggle("hqShaders"), OnOff, null),
            new("ao", Toggle("ao"), OnOff, "off"),
            new("bloom", Toggle("bloom"), OnOff, "off"),
            new("lightShafts", Toggle("lightShafts"), OnOff, "off"),
            new("lensFlares", Toggle("lensFlares"), OnOff, "off"),
            new("dof", Toggle("dof"), OnOff, "off"),
            new("motionBlur", Toggle("motionBlur"), OnOff, "off"),
        ];
    }

    /// <summary>The ini keys and values for one change ("display" sets two keys).</summary>
    internal static IEnumerable<(string Key, string Value)> Keys(string id, string value) => id switch
    {
        "display" => value switch
        {
            "fullscreen" => [("Fullscreen", "True"), ("Borderless", "False")],
            "borderless" => [("Fullscreen", "True"), ("Borderless", "True")],
            "windowed" => [("Fullscreen", "False"), ("Borderless", "False")],
            _ => throw new ArgumentException("unknown display mode"),
        },
        "worldDetail" when value is "0" or "1" or "2" => [("DetailMode", value)],
        _ when Toggles.TryGetValue(id, out var key) && value is "on" or "off" => [(key, value == "on" ? "True" : "False")],
        _ => throw new ArgumentException($"unknown setting {id}"),
    };

    public static void Apply(IReadOnlyDictionary<string, string> changes)
    {
        EnsureClosed();
        var text = File.ReadAllText(IniPath);
        var backup = LoadBackup();
        var dir = Path.Combine(AppSettings.DataDir, "backups");
        Directory.CreateDirectory(dir);
        File.Copy(IniPath, Path.Combine(dir, $"TASystemSettings-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ini"));
        foreach (var (id, value) in changes)
            foreach (var (key, v) in Keys(id, value))
            {
                (text, var before) = IniEdit.Set(text, Section, key, v);
                backup.TryAdd(key, before); // the oldest value wins: undo returns to before BoostOS
            }
        // Save the undo data first: a change must never exist without the way back.
        Save(backup);
        File.WriteAllText(IniPath, text);
    }

    public static void Undo()
    {
        EnsureClosed();
        var backup = LoadBackup();
        if (backup.Count == 0) return;
        var text = File.ReadAllText(IniPath);
        foreach (var (key, before) in backup)
            text = before is null ? IniEdit.Remove(text, Section, key).Text : IniEdit.Set(text, Section, key, before).Text;
        File.WriteAllText(IniPath, text);
        File.Delete(BackupPath);
    }

    private static void EnsureClosed()
    {
        if (RocketLeague.IsRunning()) throw new InvalidOperationException(L("צריך לסגור את רוקט ליג קודם: המשחק כותב את הקובץ מחדש כשהוא נסגר.", "Close Rocket League first: the game rewrites this file when it exits."));
        if (!File.Exists(IniPath)) throw new InvalidOperationException(L("קובץ ההגדרות של המשחק לא נמצא.", "The game's settings file was not found."));
    }

    private static Dictionary<string, string?> LoadBackup()
    {
        try { return File.Exists(BackupPath) ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(BackupPath)) ?? [] : []; }
        catch { throw new InvalidOperationException(L("קובץ הגיבוי של הגדרות הווידאו פגום, ולכן לא שיניתי כלום.", "The video settings backup is damaged, so nothing was changed.")); }
    }

    private static void Save(Dictionary<string, string?> backup)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
    }
}

using System.Globalization;
using BoostOS.Core;
using BoostOS.Tuning;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class GpuCheck
{
    private const string Fields = "name,driver_version,memory.total,pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max," +
                                  "temperature.gpu,power.draw,enforced.power.limit,power.max_limit,clocks.max.graphics,pstate,utilization.gpu";

    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();

        foreach (var name in facts.GpuNames)
            checks.Add(new(L("כרטיס גרפי", "Graphics card"), name, Info));

        var smi = FindNvidiaSmi();
        var line = smi is null ? null : Shell.Run(smi, $"--query-gpu={Fields} --format=csv,noheader,nounits")?.Split('\n')[0];
        if (line is null)
        {
            if (facts.HasNvidia)
                checks.Add(new(L("נתוני NVIDIA", "NVIDIA data"), L("לא זמינים", "Unavailable"), Warn,
                    L("לא מצאתי את nvidia-smi. בדרך כלל זה אומר שהדרייבר לא הותקן עד הסוף.", "nvidia-smi is missing. That usually means the driver didn't install fully.")));
            return new Section("gpu", L("כרטיס מסך", "Graphics"), "gpu", checks);
        }

        var v = line.Split(',').Select(s => s.Trim()).ToArray();
        string F(int i) => i < v.Length ? v[i] : "";
        double? N(int i) => double.TryParse(F(i), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        checks.Add(new(L("דרייבר NVIDIA", "NVIDIA driver"), F(1), Info,
            L("אפשר לבדוק אם יש גרסה חדשה באפליקציית NVIDIA. גרסה חדשה לא תמיד עדיפה: אם המשחק יציב, אין חובה לעדכן.",
              "The NVIDIA app shows whether a newer driver exists. Newer isn't always better: if the game is stable, there's no need to update.")));
        if (N(2) is { } vram) checks.Add(new(L("זיכרון גרפי", "Video memory"), $"{vram / 1024:0.#} GB", Info));

        AddProfileCheck(checks);
        AddRestingCheck(checks, F(12), N(13), N(8), N(7));

        if (N(5) is { } width && N(6) is { } maxWidth)
            checks.Add(width >= maxWidth
                ? new(L("חיבור PCIe", "PCIe link"), $"x{width} · Gen {F(3)} · {L("מלא", "full")}", Ok)
                : new(L("חיבור PCIe", "PCIe link"), $"x{width} · {L($"מתוך x{maxWidth} אפשריים", $"of x{maxWidth} possible")}", Warn,
                    L("הכרטיס מחובר בפחות נתיבים ממה שהוא תומך. במחשב נייח כדאי לבדוק שהוא בחריץ העליון.", "The card runs on fewer lanes than it supports. On a desktop, check it sits in the top slot.")));

        if (N(7) is { } temp)
        {
            var t = L("טמפרטורה עכשיו", "Temperature now");
            checks.Add(temp switch
            {
                < 70 => new(t, $"{temp:0}°C", Ok),
                < 85 => new(t, $"{temp:0}°C", Warn, L("חם יחסית למצב בלי משחק. כדאי לנקות אבק מהמאווררים ולהשאיר מרווח מתחת ללפטופ.", "Warm for an idle GPU. Clean the fans and leave room under the laptop.")),
                _ => new(t, $"{temp:0}°C", Bad, L("חם מדי. הכרטיס יוריד מהירות (throttling) באמצע משחק.", "Too hot. The card will throttle mid-match.")),
            });
        }

        if (N(9) is { } limit && N(10) is { } maxLimit)
            checks.Add(limit + 1 < maxLimit
                ? new(L("הספק מקסימלי (TGP)", "Power limit (TGP)"), $"{limit:0}W · {L($"מתוך {maxLimit:0}W אפשריים", $"of {maxLimit:0}W possible")}", Info,
                    facts.IsLaptop
                        ? L("במנוחה הכרטיס מוגבל להספק נמוך מהמקסימום שלו. בלפטופים ההגבלה עולה תחת עומס (Dynamic Boost) ובמצב Turbo/Performance של תוכנת היצרן. דוח המשחק מראה כמה מהזמן הכרטיס היה מוגבל.",
                            "At idle the card sits below its maximum. On laptops the limit rises under load (Dynamic Boost) and in the maker's Turbo/Performance mode. The match report shows how often the card was held back.")
                        : L("מגבלת ההספק נמוכה מהמקסימום של הכרטיס.", "The power limit is below the card's maximum."))
                : new(L("הספק מקסימלי (TGP)", "Power limit (TGP)"), $"{limit:0}W · {L("מקסימום", "maximum")}", Ok));

        if (N(11) is { } clock) checks.Add(new(L("מהירות ליבה מקסימלית", "Max core clock"), $"{clock:0} MHz", Info));

        return new Section("gpu", L("כרטיס מסך", "Graphics"), "gpu", checks);
    }

    /// <summary>The Rocket League profile in the NVIDIA driver: the two settings that matter for input lag and steady clocks.</summary>
    private static void AddProfileCheck(List<Check> checks)
    {
        Nvidia.Snapshot? snap;
        try { snap = Nvidia.Read(); }
        catch (Exception ex)
        {
            checks.Add(new(L("פרופיל NVIDIA לרוקט ליג", "NVIDIA profile for Rocket League"), L("לא הצלחתי לקרוא", "Couldn't read it"), Info, ex.Message));
            return;
        }
        if (snap is null) return;

        var values = string.Join(" · ", snap.Values.Select(x =>
            (x.Id == Nvidia.PowerManagementId ? L("ניהול חשמל: ", "Power: ") : L("השהיה נמוכה: ", "Low latency: ")) + Nvidia.Describe(x, English)));
        var title = L("פרופיל NVIDIA לרוקט ליג", "NVIDIA profile for Rocket League");
        checks.Add(Nvidia.IsRecommended(snap)
            ? new(title, values, Ok, L("ביצועים מקסימליים ו-Low Latency פעילים למשחק.", "Maximum performance and Low Latency are on for the game."), Fixes.Ref("nvidia-profile"))
            : new(title, values, Warn,
                L("שני דברים בדרייבר עוזרים לרוקט ליג: Prefer maximum performance מחזיק את הכרטיס במהירות מלאה בלי ירידות באמצע משחק, ו-Low Latency Mode מקצר את התור של הפריימים, וכך הלחיצה מגיעה למסך מהר יותר. זה רק פרופיל בדרייבר, כמו בלוח הבקרה של NVIDIA, ולא נוגע במשחק.",
                  "Two driver settings help Rocket League: Prefer maximum performance keeps the GPU at full clocks with no dips mid-match, and Low Latency Mode shortens the frame queue so your input reaches the screen sooner. It's only a driver profile, the same as the NVIDIA Control Panel, and doesn't touch the game."),
                Fixes.Ref("nvidia-profile")));
    }

    /// <summary>
    /// The global NVIDIA profile applies to every app without its own profile, the Windows desktop included.
    /// "Prefer maximum performance" there keeps the card at full clocks all day: heat with nothing to show for it.
    /// </summary>
    private static void AddRestingCheck(List<Check> checks, string pstate, double? util, double? watts, double? temp)
    {
        Nvidia.SettingValue? global;
        try { global = Nvidia.ReadGlobalPower(); }
        catch { return; }
        if (global is null) return;

        var awake = pstate == "P0" && util is < 15;
        var now = string.Join(" · ", new[] { pstate, watts is { } w ? $"{w:0} W" : null, temp is { } t ? $"{t:0}°C" : null }.Where(s => s is not null && s != ""));
        var title = L("כרטיס המסך במנוחה", "GPU at rest");
        if (global.Value == Nvidia.PreferMaxPerformance)
            checks.Add(new(title, $"{L("לא נח", "Never rests")} · {now}", Warn,
                L("בהגדרה הגלובלית של NVIDIA מוגדר Prefer maximum performance, ולכן הכרטיס רץ במהירות מלאה גם בשולחן העבודה. בלפטופ זה חום מיותר שמגיע למשחק כשהמחשב כבר חם. התיקון מחזיר רק את ההגדרה הגלובלית לברירת המחדל; הפרופיל של רוקט ליג נשאר על ביצועים מקסימליים.",
                  "NVIDIA's global profile is set to Prefer maximum performance, so the card runs at full clocks even on the desktop. On a laptop that is wasted heat, and the match starts on a hot machine. The fix resets only the global setting to the driver default; Rocket League's own profile stays at maximum performance."),
                Fixes.Ref("nvidia-global-power")));
        else if (awake)
            checks.Add(new(title, $"{L("ער", "Awake")} · {now}", Info,
                L("הכרטיס לא ירד למצב מנוחה בזמן הסריקה. לפעמים תוכנה פתוחה או מסך בקצב רענון גבוה מחזיקים אותו ער.",
                  "The card didn't drop to its resting state during the scan. An open app or a high-refresh display sometimes keeps it awake."),
                Fixes.Ref("nvidia-global-power")));
        else
            checks.Add(new(title, $"{L("נח", "Resting")} · {now}", Ok, null, Fixes.Ref("nvidia-global-power")));
    }

    private static string? FindNvidiaSmi()
    {
        string[] candidates =
        [
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}

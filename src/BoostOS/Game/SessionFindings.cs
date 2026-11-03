using System.Text.Json;
using System.Text.Json.Nodes;
using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Network;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Game;

/// <summary>One line about a session: bad, warn, info or ok, the words, and what to do (only when there's something to do).</summary>
public sealed record Finding(string Status, string Text, string? Tip = null);

public sealed record SessionsSummary(Finding Main, IReadOnlyList<string> Good);

/// <summary>
/// What recorded sessions mean, in plain words, from what was measured. One set of rules serves the session list,
/// the summary across sessions and the scan's "From your games" area, so the screens never disagree. Advice comes
/// from each session's own facts (its Wi-Fi link, Bluetooth, fans, power limit, power plan): nothing is said to
/// everyone, and nothing is suggested that is already the case.
/// </summary>
internal static class SessionFindings
{
    private static readonly HashSet<string> SystemNames =
    [
        "system", "dwm", "svchost", "audiodg", "csrss", "lsass", "services", "wininit", "smss", "fontdrvhost", "boostos",
        "msedgewebview2", "nvidia-smi", "conhost", "interrupts", "registry", "memcompression", "searchindexer", "msmpeng",
    ];

    // ---- Numbers every screen shares ----

    /// <summary>Share of the session the card was slowed by heat while maxed out: the only heat that costs frames.</summary>
    public static double? HeatCost(MatchReport m)
    {
        if (m.GpuThermalBusyPct is { } pct) return pct;
        var points = m.Timeline?.Where(p => p.GpuUtil is not null && p.ClockReasons is not null).ToList();
        if (points is null || points.Count == 0) return null;
        return Math.Round(100d * points.Count(p => p.GpuUtil >= SessionSummary.BusyUtil && (p.ClockReasons!.Value & SessionSummary.ThermalMask) != 0) / points.Count, 1);
    }

    public static bool Held(MatchReport m) => m.GpuLowClockPct >= 30;

    private static double? SpikesPerHour(MatchReport m) =>
        m.Router is { MedianMs: not null } r && m.Minutes >= 1 ? r.Spikes / m.Minutes * 60 : null;

    private static bool WifiTrouble(MatchReport m) =>
        m.Router is { MedianMs: not null } r && (r.LossPct >= 1 || SpikesPerHour(m) >= 6 && r.Spikes >= 2);

    private static BackgroundApp? BusyApp(MatchReport m) =>
        m.Background?.Where(a => !SystemNames.Contains(a.Name)).FirstOrDefault(a => a.CpuPct >= 2);

    private static bool PlanIsFull(string? plan) => plan is "boostos" or "high" or "balanced-best";

    /// <summary>Wi-Fi during the recent games: spikes an hour and loss, or null without recordings that pinged the router.</summary>
    public static (double PerHour, double Loss, double MaxMs)? WifiEvidence(IReadOnlyList<MatchReport> all)
    {
        var list = all.Where(m => m.Minutes >= 1 && m.Router?.MedianMs is not null).Take(10).ToList();
        var minutes = list.Sum(m => m.Minutes);
        if (minutes < 10) return null;
        return (list.Sum(m => m.Router!.Spikes) / minutes * 60, list.Average(m => m.Router!.LossPct), list.Max(m => m.Router!.MaxMs ?? 0));
    }

    // ---- One session ----

    public static List<Finding> For(MatchReport m, NetFacts? now = null)
    {
        var o = new List<Finding>();
        var cost = HeatCost(m);
        var temp = m.GpuTempMax;

        if (m.OnBatteryPct >= 5)
            o.Add(new("bad", L($"שיחקת על סוללה ב-{m.OnBatteryPct:0}% מהזמן: על סוללה הלפטופ חונק את המעבד ואת הכרטיס.",
                $"You played on battery {m.OnBatteryPct:0}% of the time: on battery the laptop chokes the CPU and GPU."),
                L("למשחק תמיד מחברים את המטען המקורי.", "Always plug in the original charger to play.")));

        if (cost >= 5)
            o.Add(new(cost >= 15 ? "bad" : "warn",
                L($"הכרטיס עבד במלוא הכוח והאט מחום ב-{cost:0}% מהזמן (שיא {temp:0}°C): כאן החום עלה בפריימים.",
                  $"The GPU was maxed out and slowed by heat {cost:0}% of the time (peak {temp:0}°C): here heat cost frames."), HeatTip(m)));
        else if (temp >= 92)
            o.Add(new("warn", L($"הכרטיס הגיע ל-{temp:0}°C, מעל הגבול הרגיל שלו.", $"The GPU reached {temp:0}°C, above its usual limit."), HeatTip(m)));
        else if (m.GpuThermalLimitedPct >= 15)
            o.Add(new("ok", m.GpuHotAfterSec is { } after
                ? L($"הכרטיס הגיע ל-85°C אחרי {Duration(after)} והחזיק בגבול החום שלו ({temp:0}°C) בהורדה קטנה של הבוסט. תמיד נשאר לו כוח פנוי, אז החום לא עלה בפריימים.",
                    $"The GPU reached 85°C after {Duration(after)} and held its heat limit ({temp:0}°C) by trimming its boost. It always had power to spare, so heat cost no frames.")
                : L($"הכרטיס הגיע ל-{temp:0}°C, גבול החום שלו, והחזיק שם בהורדה קטנה של הבוסט. תמיד נשאר לו כוח פנוי, אז החום לא עלה בפריימים.",
                    $"The GPU reached {temp:0}°C, its heat limit, and held there by trimming its boost. It always had power to spare, so heat cost no frames.")));
        else if (temp is not null)
            o.Add(new("ok", L($"כרטיס המסך נשאר קריר יחסית: עד {temp:0}°C.", $"The GPU stayed fairly cool: up to {temp:0}°C.")));

        if (Held(m))
            o.Add(new("warn", L($"הכרטיס נשאר על כ-{m.GpuClock:0} MHz בזמן שעבד {m.GpuUtil:0}% בממוצע: משהו החזיק את המהירות שלו, לא חום.",
                $"The GPU stayed at about {m.GpuClock:0} MHz while {m.GpuUtil:0}% busy on average: something held its speed down, not heat."), HeldTip([m])));

        if (m.GpuPowerLimitedPct >= 30)
            o.Add(new("info", L($"הכרטיס הגיע לגבול ההספק שלו ב-{m.GpuPowerLimitedPct:0}% מהזמן.", $"The GPU hit its power limit {m.GpuPowerLimitedPct:0}% of the time."),
                PowerTip(m)));

        if (m.GpuUtil >= 90 || m.GpuBusyPct >= 20)
            o.Add(new("warn", m.GpuUtil >= 90
                    ? L($"כרטיס המסך עבד כמעט במלואו ({m.GpuUtil:0}%): הוא זה שהגביל.", $"The GPU was almost fully busy ({m.GpuUtil:0}%): it was the limit.")
                    : L($"כרטיס המסך עבד במלוא הכוח ב-{m.GpuBusyPct:0}% מהזמן: ברגעים האלה הוא זה שהגביל.", $"The GPU was maxed out {m.GpuBusyPct:0}% of the time: in those moments it was the limit."),
                L("הסימונים ★ בהגדרות הווידאו של רוקט ליג מורידים ממנו עבודה בלי לפגוע במה שחשוב במשחק.", "The ★ choices in Rocket League's video settings take work off it without hurting what matters in a match.")));
        else if (m.GpuUtil < 70 && !Held(m))
            o.Add(new("ok", L($"לכרטיס היה מקום פנוי: עבד בממוצע {m.GpuUtil:0}%.", $"The GPU had room to spare: {m.GpuUtil:0}% busy on average.")));

        var busyApp = BusyApp(m);
        if (m.CpuSamples != 0 && m.BusiestCoreAvg >= 85)
            o.Add(new("warn", L($"ליבה אחת של המעבד עבדה בממוצע {m.BusiestCoreAvg:0}%: כנראה המעבד הגביל.", $"One CPU core averaged {m.BusiestCoreAvg:0}%: the CPU probably was the limit."),
                busyApp is not null ? L($"{AppName(busyApp.Name)} עבד ברקע ({busyApp.CpuPct:0.#}% מהמעבד): לסגור אותו לפני משחק.", $"{AppName(busyApp.Name)} was busy in the background ({busyApp.CpuPct:0.#}% of the CPU): close it before playing.")
                : m.PowerPlan is { } plan && !PlanIsFull(plan) ? L("תוכנית החשמל לא הייתה על ביצועים מלאים. באבחון → תיקונים יש את תוכנית BoostOS.", "The power plan wasn't on full performance. Diagnostics → Fixes has the BoostOS plan.")
                : L("הרקע היה שקט והחשמל כבר על ביצועים מלאים, אז זה המעבד עצמו. תקרת FPS קצת נמוכה יותר תיתן פריימים יציבים יותר.", "The background was quiet and power was already on full performance, so it's the CPU itself. A slightly lower FPS cap gives steadier frames.")));

        if (m.CpuMhzAvg is { } avg && m.CpuMhzLow is { } low)
            o.Add(low < avg * 0.75
                ? new("warn", L($"כשהמשחק השתמש במעבד, הוא ירד לפעמים ל-{low / 1000:0.0} GHz (ממוצע {avg / 1000:0.0}).", $"While the game was using it, the CPU sometimes dropped to {low / 1000:0.0} GHz (average {avg / 1000:0.0})."),
                    CpuDipTip(m))
                : new("ok", m.CpuTempMax is { } ct
                    ? L($"המעבד החזיק מהירות יציבה: {avg / 1000:0.0} GHz בממוצע, עד {ct:0}°C.", $"The CPU held a steady speed: {avg / 1000:0.0} GHz on average, up to {ct:0}°C.")
                    : L($"המעבד החזיק מהירות יציבה: {avg / 1000:0.0} GHz בממוצע.", $"The CPU held a steady speed: {avg / 1000:0.0} GHz on average.")));

        if (m.CpuTempMax >= 95)
            o.Add(new("warn", L($"המעבד הגיע ל-{m.CpuTempMax:0}°C.", $"The CPU reached {m.CpuTempMax:0}°C."),
                L("בלפטופ המעבד והכרטיס חולקים את צינורות הקירור, אז החום שלו מחמם גם את הכרטיס.", "On a laptop the CPU and GPU share heat pipes, so its heat warms the GPU too.")));

        var facts = m.Net ?? now;
        var roamed = m.Net?.Wifi is { Bssid: { } bssidStart } && m.WifiAtEnd is { Bssid: { } bssidEnd } && bssidStart != bssidEnd;
        if (m.Router is { MedianMs: { } median } r)
        {
            if (WifiTrouble(m))
                o.Add(new("warn", L($"ב-Wi-Fi היו {r.Spikes} קפיצות פינג (בערך {SpikesPerHour(m):0} בשעה, הכי גבוה {r.MaxMs:0} ms) ו-{r.LossPct}% אובדן. פינג רגיל לנתב: {median:0} ms.",
                    $"Wi-Fi had {r.Spikes} ping spikes (about {SpikesPerHour(m):0} an hour, highest {r.MaxMs:0} ms) and {r.LossPct}% loss. Usual ping to the router: {median:0} ms."),
                    WifiTip(facts, trouble: true, drops: r.LossPct >= 1 || roamed)));
            else
                o.Add(new("ok", L($"ה-Wi-Fi היה יציב: {median:0} ms לנתב, הכי גבוה {r.MaxMs:0} ms.", $"Wi-Fi was steady: {median:0} ms to the router, highest {r.MaxMs:0} ms.")));
        }
        if (roamed && m.Net?.Wifi is { } wStart && m.WifiAtEnd is { } wEnd)
            o.Add(new("info", L($"באמצע המשחק ה-Wi-Fi עבר לנקודת גישה אחרת (ערוץ {wStart.Channel} → {wEnd.Channel}). כל מעבר כזה הוא ניתוק קצר.",
                $"Mid-game Wi-Fi moved to another access point (channel {wStart.Channel} → {wEnd.Channel}). Every move is a short drop.")));
        else if (facts is { } f && WifiAdvice.For(f, trouble: false).Count > 0 && m.Net is not null)
            o.Add(new("warn", L("החיבור עצמו חלש מהרגיל.", "The link itself is weaker than it should be."), WifiTip(f, trouble: false)));

        if (m.Internet is { LossPct: >= 2 } net)
            o.Add(new("warn", L($"בדרך לאינטרנט אבד {net.LossPct}% מהפינגים. אם הנתב תקין, זה אצל הספק.", $"{net.LossPct}% of pings were lost on the way to the internet. If the router is fine, it's on the provider's side.")));
        if (m.Wifi is { QualityMin: < 50 } wifi)
            o.Add(new("warn", L($"עוצמת ה-Wi-Fi ירדה עד {wifi.QualityMin}%.", $"The Wi-Fi signal dropped to {wifi.QualityMin}%.")));
        if (m.RamMaxPct >= 90)
            o.Add(new("warn", L($"הזיכרון התמלא עד {m.RamMaxPct:0}%.", $"Memory filled up to {m.RamMaxPct:0}%."), L("סגור דפדפן ותוכנות כבדות לפני משחק.", "Close the browser and heavy apps before playing.")));

        if (busyApp is not null)
        {
            var quieted = m.GameModeActions?.Any(a => a.Contains(AppName(busyApp.Name), StringComparison.OrdinalIgnoreCase)) == true;
            o.Add(new("warn", L($"{AppName(busyApp.Name)} השתמש ב-{busyApp.CpuPct:0.#}% מהמעבד בזמן המשחק.", $"{AppName(busyApp.Name)} used {busyApp.CpuPct:0.#}% of the CPU while you played."),
                quieted ? L("מצב משחק כבר הוריד אותו לעדיפות הכי נמוכה. כדי לפנות את המעבד לגמרי, סגור אותו לפני משחק.", "Game Mode already dropped it to the lowest priority. To free the CPU completely, close it before you play.")
                        : L("אם אין בו צורך בזמן משחק, סגור אותו לפני. מעכשיו מצב משחק מוריד לבד תוכנות שעובדות קשה ברקע.", "If you don't need it in a match, close it first. Game Mode now lowers busy background apps by itself.")));
        }
        else if (m.Background is not null)
            o.Add(new("ok", L("שום תוכנה ברקע לא העמיסה על המעבד.", "No background app put load on the CPU.")));

        if (m.GameModeActions is { Count: > 0 } done)
            o.Add(new("ok", L($"מצב משחק: {string.Join(" · ", done)}", $"Game Mode: {string.Join(" · ", done)}")));

        if (m.SchemaVersion < 3 && o.All(x => x.Status == "ok"))
            o.Add(new("info", L("דוח ישן: מהמשחק הבא יימדדו גם מהירות המעבד, פינג, Wi-Fi ותוכנות רקע.", "An older report: from the next session CPU speed, ping, Wi-Fi and background apps are measured too.")));

        var rank = new Dictionary<string, int> { ["bad"] = 0, ["warn"] = 1, ["info"] = 2, ["ok"] = 3 };
        return [.. o.OrderBy(x => rank[x.Status])];
    }

    // ---- Across sessions ----

    public static SessionsSummary Summarize(IReadOnlyList<MatchReport> list, NetFacts? now = null)
    {
        var played = list.Where(m => m.Minutes >= 1).ToList();
        var costs = played.Select(HeatCost).OfType<double>().ToList();
        var cost = costs.Count > 0 ? costs.Average() : (double?)null;
        var peak = played.Select(m => m.GpuTempMax).OfType<double>().DefaultIfEmpty(0).Max();
        var held = played.Where(Held).ToList();
        var battery = played.Where(m => m.OnBatteryPct >= 5).ToList();
        var withRouter = played.Where(m => m.Router?.MedianMs is not null).ToList();
        var minutes = withRouter.Sum(m => m.Minutes);
        double? perHour = minutes >= 1 ? withRouter.Sum(m => m.Router!.Spikes) / minutes * 60 : null;
        var thermal = played.Select(m => m.GpuThermalLimitedPct).OfType<double>().DefaultIfEmpty().Average();

        Finding main =
            cost >= 5 ? new("bad", L($"הבעיה העיקרית: חום. הכרטיס עבד במלוא הכוח והאט מחום ב-{cost:0}% מהזמן בממוצע (שיא {peak:0}°C): שם החום עלה בפריימים.",
                $"The main problem: heat. The GPU was maxed out and slowed by heat {cost:0}% of the time on average (peak {peak:0}°C): that's where heat cost frames."), HeatTip(played.OrderByDescending(m => HeatCost(m) ?? 0).First()))
            : battery.Count > 0 ? new("bad", L($"ב-{battery.Count} מתוך {played.Count} משחקים שיחקת על סוללה, ואז הלפטופ חונק את המעבד והכרטיס.", $"In {battery.Count} of {played.Count} sessions you played on battery, which chokes the CPU and GPU."),
                L("למשחק תמיד מחברים את המטען המקורי.", "Always plug in the original charger to play."))
            : held.Count > 0 ? new("warn", L($"הבעיה העיקרית: ב-{held.Count} מתוך {played.Count} משחקים הכרטיס נתקע על כ-{held.Average(m => m.GpuClock ?? 0):0} MHz בזמן שעבד קשה. זה לא חום: משהו החזיק את המהירות שלו.",
                $"The main problem: in {held.Count} of {played.Count} sessions the GPU stayed at about {held.Average(m => m.GpuClock ?? 0):0} MHz while working hard. Not heat: something held its speed down."), HeldTip(held))
            : perHour > 12 ? new("warn", L($"הבעיה העיקרית: קפיצות פינג ב-Wi-Fi, בערך {perHour:0} בשעה.", $"The main problem: Wi-Fi ping spikes, about {perHour:0} an hour."),
                WifiTip(withRouter.FirstOrDefault(m => m.Net is not null)?.Net ?? now, trouble: true))
            : new("ok", L("לא נמצאה בעיה חוזרת. המחשב מחזיק יפה.", "No repeating problem found. The PC holds up well."));

        var good = new List<string>();
        if (thermal >= 15 && (cost ?? 0) < 5) good.Add(L($"חום: הכרטיס מגיע עד {peak:0}°C ותמיד נשאר לו כוח פנוי", $"Heat: the GPU peaks at {peak:0}°C and always has power to spare"));
        if (withRouter.Count > 0 && perHour <= 12)
            good.Add(L($"Wi-Fi: {withRouter.Average(m => m.Router!.MedianMs!.Value):0} ms לנתב, {perHour:0} קפיצות בשעה", $"Wi-Fi: {withRouter.Average(m => m.Router!.MedianMs!.Value):0} ms to the router, {perHour:0} spikes an hour"));
        var clocks = played.Select(m => m.CpuMhzAvg).OfType<double>().ToList();
        if (clocks.Count > 0) good.Add(L($"המעבד יציב: {clocks.Average() / 1000:0.0} GHz", $"CPU steady: {clocks.Average() / 1000:0.0} GHz"));
        if (played.Any(m => m.Background is not null) && !played.Any(m => BusyApp(m) is not null)) good.Add(L("הרקע שקט", "Background quiet"));
        return new SessionsSummary(main, good);
    }

    /// <summary>The scan's "From your games" area: the same judgments, as lines that count toward the readiness score.</summary>
    public static Section PlaySection(IReadOnlyList<MatchReport> all, NetFacts? now)
    {
        var list = all.Where(m => m.Minutes >= 1).Take(10).ToList();
        var title = L("מהמשחקים שלך", "From your games");
        if (list.Count == 0)
            return new Section("play", title, "chart", [new(L("משחקים שנמדדו", "Recorded games"), L("עוד אין", "None yet"), Info,
                L("שחק משחק אחד: BoostOS מקליט לבד ומוסיף כאן מה באמת קרה בזמן המשחק.", "Play one game: BoostOS records by itself and adds here what really happened while you played."))]);

        var checks = new List<Check>();
        var hours = list.Sum(m => m.Minutes) / 60;
        checks.Add(new(L("משחקים שנמדדו", "Recorded games"), L($"{list.Count} אחרונים · {hours:0.#} שעות", $"last {list.Count} · {hours:0.#} hours"), Info));

        var costs = list.Select(HeatCost).OfType<double>().ToList();
        var cost = costs.Count > 0 ? costs.Average() : 0;
        var peak = list.Select(m => m.GpuTempMax).OfType<double>().DefaultIfEmpty(0).Max();
        // How fast it heats up when nothing holds the clock down (a held card heats slowly and would skew the time).
        var hotAfter = list.Where(m => !Held(m)).Select(m => m.GpuHotAfterSec).OfType<double>().Order().ToList();
        var heatTitle = L("חום בזמן משחק", "Heat while playing");
        var heatValue = L($"שיא {peak:0}°C · עלה בפריימים {cost:0.#}% מהזמן", $"peak {peak:0}°C · cost frames {cost:0.#}% of the time");
        checks.Add(cost >= 5
            ? new(heatTitle, heatValue, cost >= 15 ? Bad : Warn, HeatTip(list.OrderByDescending(m => HeatCost(m) ?? 0).First()))
            : new(heatTitle, heatValue, Ok, hotAfter.Count > 0
                ? L($"הכרטיס מגיע ל-85°C אחרי {Duration(hotAfter[hotAfter.Count / 2])} בדרך כלל ומחזיק שם בהורדה קטנה של הבוסט, עם כוח פנוי. ככה כרטיס של לפטופ בנוי לעבוד.",
                    $"The GPU usually reaches 85°C after {Duration(hotAfter[hotAfter.Count / 2])} and holds there by trimming its boost, with power to spare. That's how a laptop GPU is built to run.")
                : null));

        var held = list.Where(Held).ToList();
        var speedTitle = L("מהירות הכרטיס", "GPU speed");
        var clocks = list.Where(m => !Held(m)).Select(m => m.GpuClock).OfType<double>().ToList();
        checks.Add(held.Count > 0
            ? new(speedTitle, L($"נתקע על ~{held.Average(m => m.GpuClock ?? 0):0} MHz ב-{held.Count} מתוך {list.Count}", $"stuck at ~{held.Average(m => m.GpuClock ?? 0):0} MHz in {held.Count} of {list.Count}"), Warn, HeldTip(held))
            : new(speedTitle, clocks.Count > 0 ? $"{clocks.Average():0} MHz {L("בממוצע", "average")}" : "—", Ok));

        var cpu = list.Select(m => m.CpuMhzAvg).OfType<double>().ToList();
        var dips = list.Where(m => m.CpuMhzLow < m.CpuMhzAvg * 0.75).ToList();
        var cpuTemp = list.Select(m => m.CpuTempMax).OfType<double>().ToList();
        if (cpu.Count > 0)
            checks.Add(dips.Count > 0
                ? new(L("מעבד בזמן משחק", "CPU while playing"), L($"ירד מתחת ל-75% מהמהירות ב-{dips.Count} משחקים", $"fell below 75% of its speed in {dips.Count} sessions"), Warn, CpuDipTip(dips[0]))
                : new(L("מעבד בזמן משחק", "CPU while playing"), $"{cpu.Average() / 1000:0.0} GHz {L("יציב", "steady")}{(cpuTemp.Count > 0 ? $" · {L("עד", "up to")} {cpuTemp.Max():0}°C" : "")}", Ok));

        var fans = list.Select(m => m.FanDutyAvg).OfType<double>().ToList();
        if (fans.Count > 0)
            checks.Add(new(L("מאווררים בזמן משחק", "Fans while playing"), $"{fans.Average():0}% {L("בממוצע", "on average")}", fans.Average() < 75 && peak >= 85 ? Warn : Ok,
                fans.Average() < 75 && peak >= 85
                    ? L("הכרטיס הגיע לגבול החום והמאווררים לא עבדו במלואם: ב-Control Center בחר Performance ומאווררים על Maximum.", "The GPU reached its heat limit while the fans weren't at full speed: choose Performance and fans on Maximum in Control Center.")
                    : null));

        var withRouter = list.Where(m => m.Router?.MedianMs is not null).ToList();
        if (withRouter.Count > 0)
        {
            var minutes = withRouter.Sum(m => m.Minutes);
            var perHour = withRouter.Sum(m => m.Router!.Spikes) / Math.Max(1, minutes) * 60;
            var loss = withRouter.Average(m => m.Router!.LossPct);
            var trouble = perHour >= 6 || loss >= 1;
            var facts = withRouter.FirstOrDefault(m => m.Net is not null)?.Net ?? now;
            checks.Add(new(L("Wi-Fi בזמן משחק", "Wi-Fi while playing"),
                L($"{perHour:0} קפיצות בשעה · הכי גבוה {withRouter.Max(m => m.Router!.MaxMs ?? 0):0} ms · אובדן {loss:0.#}%", $"{perHour:0} spikes an hour · highest {withRouter.Max(m => m.Router!.MaxMs ?? 0):0} ms · {loss:0.#}% loss"),
                perHour > 12 || loss >= 2 ? Warn : Ok, trouble ? WifiTip(facts, trouble: true, drops: loss >= 0.5, count: 1) : null));
        }

        var busy = list.Select(BusyApp).OfType<BackgroundApp>().GroupBy(a => a.Name).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (list.Any(m => m.Background is not null))
            checks.Add(busy is null
                ? new(L("רקע בזמן משחק", "Background while playing"), L("שקט", "Quiet"), Ok)
                : new(L("רקע בזמן משחק", "Background while playing"), L($"{AppName(busy.Key)} עבד ב-{busy.Count()} משחקים", $"{AppName(busy.Key)} was busy in {busy.Count()} sessions"), Warn,
                    L("מעכשיו מצב משחק מוריד לבד תוכנות שעובדות קשה ברקע. כדי לפנות את המעבד לגמרי, סגור אותה לפני משחק.", "Game Mode now lowers busy background apps by itself. To free the CPU completely, close it before playing.")));

        var battery = list.Count(m => m.OnBatteryPct >= 5);
        if (battery > 0)
            checks.Add(new(L("סוללה בזמן משחק", "Battery while playing"), L($"{battery} משחקים על סוללה", $"{battery} sessions on battery"), Bad,
                L("על סוללה הלפטופ חונק את המעבד ואת הכרטיס. למשחק תמיד מחברים מטען.", "On battery the laptop chokes the CPU and GPU. Always plug in to play.")));

        return new Section("play", title, "chart", checks);
    }

    // ---- Advice, from each session's own facts ----

    private static string HeatTip(MatchReport m)
    {
        var parts = new List<string>();
        if (PowerCheck.GpuBelowFullPower(m.GpuPowerLimitAvg, m.GpuPowerDefault, m.GpuPowerMax) == true)
            parts.Add(L($"הכרטיס קיבל רק {m.GpuPowerLimitAvg:0}W מתוך {m.GpuPowerMax:0}W, כלומר הלפטופ לא היה במצב Performance ב-Control Center.",
                $"The GPU only got {m.GpuPowerLimitAvg:0} of {m.GpuPowerMax:0} W, so the laptop wasn't in Performance mode in Control Center."));
        if (m.FanDutyAvg is { } fan)
            parts.Add(fan < 75
                ? L($"המאווררים עבדו בממוצע רק {fan:0}%: ב-Control Center יש עוד מקום (Performance, מאווררים על Maximum).", $"The fans averaged only {fan:0}%: Control Center has room left (Performance, fans on Maximum).")
                : L($"המאווררים כבר עבדו {fan:0}% בממוצע, כמעט מקסימום, אז הקירור כבר במלוא הכוח.", $"The fans already averaged {fan:0}%, close to maximum, so cooling is already at full strength."));
        if (m.CpuTempMax >= 90)
            parts.Add(L($"המעבד הגיע ל-{m.CpuTempMax:0}°C, ובלפטופ הוא חולק את צינורות הקירור עם הכרטיס.", $"The CPU reached {m.CpuTempMax:0}°C, and on a laptop it shares the heat pipes with the GPU."));
        parts.Add(parts.Count > 0
            ? L("מה שנשאר: הגדרות וידאו קלות יותר (הסימונים ★), להגביה את גב הלפטופ, לנקות אבק ולוודא שכל ברגי הקירור במקום.",
                "What's left: lighter video settings (the ★ choices), raise the back of the laptop, clean the dust and make sure every cooler screw is in place.")
            : L("כדי לקרר: הגדרות וידאו קלות יותר (הסימונים ★), להגביה את גב הלפטופ, לנקות אבק ולוודא שכל ברגי הקירור במקום.",
                "To cool it: lighter video settings (the ★ choices), raise the back of the laptop, clean the dust and make sure every cooler screw is in place."));
        return string.Join(" ", parts);
    }

    private static string HeldTip(IReadOnlyList<MatchReport> held)
    {
        var known = held.Where(m => PowerCheck.GpuBelowFullPower(m.GpuPowerLimitAvg, m.GpuPowerDefault, m.GpuPowerMax) is not null).ToList();
        var low = known.FirstOrDefault(m => PowerCheck.GpuBelowFullPower(m.GpuPowerLimitAvg, m.GpuPowerDefault, m.GpuPowerMax) == true);
        if (low is not null)
            return L($"גבול ההספק של הכרטיס עמד על {low.GpuPowerLimitAvg:0}W, הבסיס שלו (עם Dynamic Boost הוא מגיע ל-{low.GpuPowerMax:0}W). ככה נראה מצב שקט או חסכוני ב-Control Center: עבור ל-Performance לפני משחק.",
                $"The GPU's power limit sat at {low.GpuPowerLimitAvg:0} W, its base (with Dynamic Boost it reaches {low.GpuPowerMax:0} W). That's what a quiet or power-saving mode in Control Center looks like: switch to Performance before playing.");
        if (known.Count > 0)
            return L($"גבול ההספק היה מלא ({known[0].GpuPowerLimitAvg:0}W), אז זה לא מצב הלפטופ. אם זה חוזר, בדוק באפליקציית NVIDIA שאין Battery Boost או WhisperMode פעילים.",
                $"The power limit was full ({known[0].GpuPowerLimitAvg:0} W), so it isn't the laptop's mode. If it happens again, check the NVIDIA app for Battery Boost or WhisperMode.");
        return L("לרוב זה מצב שקט או חסכוני ב-Control Center: ב-Performance הכרטיס מאיץ. המשחקים האלה הוקלטו לפני ש-BoostOS רשם את גבול ההספק; מהמשחק הבא יהיה ברור מה עצר אותו.",
            "Usually it's a quiet or power-saving mode in Control Center: in Performance the card boosts. These sessions were recorded before BoostOS kept the power limit; from the next game it will be clear what held it.");
    }

    private static string PowerTip(MatchReport m) =>
        m.OnBatteryPct >= 5 ? L("זה קרה על סוללה: חבר מטען.", "It happened on battery: plug in.")
        : PowerCheck.GpuBelowFullPower(m.GpuPowerLimitAvg, m.GpuPowerDefault, m.GpuPowerMax) == true
            ? L($"הגבול היה {m.GpuPowerLimitAvg:0}W במקום {m.GpuPowerMax:0}W: עבור ל-Performance ב-Control Center.", $"The limit was {m.GpuPowerLimitAvg:0} W instead of {m.GpuPowerMax:0} W: switch to Performance in Control Center.")
            : L("בלפטופ זה רגיל כשהכרטיס עובד קשה, והגבול כבר היה מלא.", "Normal on a laptop when the card works hard, and the limit was already full.");

    private static string CpuDipTip(MatchReport m) =>
        m.OnBatteryPct >= 5 ? L("זה קרה על סוללה: חבר מטען.", "It happened on battery: plug in.")
        : m.CpuTempMax >= 95 ? L($"המעבד הגיע ל-{m.CpuTempMax:0}°C: זה חום.", $"The CPU reached {m.CpuTempMax:0}°C: that's heat.")
        : m.PowerPlan is { } plan && !PlanIsFull(plan) ? L("תוכנית החשמל לא הייתה על ביצועים מלאים: באבחון → תיקונים יש את תוכנית BoostOS.", "The power plan wasn't on full performance: Diagnostics → Fixes has the BoostOS plan.")
        : L("החשמל היה על ביצועים מלאים והמעבד לא היה חם, אז זו כנראה הגבלת הספק של הלפטופ. מצב Performance ב-Control Center נותן לו יותר.", "Power was on full performance and the CPU wasn't hot, so it's likely the laptop's power limit. Performance mode in Control Center gives it more.");

    private static string? WifiTip(NetFacts? facts, bool trouble, bool drops = false, int count = 2)
    {
        if (facts is null) return L("בדיקת הרשת באבחון תראה אם זה ה-Wi-Fi או הנתב.", "The network test in Diagnostics shows whether it's the Wi-Fi or the router.");
        if (!facts.OnWifi) return L("החיבור בכבל, אז זה הנתב או הספק.", "You're on a cable, so it's the router or the provider.");
        var tips = WifiAdvice.For(facts, trouble, drops);
        return tips.Count == 0 ? null : string.Join(" ", tips.Take(count).Select(t => WifiAdvice.Text(t, facts)));
    }

    private static string Duration(double seconds) =>
        seconds < 90 ? L($"{seconds:0} שניות", $"{seconds:0} seconds") : L($"{seconds / 60:0.#} דקות", $"{seconds / 60:0.#} minutes");

    private static readonly Dictionary<string, string> Names = new()
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["discord"] = "Discord", ["spotify"] = "Spotify",
        ["onedrive"] = "OneDrive", ["steam"] = "Steam", ["steamwebhelper"] = "Steam", ["epicgameslauncher"] = "Epic Games Launcher",
        ["epicwebhelper"] = "Epic Games Launcher", ["explorer"] = "Explorer", ["claude"] = "Claude", ["chatgpt"] = "ChatGPT",
        ["whatsapp.root"] = "WhatsApp", ["whatsapp"] = "WhatsApp", ["phoneexperiencehost"] = "Phone Link", ["ms-teams"] = "Teams",
        ["obs64"] = "OBS", ["nvcontainer"] = "NVIDIA Container", ["devicehub"] = "DeviceHub",
    };

    public static string AppName(string process) => Names.GetValueOrDefault(process, process);

    // ---- For the page ----

    /// <summary>A report as the page shows it: the stored numbers plus what they mean, in the current language.</summary>
    public static JsonObject Annotate(MatchReport m, NetFacts? now = null)
    {
        var node = JsonSerializer.SerializeToNode(m, Program.Json)!.AsObject();
        node["findings"] = JsonSerializer.SerializeToNode(For(m, now), Program.Json);
        node["heatCost"] = HeatCost(m);
        node["held"] = Held(m);
        return node;
    }

    /// <summary>Every session with its findings, and the summary across them (the "matches" message).</summary>
    public static object Payload(IReadOnlyList<MatchReport> list)
    {
        var now = NetFacts.RecentOrNull();
        return new { list = list.Select(m => Annotate(m, now)).ToList(), summary = Summarize(list, now) };
    }
}

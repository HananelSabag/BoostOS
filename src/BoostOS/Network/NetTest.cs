using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using BoostOS.Core;
using BoostOS.Diagnostics;
using static BoostOS.Core.I18n;

namespace BoostOS.Network;

public sealed record PingSample(string Target, int Index, double T, double? Ms);

public sealed record TargetStats(
    string Id, string Label, string Address, int Sent, int Lost, double LossPct,
    double? Min, double? Avg, double? Median, double? P95, double? Max, double? Jitter,
    int SpikeEvents, double SpikeThreshold, double? PeriodSeconds);

public sealed record NetFinding(CheckStatus Status, string Text);

public sealed record NetTestResult(int Seconds, IReadOnlyList<TargetStats> Targets, CheckStatus Status, string Headline, IReadOnlyList<NetFinding> Findings);

/// <summary>
/// Pings the router and the internet side by side. Comparing the observations can help investigate
/// instability, but ICMP responses alone do not identify the cause or establish game traffic quality.
/// </summary>
public static class NetTest
{
    internal sealed record Target(string Id, string Label, IPAddress Address, int IntervalMs);

    public static async Task<NetTestResult> RunAsync(int seconds, Action<PingSample> onSample, CancellationToken ct)
    {
        // 2.5 pings a second per target, 5 in total, to reduce test-induced ICMP limiting.
        // A regular loss pattern may suggest limiting, but cannot establish that game traffic is unaffected.
        var targets = new List<Target>();
        if (NetworkCheck.FindActiveLink() is { } link)
            targets.Add(new("router", link.IsWifi ? L("נתב (החיבור האלחוטי)", "Router (the Wi-Fi link)") : L("נתב", "Router"), link.Gateway, 400));
        targets.Add(new("internet", L("אינטרנט (1.1.1.1)", "Internet (1.1.1.1)"), IPAddress.Parse("1.1.1.1"), 400));

        var runs = await Task.WhenAll(targets.Select(t => PingLoop(t, seconds, onSample, ct))).ConfigureAwait(false);
        var stats = targets.Zip(runs, Analyze).ToList();
        var routerIndex = targets.FindIndex(t => t.Id == "router");
        List<double> routerLossTimes = routerIndex < 0 ? [] : runs[routerIndex].Where(s => s.Ms is null).Select(s => s.T).ToList();
        return Judge(seconds, stats, LooksRateLimited(routerLossTimes));
    }

    /// <summary>
    /// Finds a regular timing pattern that can occur with ICMP rate limiting. Timing alone cannot
    /// distinguish rate limiting from real loss, so it must never suppress a loss warning.
    /// </summary>
    internal static bool LooksRateLimited(List<double> lossTimes)
    {
        if (lossTimes.Count < 6 || lossTimes[0] < 5) return false;
        var gaps = lossTimes.Zip(lossTimes.Skip(1), (a, b) => b - a).ToList();
        var mean = gaps.Average();
        var std = Math.Sqrt(gaps.Average(g => (g - mean) * (g - mean)));
        return mean > 0.3 && std / mean < 0.4;
    }

    private static async Task<List<(double T, double? Ms)>> PingLoop(Target target, int seconds, Action<PingSample> onSample, CancellationToken ct)
    {
        var samples = new List<(double, double?)>();
        var payload = new byte[32];
        using var ping = new Ping();
        var clock = Stopwatch.StartNew();

        for (var i = 0; clock.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested; i++)
        {
            var started = clock.Elapsed;
            double? ms = null;
            try
            {
                var sent = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(target.Address, 1000, payload).ConfigureAwait(false);
                // Stopwatch instead of RoundtripTime: the router answers in a few ms and whole-ms rounding hides jitter.
                if (reply.Status == IPStatus.Success) ms = Math.Round(sent.Elapsed.TotalMilliseconds, 1);
            }
            catch (PingException)
            {
                // Counted as lost.
            }

            var t = Math.Round(started.TotalSeconds, 2);
            samples.Add((t, ms));
            onSample(new PingSample(target.Id, i, t, ms));

            var wait = target.IntervalMs - (clock.Elapsed - started).TotalMilliseconds;
            if (wait > 0)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }
        return samples;
    }

    internal static TargetStats Analyze(Target target, List<(double T, double? Ms)> samples)
    {
        var ok = samples.Where(s => s.Ms is not null).Select(s => s.Ms!.Value).ToList();
        var lost = samples.Count - ok.Count;
        var sorted = ok.OrderBy(x => x).ToList();
        double? Pct(double p) => sorted.Count == 0 ? null : sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(p * sorted.Count))];

        var median = Pct(0.5);
        double? jitter = ok.Count < 2 ? null : Math.Round(ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average(), 1);
        var threshold = median is { } m ? m + Math.Max(15, m * 0.5) : 0;

        // A spike "event" is a run of slow replies; a new event starts after a second of calm.
        // Lost replies are reported as loss, not spikes (a router that rate-limits pings would otherwise look spiky).
        var eventStarts = new List<double>();
        var lastSpike = double.NegativeInfinity;
        foreach (var (t, ms) in samples)
        {
            if (ms is null || ms <= threshold) continue;
            if (t - lastSpike > 1.0) eventStarts.Add(t);
            lastSpike = t;
        }

        double? period = null;
        if (eventStarts.Count >= 3)
        {
            var gaps = eventStarts.Zip(eventStarts.Skip(1), (a, b) => b - a).ToList();
            var mean = gaps.Average();
            var std = Math.Sqrt(gaps.Average(g => (g - mean) * (g - mean)));
            if (mean >= 5 && std / mean < 0.25) period = Math.Round(mean, 1);
        }

        return new TargetStats(target.Id, target.Label, target.Address.ToString(), samples.Count, lost,
            samples.Count == 0 ? 0 : Math.Round(100.0 * lost / samples.Count, 1),
            sorted.Count == 0 ? null : sorted[0], ok.Count == 0 ? null : Math.Round(ok.Average(), 1), median, Pct(0.95),
            sorted.Count == 0 ? null : sorted[^1], jitter, eventStarts.Count, Math.Round(threshold, 1), period);
    }

    internal static NetTestResult Judge(int seconds, List<TargetStats> stats, bool pacedLoss)
    {
        var findings = new List<NetFinding>();
        var router = stats.FirstOrDefault(s => s.Id == "router");
        var net = stats.First(s => s.Id == "internet");

        // Only a fully successful internet sample supports treating gateway-only loss as inconclusive.
        // A regular pattern, or a lower loss rate at another target, cannot establish that loss is harmless.
        var routerOnly = router is not null && router.LossPct > 0.5 && net.Sent > 0 && net.Lost == 0;
        if (routerOnly)
            findings.Add(new(CheckStatus.Info,
                L($"הנתב לא ענה ל-{router!.LossPct}% מהפינגים אליו, אבל כל הפינגים לאינטרנט עברו. ייתכן שהוא מגביל תשובות ICMP; הבדיקה הזו לבדה לא מוכיחה איבוד בתעבורת המשחק.", $"The router did not answer {router!.LossPct}% of pings to itself, but all internet pings succeeded. It may limit ICMP replies; this test alone does not establish loss in game traffic.")));
        else if (pacedLoss)
            findings.Add(new(CheckStatus.Info,
                L("פינגים לנתב לא חזרו במרווחים קבועים. זה עשוי להיות סינון ICMP, אבל הדפוס לבדו לא שולל איבוד חבילות אמיתי.", "Unanswered router pings follow a regular pattern. ICMP limiting is possible, but timing alone cannot rule out real packet loss.")));

        if (router is not null)
        {
            var clean = !routerOnly;
            if (router.Sent == 0)
            {
                findings.Add(new(CheckStatus.Warn, L("לא נאספו דגימות לנתב. אי אפשר לקבוע שהחיבור יציב; הרץ שוב.", "No router samples were collected. Link stability could not be assessed; run the test again.")));
                clean = false;
            }
            else if (!routerOnly && router.LossPct >= 2)
            {
                findings.Add(new(CheckStatus.Bad, L($"{router.LossPct}% מהפינגים לנתב לא נענו. ייתכן שיש בעיה בחיבור המקומי או בתשובות ICMP של הנתב; השווה לבדיקת כבל ולנתוני המשחק.", $"{router.LossPct}% of router pings went unanswered. The local link or the router's ICMP handling may be involved; compare a wired test and the game's network stats.")));
                clean = false;
            }
            else if (!routerOnly && router.LossPct > 0.5)
            {
                findings.Add(new(CheckStatus.Warn, L($"{router.LossPct}% מהפינגים לנתב לא נענו. כדאי להריץ שוב ולהשוות לנתוני הרשת במשחק.", $"{router.LossPct}% of router pings went unanswered. Repeat the test and compare the game's network stats.")));
                clean = false;
            }

            if (router.PeriodSeconds is { } p)
            {
                findings.Add(new(CheckStatus.Bad,
                    L($"קפיצות פינג לנתב כל {p:0} שניות בערך. פעילות רקע, הפרעות או סריקות Wi-Fi עשויות לגרום לכך; הדפוס לבדו לא מזהה את הסיבה. השווה בדיקה נוספת בחיבור כבל.", $"Ping spikes to the router about every {p:0} seconds. Background activity, interference or Wi-Fi scans may cause this; the pattern alone does not identify the cause. Compare another test over a cable.")));
                clean = false;
            }
            else if (router.SpikeEvents >= 3)
            {
                findings.Add(new(CheckStatus.Warn,
                    L($"{router.SpikeEvents} קפיצות פינג לנתב, בלי דפוס קבוע. זה בדרך כלל הפרעות: רשתות שכנות, Bluetooth או ערוץ צפוף. שווה לנסות בנתב ערוץ 5GHz אחר או רוחב 80MHz, ולהריץ שוב.", $"{router.SpikeEvents} ping spikes to the router with no fixed pattern. Usually interference: neighbours' networks, Bluetooth or a crowded channel. Try another 5GHz channel or 80MHz width on the router and test again.")));
                clean = false;
            }

            if (router.Jitter is > 5)
            {
                findings.Add(new(CheckStatus.Warn, L($"רעד (jitter) של {router.Jitter}ms לנתב. ב-Wi-Fi טוב הוא מתחת ל-3ms.", $"{router.Jitter}ms of jitter to the router. Good Wi-Fi stays under 3ms.")));
                clean = false;
            }

            if (clean)
                findings.Add(new(CheckStatus.Ok,
                    L($"בפינגים לנתב נמדדו חציון {router.Median}ms, רעד {router.Jitter}ms, {router.LossPct}% ללא תשובה ו-{router.SpikeEvents} קפיצות בבדיקה הזו.", $"Router probes measured {router.Median}ms median, {router.Jitter}ms jitter, {router.LossPct}% unanswered and {router.SpikeEvents} spike events in this test.")));
        }

        if (net.Sent == 0)
            findings.Add(new(CheckStatus.Warn, L("לא נאספו דגימות אינטרנט. אי אפשר לקבוע שהרשת יציבה; הרץ שוב.", "No internet samples were collected. Network stability could not be assessed; run the test again.")));
        else if (net.Lost == net.Sent)
            findings.Add(new(CheckStatus.Bad, L("אף פינג לאינטרנט לא נענה. ייתכן שאין חיבור או ש-ICMP חסום; אי אפשר לאשר שהרשת יציבה.", "No internet ping received a reply. Connectivity may be unavailable or ICMP blocked; network stability could not be confirmed.")));
        else if (net.LossPct >= 1)
            findings.Add(new(net.LossPct >= 5 ? CheckStatus.Bad : CheckStatus.Warn,
                L($"{net.LossPct}% מהפינגים לאינטרנט לא נענו. זה עשוי להעיד על איבוד בחיבור או על סינון ICMP; הבדיקה לא מזהה בוודאות היכן.", $"{net.LossPct}% of internet pings went unanswered. This may reflect connection loss or ICMP filtering; this test cannot locate the cause with certainty.")));
        if (net.SpikeEvents >= 3)
            findings.Add(new(CheckStatus.Warn,
                L($"נמדדו {net.SpikeEvents} קפיצות פינג לאינטרנט. הרץ שוב והשווה לנתוני הרשת במשחק; פינגים לבדם לא מזהים את מקור התנודות.", $"Measured {net.SpikeEvents} internet ping spike events. Repeat the test and compare the game's network stats; pings alone do not identify the source of the variation.")));
        if (net.Jitter is > 10)
            findings.Add(new(CheckStatus.Warn,
                L($"בין תשובות אינטרנט מוצלחות נמדד הפרש ממוצע של {net.Jitter}ms (jitter). הרץ שוב והשווה לנתוני הרשת במשחק כדי לבדוק אם התנודות נמשכות.", $"Successful internet replies varied by {net.Jitter}ms on average between replies (jitter). Repeat the test and compare the game's network stats to check whether the variation persists.")));
        findings.Add(new(CheckStatus.Info,
            L($"פינג לאינטרנט: {net.Median}ms (חציון), {net.P95}ms ב-95% מהזמן. הפינג לשרתים של רוקט ליג תלוי באזור השרת ויהיה שונה.", $"Internet ping: {net.Median}ms median, {net.P95}ms 95% of the time. Ping to Rocket League servers depends on the region and will differ.")));

        var status = findings.Any(f => f.Status == CheckStatus.Bad) ? CheckStatus.Bad
            : findings.Any(f => f.Status == CheckStatus.Warn) ? CheckStatus.Warn : CheckStatus.Ok;
        var headline = status switch
        {
            CheckStatus.Ok => L("הרשת יציבה", "The network is steady"),
            CheckStatus.Warn => L("יש מה לשפר ברשת", "The network could be better"),
            _ => L("נמצאה בעיה ברשת", "Found a network problem"),
        };
        return new NetTestResult(seconds, stats, status, headline, findings);
    }
}

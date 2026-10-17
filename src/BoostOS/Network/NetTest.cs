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
/// Pings the router and the internet side by side. The router line isolates the Wi-Fi link itself:
/// if the router line is clean and the internet line is not, the problem is outside the house.
/// </summary>
public static class NetTest
{
    internal sealed record Target(string Id, string Label, IPAddress Address, int IntervalMs);

    public static async Task<NetTestResult> RunAsync(int seconds, Action<PingSample> onSample, CancellationToken ct)
    {
        // 2.5 pings a second per target, 5 in total. Home routers cap how much ICMP they answer: the router here
        // lets a ~15 second burst through and then drops exactly one ping a second once the total passes ~7/s.
        // That looks like Wi-Fi loss but isn't, so stay well under it and still detect the pattern below.
        var targets = new List<Target>();
        if (NetworkCheck.FindActiveLink() is { } link)
            targets.Add(new("router", link.IsWifi ? L("נתב (החיבור האלחוטי)", "Router (the Wi-Fi link)") : L("נתב", "Router"), link.Gateway, 400));
        targets.Add(new("internet", L("אינטרנט (1.1.1.1)", "Internet (1.1.1.1)"), IPAddress.Parse("1.1.1.1"), 400));

        var runs = await Task.WhenAll(targets.Select(t => PingLoop(t, seconds, onSample, ct)));
        var stats = targets.Zip(runs, Analyze).ToList();
        var lossTimes = runs.SelectMany(r => r.Where(s => s.Ms is null).Select(s => s.T)).Order().ToList();
        return Judge(seconds, stats, LooksRateLimited(lossTimes));
    }

    /// <summary>
    /// Real Wi-Fi loss comes in bursts. A router that rate-limits pings drops them like a metronome,
    /// and only after an initial burst has been let through.
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
                var reply = await ping.SendPingAsync(target.Address, 1000, payload);
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
                try { await Task.Delay(TimeSpan.FromMilliseconds(wait), ct); }
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

        // Internet pings travel through the router, so real Wi-Fi loss shows up on both lines. Loss only on the
        // router line means the router is refusing to answer pings, not dropping traffic.
        var routerOnly = router is not null && router.LossPct > 0.5 && net.LossPct < router.LossPct / 3;
        var routerLimited = routerOnly || pacedLoss;
        if (routerOnly)
            findings.Add(new(CheckStatus.Info,
                L($"הנתב לא ענה ל-{router!.LossPct}% מהפינגים אליו, אבל הפינגים לאינטרנט, שעוברים דרכו, כן עברו. כלומר הנתב פשוט מגביל תשובות לפינג. זה לא איבוד אמיתי.", $"The router ignored {router!.LossPct}% of pings to itself, yet pings to the internet, which pass through it, got through. The router just limits ping replies. That's not real loss.")));
        else if (pacedLoss)
            findings.Add(new(CheckStatus.Info,
                L("חלק מהפינגים לא חזרו, אבל במרווחים קבועים לגמרי ורק אחרי כמה שניות של בדיקה. ככה נראה נתב שמגביל כמה פינגים הוא מסכים לענות בשנייה. זה לא איבוד אמיתי של חבילות, והמשחק לא מושפע מזה.", "Some pings didn't come back, but at perfectly even intervals and only after a few seconds. That's a router capping how many pings it answers per second, not real packet loss. The game isn't affected.")));

        if (router is not null)
        {
            var clean = true;
            if (routerLimited)
            {
                // Judged on spikes and jitter only.
            }
            else if (router.LossPct >= 2)
            {
                findings.Add(new(CheckStatus.Bad, L($"{router.LossPct}% מהחבילות לנתב הלכו לאיבוד. הבעיה ב-Wi-Fi עצמו: אות, הפרעות או הכרטיס.", $"{router.LossPct}% of packets to the router were lost. The problem is the Wi-Fi itself: signal, interference or the card.")));
                clean = false;
            }
            else if (router.LossPct > 0.5)
            {
                findings.Add(new(CheckStatus.Warn, L($"איבוד חבילות קל לנתב ({router.LossPct}%). במשחק זה מרגיש כמו קפיצה קטנה של הכדור או של המכונית.", $"Slight packet loss to the router ({router.LossPct}%). In game it feels like the ball or a car skipping.")));
                clean = false;
            }

            if (router.PeriodSeconds is { } p)
            {
                findings.Add(new(CheckStatus.Bad,
                    L($"קפיצות פינג קבועות לנתב כל {p:0} שניות בערך. זה הסימן הקלאסי של סריקת רשתות ברקע. התיקון: לעצור את WLAN AutoConfig בזמן משחק. מצב משחק יעשה את זה ויחזיר כשתסיים.", $"Ping spikes to the router every {p:0} seconds or so: the classic sign of background network scans. The fix is pausing WLAN AutoConfig during matches, which Game Mode now does and undoes afterwards.")));
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
                    L($"החיבור לנתב נקי: חציון {router.Median}ms, רעד {router.Jitter}ms, בלי קפיצות. ה-Wi-Fi עצמו לא הבעיה.", $"The link to the router is clean: {router.Median}ms median, {router.Jitter}ms jitter, no spikes. Wi-Fi isn't the problem.")));
        }

        if (!pacedLoss && net.LossPct >= 1 && (router is null || router.LossPct < 0.5))
            findings.Add(new(CheckStatus.Warn, L($"{net.LossPct}% איבוד חבילות אחרי הנתב. זה כבר מחוץ לבית, אצל ספק האינטרנט.", $"{net.LossPct}% packet loss beyond the router. That's outside the house, at the internet provider.")));
        if (router is not null && net.SpikeEvents >= 3 && router.SpikeEvents == 0)
            findings.Add(new(CheckStatus.Info, L("יש קפיצות לכיוון האינטרנט אבל לא לנתב, כלומר הבעיה בדרך החוצה ולא ב-Wi-Fi.", "Spikes toward the internet but not to the router: the problem is on the way out, not the Wi-Fi.")));
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

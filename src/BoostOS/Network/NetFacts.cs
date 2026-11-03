using System.Globalization;
using BoostOS.Core;
using BoostOS.Diagnostics;
using static BoostOS.Core.I18n;

namespace BoostOS.Network;

/// <summary>The Wi-Fi link as Windows reports it (netsh wlan show interfaces).</summary>
public sealed record WifiLink(string Adapter, string? Band, int? Channel, string? Radio, int? Rssi, int? SignalPct,
    double? RxMbps, double? TxMbps, string? Bssid)
{
    public bool Is24 => Band is { } b ? b.StartsWith("2.4", StringComparison.Ordinal) : Channel is >= 1 and <= 14;
    public bool Is6 => Band?.StartsWith('6') == true;
    /// <summary>5 GHz channels 52–144: the router must leave them for a minute when it hears radar.</summary>
    public bool Dfs => !Is24 && !Is6 && Channel is >= 52 and <= 144;
    /// <summary>Above 1201 Mbps a two-antenna card needs a 160 MHz wide channel.</summary>
    public bool Wide => RxMbps > 1201 || TxMbps > 1201;
    public bool Weak => Rssi < -67;
    public bool Strong => Rssi >= -60;
    public bool Intel => Adapter.Contains("Intel", StringComparison.OrdinalIgnoreCase);

    public static WifiLink? Read() => Parse(Shell.Run("netsh", "wlan show interfaces"));

    internal static WifiLink? Parse(string? text)
    {
        var w = Shell.ParseColonList(text);
        if (!w.TryGetValue("State", out var state) || !state.Equals("connected", StringComparison.OrdinalIgnoreCase)) return null;
        int? Int(string key) => int.TryParse(w.GetValueOrDefault(key, "").TrimEnd('%', ' '), out var v) ? v : null;
        double? Num(string key) => double.TryParse(w.GetValueOrDefault(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        string? Str(string key) => w.TryGetValue(key, out var v) && v != "" ? v : null;
        return new WifiLink(Str("Description") ?? "Wi-Fi", Str("Band"), Int("Channel"), Str("Radio type"), Int("Rssi"), Int("Signal"),
            Num("Receive rate (Mbps)"), Num("Transmit rate (Mbps)"), Str("AP BSSID"));
    }
}

/// <summary>
/// Everything about the connection that advice depends on, read once: cable or Wi-Fi, the Wi-Fi link, whether the
/// PC has an Ethernet port, and what else is on the air (Bluetooth sound, Bluetooth controllers). The scan, the
/// network test and every recorded session keep the same facts, so the same rules judge all three.
/// </summary>
public sealed record NetFacts(bool OnWifi, WifiLink? Wifi, bool EthernetPort, string? BluetoothAudio, bool BluetoothInput)
{
    private static readonly object CacheGate = new();
    private static (NetFacts Facts, long At)? cache;

    /// <summary>Facts read in the last half minute, or null: for places that must not wait on Windows.</summary>
    public static NetFacts? RecentOrNull()
    {
        lock (CacheGate) return cache is { } c && Environment.TickCount64 - c.At < 30_000 ? c.Facts : null;
    }

    /// <summary>The facts from the last half minute, so one scan doesn't ask Windows the same questions twice.</summary>
    public static NetFacts Recent()
    {
        lock (CacheGate)
        {
            if (cache is { } c && Environment.TickCount64 - c.At < 30_000) return c.Facts;
            var facts = Collect();
            cache = (facts, Environment.TickCount64);
            return facts;
        }
    }

    public static NetFacts Collect()
    {
        var link = NetworkCheck.FindActiveLink();
        var onWifi = link?.IsWifi ?? false;
        var ethernet = false;
        try
        {
            ethernet = Wmi.Query("SELECT NdisPhysicalMedium FROM MSFT_NetAdapter WHERE ConnectorPresent = TRUE", @"root\StandardCimv2")
                .Any(a => a.Long("NdisPhysicalMedium") == 14);
        }
        catch { }
        var audio = AudioRoute.DefaultOutput();
        var btInput = false;
        // Only devices that answer as a controller, mouse or keyboard: a paired device that's asleep or on its dongle shows as "other".
        try { btInput = InputCheck.Enumerate().Any(d => d.Link == InputCheck.Link.Bluetooth && d.Kind != InputCheck.Kind.Other); } catch { }
        return new NetFacts(onWifi, onWifi ? WifiLink.Read() : null, ethernet, audio is { Bluetooth: true } ? audio.Name : null, btInput);
    }
}

/// <summary>
/// What to try about Wi-Fi, decided from the facts rather than said to everyone. Band and signal problems are always
/// worth saying; the rest only when there is evidence of trouble (spikes or loss), and in order of likely payoff.
/// </summary>
public static class WifiAdvice
{
    public enum Tip { Band24, Weak, BluetoothAudio, BluetoothInput, Wide, Dfs, Cable, Clean }

    /// <param name="trouble">Spikes or loss were measured (in games or the network test).</param>
    /// <param name="drops">Pings were lost or the link moved: what a DFS channel change looks like.</param>
    public static List<Tip> For(NetFacts facts, bool trouble, bool drops = false)
    {
        var tips = new List<Tip>();
        if (!facts.OnWifi || facts.Wifi is not { } w) return tips;
        if (w.Is24) tips.Add(Tip.Band24);
        if (w.Weak) tips.Add(Tip.Weak);
        if (!trouble) return tips;
        // Wi-Fi and Bluetooth share the antenna on most laptop cards; sound streams without a break.
        if (facts.BluetoothAudio is not null) tips.Add(Tip.BluetoothAudio);
        else if (facts.BluetoothInput) tips.Add(Tip.BluetoothInput);
        if (w.Wide) tips.Add(Tip.Wide);
        if (w.Dfs && drops) tips.Add(Tip.Dfs);
        if (tips.Count == 0) tips.Add(Tip.Clean);
        if (facts.EthernetPort) tips.Add(Tip.Cable);
        return tips;
    }

    public static string Text(Tip tip, NetFacts facts)
    {
        var w = facts.Wifi;
        var card = w is null ? "Wi-Fi" : w.Intel ? L($"בכרטיס {Short(w.Adapter)},", $"On the {Short(w.Adapter)},") : L("ברוב כרטיסי הלפטופ", "On most laptop cards,");
        return tip switch
        {
            Tip.Band24 => L($"החיבור ב-2.4GHz (ערוץ {w?.Channel}): רשת צפופה ואיטית שגם מתנגשת עם Bluetooth. התחבר לרשת ה-5GHz של הנתב.",
                $"The link is on 2.4GHz (channel {w?.Channel}): crowded, slow, and it clashes with Bluetooth. Join the router's 5GHz network."),
            Tip.Weak => L($"האות חלש ({w?.Rssi} dBm). קרוב יותר לנתב, פחות קירות, או מגדיל טווח.",
                $"The signal is weak ({w?.Rssi} dBm). Get closer to the router, put fewer walls in between, or add an extender."),
            Tip.BluetoothAudio => L($"הסאונד יוצא ב-Bluetooth ({facts.BluetoothAudio}). {card} ה-Wi-Fi וה-Bluetooth חולקים אנטנה, והזרמת סאונד לוקחת ממנו זמן אוויר בלי הפסקה. כדי לדעת בוודאות: הרץ את בדיקת הרשת פעם כשהסאונד ב-Bluetooth ופעם כשהוא באוזניות או ברמקולי הלפטופ.",
                $"Sound goes out over Bluetooth ({facts.BluetoothAudio}). {card} Wi-Fi and Bluetooth share the antenna, and streaming sound takes air time from Wi-Fi without a break. To know for sure, run the network test once with sound on Bluetooth and once on the headset or the laptop speakers."),
            Tip.BluetoothInput => L($"יש שלט או עכבר ב-Bluetooth. {card} הם חולקים אנטנה עם ה-Wi-Fi. הרץ את בדיקת הרשת עם ההתקן ובלעדיו (או עם כבל) והשווה.",
                $"A controller or mouse is on Bluetooth. {card} it shares the antenna with Wi-Fi. Run the network test with and without it (or on a cable) and compare."),
            Tip.Wide => L($"הנתב משדר בערוץ רחב 160MHz (קצב {w?.RxMbps:0} Mbps). רחב יותר = יותר הפרעות, ורוקט ליג צריך פחות מ-1Mbps. בהגדרות ה-5GHz של הנתב בחר 80MHz.",
                $"The router uses a 160MHz wide channel ({w?.RxMbps:0} Mbps). Wider catches more interference, and Rocket League needs under 1Mbps. Set the router's 5GHz width to 80MHz."),
            Tip.Dfs => L($"ערוץ {w?.Channel} הוא ערוץ DFS: כשהנתב קולט מכ\"ם (מזג אוויר, מטוסים) הוא חייב לעבור ערוץ, וה-Wi-Fi נופל לכמה שניות עד דקה. אם יש התנתקויות קצרות, קבע בנתב ערוץ 36–48.",
                $"Channel {w?.Channel} is a DFS channel: when the router hears radar (weather, planes) it must switch, and Wi-Fi drops for seconds up to a minute. If you see short drops, set the router to channel 36–48."),
            Tip.Cable => L("ללפטופ יש יציאת רשת: כבל לנתב מבטל את כל בעיות ה-Wi-Fi בבת אחת.",
                "This PC has an Ethernet port: a cable to the router removes every Wi-Fi issue at once."),
            _ => L("החיבור עצמו נקי: 5GHz, אות חזק, ערוץ רגיל ובלי Bluetooth. קפיצות כאלה מגיעות בדרך כלל מהנתב או ממכשיר אחר ברשת שמוריד או מזרים.",
                "The link itself is clean: 5GHz, strong signal, a regular channel, no Bluetooth. Spikes like these usually come from the router or another device on the network downloading or streaming."),
        };
    }

    private static string Short(string adapter) => adapter.Replace("Intel(R) ", "Intel ").Replace(" 160MHz", "");
}

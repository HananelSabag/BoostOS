using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BoostOS.Core;
using BoostOS.Network;
using BoostOS.Tuning;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class NetworkCheck
{
    public sealed record ActiveLink(NetworkInterface Nic, IPAddress Gateway, bool IsWifi);

    /// <summary>The adapter that carries the default IPv4 route, and its gateway (the router).</summary>
    public static ActiveLink? FindActiveLink()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var gw = nic.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            if (gw is not null)
                return new ActiveLink(nic, gw, nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        return null;
    }

    public static Section Run(Facts scan)
    {
        var checks = new List<Check>();
        var title = L("רשת", "Network");
        var connection = L("סוג חיבור", "Connection");
        var link = FindActiveLink();
        if (link is null)
        {
            checks.Add(new(connection, L("אין חיבור לאינטרנט", "No internet connection"), Bad));
            return new Section("network", title, "wifi", checks);
        }

        var adapters = Wmi.Query("SELECT Name, InterfaceDescription, DriverVersionString, NdisPhysicalMedium, MediaConnectState FROM MSFT_NetAdapter WHERE ConnectorPresent = TRUE",
            @"root\StandardCimv2");
        var router = L("נתב", "Router");

        if (!link.IsWifi)
        {
            checks.Add(new(connection, $"{L("כבל", "Cable")} · {link.Nic.Description} · {link.Nic.Speed / 1_000_000} Mbps", Ok, L("כבל הוא הכי טוב למשחק.", "A cable is the best link for gaming.")));
            checks.Add(new(router, link.Gateway.ToString(), Info));
            return new Section("network", title, "wifi", checks);
        }

        // Advice about the link is judged against what the games showed: a strong link with spike-free games needs nothing.
        var facts = NetFacts.Recent();
        var w = facts.Wifi;
        var played = Game.SessionFindings.WifiEvidence(Game.MatchStore.Load());
        // A few spikes an hour are worth a test (info); a dozen or real loss is a problem (warn).
        var level = played is { } e ? e.PerHour >= 12 || e.Loss >= 1 ? Warn : e.PerHour >= 6 ? Info : Ok : Ok;
        var trouble = level != Ok;
        var drops = played is { Loss: >= 0.5 };
        var evidence = played is { } p
            ? L($"במשחקים האחרונים: {p.PerHour:0} קפיצות פינג בשעה, הכי גבוה {p.MaxMs:0} ms.", $"In your recent games: {p.PerHour:0} ping spikes an hour, highest {p.MaxMs:0} ms.")
            : L("עוד אין משחקים מוקלטים עם פינג, אז בדיקת הרשת היא הדרך לדעת.", "No recorded games with ping yet, so the network test is the way to know.");
        string Tips(bool withTrouble) => string.Join(" ", WifiAdvice.For(facts, withTrouble, drops).Take(2).Select(t => WifiAdvice.Text(t, facts)));

        if (w is null)
        {
            checks.Add(new(connection, "Wi-Fi", Info, L("לא הצלחתי לקרוא את פרטי החיבור האלחוטי.", "Couldn't read the Wi-Fi link details.")));
            checks.Add(new(router, link.Gateway.ToString(), Info));
            return new Section("network", title, "wifi", checks);
        }

        var good = !w.Is24 && w.Strong;
        // A strong link: the evidence here, and each line below carries its own fix. A weak one: the fix right here.
        var below = trouble ? L(" מה לנסות מופיע בשורות למטה.", " What to try is in the lines below.") : "";
        checks.Add(good
            ? new(connection, $"Wi-Fi · {L($"אות חזק ב-{w.Band}", $"strong {w.Band} signal")}", level, evidence + below)
            : new(connection, "Wi-Fi", Warn, $"{evidence} {Tips(trouble)}".Trim()));

        var bandTitle = L("תדר", "Band");
        var bandValue = $"{w.Band} · {L("ערוץ", "channel")} {w.Channel}{(w.Dfs ? " (DFS)" : "")}";
        checks.Add(w.Is24
            ? new(bandTitle, bandValue, Bad, WifiAdvice.Text(WifiAdvice.Tip.Band24, facts))
            : new(bandTitle, bandValue, Ok, w.Dfs && drops ? WifiAdvice.Text(WifiAdvice.Tip.Dfs, facts) : null));

        if (w.Rssi is { } rssi)
        {
            var sig = L("עוצמת אות", "Signal");
            checks.Add(rssi switch
            {
                >= -60 => new(sig, $"{rssi} dBm ({w.SignalPct}%)", Ok, L("אות חזק מאוד. המרחק מהנתב הוא לא הבעיה.", "Very strong. Distance from the router isn't the problem.")),
                >= -67 => new(sig, $"{rssi} dBm", Warn, L("אות בינוני. קרבה לנתב או פחות קירות באמצע יעזרו ליציבות.", "Medium signal. Getting closer or fewer walls in between will steady it.")),
                _ => new(sig, $"{rssi} dBm", Bad, WifiAdvice.Text(WifiAdvice.Tip.Weak, facts)),
            });
        }

        var linkTitle = L("מהירות קישור", "Link rate");
        if (w.RxMbps is { } rx)
            checks.Add(new(linkTitle, $"{rx:0} Mbps · {w.Radio}{(w.Wide ? $" · {L("ערוץ רחב 160MHz", "160MHz wide channel")}" : "")}", Info,
                w.Wide && trouble ? WifiAdvice.Text(WifiAdvice.Tip.Wide, facts) : null));

        if (facts.BluetoothAudio is { } speaker)
            checks.Add(new(L("סאונד ב-Bluetooth", "Sound over Bluetooth"), speaker, level == Warn ? Warn : Info,
                trouble ? WifiAdvice.Text(WifiAdvice.Tip.BluetoothAudio, facts)
                        : L("ה-Wi-Fi בזמן המשחקים היה יציב, אז זה לא מפריע כאן. אם יופיעו קפיצות, זה הדבר הראשון לבדוק.",
                            "Wi-Fi was steady in your games, so it doesn't get in the way here. If spikes show up, check this first.")));

        var wifiAdapter = adapters.FirstOrDefault(a => a.Long("NdisPhysicalMedium") == 9);
        if (wifiAdapter is not null)
        {
            checks.Add(new(L("כרטיס Wi-Fi", "Wi-Fi card"), $"{wifiAdapter.Str("InterfaceDescription")} · {L("דרייבר", "driver")} {wifiAdapter.Str("DriverVersionString")}", Info));

            var props = Wmi.Query($"SELECT DisplayName, DisplayValue, RegistryKeyword FROM MSFT_NetAdapterAdvancedPropertySettingData WHERE Name = '{wifiAdapter.Str("Name").Replace("'", "''")}'",
                    @"root\StandardCimv2")
                .GroupBy(p => p.Str("RegistryKeyword"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (props.TryGetValue("RoamAggressiveness", out var roam))
                checks.Add(roam.Str("DisplayValue").Contains("Lowest", StringComparison.OrdinalIgnoreCase)
                    ? new("Roaming Aggressiveness", roam.Str("DisplayValue"), Ok, null, Fixes.Ref("wifi-roaming"))
                    : new("Roaming Aggressiveness", roam.Str("DisplayValue"), Warn,
                        L("הכרטיס מחפש מדי פעם נקודת גישה טובה יותר, וכל חיפוש כזה יכול לייצר קפיצת פינג. בבית עם נתב אחד אין בזה צורך. אם יש לך Mesh או מגדיל טווח, אל תתקן את זה. התיקון מנתק את ה-Wi-Fi לכמה שניות.",
                          "The card keeps hunting for a better access point, and each hunt can cause a ping spike. With one router at home there's nothing to roam to. Skip this if you use mesh or an extender. Fixing it drops Wi-Fi for a few seconds."),
                        Fixes.Ref("wifi-roaming")));

            if (props.TryGetValue("MIMOPowerSaveMode", out var mimo))
                checks.Add(mimo.Str("DisplayValue").Contains("No SMPS", StringComparison.OrdinalIgnoreCase)
                    ? new("MIMO Power Save", mimo.Str("DisplayValue"), Ok)
                    : new("MIMO Power Save", mimo.Str("DisplayValue"), Info,
                        L("חיסכון חשמל של האנטנות. ההשפעה קטנה ולא מוכחת, אז BoostOS לא משנה את זה.", "Antenna power saving. The effect is small and unproven, so BoostOS leaves it alone.")));
        }

        var (ac, dc) = PowerCheck.ReadSetting(PowerCheck.WirelessSubgroup, PowerCheck.WirelessPowerSaving);
        var wifiPower = L("חיסכון חשמל ב-Wi-Fi (במטען)", "Wi-Fi power saving (plugged in)");
        if (ac is not null)
            checks.Add(ac == 0
                ? new(wifiPower, "Maximum Performance", Ok,
                    dc is > 0 ? L("על סוללה הכרטיס עובר לחיסכון בחשמל: עוד סיבה לשחק עם מטען.", "On battery the card saves power: one more reason to play plugged in.") : null, Fixes.Ref("wifi-power-ac"))
                : new(wifiPower, L($"רמה {ac}", $"level {ac}"), Warn,
                    L("כרטיס ה-Wi-Fi נרדם בין חבילות כדי לחסוך חשמל, וזה מוסיף השהיה.", "The Wi-Fi card dozes between packets to save power, which adds latency."), Fixes.Ref("wifi-power-ac")));

        var auto = Shell.Run("netsh", "wlan show autoconfig") ?? "";
        var scanTitle = L("סריקת רשתות ברקע", "Background network scans");
        // Scans are judged by the last network test: only spikes on a fixed beat point at them.
        var periodic = Agent.LastNet();
        if (auto.Contains("is enabled", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(scanTitle, L("פעילה (WLAN AutoConfig)", "On (WLAN AutoConfig)"), Info, periodic switch
            {
                true => L("בבדיקת הרשת האחרונה נמצאו קפיצות בקצב קבוע, וזה הדפוס שסריקות רקע משאירות. הסריקה נשארת פעילה כי בלעדיה ווינדוס לא מתחבר לבד לרשתות; כבל או נתב שמחזיק את הכרטיס בערוץ אחד עוקפים את זה.",
                          "The last network test found spikes on a fixed beat, the pattern background scans leave. Scanning stays on because without it Windows won't join networks by itself; a cable avoids it."),
                false => L("בבדיקת הרשת האחרונה לא נמצאו קפיצות בקצב קבוע, אז הסריקות לא מפריעות כאן.", "The last network test found no spikes on a fixed beat, so the scans don't get in the way here."),
                null => L("ווינדוס סורק רשתות ברקע גם כשאתה מחובר, ולפעמים זה משאיר קפיצת פינג בקצב קבוע. בדיקת הרשת תראה אם זה קורה כאן.",
                          "Windows scans for networks even while connected, which sometimes leaves a ping spike on a fixed beat. The network test shows whether it happens here."),
            }));
        else if (auto.Contains("is disabled", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(scanTitle, L("כבויה", "Off"), Ok,
                L("שים לב: ככה ווינדוס לא יתחבר לבד לרשתות אחרות עד שתפעיל אותה שוב.", "Note: Windows won't join other networks by itself until it's back on.")));

        if (facts.BluetoothInput)
            checks.Add(new(L("שלט או עכבר ב-Bluetooth", "Bluetooth controller or mouse"), L("מחובר", "Connected"), Info,
                trouble ? WifiAdvice.Text(WifiAdvice.Tip.BluetoothInput, facts)
                        : L("ה-Wi-Fi בזמן המשחקים היה יציב, אז זה לא מפריע כאן.", "Wi-Fi was steady in your games, so it doesn't get in the way here.")));

        checks.Add(new(router, link.Gateway.ToString(), Info));
        return new Section("network", title, "wifi", checks);
    }
}

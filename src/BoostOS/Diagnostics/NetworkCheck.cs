using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BoostOS.Core;
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

    public static Section Run(Facts facts)
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
        var ethernetPort = adapters.FirstOrDefault(a => a.Long("NdisPhysicalMedium") == 14);
        var router = L("נתב", "Router");

        if (!link.IsWifi)
        {
            checks.Add(new(connection, $"{L("כבל", "Cable")} · {link.Nic.Description} · {link.Nic.Speed / 1_000_000} Mbps", Ok, L("כבל הוא הכי טוב למשחק.", "A cable is the best link for gaming.")));
            checks.Add(new(router, link.Gateway.ToString(), Info));
            return new Section("network", title, "wifi", checks);
        }

        var w = Shell.ParseColonList(Shell.Run("netsh", "wlan show interfaces"));
        var band = w.GetValueOrDefault("Band", "");
        var hasRssi = int.TryParse(w.GetValueOrDefault("Rssi", ""), out var rssi);
        var strongFastWifi = !band.StartsWith("2.4", StringComparison.Ordinal) && band != "" && hasRssi && rssi >= -60;

        // A strong 5/6 GHz link close to the router is fine for Rocket League; only nag about a cable when the link is weak.
        checks.Add(strongFastWifi
            ? new(connection, $"Wi-Fi · {L("אות חזק ב-5GHz", "strong 5GHz signal")}", Ok,
                L("כבל הוא עדיין הכי יציב, אבל Wi-Fi חזק על 5GHz קרוב לנתב מספיק לרוקט ליג. בדיקת הרשת תראה אם יש קפיצות.",
                  "A cable is still the steadiest, but strong 5GHz Wi-Fi near the router is enough for Rocket League. The network test shows any spikes."))
            : new(connection, "Wi-Fi", Warn,
                ethernetPort is not null
                    ? L($"ללפטופ יש יציאת כבל רשת ({ethernetPort.Str("InterfaceDescription")}). כבל לנתב פותר בבת אחת את כל מה שקשור ל-Wi-Fi: קפיצות פינג, איבוד חבילות והפרעות.",
                        $"This PC has an Ethernet port ({ethernetPort.Str("InterfaceDescription")}). A cable to the router fixes every Wi-Fi issue at once: ping spikes, packet loss and interference.")
                    : L("כבל רשת תמיד יציב יותר מ-Wi-Fi. אם אין אפשרות, למטה יש מה לשפר.", "A cable is always steadier than Wi-Fi. If that's not an option, see below.")));

        var channel = w.GetValueOrDefault("Channel", "?");
        var radio = w.GetValueOrDefault("Radio type", "");
        _ = double.TryParse(w.GetValueOrDefault("Receive rate (Mbps)", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var rx);

        var bandTitle = L("תדר", "Band");
        if (band.StartsWith("2.4", StringComparison.Ordinal))
            checks.Add(new(bandTitle, $"{band} · {L("ערוץ", "channel")} {channel}", Bad,
                L("רשת 2.4GHz צפופה, איטית ומתנגשת עם Bluetooth. ברוב הנתבים יש גם רשת 5GHz: התחבר אליה.", "2.4GHz is crowded, slow and clashes with Bluetooth. Most routers also offer 5GHz: connect to it.")));
        else if (band != "")
            checks.Add(new(bandTitle, $"{band} · {L("ערוץ", "channel")} {channel}", Ok));

        if (hasRssi)
        {
            var sig = L("עוצמת אות", "Signal");
            checks.Add(rssi switch
            {
                >= -60 => new(sig, $"{rssi} dBm ({w.GetValueOrDefault("Signal", "")})", Ok, L("אות חזק מאוד. המרחק מהנתב הוא לא הבעיה.", "Very strong. Distance from the router isn't the problem.")),
                >= -70 => new(sig, $"{rssi} dBm", Warn, L("אות בינוני. קרבה לנתב או פחות קירות באמצע יעזרו ליציבות.", "Medium signal. Getting closer or fewer walls in between will steady it.")),
                _ => new(sig, $"{rssi} dBm", Bad, L("אות חלש: זה מתכון לאיבוד חבילות. צריך להתקרב לנתב, לשים מגדיל טווח, או לעבור לכבל.", "Weak signal: a recipe for packet loss. Move closer, add an extender, or use a cable.")),
            });
        }

        var linkTitle = L("מהירות קישור", "Link rate");
        if (rx > 0)
            checks.Add(rx > 1201
                ? new(linkTitle, $"{rx:0} Mbps · {radio} · {L("ערוץ רחב 160MHz", "160MHz wide channel")}", Info,
                    L("ערוץ 160MHz נותן מהירות ענקית, אבל הוא רחב ולכן נתפס יותר להפרעות. למשחק צריך יציבות ולא מהירות: רוקט ליג משתמש בפחות מ-1Mbps. אם בדיקת הרשת מוצאת קפיצות, נסה להגדיר בנתב 80MHz.",
                      "160MHz is huge bandwidth but catches more interference. Games need stability, not speed: Rocket League uses under 1Mbps. If the network test finds spikes, try 80MHz on the router."))
                : new(linkTitle, $"{rx:0} Mbps · {radio}", Info));

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
        if (auto.Contains("is enabled", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(scanTitle, L("פעילה (WLAN AutoConfig)", "On (WLAN AutoConfig)"), Info,
                L("ווינדוס סורק רשתות Wi-Fi ברקע גם כשאתה מחובר. בכרטיסים של אינטל זה ידוע כגורם לקפיצת פינג כל כמה עשרות שניות. בדיקת הרשת תגלה אם זה קורה אצלך, ואם כן, מצב משחק יעצור את הסריקה רק בזמן משחק.",
                  "Windows scans for Wi-Fi networks even while connected. On Intel cards that's known to cause a ping spike every few tens of seconds. The network test shows whether it happens here; if it does, Game Mode pauses scanning only during matches.")));
        else if (auto.Contains("is disabled", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(scanTitle, L("כבויה", "Off"), Ok,
                L("שים לב: ככה ווינדוס לא יתחבר לבד לרשתות אחרות עד שתפעיל אותה שוב.", "Note: Windows won't join other networks by itself until it's back on.")));

        if (InputCheck.Enumerate().Any(d => d.Link == InputCheck.Link.Bluetooth))
            checks.Add(new(L("Bluetooth פעיל", "Bluetooth in use"), L("יש התקן Bluetooth מחובר", "A Bluetooth device is connected"), Info,
                L("בכרטיסים של אינטל ה-Bluetooth וה-Wi-Fi יושבים על אותו שבב וחולקים אנטנות. על 5GHz ההשפעה בדרך כלל קטנה, אבל שווה להריץ את בדיקת הרשת פעם עם ההתקן ופעם בלעדיו ולהשוות.",
                  "On Intel cards Bluetooth and Wi-Fi share one chip and its antennas. On 5GHz the effect is usually small, but it's worth running the network test with and without the device.")));

        checks.Add(new(router, link.Gateway.ToString(), Info));
        return new Section("network", title, "wifi", checks);
    }
}

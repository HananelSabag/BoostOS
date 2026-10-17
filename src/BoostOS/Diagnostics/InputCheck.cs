using System.Text.RegularExpressions;
using BoostOS.Core;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static partial class InputCheck
{
    public enum Link { Usb, Bluetooth, Other }
    public enum Kind { Controller, Mouse, Keyboard, Other }

    public sealed record InputDevice(string Name, Kind Kind, Link Link, string Vid, string Pid);

    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["054C"] = "Sony", ["045E"] = "Microsoft", ["046D"] = "Logitech", ["1532"] = "Razer", ["1038"] = "SteelSeries",
        ["1B1C"] = "Corsair", ["3842"] = "EVGA", ["0951"] = "HyperX", ["03F0"] = "HP", ["2DC8"] = "8BitDo", ["0E6F"] = "PDP",
        ["0F0D"] = "Hori", ["28DE"] = "Valve", ["057E"] = "Nintendo", ["0B05"] = "ASUS", ["1689"] = "Razer",
    };

    private static readonly Dictionary<string, string> Controllers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["054C:0CE6"] = "DualSense", ["054C:0DF2"] = "DualSense Edge", ["054C:05C4"] = "DualShock 4", ["054C:09CC"] = "DualShock 4",
        ["045E:0B12"] = "Xbox Series Controller", ["045E:0B13"] = "Xbox Series Controller", ["045E:02EA"] = "Xbox One Controller",
        ["045E:0B00"] = "Xbox Elite Controller 2", ["045E:0B05"] = "Xbox Elite Controller 2", ["057E:2009"] = "Switch Pro Controller",
    };

    public static List<InputDevice> Enumerate()
    {
        var entities = Wmi.Query("SELECT Name, PNPClass, DeviceID FROM Win32_PnPEntity WHERE Present = TRUE AND " +
                                 "(PNPClass = 'HIDClass' OR PNPClass = 'XnaComposite' OR PNPClass = 'Mouse' OR PNPClass = 'Keyboard')");

        // One physical device shows up as many HID collections; group them by vendor/product/link.
        var groups = new Dictionary<string, List<(string Name, string Class)>>();
        var info = new Dictionary<string, (string Vid, string Pid, Link Link)>();
        foreach (var e in entities)
        {
            if (ParseDeviceId(e.Str("DeviceID")) is not { } parsed) continue; // built-in touchpad, ACPI keyboard etc.
            var key = $"{parsed.Vid}:{parsed.Pid}:{parsed.Link}";
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add((e.Str("Name"), e.Str("PNPClass")));
            info[key] = parsed;
        }

        var result = new List<InputDevice>();
        foreach (var (key, members) in groups)
        {
            var (vid, pid, link) = info[key];
            var named = members.Select(m => m.Name).FirstOrDefault(n => !IsGeneric(n));
            var vendor = Vendors.GetValueOrDefault(vid, $"VID {vid}");

            Kind kind;
            string name;
            if (Controllers.TryGetValue($"{vid}:{pid}", out var pad) || members.Any(m => m.Class == "XnaComposite" || m.Name.Contains("game controller", StringComparison.OrdinalIgnoreCase)))
            {
                kind = Kind.Controller;
                name = pad is not null ? $"{vendor} {pad}" : named ?? $"{vendor} {L("שלט", "controller")}";
            }
            else if (named is not null && MouseWords().IsMatch(named) || named is null && members.Any(m => m.Class == "Mouse") && !members.Any(m => m.Class == "Keyboard"))
            {
                kind = Kind.Mouse;
                name = named ?? $"{vendor} {L("עכבר", "mouse")}";
            }
            else if (named is not null || members.Any(m => m.Class == "Keyboard"))
            {
                kind = named is not null && !KeyboardWords().IsMatch(named) && members.All(m => m.Class != "Keyboard") ? Kind.Other : Kind.Keyboard;
                name = named ?? $"{vendor} {L("מקלדת", "keyboard")}";
            }
            else
            {
                kind = Kind.Other;
                name = $"{vendor} ({pid})";
            }
            result.Add(new InputDevice(name, kind, link, vid, pid));
        }
        return result;
    }

    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();
        var devices = Enumerate();

        var padTitle = L("שלט", "Controller");
        var pads = devices.Where(d => d.Kind == Kind.Controller).ToList();
        if (pads.Count == 0)
            checks.Add(new(padTitle, L("לא מחובר כרגע", "Not connected"), Info, L("חבר את השלט והרץ סריקה שוב כדי לבדוק אותו.", "Connect the controller and scan again to check it.")));
        foreach (var pad in pads)
            checks.Add(pad.Link == Link.Bluetooth
                ? new(padTitle, $"{pad.Name} · Bluetooth", Warn,
                    L("Bluetooth מוסיף השהיה ולפעמים מגמגם, במיוחד בלפטופ שבו ה-Bluetooth וה-Wi-Fi חולקים את אותו שבב. בכבל USB השלט מגיב הכי מהר.",
                      "Bluetooth adds latency and sometimes stutters, especially on laptops where Bluetooth and Wi-Fi share a chip. On a USB cable the controller responds fastest."))
                : new(padTitle, $"{pad.Name} · USB", Ok, L("חיבור בכבל: ההשהיה הכי נמוכה שאפשר. בבדיקת השלט אפשר למדוד drift בסטיקים.", "Wired: the lowest latency there is. The controller test measures stick drift.")));

        var mouseTitle = L("עכבר", "Mouse");
        foreach (var mouse in devices.Where(d => d.Kind == Kind.Mouse))
            checks.Add(mouse.Link == Link.Bluetooth
                ? new(mouseTitle, $"{mouse.Name} · Bluetooth", Info,
                    L("ב-Bluetooth עכבר מדווח בערך 125 פעמים בשנייה. אם יש לו דונגל אלחוטי, הדונגל נותן עד 1000Hz.", "Over Bluetooth a mouse reports about 125 times a second. Its wireless dongle, if it has one, gives up to 1000Hz."))
                : new(mouseTitle, $"{mouse.Name} · USB", Ok));

        foreach (var kb in devices.Where(d => d.Kind == Kind.Keyboard))
            checks.Add(new(L("מקלדת", "Keyboard"), $"{kb.Name} · {(kb.Link == Link.Bluetooth ? "Bluetooth" : "USB")}", Info));

        // Bluetooth devices that are asleep only expose a generic GATT entry, so we can name the vendor but not the type.
        var others = devices.Where(d => d.Kind == Kind.Other)
            .Select(d => d.Link == Link.Bluetooth ? $"{d.Name} (Bluetooth)" : d.Name).Distinct().ToList();
        if (others.Count > 0)
            checks.Add(new(L("עוד התקנים", "Other devices"), string.Join(" · ", others), Info));

        return new Section("input", L("שלטים, עכבר ומקלדת", "Controllers & input"), "gamepad", checks);
    }

    /// <summary>
    /// Vendor, product and link from a PnP device id. Handles USB ("USB\VID_054C&amp;PID_0DF2"), classic Bluetooth HID
    /// ("…{00001124…}_VID&amp;0002054c_PID&amp;09cc") and Bluetooth LE HID ("…{00001812…}_DEV_VID&amp;021532_PID&amp;0082").
    /// </summary>
    internal static (string Vid, string Pid, Link Link)? ParseDeviceId(string id)
    {
        var vm = VidRegex().Match(id);
        if (!vm.Success) return null;
        var vid = vm.Groups[1].Value[^4..].ToUpperInvariant();
        var pm = PidRegex().Match(id);
        var pid = pm.Success ? pm.Groups[1].Value[..4].ToUpperInvariant() : "";
        var link = id.Contains("00001124-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase)
                   || id.Contains("00001812-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase)
                   || id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)
            ? Link.Bluetooth
            : id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) || id.StartsWith("HID\\VID", StringComparison.OrdinalIgnoreCase) ? Link.Usb : Link.Other;
        return (vid, pid, link);
    }

    private static bool IsGeneric(string name) =>
        name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) || name.StartsWith("USB Input Device", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Bluetooth Low Energy GATT", StringComparison.OrdinalIgnoreCase) || name.StartsWith("HID Keyboard", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft Input Configuration", StringComparison.OrdinalIgnoreCase) || name == "";

    [GeneratedRegex(@"VID[_&]([0-9A-F]{4,8})", RegexOptions.IgnoreCase)] private static partial Regex VidRegex();
    [GeneratedRegex(@"PID[_&]([0-9A-F]{4,8})", RegexOptions.IgnoreCase)] private static partial Regex PidRegex();
    [GeneratedRegex(@"mouse|basilisk|viper|deathadder|g\s?pro|g\d{3}|rival|aerox|prime|model [oid]", RegexOptions.IgnoreCase)] private static partial Regex MouseWords();
    [GeneratedRegex(@"keyboard|apex|huntsman|blackwidow|k\d{2,3}|alloy", RegexOptions.IgnoreCase)] private static partial Regex KeyboardWords();
}

using System.Runtime.InteropServices;
using BoostOS.Core;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class PowerCheck
{
    public static readonly Guid WirelessSubgroup = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    public static readonly Guid WirelessPowerSaving = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");

    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    private static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();
        var ps = SystemInformation.PowerStatus;

        if (facts.IsLaptop)
        {
            var pct = ps.BatteryLifePercent is >= 0 and <= 1 ? $" · {L("סוללה", "battery")} {ps.BatteryLifePercent * 100:0}%" : "";
            var charger = L("מטען", "Charger");
            checks.Add(ps.PowerLineStatus == PowerLineStatus.Offline
                ? new(charger, L("על סוללה", "On battery") + pct, Bad,
                    L("על סוללה הלפטופ מגביל מאוד את המעבד ואת הכרטיס. למשחק תמיד מחברים מטען.", "On battery the laptop heavily limits the CPU and GPU. Always plug in to play."))
                : new(charger, L("מחובר", "Plugged in") + pct, Ok));
        }

        var plan = L("תוכנית חשמל", "Power plan");
        var scheme = ActiveScheme();
        if (scheme is { } s)
        {
            var name = FriendlyName(s);
            if (s == Tuning.PowerPlan.Scheme)
                checks.Add(new(plan, L("BoostOS · ביצועים מלאים במטען, חיסכון בסוללה", "BoostOS · full performance plugged in, saving on battery"), Ok, null, Tuning.Fixes.Ref("power-plan")));
            else if (s == PowerSaver)
                checks.Add(new(plan, name, Bad, L("תוכנית חיסכון בחשמל חונקת את המעבד. עבור ל-Balanced עם מצב Best performance.", "Power saver starves the CPU. Switch to Balanced with Best performance.")));
            else if (s == HighPerformance || s == Ultimate)
                checks.Add(new(plan, name, Ok));
            else if (s == Balanced)
            {
                var overlay = Overlay();
                if (overlay == OverlayBestPerformance)
                    checks.Add(new(plan, $"{name} · Best performance", Ok, null, Tuning.Fixes.Ref("power-overlay")));
                else
                    checks.Add(new(plan, $"{name} · {(overlay == OverlayBestEfficiency ? "Best power efficiency" : L("מצב רגיל", "normal mode"))}",
                        overlay == OverlayBestEfficiency ? Bad : Warn,
                        L("מצב החשמל לא על Best performance, אז המעבד מוריד מהירות מהר יותר. Settings → System → Power & battery → Power mode → Best performance.",
                          "Power mode isn't Best performance, so the CPU clocks down sooner. Settings → System → Power & battery → Power mode → Best performance."),
                        Tuning.Fixes.Ref("power-overlay")));
            }
            else
                checks.Add(new(plan, name, Info));

            // Balanced keeps PCI Express, USB and Wi-Fi power saving on even on the charger: small delays for nothing.
            if (s == Balanced)
            {
                var (aspmAc, _) = ReadSetting(new("501a4d13-42af-4429-9fd1-a8218c268e20"), new("ee12f906-d277-404b-b6da-e5fa1a576df5"));
                checks.Add(new(L("תוכנית חשמל למשחק", "Gaming power plan"),
                    aspmAc is > 0 ? L("חיסכון בחשמל פעיל גם במטען", "Power saving stays on when plugged in") : L("לא מוגדרת", "Not set"), Warn,
                    L("תוכנית BoostOS: במטען המעבד מאיץ במלואו, ובלי חיסכון בחשמל ל-PCI Express, ל-USB ול-Wi-Fi (חיסכון שמוסיף השהיות קטנות). על סוללה ההאצה כבויה והחיסכון פעיל, ללפטופ קריר ולסוללה ארוכה. אפשר להחזיר בכל רגע.",
                      "The BoostOS plan: on the charger the CPU boosts fully, with no power saving on PCI Express, USB and Wi-Fi (savings that add small delays). On battery boost is off and every saving is on, for a cool laptop and a long battery. Can be undone any time."),
                    Tuning.Fixes.Ref("power-plan")));
            }
        }

        if (facts.IsLaptop)
        {
            var app = MakerApp(facts.Machine);
            if (app == "Gigabyte Control Center" && !GigabyteControlCenterInstalled())
                checks.Add(new(L("Gigabyte Control Center", "Gigabyte Control Center"), L("לא מותקן", "Not installed"), Warn,
                    L("בלי Control Center הלפטופ נשאר על פרופיל המאווררים הרגיל (Normal) ועל הספק הבסיס של הכרטיס. התקן אותו מדף התמיכה של Gigabyte לדגם שלך, ואז בחר מאווררים: Gaming, ובהגדרות הכרטיס: NVIDIA Dynamic Boost. זה השינוי הכי משמעותי לחום.",
                      "Without Control Center the laptop stays on the Normal fan profile and the GPU's base power. Install it from Gigabyte's support page for your model, then choose Fan: Gaming and GPU: NVIDIA Dynamic Boost. It's the biggest single change for heat.")));
            else
                checks.Add(new(L("מצב ביצועים של היצרן", "Maker's performance mode"), L("לא נקרא אוטומטית", "Can't be read automatically"), Info,
                    L($"ללפטופ יש מצב ביצועים משלו ({app}: Gaming / Turbo / Performance). הוא משפיע על המאווררים ועל ההספק של המעבד והכרטיס יותר מכל הגדרה של ווינדוס. למשחק מומלץ Gaming או Turbo.",
                      $"The laptop has its own performance mode ({app}: Gaming / Turbo / Performance). It affects fans and CPU/GPU power more than any Windows setting. Use Gaming or Turbo for play.")));
        }

        return new Section("power", L("חשמל וביצועים", "Power"), "bolt", checks);
    }

    private static string MakerApp(string machine)
    {
        var m = machine.ToUpperInvariant();
        return m switch
        {
            _ when m.Contains("GIGABYTE") || m.Contains("AORUS") => "Gigabyte Control Center",
            _ when m.Contains("ASUS") => "Armoury Crate",
            _ when m.Contains("LENOVO") => "Lenovo Vantage / Legion Space",
            _ when m.Contains("MSI") || m.Contains("MICRO-STAR") => "MSI Center",
            _ when m.Contains("HP") => "OMEN Gaming Hub",
            _ when m.Contains("ACER") => "PredatorSense / NitroSense",
            _ when m.Contains("DELL") || m.Contains("ALIENWARE") => "Alienware Command Center",
            _ when m.Contains("RAZER") => "Razer Synapse",
            _ => L("תוכנת היצרן", "the maker's app"),
        };
    }

    /// <summary>Gigabyte's laptop app, found by its uninstall entry or a running copy.</summary>
    private static bool GigabyteControlCenterInstalled()
    {
        foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = root.OpenSubKey(path);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    var display = app?.GetValue("DisplayName") as string ?? "";
                    if (display.Contains("Control Center", StringComparison.OrdinalIgnoreCase)
                        && (app?.GetValue("Publisher") as string ?? "").Contains("GIGABYTE", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        var running = System.Diagnostics.Process.GetProcessesByName("GCC");
        foreach (var p in running) p.Dispose();
        return running.Length > 0;
    }

    public static Guid? ActiveScheme()
    {
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { Native.LocalFree(ptr); }
    }

    public static (uint? Ac, uint? Dc) ReadSetting(Guid subgroup, Guid setting)
    {
        if (ActiveScheme() is not { } scheme) return (null, null);
        var s1 = scheme; var g1 = subgroup; var p1 = setting;
        var s2 = scheme; var g2 = subgroup; var p2 = setting;
        uint? ac = Native.PowerReadACValueIndex(IntPtr.Zero, ref s1, ref g1, ref p1, out var a) == 0 ? a : null;
        uint? dc = Native.PowerReadDCValueIndex(IntPtr.Zero, ref s2, ref g2, ref p2, out var d) == 0 ? d : null;
        return (ac, dc);
    }

    public static Guid? Overlay()
    {
        try { return Native.PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    private static string FriendlyName(Guid scheme)
    {
        uint size = 0;
        if (Native.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            return scheme.ToString();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            return Native.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                ? Marshal.PtrToStringUni(buffer) ?? scheme.ToString()
                : scheme.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

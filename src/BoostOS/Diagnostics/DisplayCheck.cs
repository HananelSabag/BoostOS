using System.Runtime.InteropServices;
using BoostOS.Core;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class DisplayCheck
{
    public sealed record DisplayInfo(
        string GdiName, string Monitor, string Adapter, string Connector, bool IsInternal, bool IsPrimary,
        int Width, int Height, int Hz, int MaxHzAtRes, int NativeWidth, int NativeHeight);

    // Monitors that go faster than their default EDID once Overclocking is switched on in the monitor's own menu.
    private static readonly Dictionary<string, int> OverclockMaxHz = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VG27AQ1A"] = 170,
    };

    public static Section Run(Facts facts)
    {
        var displays = Enumerate();
        var checks = new List<Check>();
        var sectionTitle = L("מסכים", "Displays");
        if (displays.Count == 0)
        {
            checks.Add(new(sectionTitle, L("לא הצלחתי לקרוא את המסכים", "Couldn't read the displays"), Warn));
            return new Section("display", sectionTitle, "display", checks);
        }

        foreach (var d in displays)
        {
            string T(string label) => displays.Count > 1 ? $"{label} · {d.Monitor}" : label;
            var refresh = T(L("קצב רענון", "Refresh rate"));

            checks.Add(new(displays.Count > 1 ? d.Monitor : L("מסך", "Display"),
                $"{d.Monitor} · {d.Width}×{d.Height} · {L($"חיבור {d.Connector}", d.Connector)}", Info));

            if (d.Hz + 1 < d.MaxHzAtRes)
                checks.Add(new(refresh, L($"רץ על {d.Hz}Hz מתוך {d.MaxHzAtRes}Hz", $"Running {d.Hz}Hz of {d.MaxHzAtRes}Hz"), Bad,
                    L($"המסך תומך ב-{d.MaxHzAtRes}Hz ורץ על {d.Hz}Hz. בלי להוסיף כלום מקבלים יותר פריימים על המסך. Settings → System → Display → Advanced display → Choose a refresh rate.",
                      $"The display supports {d.MaxHzAtRes}Hz but runs at {d.Hz}Hz. More frames on screen for free. Settings → System → Display → Advanced display → Choose a refresh rate.")));
            else if (OverclockMaxHz.TryGetValue(d.Monitor, out var ocHz) && ocHz > d.MaxHzAtRes)
                // Not a problem to fix: overclocked panels often trade response time (overshoot) for the extra Hz.
                checks.Add(new(refresh, $"{d.Hz}Hz · {L("המקסימום בלי Overclocking", "max without overclocking")}", Ok,
                    L($"ה-{d.Monitor} יכול להגיע ל-{ocHz}Hz עם Overclocking בתפריט של המסך, אבל זו לא חובה: ב-Overclock הרבה מסכים מקבלים overshoot וזמן תגובה פחות נקי, וההבדל בין {d.MaxHzAtRes} ל-{ocHz} קטן.",
                      $"The {d.Monitor} can reach {ocHz}Hz with Overclocking in the monitor's menu, but it's optional: overclocked panels often get overshoot and less clean response times, and {d.MaxHzAtRes} vs {ocHz} is a small step.")));
            else
                checks.Add(new(refresh, $"{d.Hz}Hz · {L("המקסימום", "maximum")}", Ok));

            if ((long)d.Width * d.Height < (long)d.NativeWidth * d.NativeHeight)
                checks.Add(new(T(L("רזולוציה", "Resolution")), $"{d.Width}×{d.Height} · {L("המקורית", "native")} {d.NativeWidth}×{d.NativeHeight}", Warn,
                    L("המסך לא ברזולוציה המקורית שלו, אז התמונה מטושטשת. אם זה בשביל FPS, עדיף להוריד רזולוציה בתוך המשחק ולא בווינדוס.",
                      "The display isn't at its native resolution, so the picture is soft. If it's for FPS, lower the resolution inside the game instead.")));

            if (facts.HasNvidia && facts.HasTwoGpus)
            {
                var wired = T(L("מחובר לכרטיס", "Driven by"));
                if (d.Adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    checks.Add(new(wired, d.Adapter, Ok,
                        L("המסך מחובר ישירות לכרטיס של NVIDIA, בלי מעבר דרך הכרטיס המובנה. זה המצב הכי טוב בלפטופ.", "Wired straight to the NVIDIA GPU, no detour through the integrated one. The best case on a laptop.")));
                else if (d.IsInternal)
                    checks.Add(new(wired, d.Adapter, Info,
                        L("מסך הלפטופ עובר דרך הכרטיס המובנה (Optimus): NVIDIA מצייר ואינטל מציג, וזה מוסיף קצת השהיה. מסך חיצוני שמחובר ליציאה של NVIDIA עוקף את זה.",
                          "The laptop panel goes through the integrated GPU (Optimus): NVIDIA draws, Intel displays, which adds a little latency. An external display on an NVIDIA port skips that.")));
                else
                    checks.Add(new(wired, d.Adapter, Warn,
                        L("המסך החיצוני מחובר ליציאה של הכרטיס המובנה, אז כל פריים עובר העתקה מ-NVIDIA דרך אינטל. נסה יציאה אחרת בלפטופ: לרוב רק חלק מהיציאות מחוברות ישירות ל-NVIDIA.",
                          "The external display is on an integrated-GPU port, so every frame is copied from NVIDIA through Intel. Try another port: usually only some are wired to NVIDIA.")));
            }
        }

        return new Section("display", sectionTitle, "display", checks);
    }

    public static List<DisplayInfo> Enumerate()
    {
        var topology = ReadTopology();
        var result = new List<DisplayInfo>();

        for (uint i = 0; ; i++)
        {
            var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var current = Native.NewDevMode();
            if (!Native.EnumDisplaySettings(dd.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref current)) continue;

            int maxHzAtRes = 0, nativeW = 0, nativeH = 0;
            for (var m = 0; ; m++)
            {
                var mode = Native.NewDevMode();
                if (!Native.EnumDisplaySettings(dd.DeviceName, m, ref mode)) break;
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                    maxHzAtRes = Math.Max(maxHzAtRes, mode.dmDisplayFrequency);
                if ((long)mode.dmPelsWidth * mode.dmPelsHeight > (long)nativeW * nativeH)
                    (nativeW, nativeH) = (mode.dmPelsWidth, mode.dmPelsHeight);
            }

            topology.TryGetValue(dd.DeviceName, out var t);
            var isInternal = t.Tech is 6 or 11 or 13 or 0x80000000u;
            var monitor = !string.IsNullOrWhiteSpace(t.Monitor) ? t.Monitor : isInternal ? L("מסך הלפטופ", "Laptop screen") : L("מסך", "Display");

            result.Add(new DisplayInfo(dd.DeviceName, monitor, dd.DeviceString, t.Found ? ConnectorName(t.Tech) : L("לא ידוע", "unknown"), isInternal,
                (dd.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                current.dmPelsWidth, current.dmPelsHeight, current.dmDisplayFrequency, maxHzAtRes, nativeW, nativeH));
        }
        return result;
    }

    private readonly record struct Target(bool Found, string Monitor, uint Tech);

    /// <summary>Maps each GDI source (\\.\DISPLAYn) to its monitor's friendly name and connector type.</summary>
    private static Dictionary<string, Target> ReadTopology()
    {
        var map = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var nPaths, out var nModes) != 0) return map;
            var paths = new Native.DISPLAYCONFIG_PATH_INFO[nPaths];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[nModes];
            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref nPaths, paths, ref nModes, modes, IntPtr.Zero) != 0) return map;

            foreach (var p in paths.Take((int)nPaths))
            {
                var source = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new()
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    },
                };
                if (Native.DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new()
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    },
                };
                var name = Native.DisplayConfigGetDeviceInfo(ref target) == 0 ? target.monitorFriendlyDeviceName : "";
                map[source.viewGdiDeviceName] = new Target(true, name, p.targetInfo.outputTechnology);
            }
        }
        catch
        {
            // Older drivers can fail CCD queries; the GDI data alone is still useful.
        }
        return map;
    }

    private static string ConnectorName(uint tech) => tech switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 or 11 or 13 or 0x80000000u => L("מובנה", "built-in"),
        10 => "DisplayPort",
        15 => "Miracast",
        16 or 17 => L("וירטואלי", "virtual"),
        18 => "DisplayPort (USB-C)",
        _ => L("אחר", "other"),
    };
}

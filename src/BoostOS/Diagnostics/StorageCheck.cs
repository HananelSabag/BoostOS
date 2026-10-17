using BoostOS.Core;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class StorageCheck
{
    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();

        foreach (var d in Wmi.Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            var bus = d.Long("BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "" };
            var sizeGb = d.Long("Size") / 1_000_000_000;
            checks.Add(DescribeDisk(d.Str("FriendlyName"), d.Long("MediaType"), bus, sizeGb));
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var freeGb = drive.AvailableFreeSpace / 1_000_000_000d;
            var pct = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
            var title = $"{L("כונן", "Drive")} {drive.Name.TrimEnd('\\')}";
            var value = $"{L($"פנויים {freeGb:0} GB", $"{freeGb:0} GB free")} · {pct:0}%";
            checks.Add(freeGb < 20 || pct < 8
                ? new(title, value, Warn, L("כונן כמעט מלא: עדכונים של משחקים נכשלים, ו-SSD מלא נהיה איטי יותר.", "Almost full: game updates fail and a full SSD slows down."))
                : new(title, value, Ok));
        }

        return new Section("storage", L("אחסון", "Storage"), "disk", checks);
    }

    internal static Check DescribeDisk(string name, long mediaType, string bus, long sizeGb)
    {
        var media = mediaType switch
        {
            3 => "HDD",
            4 => "SSD",
            5 => "SCM",
            _ => L("סוג מדיה לא ידוע", "unknown media type"),
        };
        var status = mediaType switch { 3 => Warn, 4 or 5 => Ok, _ => Info };
        return new Check(L("דיסק", "Disk"), $"{name} · {media}{(bus == "" ? "" : $" {bus}")} · {sizeGb} GB", status,
            mediaType == 3 ? L("דיסק מכני איטי בטעינות. משחקים כדאי להחזיק על SSD.", "A spinning disk loads slowly. Keep games on an SSD.") : null);
    }
}

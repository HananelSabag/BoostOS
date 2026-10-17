using BoostOS.Core;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class StorageCheck
{
    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();
        var disk = L("דיסק", "Disk");

        foreach (var d in Wmi.Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            var bus = d.Long("BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "" };
            var sizeGb = d.Long("Size") / 1_000_000_000;
            checks.Add(d.Long("MediaType") == 3
                ? new(disk, $"{d.Str("FriendlyName")} · HDD · {sizeGb} GB", Warn, L("דיסק מכני איטי בטעינות. משחקים כדאי להחזיק על SSD.", "A spinning disk loads slowly. Keep games on an SSD."))
                : new(disk, $"{d.Str("FriendlyName")} · SSD {bus} · {sizeGb} GB", Ok));
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
}

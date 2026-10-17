using BoostOS.Core;
using Microsoft.Win32;
using static BoostOS.Core.CheckStatus;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class SystemCheck
{
    public static Section Run(Facts facts)
    {
        var checks = new List<Check>();

        var cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").FirstOrDefault();
        if (cpu is not null)
            checks.Add(new(L("מעבד", "CPU"),
                $"{cpu.Str("Name")} · {L($"{cpu.Long("NumberOfCores")} ליבות / {cpu.Long("NumberOfLogicalProcessors")} תהליכונים", $"{cpu.Long("NumberOfCores")} cores / {cpu.Long("NumberOfLogicalProcessors")} threads")}", Info));

        var load = facts.CpuLoad;
        var loadTitle = L("עומס מעבד במנוחה", "CPU load at idle");
        checks.Add(load switch
        {
            < 20 => new(loadTitle, $"{load:0}%", Ok),
            < 45 => new(loadTitle, $"{load:0}%", Warn,
                L("יש תוכנות שעובדות ברקע גם כשלא עושים כלום. רוקט ליג נשען בעיקר על המעבד, אז כל אחוז פנוי עוזר.",
                  "Something keeps working in the background even at idle. Rocket League leans on the CPU, so every free percent helps.")),
            _ => new(loadTitle, $"{load:0}%", Bad,
                L("המעבד עמוס עוד לפני שהמשחק התחיל. בדוק את 'תהליכים ברקע' למטה.", "The CPU is busy before the game even starts. See 'Background apps' below.")),
        });

        var sticks = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        if (sticks.Count > 0)
        {
            var gb = (int)Math.Round(sticks.Sum(s => s.Long("Capacity")) / 1024d / 1024 / 1024);
            var ramTitle = L("זיכרון RAM", "Memory");
            checks.Add(gb >= 16
                ? new(ramTitle, $"{gb} GB", Ok)
                : new(ramTitle, $"{gb} GB", gb >= 8 ? Warn : Bad,
                    L("פחות מ-16GB: ווינדוס, דפדפן ודיסקורד יחד עם המשחק כבר לוחצים על הזיכרון.", "Under 16GB: Windows, a browser and Discord next to the game already squeeze memory.")));

            var chTitle = L("ערוצי זיכרון", "Memory channels");
            checks.Add(sticks.Count >= 2
                ? new(chTitle, L($"{sticks.Count} כרטיסים · Dual Channel", $"{sticks.Count} sticks · Dual Channel"), Ok)
                : new(chTitle, L("כרטיס אחד · Single Channel", "One stick · Single Channel"), Bad,
                    L("עם כרטיס זיכרון אחד המעבד מקבל חצי מרוחב הפס. במשחקים שתלויים במעבד, כמו רוקט ליג, זה מוריד FPS בצורה מורגשת. כדאי להוסיף כרטיס זהה.",
                      "With one memory stick the CPU gets half the bandwidth. In CPU-bound games like Rocket League that costs noticeable FPS. Add a matching stick.")));

            var rated = sticks.Max(s => s.Long("Speed"));
            var running = sticks.Where(s => s.Long("ConfiguredClockSpeed") > 0).Select(s => s.Long("ConfiguredClockSpeed")).DefaultIfEmpty(rated).Min();
            var speedTitle = L("מהירות זיכרון", "Memory speed");
            if (rated > 0)
                checks.Add(running >= rated
                    ? new(speedTitle, $"{running} MT/s · {L("מלא", "full speed")}", Ok)
                    : facts.IsLaptop
                        ? new(speedTitle, L($"{running} מתוך {rated} MT/s", $"{running} of {rated} MT/s"), Info,
                            L("בלפטופ המהירות נקבעת בדרך כלל על ידי היצרן ואי אפשר לשנות אותה.", "On a laptop the maker usually sets this and it can't be changed."))
                        : new(speedTitle, L($"{running} מתוך {rated} MT/s", $"{running} of {rated} MT/s"), Warn,
                            L("הזיכרון לא רץ במהירות שלו. כדאי להפעיל XMP/EXPO ב-BIOS.", "Memory isn't running at its rated speed. Turn on XMP/EXPO in the BIOS.")));
        }

        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        if (board is not null)
            checks.Add(new(L("לוח אם", "Motherboard"), $"{board.Str("Manufacturer")} {board.Str("Product")}", Info));

        var os = Wmi.Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (os is not null)
        {
            var displayVersion = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string;
            checks.Add(new("Windows", $"{os.Str("Caption").Replace("Microsoft ", "")} {displayVersion} (build {os.Str("BuildNumber")})", Info));
        }

        return new Section("system", L("המחשב", "The PC"), "chip", checks);
    }
}

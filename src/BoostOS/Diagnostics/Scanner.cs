using BoostOS.Core;
using static BoostOS.Core.I18n;

namespace BoostOS.Diagnostics;

internal static class Scanner
{
    private static readonly (string Id, string Icon, Func<Facts, Section> Run)[] Steps =
    [
        ("system", "chip", SystemCheck.Run),
        ("display", "display", DisplayCheck.Run),
        ("gpu", "gpu", GpuCheck.Run),
        ("power", "bolt", PowerCheck.Run),
        ("network", "wifi", NetworkCheck.Run),
        ("rocketleague", "car", RocketLeague.Run),
        ("windows", "windows", WindowsCheck.Run),
        ("background", "layers", BackgroundCheck.Run),
        ("input", "gamepad", InputCheck.Run),
        ("storage", "disk", StorageCheck.Run),
    ];

    /// <summary>Runs every check in parallel. A check that throws becomes a single "couldn't read" line instead of failing the scan.</summary>
    public static async Task<ScanReport> RunAsync()
    {
        var facts = await Task.Run(Facts.Collect);
        var sections = await Task.WhenAll(Steps.Select(step => Task.Run(() =>
        {
            try { return step.Run(facts); }
            catch (Exception ex)
            {
                return new Section(step.Id, step.Id, step.Icon, [new Check(L("שגיאה בבדיקה", "Check failed"), ex.Message, CheckStatus.Info)]);
            }
        })));
        return new ScanReport(DateTime.Now, Score(sections), facts.Machine, sections);
    }

    internal static int Score(IEnumerable<Section> sections)
    {
        var penalty = sections.SelectMany(s => s.Checks).Sum(c => c.Status switch
        {
            CheckStatus.Bad => 12,
            CheckStatus.Warn => 5,
            _ => 0,
        });
        return Math.Clamp(100 - penalty, 0, 100);
    }
}

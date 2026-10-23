using BoostOS.Core;
using BoostOS.Diagnostics;

namespace BoostOS.Tuning;

/// <summary>
/// The BoostOS power plan: a copy of Balanced with two clear personalities. On the charger it lets the CPU boost
/// aggressively and switches off the power saving that adds delay (PCI Express link states, USB suspend, Wi-Fi
/// power saving). On battery it turns CPU boost off and keeps every saving on, for a cooler, longer-lasting laptop.
/// Creating and switching plans works without administrator rights; undo switches back and removes the copy.
/// </summary>
internal static class PowerPlan
{
    public static readonly Guid Scheme = new("b0057050-6a3e-4c2d-9f1e-7e5d0b005705");

    private const string Processor = "54533251-82be-4824-96c1-47b60b740d00";

    /// <summary>(subgroup, setting, plugged in, on battery). Values follow powercfg's own indexes.</summary>
    internal static readonly (string Sub, string Setting, uint Ac, uint Dc, string What)[] Values =
    [
        (Processor, "be337238-0d82-4146-a960-4f3749d470c7", 2, 0, "CPU boost: aggressive / off"),
        (Processor, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 25, 70, "Energy performance preference"),
        (Processor, "893dee8e-2bef-41e0-89c6-b55d0929964c", 5, 5, "Minimum processor state"),
        (Processor, "bc5038f7-23e0-4960-96da-33abaf5935ec", 100, 100, "Maximum processor state"),
        ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 2, "PCI Express link state: off / maximum savings"),
        ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1, "USB selective suspend"),
        ("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, 2, "Wi-Fi power saving"),
        ("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0, 600, "Turn off disks after (seconds)"),
    ];

    public static bool IsActive => PowerCheck.ActiveScheme() == Scheme;

    /// <summary>Creates (or refreshes) the plan, switches to it and returns the plan that was active before.</summary>
    public static string Apply()
    {
        var before = PowerCheck.ActiveScheme() ?? PowerCheck.Balanced;
        if (Shell.Run("powercfg", $"/query {Scheme}") is null)
            Ensure(Shell.Run("powercfg", $"/duplicatescheme {PowerCheck.Balanced} {Scheme}"), "create the BoostOS power plan");
        Shell.Run("powercfg", $"/changename {Scheme} \"BoostOS\" \"Plugged in: full performance. On battery: cool and long-lasting.\"");
        foreach (var (sub, setting, ac, dc, _) in Values)
        {
            Ensure(Shell.Run("powercfg", $"/setacvalueindex {Scheme} {sub} {setting} {ac}"), "set a plugged-in value");
            Ensure(Shell.Run("powercfg", $"/setdcvalueindex {Scheme} {sub} {setting} {dc}"), "set a battery value");
        }
        Ensure(Shell.Run("powercfg", $"/setactive {Scheme}"), "switch to the BoostOS power plan");
        // Keep the plan that was there before BoostOS, even when Apply runs again on our own plan.
        return (before == Scheme ? PowerCheck.Balanced : before).ToString();
    }

    public static void Undo(string? before)
    {
        var target = Guid.TryParse(before, out var g) ? g : PowerCheck.Balanced;
        if (Shell.Run("powercfg", $"/setactive {target}") is null) Shell.Run("powercfg", $"/setactive {PowerCheck.Balanced}");
        Shell.Run("powercfg", $"/delete {Scheme}");
    }

    private static void Ensure(string? output, string what)
    {
        if (output is null) throw new InvalidOperationException(I18n.L($"ווינדוס לא אישר: {what}", $"Windows refused to {what}"));
    }
}

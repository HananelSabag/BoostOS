namespace BoostOS.Core;

/// <summary>
/// Two languages, side by side at the call site: L("עברית", "English"). Keeping both strings together
/// means a check can never ship in one language only.
/// </summary>
internal static class I18n
{
    public static bool English { get; set; }

    public static string L(string he, string en) => English ? en : he;
}

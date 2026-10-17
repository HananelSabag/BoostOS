using System.Management;

namespace BoostOS.Core;

/// <summary>Thin WMI helpers. Every call swallows errors: a missing class must never break the whole scan.</summary>
internal static class Wmi
{
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string Str(this ManagementBaseObject o, string prop)
    {
        try { return o[prop]?.ToString()?.Trim() ?? ""; }
        catch { return ""; }
    }

    public static long Long(this ManagementBaseObject o, string prop)
    {
        try { return o[prop] is { } v ? Convert.ToInt64(v) : 0; }
        catch { return 0; }
    }
}

using System.Diagnostics;

namespace BoostOS.Core;

internal static class Shell
{
    /// <summary>Runs a console tool hidden and returns its stdout, or null if it is missing or times out.</summary>
    public static string? Run(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            return output.Result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses "Key : Value" lines (netsh style) into a dictionary.</summary>
    public static Dictionary<string, string> ParseColonList(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (text is null) return map;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var i = line.IndexOf(" : ", StringComparison.Ordinal);
            if (i <= 0) continue;
            var key = line[..i].Trim();
            if (!map.ContainsKey(key)) map[key] = line[(i + 3)..].Trim();
        }
        return map;
    }
}

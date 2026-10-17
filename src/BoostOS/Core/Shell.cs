using System.Diagnostics;

namespace BoostOS.Core;

internal static class Shell
{
    /// <summary>Runs a console tool hidden and returns its stdout, or null if it fails, is missing or times out.</summary>
    public static string? Run(string exe, string args, int timeoutMs = 8000)
        => RunAsync(exe, args, timeoutMs).GetAwaiter().GetResult();

    private static async Task<string?> RunAsync(string exe, string args, int timeoutMs)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMs);
            if (!process.Start()) return null;
            // Both pipes must be drained concurrently: a full stderr pipe can block the tool before it exits.
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).WaitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
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

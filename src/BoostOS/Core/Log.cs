namespace BoostOS.Core;

/// <summary>
/// A small text log next to the settings (log.txt). The background recorder has no window, so this is where a
/// failure leaves a trace. Both copies append to the same file; one old file is kept when it grows past 512 KB.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    /// <summary>"app" for the window, "recorder" for the background recorder.</summary>
    public static string Role { get; set; } = "app";

    /// <summary>Where log.txt goes; tests point it at a temporary folder so they never write to the real log.</summary>
    internal static string? Folder { get; set; }

    public static string FilePath => Path.Combine(Folder ?? AppSettings.DataDir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var folder = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    try { File.Move(FilePath, Path.Combine(folder, "log.old.txt"), overwrite: true); } catch (IOException) { }
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Role,-8}  {message}");
            }
        }
        catch { /* logging must never break what it watches */ }
    }

    public static void Error(string what, Exception error) =>
        Write($"{what} failed: {error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}");
}

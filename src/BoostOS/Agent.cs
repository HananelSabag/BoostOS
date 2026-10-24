using System.Diagnostics;
using BoostOS.Core;
using BoostOS.Game;
using Microsoft.Win32;

namespace BoostOS;

/// <summary>
/// The background recorder: BoostOS without a window. It starts with Windows (HKCU Run), looks for a running game
/// every four seconds and, when one starts from anywhere (Epic, Steam, a shortcut, BoostOS), turns Game Mode on,
/// records the session and puts everything back afterwards. No WebView, no tray icon; a few MB of memory and a
/// process snapshot every few seconds.
/// </summary>
internal sealed class Agent : ApplicationContext
{
    private const string MutexName = @"Local\BoostOS.Agent";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "BoostOS Recorder";

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    private readonly GameWatcher watcher;
    private AppSettings settings = AppSettings.Load();
    private DateTime settingsStamp;
    private bool busy;

    public static bool IsRunning() => Mutex.TryOpenExisting(MutexName, out var m) && Dispose(m);
    private static bool Dispose(Mutex m) { m.Dispose(); return true; }

    public static int Run()
    {
        using var single = new Mutex(true, MutexName, out var first);
        if (!first) return 0;
        // A game session left half-done by a crash or a shutdown: put the PC back once nothing is running.
        try { if (GameMode.IsActive && GameLibrary.Running() is null) GameMode.Restore(LastNet()); } catch { }
        var settings = AppSettings.Load();
        if (!settings.BackgroundRecorder) return 0;
        Sync(settings); // keeps the start-with-Windows entry pointing at this copy
        Application.Run(new Agent());
        return 0;
    }

    private Agent()
    {
        watcher = new GameWatcher(() => settings, LastNet);
        timer.Tick += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            try
            {
                ReloadSettings();
                if (!settings.BackgroundRecorder && watcher.Current is null) { ExitThread(); return; }
                await Task.Run(watcher.Tick);
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            finally { busy = false; }
        };
        timer.Start();
    }

    private void ReloadSettings()
    {
        var path = Path.Combine(AppSettings.DataDir, "settings.json");
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (stamp == settingsStamp) return;
        settingsStamp = stamp;
        settings = AppSettings.Load();
    }

    internal static bool? LastNet()
    {
        try
        {
            var path = Path.Combine(AppSettings.DataDir, "last-net.json");
            return File.Exists(path) ? System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("periodic").GetBoolean() : null;
        }
        catch { return null; }
    }

    protected override void ExitThreadCore()
    {
        timer.Dispose();
        watcher.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>Starts with Windows or not, as the setting says, and runs now when it should.</summary>
    public static void Sync(AppSettings settings)
    {
        // Only an installed copy registers itself, never a build running from a project's bin folder.
        if (Environment.ProcessPath is not { } path || path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath!;
            if (settings.BackgroundRecorder) key.SetValue(RunValue, $"\"{exe}\" --agent");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            if (settings.BackgroundRecorder && !IsRunning())
                Process.Start(new ProcessStartInfo(exe, "--agent") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }
}

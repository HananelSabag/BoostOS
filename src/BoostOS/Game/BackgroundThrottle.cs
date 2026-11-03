using System.Runtime.InteropServices;
using BoostOS.Core;

namespace BoostOS.Game;

/// <summary>
/// Efficiency mode for background apps during a game: the same switch as Task Manager's leaf icon (EcoQoS plus idle
/// priority). The apps keep running and keep their windows; they just stop competing with the game for the CPU.
/// Only a known list of heavy desktop apps is touched, never a game, an anti-cheat, the system or anything that
/// plays sound in real time (voice chat, music). Everything goes back when the game closes.
/// </summary>
internal static class BackgroundThrottle
{
    public sealed record Throttled(int Pid, long CreateTime, string Name, int OldPriority)
    {
        /// <summary>Lowered because it was measured working hard, not because it is on the known list.</summary>
        public bool Busy { get; init; }
    }

    // Heavy apps people leave open while they play. Lower case, without ".exe".
    internal static readonly HashSet<string> Targets =
    [
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc",
        "chatgpt", "claude", "codex", "node", "python", "pythonw", "java", "javaw",
        "qemu-system-x86_64", "qemu-system-x86_64-headless",
        "code", "cursor", "devenv", "rider64", "idea64", "studio64", "githubdesktop",
        "whatsapp", "whatsapp.root", "telegram", "slack", "ms-teams", "teams", "signal", "zoom",
        "dropbox", "googledrivefs", "winword", "excel", "powerpnt", "outlook", "olk", "notion", "obsidian",
        "epicwebhelper", "adobe desktop service", "creative cloud",
    ];

    // Never, even if a name above ever collides with one of these.
    private static readonly HashSet<string> Never =
    [
        "boostos", "easyanticheat", "easyanticheat_eos", "beservice", "vgc", "faceitclient", "discord", "spotify",
        "audiodg", "dwm", "explorer", "csrss", "winlogon", "system", "idle",
    ];

    private const int ProcessPowerThrottling = 4;
    private const uint ThrottleExecutionSpeed = 0x1;
    private const uint SetInformation = 0x0200, QueryLimited = 0x1000;
    private const uint IdlePriority = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint GetPriorityClass(IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool SetPriorityClass(IntPtr process, uint priority);
    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    // Never lowered even when measured busy: anything that plays or carries sound and voice, records or streams the
    // screen, drives controllers or lighting, remote desktop, and the launchers and overlays games rely on.
    private static readonly HashSet<string> NeverBusy =
    [
        "discord", "spotify", "teamspeak", "ts3client_win64", "ts6client", "mumble", "voicemeeter", "voicemeeterpro",
        "voicemeeter8", "voicemeeter8x64", "audiorelay", "steelseriesgg", "steelseriessonar", "steelseriesengine",
        "obs64", "obs32", "streamlabs obs", "xsplit.core", "nvcontainer", "nvidia share", "nvidia overlay", "nvidia_overlay",
        "nvidia app", "nvsphelper64", "radeonsoftware", "amdrsserv",
        "devicehub", "razer synapse 3", "razercentralservice", "ds4windows", "dsx", "x360ce", "antimicrox", "joytokey",
        "anydesk", "teamviewer", "vmware-view", "vmware-remotemks", "parsecd", "sunshine", "moonlight",
        "epicgameslauncher", "eosoverlayrenderer-win64-shipping", "steam", "steamwebhelper", "gameoverlayui", "bakkesmod",
        "msedgewebview2", "taskmgr", "conhost",
    ];

    internal static bool IsTarget(string name, IReadOnlySet<string> gameProcesses) =>
        Targets.Contains(name) && !Never.Contains(name) && !gameProcesses.Contains(name);

    internal static bool MayQuietWhenBusy(string name, IReadOnlySet<string> gameProcesses) =>
        !Never.Contains(name) && !NeverBusy.Contains(name) && !gameProcesses.Contains(name);

    /// <summary>Puts every running target (not already done) into efficiency mode and returns what changed.</summary>
    public static List<Throttled> Apply(IEnumerable<string> gameProcesses, IReadOnlyCollection<Throttled> already)
    {
        var game = new HashSet<string>(gameProcesses);
        var done = new HashSet<(int, long)>(already.Select(t => (t.Pid, t.CreateTime)));
        var result = new List<Throttled>();
        foreach (var e in ProcessInfo.Snapshot())
        {
            if (!IsTarget(e.Name, game) || done.Contains((e.Pid, e.CreateTime))) continue;
            if (Lower(e) is { } lowered) result.Add(lowered);
        }
        return result;
    }

    private static readonly object BaselineGate = new();
    private static Dictionary<(int, long), long>? baseline;
    private static long baselineAt;

    /// <summary>Notes every process's CPU time now: the starting line the next <see cref="ApplyBusy"/> measures from.</summary>
    public static void Mark()
    {
        var snapshot = ProcessInfo.Snapshot();
        lock (BaselineGate)
        {
            baseline = snapshot.ToDictionary(e => (e.Pid, e.CreateTime), e => e.CpuTime);
            baselineAt = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Lowers apps measured working hard since the last look: any app of this user that took <paramref name="minShare"/>%
    /// or more of the whole CPU, whatever it is, except the game, Windows' own programs and everything in NeverBusy.
    /// </summary>
    /// <param name="lower">How to lower one process; a dry run passes one that only records (--busy-test).</param>
    public static List<Throttled> ApplyBusy(IEnumerable<string> gameProcesses, IReadOnlyCollection<Throttled> already, double minShare = 2,
        Func<ProcessInfo.Entry, Throttled?>? lower = null)
    {
        lower ??= Lower;
        var snapshot = ProcessInfo.Snapshot();
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        Dictionary<(int, long), long>? before;
        long since;
        lock (BaselineGate)
        {
            before = baseline;
            since = baselineAt;
            baseline = snapshot.ToDictionary(e => (e.Pid, e.CreateTime), e => e.CpuTime);
            baselineAt = now;
        }
        if (before is null) return [];
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(since, now);
        if (elapsed < TimeSpan.FromSeconds(10)) return []; // too short to call anything busy
        var wall = elapsed.Ticks * (double)Environment.ProcessorCount; // all cores, in the same 100 ns units as CPU time

        var game = new HashSet<string>(gameProcesses);
        var done = new HashSet<(int, long)>(already.Select(t => (t.Pid, t.CreateTime)));
        var mySession = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var result = new List<Throttled>();
        foreach (var e in snapshot)
        {
            if (e.SessionId != mySession || e.Pid == Environment.ProcessId || done.Contains((e.Pid, e.CreateTime))) continue;
            if (!MayQuietWhenBusy(e.Name, game) || !before.TryGetValue((e.Pid, e.CreateTime), out var cpuBefore)) continue;
            if (100d * (e.CpuTime - cpuBefore) / wall < minShare) continue;
            // Windows' own programs (Explorer, the shell, input, Defender's tray) are never touched.
            if (ProcessInfo.ImagePath(e.Pid) is not { } path || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
            if (lower(e) is { } lowered) result.Add(lowered with { Busy = true });
        }
        return result;
    }

    /// <summary>Efficiency mode for one process: EcoQoS and idle priority, remembering the priority it had.</summary>
    private static Throttled? Lower(ProcessInfo.Entry e)
    {
        var h = OpenProcess(SetInformation | QueryLimited, false, e.Pid);
        if (h == IntPtr.Zero) return null; // another user's or a protected process: leave it
        try
        {
            var old = GetPriorityClass(h);
            if (old == 0 || old == IdlePriority) return null; // unreadable, or the user already set it
            var state = new PowerThrottlingState { Version = 1, ControlMask = ThrottleExecutionSpeed, StateMask = ThrottleExecutionSpeed };
            SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
            return SetPriorityClass(h, IdlePriority) ? new Throttled(e.Pid, e.CreateTime, e.Name, (int)old) : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Gives each process still running its old priority back and hands power decisions back to Windows.</summary>
    public static void Restore(IEnumerable<Throttled> list)
    {
        var alive = ProcessInfo.Snapshot().ToDictionary(e => (e.Pid, e.CreateTime));
        foreach (var t in list)
        {
            if (!alive.ContainsKey((t.Pid, t.CreateTime))) continue; // exited, or the id now belongs to another process
            var h = OpenProcess(SetInformation | QueryLimited, false, t.Pid);
            if (h == IntPtr.Zero) continue;
            try
            {
                var state = new PowerThrottlingState { Version = 1, ControlMask = 0, StateMask = 0 };
                SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
                SetPriorityClass(h, (uint)t.OldPriority);
            }
            finally { CloseHandle(h); }
        }
    }
}

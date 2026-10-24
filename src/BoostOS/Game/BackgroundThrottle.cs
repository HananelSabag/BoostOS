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
    public sealed record Throttled(int Pid, long CreateTime, string Name, int OldPriority);

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

    internal static bool IsTarget(string name, IReadOnlySet<string> gameProcesses) =>
        Targets.Contains(name) && !Never.Contains(name) && !gameProcesses.Contains(name);

    /// <summary>Puts every running target (not already done) into efficiency mode and returns what changed.</summary>
    public static List<Throttled> Apply(IEnumerable<string> gameProcesses, IReadOnlyCollection<Throttled> already)
    {
        var game = new HashSet<string>(gameProcesses);
        var done = new HashSet<(int, long)>(already.Select(t => (t.Pid, t.CreateTime)));
        var result = new List<Throttled>();
        foreach (var e in ProcessInfo.Snapshot())
        {
            if (!IsTarget(e.Name, game) || done.Contains((e.Pid, e.CreateTime))) continue;
            var h = OpenProcess(SetInformation | QueryLimited, false, e.Pid);
            if (h == IntPtr.Zero) continue; // another user's or a protected process: leave it
            try
            {
                var old = GetPriorityClass(h);
                if (old == 0 || old == IdlePriority) continue; // unreadable, or the user already set it
                var state = new PowerThrottlingState { Version = 1, ControlMask = ThrottleExecutionSpeed, StateMask = ThrottleExecutionSpeed };
                SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
                if (SetPriorityClass(h, IdlePriority)) result.Add(new Throttled(e.Pid, e.CreateTime, e.Name, (int)old));
            }
            finally { CloseHandle(h); }
        }
        return result;
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

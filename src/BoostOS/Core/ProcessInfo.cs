using System.Runtime.InteropServices;
using System.Text;

namespace BoostOS.Core;

/// <summary>
/// The process list and image paths straight from the kernel's own snapshot (NtQuerySystemInformation).
/// No process is ever opened, so a game protected by Easy Anti-Cheat is never touched: this is the same
/// information Task Manager shows, read without a handle.
/// </summary>
internal static class ProcessInfo
{
    /// <summary>One process: id, image name (lower case, no ".exe"), start time and total CPU time (100 ns units).</summary>
    public sealed record Entry(int Pid, string Name, long CreateTime, long CpuTime);

    private const int SystemProcessInformation = 5;
    private const int SystemProcessIdInformation = 88;
    private const uint InfoLengthMismatch = 0xC0000004;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

    public static List<Entry> Snapshot()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemProcessInformation, buf, size, out var needed);
                if (status == InfoLengthMismatch) { size = Math.Max(size * 2, needed + (64 << 10)); continue; }
                return status == 0 ? Parse(buf) : [];
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        return [];
    }

    // SYSTEM_PROCESS_INFORMATION (x64): CreateTime @32, UserTime @40, KernelTime @48, ImageName @56, UniqueProcessId @80.
    private static List<Entry> Parse(IntPtr buf)
    {
        var list = new List<Entry>(400);
        var offset = 0;
        while (true)
        {
            var entry = buf + offset;
            var next = Marshal.ReadInt32(entry);
            var pid = (int)Marshal.ReadInt64(entry, 80);
            var nameLength = (ushort)Marshal.ReadInt16(entry, 56);
            var namePtr = Marshal.ReadIntPtr(entry, 64);
            var name = pid == 0 ? "idle" : nameLength > 0 && namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr, nameLength / 2) : "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            list.Add(new Entry(pid, name.ToLowerInvariant(), Marshal.ReadInt64(entry, 32),
                Marshal.ReadInt64(entry, 40) + Marshal.ReadInt64(entry, 48)));
            if (next == 0) break;
            offset += next;
        }
        return list;
    }

    /// <summary>Full path of a process's executable, without opening the process (SystemProcessIdInformation).</summary>
    public static string? ImagePath(int pid)
    {
        const int nameBytes = 2048;
        var info = Marshal.AllocHGlobal(24);  // { HANDLE ProcessId; UNICODE_STRING ImageName; }
        var name = Marshal.AllocHGlobal(nameBytes);
        try
        {
            Marshal.WriteInt64(info, 0, pid);
            Marshal.WriteInt16(info, 8, 0);
            Marshal.WriteInt16(info, 10, nameBytes);
            Marshal.WriteInt32(info, 12, 0);
            Marshal.WriteIntPtr(info, 16, name);
            if (NtQuerySystemInformation(SystemProcessIdInformation, info, 24, out _) != 0) return null;
            var length = (ushort)Marshal.ReadInt16(info, 8);
            return length == 0 ? null : ToDosPath(Marshal.PtrToStringUni(name, length / 2));
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(info);
        }
    }

    private static Dictionary<string, string>? devices;

    /// <summary>"\Device\HarddiskVolume3\Games\x.exe" → "C:\Games\x.exe".</summary>
    internal static string ToDosPath(string ntPath, IReadOnlyDictionary<string, string>? map = null)
    {
        map ??= devices ??= DeviceMap();
        foreach (var (device, drive) in map)
            if (ntPath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                return drive + ntPath[device.Length..];
        return ntPath;
    }

    private static Dictionary<string, string> DeviceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var target = new StringBuilder(512);
        foreach (var root in Environment.GetLogicalDrives())
        {
            var drive = root.TrimEnd('\\');
            if (QueryDosDevice(drive, target, target.Capacity) != 0) map[target.ToString()] = drive;
        }
        return map;
    }
}

using System.Runtime.InteropServices;
using System.Text;

namespace BoostOS.Tuning;

/// <summary>
/// Reads and changes the NVIDIA driver profile for Rocket League (the same per-game settings the NVIDIA Control
/// Panel edits). This talks to the driver's settings database only; it never touches the game process.
/// Ids, struct layouts and status codes come from NVIDIA's open NVAPI headers (github.com/NVIDIA/nvapi).
/// Reading works as a normal user; saving needs administrator rights.
/// </summary>
internal static class Nvidia
{
    public const string GameExe = "rocketleague.exe";

    public const uint PowerManagementId = 0x1057EB71;  // PREFERRED_PSTATE_ID, "Power management mode"
    public const uint PreferMaxPerformance = 1;        // PREFERRED_PSTATE_PREFER_MAX
    public const uint PrerenderLimitId = 0x007BA09E;   // PRERENDERLIMIT_ID; 1 = "Low Latency Mode: On"
    public const uint LowLatencyOn = 1;

    public static readonly (uint Id, uint Value)[] Recommended =
    [
        (PowerManagementId, PreferMaxPerformance),
        (PrerenderLimitId, LowLatencyOn),
    ];

    public sealed record SettingValue(uint Id, uint Value, bool UserSet);

    public sealed record Snapshot(string Profile, List<SettingValue> Values);

    public sealed class NvapiException(string message, int status = 0) : Exception(message)
    {
        public int Status { get; } = status;
        public bool NeedsAdmin => Status == InvalidUserPrivilege;
    }

    private const int Ok = 0, IncompatibleStructVersion = -9, InvalidUserPrivilege = -137, SettingNotFound = -160, ExecutableNotFound = -166;

    // ---- Public operations ----

    /// <summary>Current values of the recommended settings in Rocket League's profile, or null when there is no NVIDIA driver.</summary>
    public static Snapshot? Read()
    {
        using var s = DrsSession.Open();
        if (s is null) return null;
        var (profile, name) = s.FindGameProfile(create: false);
        if (profile == IntPtr.Zero) return new Snapshot("", Recommended.Select(r => Default(r.Id)).ToList());
        return new Snapshot(name, Recommended.Select(r => s.Get(profile, r.Id) ?? Default(r.Id)).ToList());
    }

    // Driver defaults (PREFERRED_PSTATE_DEFAULT = Optimal power, PRERENDERLIMIT_DEFAULT = application controlled).
    private static SettingValue Default(uint id) => new(id, id == PowerManagementId ? 5u : 0u, false);

    public static string Describe(SettingValue v, bool english) => v.Id switch
    {
        PowerManagementId => v.Value switch
        {
            0 => english ? "Adaptive" : "Adaptive (מסתגל)",
            1 => english ? "Prefer maximum performance" : "Prefer maximum performance (ביצועים מקסימליים)",
            3 => english ? "Prefer consistent performance" : "Prefer consistent performance",
            5 => english ? "Optimal power (default)" : "Optimal power (ברירת מחדל)",
            _ => english ? "Normal" : "רגיל",
        },
        PrerenderLimitId => v.Value switch
        {
            0 => english ? "Off (game decides)" : "כבוי (המשחק מחליט)",
            1 => english ? "On" : "פעיל (On)",
            _ => english ? $"{v.Value} frames" : $"{v.Value} פריימים",
        },
        _ => v.Value.ToString(),
    };

    /// <summary>Applies the recommended values and returns what was there before, for undo.</summary>
    public static Snapshot Apply()
    {
        using var s = DrsSession.Open() ?? throw new NvapiException("NVIDIA driver not found");
        var (profile, name) = s.FindGameProfile(create: true);
        var before = new Snapshot(name, Recommended.Select(r => s.Get(profile, r.Id) ?? Default(r.Id)).ToList());
        foreach (var (id, value) in Recommended) s.Set(profile, id, value);
        s.Save();
        return before;
    }

    /// <summary>Puts back what <see cref="Apply"/> found: the user's own value, or the driver default.</summary>
    public static void Restore(Snapshot before)
    {
        using var s = DrsSession.Open() ?? throw new NvapiException("NVIDIA driver not found");
        var (profile, _) = s.FindGameProfile(create: false);
        if (profile == IntPtr.Zero) return;
        foreach (var v in before.Values)
        {
            if (v.UserSet) s.Set(profile, v.Id, v.Value);
            else s.RestoreDefault(profile, v.Id);
        }
        s.Save();
    }

    /// <summary>Raw fields of each recommended setting, for troubleshooting (BoostOS.exe --nvidia-json).</summary>
    public static object Dump()
    {
        using var s = DrsSession.Open();
        if (s is null) return new { driver = false };
        var (profile, name) = s.FindGameProfile(create: false);
        return new { driver = true, profile = name, found = profile != IntPtr.Zero, settings = profile == IntPtr.Zero ? [] : Recommended.Select(r => s.Raw(profile, r.Id)).ToArray() };
    }

    public static bool IsRecommended(Snapshot snap) =>
        Recommended.All(r => snap.Values.Any(v => v.Id == r.Id && v.Value == r.Value));

    // ---- NVAPI plumbing ----

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgsFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionFn(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionFn(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindAppFn(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr application);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileSettingFn(IntPtr session, IntPtr profile, uint settingId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfileFn(IntPtr session, IntPtr profileInfo, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateAppFn(IntPtr session, IntPtr profile, IntPtr application);

    private static T Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        if (p == IntPtr.Zero) throw new NvapiException($"NVAPI function 0x{id:X8} not available");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private static void Check(int status, string what)
    {
        if (status != Ok) throw new NvapiException($"{what} failed ({status})", status);
    }

    private const int UnicodeBytes = 2048 * 2; // NvAPI_UnicodeString

    // NVDRS_SETTING: newer headers added a 64-bit member to its value unions (12328 bytes), older drivers expect 12320.
    private static readonly int[] SettingSizes = [12328, 12320];
    // NVDRS_APPLICATION V4..V1.
    private static readonly (int Size, int Ver)[] AppVersions = [(20492, 4), (16396, 3), (16392, 2), (12296, 1)];
    private const int ProfileSize = 4116; // NVDRS_PROFILE_V1

    private static uint Version(int size, int ver) => (uint)size | ((uint)ver << 16);

    private static void WriteUnicode(IntPtr buffer, int offset, string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        Marshal.Copy(bytes, 0, buffer + offset, Math.Min(bytes.Length, UnicodeBytes));
    }

    private static string ReadUnicode(IntPtr buffer, int offset) => Marshal.PtrToStringUni(buffer + offset) ?? "";

    private sealed class DrsSession : IDisposable
    {
        private readonly IntPtr handle;
        private int settingSize = SettingSizes[0];

        private DrsSession(IntPtr handle) => this.handle = handle;

        public static DrsSession? Open()
        {
            try
            {
                if (Fn<NoArgsFn>(0x0150E828)() != Ok) return null;            // NvAPI_Initialize
            }
            catch (DllNotFoundException)
            {
                return null; // no NVIDIA driver on this PC
            }
            Check(Fn<CreateSessionFn>(0x0694D52E)(out var h), "DRS_CreateSession");
            var s = new DrsSession(h);
            Check(Fn<SessionFn>(0x375DBD6B)(h), "DRS_LoadSettings");
            return s;
        }

        public (IntPtr Profile, string Name) FindGameProfile(bool create)
        {
            var name = Marshal.AllocHGlobal(UnicodeBytes);
            try
            {
                Zero(name, UnicodeBytes);
                WriteUnicode(name, 0, GameExe);
                foreach (var (size, ver) in AppVersions)
                {
                    var app = Marshal.AllocHGlobal(size);
                    try
                    {
                        Zero(app, size);
                        Marshal.WriteInt32(app, 0, (int)Version(size, ver));
                        var status = Fn<FindAppFn>(0xEEE566B2)(handle, name, out var profile, app);
                        if (status == IncompatibleStructVersion) continue;
                        if (status == ExecutableNotFound) return create ? CreateGameProfile() : (IntPtr.Zero, "");
                        Check(status, "DRS_FindApplicationByName");
                        return (profile, ReadUnicode(app, 8 + UnicodeBytes)); // userFriendlyName
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(app);
                    }
                }
                throw new NvapiException("No supported NVDRS_APPLICATION version");
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        private (IntPtr, string) CreateGameProfile()
        {
            const string profileName = "Rocket League (BoostOS)";
            var info = Marshal.AllocHGlobal(ProfileSize);
            var app = Marshal.AllocHGlobal(AppVersions[^1].Size);
            try
            {
                Zero(info, ProfileSize);
                Marshal.WriteInt32(info, 0, (int)Version(ProfileSize, 1));
                WriteUnicode(info, 4, profileName);
                Check(Fn<CreateProfileFn>(0xCC176068)(handle, info, out var profile), "DRS_CreateProfile");

                Zero(app, AppVersions[^1].Size);
                Marshal.WriteInt32(app, 0, (int)Version(AppVersions[^1].Size, 1));
                WriteUnicode(app, 8, GameExe);
                Check(Fn<CreateAppFn>(0x4347A9DE)(handle, profile, app), "DRS_CreateApplication");
                return (profile, profileName);
            }
            finally
            {
                Marshal.FreeHGlobal(info);
                Marshal.FreeHGlobal(app);
            }
        }

        public SettingValue? Get(IntPtr profile, uint id)
        {
            foreach (var size in SettingSizes.OrderBy(sz => sz == settingSize ? 0 : 1))
            {
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    Zero(buf, size);
                    Marshal.WriteInt32(buf, 0, (int)Version(size, 1));
                    var status = Fn<GetSettingFn>(0x73BF8338)(handle, profile, id, buf);
                    if (status == IncompatibleStructVersion) continue;
                    if (status == SettingNotFound) { settingSize = size; return null; }
                    Check(status, "DRS_GetSetting");
                    settingSize = size;
                    var location = Marshal.ReadInt32(buf, 4108);       // 0 = this profile, 1+ = global/base/default
                    var isPredefined = Marshal.ReadInt32(buf, 4112) != 0;
                    var current = (uint)Marshal.ReadInt32(buf, 4120 + (size - 4120) / 2);
                    return new SettingValue(id, current, location == 0 && !isPredefined);
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            throw new NvapiException("No supported NVDRS_SETTING version");
        }

        public object Raw(IntPtr profile, uint id)
        {
            Get(profile, id); // settles settingSize
            var size = settingSize;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                Zero(buf, size);
                Marshal.WriteInt32(buf, 0, (int)Version(size, 1));
                var status = Fn<GetSettingFn>(0x73BF8338)(handle, profile, id, buf);
                var union = (size - 4120) / 2;
                return new
                {
                    id = $"0x{id:X8}", status, structSize = size,
                    name = ReadUnicode(buf, 4),
                    settingId = $"0x{(uint)Marshal.ReadInt32(buf, 4100):X8}",
                    location = Marshal.ReadInt32(buf, 4108),
                    isCurrentPredefined = Marshal.ReadInt32(buf, 4112),
                    isPredefinedValid = Marshal.ReadInt32(buf, 4116),
                    predefined = (uint)Marshal.ReadInt32(buf, 4120),
                    current = (uint)Marshal.ReadInt32(buf, 4120 + union),
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void Set(IntPtr profile, uint id, uint value)
        {
            var size = settingSize;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                Zero(buf, size);
                Marshal.WriteInt32(buf, 0, (int)Version(size, 1));
                Marshal.WriteInt32(buf, 4100, (int)id);    // settingId
                Marshal.WriteInt32(buf, 4104, 0);          // NVDRS_DWORD_TYPE
                Marshal.WriteInt32(buf, 4120 + (size - 4120) / 2, (int)value); // u32CurrentValue
                Check(Fn<SetSettingFn>(0x577DD202)(handle, profile, buf), "DRS_SetSetting");
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public void RestoreDefault(IntPtr profile, uint id)
        {
            var status = Fn<ProfileSettingFn>(0x53F0381E)(handle, profile, id);  // DRS_RestoreProfileDefaultSetting
            if (status is SettingNotFound or Ok) return;
            // A profile we created ourselves has no predefined value to go back to: delete the override instead.
            var deleted = Fn<ProfileSettingFn>(0xE4A26362)(handle, profile, id);
            if (deleted is not (Ok or SettingNotFound)) Check(status, "DRS_RestoreProfileDefaultSetting");
        }

        public void Save() => Check(Fn<SessionFn>(0xFCBC7E14)(handle), "DRS_SaveSettings");

        public void Dispose() => Fn<SessionFn>(0xDAD9CFF8)(handle);

        private static void Zero(IntPtr p, int size) => Marshal.Copy(new byte[size], 0, p, size);
    }
}

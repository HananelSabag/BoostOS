using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Tuning;

namespace BoostOS.Tests;

public class ParsingTests
{
    [Fact]
    public void Netsh_output_becomes_a_lookup()
    {
        const string netsh = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                Band                   : 5 GHz
                Channel                : 36
                Receive rate (Mbps)    : 2402
                Signal                 : 91%
            """;
        var map = Shell.ParseColonList(netsh);
        Assert.Equal("5 GHz", map["Band"]);
        Assert.Equal("36", map["channel"]); // keys are case-insensitive
        Assert.Equal("2402", map["Receive rate (Mbps)"]);
        Assert.Equal("91%", map["Signal"]);
    }

    [Fact]
    public void DirectX_preference_flags_are_parsed()
    {
        var flags = WindowsCheck.ParseFlags("SwapEffectUpgradeEnable=1;AutoHDREnable=2096;GpuPreference=2;");
        Assert.Equal("1", flags["SwapEffectUpgradeEnable"]);
        Assert.Equal("2", flags["GpuPreference"]);
        Assert.Empty(WindowsCheck.ParseFlags(null));
    }

    [Theory]
    [InlineData(@"USB\VID_054C&PID_0DF2&MI_03\6&1BBDA81F&1&0003", "054C", "0DF2", "Usb")]
    [InlineData(@"HID\VID_1038&PID_161C&MI_03&COL01\8&229CF493&0&0000", "1038", "161C", "Usb")]
    [InlineData(@"HID\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&021532_PID&0082_REV&0001_E0CA11FD7EDD&COL01\9&2D310FB6&0&0000", "1532", "0082", "Bluetooth")]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&09cc&Col01\8&1234", "054C", "09CC", "Bluetooth")]
    public void Device_ids_give_vendor_product_and_link(string id, string vid, string pid, string link)
    {
        var parsed = InputCheck.ParseDeviceId(id);
        Assert.NotNull(parsed);
        Assert.Equal(vid, parsed.Value.Vid);
        Assert.Equal(pid, parsed.Value.Pid);
        Assert.Equal(link, parsed.Value.Link.ToString());
    }

    [Fact]
    public void Devices_without_a_vendor_id_are_skipped()
    {
        Assert.Null(InputCheck.ParseDeviceId(@"HID\ELAN0412&COL02\5&1E56702D&0&0001"));
    }

    [Fact]
    public void Ini_section_is_read_without_other_sections()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[SystemSettings]\nResX=2560\nUseVsync=False\n[SystemSettingsEditor]\nResX=1280\n");
            var ini = RocketLeague.ReadIni(path, "SystemSettings");
            Assert.Equal("2560", ini["ResX"]);
            Assert.Equal("False", ini["usevsync"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Ini_edit_changes_one_value_and_keeps_the_rest()
    {
        const string text = "[SystemSettings]\r\nUseVsync=True\r\nResX=2560\r\n[SystemSettingsEditor]\r\nUseVsync=True\r\n";
        var (edited, before) = IniEdit.Set(text, "SystemSettings", "UseVsync", "False");
        Assert.Equal("True", before);
        Assert.Equal("[SystemSettings]\r\nUseVsync=False\r\nResX=2560\r\n[SystemSettingsEditor]\r\nUseVsync=True\r\n", edited);
    }

    [Fact]
    public void Ini_edit_adds_a_missing_key_inside_its_section()
    {
        var (edited, before) = IniEdit.Set("[A]\nx=1\n[B]\ny=2", "A", "UseVsync", "False");
        Assert.Null(before);
        Assert.Equal("[A]\nx=1\nUseVsync=False\n[B]\ny=2", edited);
    }

    [Fact]
    public void Ini_edit_refuses_a_missing_section()
    {
        Assert.Throws<InvalidOperationException>(() => IniEdit.Set("[A]\nx=1", "SystemSettings", "UseVsync", "False"));
    }

    [Fact]
    public void Nvidia_values_have_readable_names()
    {
        Assert.Contains("Prefer maximum performance", Nvidia.Describe(new(Nvidia.PowerManagementId, 1, true), english: true));
        Assert.Contains("default", Nvidia.Describe(new(Nvidia.PowerManagementId, 5, false), english: true));
        Assert.Equal("On", Nvidia.Describe(new(Nvidia.PrerenderLimitId, 1, true), english: true));
    }

    [Fact]
    public void Both_languages_live_at_the_call_site()
    {
        I18n.English = true;
        Assert.Equal("Router", I18n.L("נתב", "Router"));
        I18n.English = false;
        Assert.Equal("נתב", I18n.L("נתב", "Router"));
    }
}

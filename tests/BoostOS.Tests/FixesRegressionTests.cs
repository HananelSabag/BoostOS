using System.Text.Json;
using BoostOS.Tuning;

namespace BoostOS.Tests;

public class FixesRegressionTests
{
    [Fact]
    public void Wireless_undo_uses_the_saved_power_plan_and_value()
    {
        var scheme = Guid.NewGuid();
        var before = JsonSerializer.Serialize(new Fixes.WirelessAcSnapshot(scheme, 3));
        (Guid Scheme, uint Value)? written = null;

        Fixes.RestoreWirelessAc(before, (id, value) => written = (id, value));

        Assert.Equal((scheme, 3u), written);
    }

    [Fact]
    public void Roaming_undo_uses_the_saved_adapter_and_value()
    {
        var adapter = Guid.NewGuid();
        var before = JsonSerializer.Serialize(new Fixes.RoamingSnapshot(adapter, "4"));
        (Guid Adapter, string Value)? written = null;

        Fixes.RestoreRoaming(before, (id, value) => written = (id, value));

        Assert.Equal((adapter, "4"), written);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("{}")]
    [InlineData("{\"Scheme\":\"2d67f69c-7670-4ab6-a420-fbbf429d6164\"}")]
    [InlineData("{\"Scheme\":\"00000000-0000-0000-0000-000000000000\",\"Value\":3}")]
    [InlineData("{broken")]
    public void Invalid_or_legacy_wireless_backups_cannot_change_a_plan(string? before)
    {
        var wrote = false;
        Assert.ThrowsAny<Exception>(() => Fixes.RestoreWirelessAc(before, (_, _) => wrote = true));
        Assert.False(wrote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("4")]
    [InlineData("{}")]
    [InlineData("{\"AdapterId\":\"2d67f69c-7670-4ab6-a420-fbbf429d6164\"}")]
    [InlineData("{\"AdapterId\":\"00000000-0000-0000-0000-000000000000\",\"Value\":\"4\"}")]
    [InlineData("{\"AdapterId\":\"2d67f69c-7670-4ab6-a420-fbbf429d6164\",\"Value\":\"unknown\"}")]
    public void Invalid_or_legacy_roaming_backups_cannot_change_an_adapter(string? before)
    {
        var wrote = false;
        Assert.ThrowsAny<Exception>(() => Fixes.RestoreRoaming(before, (_, _) => wrote = true));
        Assert.False(wrote);
    }

    [Theory]
    [InlineData("wifi-power-ac")]
    [InlineData("wifi-roaming")]
    public void An_existing_target_backup_blocks_a_different_target(string id)
    {
        var target = Guid.NewGuid();
        var before = Snapshot(id, target);
        var record = new FixRecord(id, DateTime.UtcNow, before);
        var log = new List<FixRecord> { record };

        Assert.Throws<InvalidOperationException>(() => Fixes.EnsureTargetAvailable(id, Guid.NewGuid(), log));
        Assert.Same(record, Assert.Single(log));
        Fixes.EnsureTargetAvailable(id, target, log); // Reapplying to the original target keeps its first backup.
        Assert.Equal(before, Assert.Single(log).Before);
    }

    [Theory]
    [InlineData("wifi-power-ac")]
    [InlineData("wifi-roaming")]
    public void A_legacy_backup_blocks_reapply_instead_of_losing_its_unknown_target(string id)
    {
        var record = new FixRecord(id, DateTime.UtcNow, "3");
        var log = new List<FixRecord> { record };
        Assert.Throws<InvalidOperationException>(() => Fixes.EnsureTargetAvailable(id, Guid.NewGuid(), log));
        Assert.Same(record, Assert.Single(log));
    }

    [Fact]
    public void A_failed_undo_retains_the_original_backup()
    {
        var record = new FixRecord("wifi-roaming", DateTime.UtcNow, "legacy value");
        var log = new List<FixRecord> { record };

        Assert.Throws<InvalidOperationException>(() => Fixes.UndoRecord(log, record,
            _ => throw new InvalidOperationException("adapter unavailable")));

        Assert.Same(record, Assert.Single(log));
    }

    [Fact]
    public void A_successful_undo_removes_only_its_own_backup_after_restoring()
    {
        var record = new FixRecord("wifi-roaming", DateTime.UtcNow, "saved value");
        var other = new FixRecord("wifi-power-ac", DateTime.UtcNow, "other backup");
        var log = new List<FixRecord> { record, other };
        var restored = false;

        Fixes.UndoRecord(log, record, before =>
        {
            Assert.Contains(record, log);
            Assert.Equal(record.Before, before);
            restored = true;
        });

        Assert.True(restored);
        Assert.Same(other, Assert.Single(log));
    }

    [Fact]
    public void A_wifi_property_write_requires_an_exact_adapter_and_a_confirmed_result()
    {
        var adapter = Guid.NewGuid();
        var result = Fixes.SetAdapterProperty(adapter, "RoamAggressiveness", "0", (script, timeout) =>
        {
            Assert.Contains($"$_.InterfaceGuid -eq [guid]'{adapter}'", script);
            Assert.Contains("$adapter.Count -ne 1", script);
            Assert.Contains("$ErrorActionPreference = 'Stop'", script);
            Assert.Contains("Set-NetAdapterAdvancedProperty -InputObject $property[0]", script);
            Assert.Equal(60_000, timeout);
            return "{\"Applied\":true,\"Before\":\"3\"}";
        });
        Assert.Equal("3", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ok")]
    [InlineData("{\"Applied\":false,\"Before\":\"3\"}")]
    [InlineData("{\"Applied\":true}")]
    [InlineData("{\"Applied\":true,\"Before\":\"unknown\"}")]
    [InlineData("{\"Applied\":true,\"Before\":3}")]
    public void Unconfirmed_wifi_property_writes_are_failures(string? output)
    {
        Assert.Throws<InvalidOperationException>(() =>
            Fixes.SetAdapterProperty(Guid.NewGuid(), "RoamAggressiveness", "0", (_, _) => output));
    }

    [Theory]
    [InlineData("not a value")]
    [InlineData("0'; throw 'unexpected command")]
    public void Invalid_wifi_values_are_rejected_before_running_a_tool(string value)
    {
        var ran = false;
        Assert.Throws<ArgumentException>(() => Fixes.SetAdapterProperty(Guid.NewGuid(), "RoamAggressiveness", value,
            (_, _) => { ran = true; return null; }));
        Assert.False(ran);
    }

    [Fact]
    public void Undo_of_a_previously_absent_ini_key_removes_it_and_preserves_other_sections()
    {
        const string original = "[SystemSettings]\r\nResX=2560\r\n[SystemSettingsEditor]\r\nUseVsync=True\r\n";
        var (applied, before) = IniEdit.Set(original, "SystemSettings", "UseVsync", "False");
        Assert.Null(before);

        var (restored, removed) = IniEdit.Remove(applied, "SystemSettings", "UseVsync");

        Assert.Equal("False", removed);
        Assert.Equal(original, restored);
    }

    private static string Snapshot(string id, Guid target) => id == "wifi-power-ac"
        ? JsonSerializer.Serialize(new Fixes.WirelessAcSnapshot(target, 3))
        : JsonSerializer.Serialize(new Fixes.RoamingSnapshot(target, "4"));
}

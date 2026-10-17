using System.Runtime.InteropServices;
using BoostOS.Tuning;

namespace BoostOS.Tests;

public class NvidiaRegressionTests
{
    [Fact]
    public void An_old_layout_driver_can_restore_the_original_user_setting()
    {
        const uint originalValue = 3; // An existing custom power mode that undo must preserve.
        var attempts = new List<int>();
        uint? restored = null;

        var acceptedSize = Nvidia.SetWithCompatibleLayout(Nvidia.PowerManagementId, originalValue, 12328, buffer =>
        {
            var version = (uint)Marshal.ReadInt32(buffer, 0);
            var size = (int)(version & 0xFFFF);
            attempts.Add(size);
            if (size != 12320) return -9; // The older driver rejects the newer structure before reading values.
            Assert.Equal(1u, version >> 16);
            Assert.Equal(Nvidia.PowerManagementId, (uint)Marshal.ReadInt32(buffer, 4100));
            Assert.Equal(0, Marshal.ReadInt32(buffer, 4104)); // DWORD type.
            restored = (uint)Marshal.ReadInt32(buffer, 8220); // Old-layout current value, not the new 8224 offset.
            return 0;
        });

        Assert.Equal([12328, 12320], attempts);
        Assert.Equal(12320, acceptedSize);
        Assert.Equal(originalValue, restored);
    }

    [Fact]
    public void A_privilege_failure_is_not_retried_as_a_layout_problem()
    {
        var attempts = 0;
        var error = Assert.Throws<Nvidia.NvapiException>(() =>
            Nvidia.SetWithCompatibleLayout(Nvidia.PowerManagementId, 1, 12328, _ =>
            {
                attempts++;
                return -137;
            }));

        Assert.Equal(1, attempts);
        Assert.Equal(-137, error.Status);
        Assert.True(error.NeedsAdmin);
    }
}

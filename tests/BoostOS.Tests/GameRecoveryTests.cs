using System.Text.Json;
using BoostOS.Game;

namespace BoostOS.Tests;

public class GameRecoveryTests
{
    [Fact]
    public void Recovery_clears_only_successful_items_and_attempts_every_independent_item()
    {
        var overlay = Guid.NewGuid();
        var failedApp = new GameMode.Relaunch("Steam", "steam.exe", "-silent");
        var restoredApp = new GameMode.Relaunch("Other app", "other.exe", "");
        var snap = new GameMode.Snapshot
        {
            Overlay = overlay,
            WifiScanStoppedOn = "Wi-Fi",
            Relaunch = [failedApp, restoredApp],
        };
        var attempted = new List<string>();

        var pending = GameMode.RestorePending(snap,
            app =>
            {
                attempted.Add(app.Label);
                if (app == failedApp) throw new IOException("App is unavailable");
            },
            value =>
            {
                Assert.Equal(overlay, value);
                attempted.Add("power");
                return false;
            },
            nic =>
            {
                Assert.Equal("Wi-Fi", nic);
                attempted.Add("wifi");
                throw new UnauthorizedAccessException("Permission denied");
            });

        Assert.True(pending);
        Assert.Equal(new[] { "power", "wifi", "Steam", "Other app" }, attempted);
        Assert.Equal(overlay, snap.Overlay);
        Assert.Equal("Wi-Fi", snap.WifiScanStoppedOn);
        Assert.Equal(failedApp, Assert.Single(snap.Relaunch));
        Assert.Equal(3, snap.Steps.Count(step => step.Result == "failed"));
        Assert.Single(snap.Steps, step => step.Result == "done");
    }

    [Fact]
    public void Retrying_recovery_does_not_reopen_apps_that_already_recovered()
    {
        var pendingApp = new GameMode.Relaunch("Pending", "pending.exe", "");
        var restoredApp = new GameMode.Relaunch("Restored", "restored.exe", "");
        var snap = new GameMode.Snapshot { Relaunch = [pendingApp, restoredApp] };
        GameMode.RestorePending(snap,
            app => { if (app == pendingApp) throw new IOException("Try again"); },
            _ => throw new InvalidOperationException("No power change was recorded"),
            _ => throw new InvalidOperationException("No Wi-Fi change was recorded"));
        var attempts = new List<GameMode.Relaunch>();

        var pending = GameMode.RestorePending(snap, attempts.Add,
            _ => throw new InvalidOperationException("No power change was recorded"),
            _ => throw new InvalidOperationException("No Wi-Fi change was recorded"));

        Assert.False(pending);
        Assert.Equal(pendingApp, Assert.Single(attempts));
        Assert.Empty(snap.Relaunch);
        Assert.Equal("done", Assert.Single(snap.Steps).Result);
    }

    [Fact]
    public void Successful_recovery_clears_power_and_legacy_wifi_changes()
    {
        var overlay = Guid.NewGuid();
        var snap = new GameMode.Snapshot { Overlay = overlay, WifiScanStoppedOn = "Legacy adapter" };

        var pending = GameMode.RestorePending(snap,
            _ => throw new InvalidOperationException("No apps were recorded"),
            value => value == overlay,
            value => value == "Legacy adapter");

        Assert.False(pending);
        Assert.Null(snap.Overlay);
        Assert.Null(snap.WifiScanStoppedOn);
        Assert.Equal(2, snap.Steps.Count);
        Assert.All(snap.Steps, step => Assert.Equal("done", step.Result));
    }

    [Fact]
    public void An_empty_snapshot_requires_no_recovery_actions()
    {
        var snap = new GameMode.Snapshot { Steps = [new("Previous error", "Already resolved", "failed")] };

        var pending = GameMode.RestorePending(snap,
            _ => throw new InvalidOperationException("No actions should run"),
            _ => throw new InvalidOperationException("No actions should run"),
            _ => throw new InvalidOperationException("No actions should run"));

        Assert.False(pending);
        Assert.Empty(snap.Steps);
    }

    [Fact]
    public void Saving_a_snapshot_replaces_the_complete_file_and_leaves_no_temporary_files()
    {
        WithSnapshotPath(path =>
        {
            GameMode.SaveSnapshot(new GameMode.Snapshot { Since = DateTime.UnixEpoch }, path);
            var replacement = new GameMode.Snapshot
            {
                Since = DateTime.UnixEpoch.AddMinutes(1),
                Overlay = Guid.NewGuid(),
                Relaunch = [new("Steam", "steam.exe", "-silent")],
                Steps = [new("Power", "Changed", "done")],
            };

            GameMode.SaveSnapshot(replacement, path);

            var saved = JsonSerializer.Deserialize<GameMode.Snapshot>(File.ReadAllText(path));
            Assert.NotNull(saved);
            Assert.Equal(replacement.Since, saved.Since);
            Assert.Equal(replacement.Overlay, saved.Overlay);
            Assert.Equal(replacement.Relaunch, saved.Relaunch);
            Assert.Equal(replacement.Steps, saved.Steps);
            Assert.Equal(path, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        });
    }

    [Fact]
    public void A_failed_snapshot_replacement_preserves_the_original_and_cleans_the_temporary_file()
    {
        WithSnapshotPath(path =>
        {
            GameMode.SaveSnapshot(new GameMode.Snapshot { Overlay = Guid.NewGuid() }, path);
            var original = File.ReadAllText(path);
            // Windows denies replacing a file opened without FileShare.Delete. No real app data is touched.
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var error = Record.Exception(() => GameMode.SaveSnapshot(new GameMode.Snapshot(), path));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Expected a Windows file replacement error, got {error?.GetType().Name ?? "no error"}.");

            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        });
    }

    private static void WithSnapshotPath(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boostos-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { test(Path.Combine(directory, "gamemode.json")); }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}

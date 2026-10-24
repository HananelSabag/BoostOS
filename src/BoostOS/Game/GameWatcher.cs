using BoostOS.Core;

namespace BoostOS.Game;

/// <summary>
/// Watches for library games, every few seconds, from the kernel's process snapshot (never a handle to a game).
/// The copy of BoostOS that owns a game session turns Game Mode on, records the session, keeps background apps
/// quiet and puts everything back when the game closes. Ownership is a lock file, so when the background recorder
/// and the BoostOS window both run, each game is recorded exactly once.
/// </summary>
internal sealed class GameWatcher : IDisposable
{
    public sealed record Ended(GameInfo Game, bool Owner, MatchReport? Report, GameModeStatus? Restored, Exception? Error);

    private readonly Func<AppSettings> settings;
    private readonly Func<bool?> periodicWifiSpikes;
    private MatchRecorder? recorder;
    private FileStream? sessionLock;
    private GameInfo? watched;
    private DateTime lastQuiet;

    public GameWatcher(Func<AppSettings> settings, Func<bool?> periodicWifiSpikes)
    {
        this.settings = settings;
        this.periodicWifiSpikes = periodicWifiSpikes;
    }

    public GameInfo? Current => watched;
    public bool OwnsSession => sessionLock is not null;
    public event Action<GameInfo, GameModeStatus?>? Started;
    public event Action<Ended>? Finished;

    private static string LockPath => Path.Combine(AppSettings.DataDir, "session.lock");

    /// <summary>One look. Runs on a worker thread; events are raised from it.</summary>
    public void Tick()
    {
        var running = GameLibrary.Running();
        if (running is not null)
        {
            var isNew = watched?.Id != running.Id;
            watched = running;
            if (isNew)
            {
                GameModeStatus? applied = null;
                if (TryOwn())
                {
                    var s = settings();
                    // Started from Epic, Steam or a shortcut: Game Mode still comes on by itself.
                    if (s.GameModeOnPlay && !GameMode.IsActive)
                        applied = GameMode.Apply(periodicWifiSpikes(), running.Store, running.Processes);
                    if (s.TrackMatches) recorder ??= new MatchRecorder(GameMode.IsActive, running);
                }
                Started?.Invoke(running, applied);
            }
            else if (!OwnsSession && TryOwn() && settings().TrackMatches)
                // The copy that owned this session closed mid-game: take over, so Game Mode still gets put back.
                recorder ??= new MatchRecorder(GameMode.IsActive, running);
            if (OwnsSession && GameMode.IsActive && DateTime.UtcNow - lastQuiet > TimeSpan.FromSeconds(60))
            {
                lastQuiet = DateTime.UtcNow;
                try { GameMode.KeepQuiet(running.Processes); } catch { /* best effort */ }
            }
            return;
        }

        if (watched is null) return;
        var game = watched;
        watched = null;
        if (!OwnsSession) { Finished?.Invoke(new(game, false, null, null, null)); return; }

        MatchReport? report = null;
        Exception? error = null;
        if (recorder is { } r)
        {
            recorder = null;
            try
            {
                report = r.Finish();
                MatchStore.Add(report);
            }
            catch (Exception ex) { error = ex; try { r.Dispose(); } catch { } }
        }
        GameModeStatus? restored = null;
        // Recheck: another game may already be running (back-to-back sessions keep Game Mode on).
        if (GameLibrary.Running() is null && GameMode.IsActive)
        {
            try { restored = GameMode.Restore(periodicWifiSpikes()); }
            catch (Exception ex) { error ??= ex; }
        }
        Release();
        Finished?.Invoke(new(game, true, report, restored, error));
    }

    /// <summary>Takes the session lock, unless another BoostOS already holds it.</summary>
    private bool TryOwn()
    {
        if (sessionLock is not null) return true;
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            sessionLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void Release()
    {
        try { sessionLock?.Dispose(); } catch { }
        sessionLock = null;
    }

    public void Dispose()
    {
        try { recorder?.Dispose(); } catch { }
        recorder = null;
        Release();
    }
}

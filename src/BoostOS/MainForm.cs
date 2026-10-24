using System.Diagnostics;
using System.Text.Json;
using BoostOS.Core;
using BoostOS.Diagnostics;
using BoostOS.Game;
using BoostOS.Network;
using BoostOS.Tuning;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BoostOS;

/// <summary>
/// The host window: a WebView2 that fills the screen and shows wwwroot. The page talks to us with
/// chrome.webview.postMessage({ cmd }) and we answer with { type, data }.
/// While the window is minimized (always the case once the game starts) the page is suspended:
/// no script, no rendering, no GPU work. Only a 4-second process check keeps running.
/// </summary>
internal sealed class MainForm : Form
{
    private const string Host = "boostos.example";
    private static readonly Color Backdrop = Color.FromArgb(7, 9, 15);

    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Backdrop };
    private readonly System.Windows.Forms.Timer gameWatch = new() { Interval = 4000 };
    private readonly SemaphoreSlim systemChanges = new(1, 1);
    private AppSettings settings = AppSettings.Load();
    private CancellationTokenSource? netTest;
    private readonly GameWatcher watcher;
    private bool fullscreen;
    private bool checkingGame;
    private string? startupRecoveryError;
    private bool? periodicWifiSpikes = LoadLastNetVerdict(); // from the last network test; null = never tested

    private static string LastNetPath => Path.Combine(AppSettings.DataDir, "last-net.json");

    private static bool? LoadLastNetVerdict()
    {
        try { return File.Exists(LastNetPath) ? JsonDocument.Parse(File.ReadAllText(LastNetPath)).RootElement.GetProperty("periodic").GetBoolean() : null; }
        catch { return null; }
    }

    public MainForm(bool windowed)
    {
        I18n.English = settings.Language == "en";
        Text = "BoostOS";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Backdrop;
        MinimumSize = new Size(1000, 640);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(web);

        if (!windowed && settings.StartFullscreen) SetFullscreen(true);
        Load += async (_, _) =>
        {
            try { await InitWebAsync(); }
            catch (Exception ex) { ReportError("startup", ex); }
        };
        Resize += async (_, _) =>
        {
            try { await OnResizedAsync(); }
            catch (Exception ex) { ReportError("resize", ex); }
        };
        watcher = new GameWatcher(() => settings, () => periodicWifiSpikes);
        watcher.Started += OnGameStarted;
        watcher.Finished += OnGameFinished;
        FormClosing += (_, _) =>
        {
            netTest?.Cancel();
            watcher.Dispose();
        };
        gameWatch.Tick += (_, _) => WatchGame();
        gameWatch.Start();
        // The background recorder: every game gets recorded, however it was started.
        Agent.Sync(settings);
    }

    private async Task InitWebAsync()
    {
        // UI sounds play from controller input too, which a browser wouldn't count as a user gesture.
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        // BOOSTOS_WEBVIEW_DIR lets a development copy run next to an installed one (WebView2 locks its data folder).
        var webData = Environment.GetEnvironmentVariable("BOOSTOS_WEBVIEW_DIR") is { Length: > 0 } dir ? dir : Path.Combine(AppSettings.DataDir, "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, webData, options);
        await web.EnsureCoreWebView2Async(env);

        var core = web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        // A console, not a browser: no reload, find, print or zoom shortcuts.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        // Game pictures copied from the launchers' own caches, answered here (a folder mapping on a second host
        // stopped serving images after a WebView2 update). Only plain file names inside the art folder are served.
        Directory.CreateDirectory(GameLibrary.ArtDir);
        core.AddWebResourceRequestedFilter($"https://{GameLibrary.ArtHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnArtRequested;
        core.WebMessageReceived += OnMessage;

        // A previous run may have ended before it could restore. Never undo a session still in progress.
        try
        {
            await ChangeSystemAsync(() => !RocketLeague.IsRunning() && GameLibrary.Running() is null && GameMode.IsActive
                ? GameMode.Restore(periodicWifiSpikes) : null);
        }
        catch (Exception ex) { startupRecoveryError = ex.Message; }
        core.Navigate($"https://{Host}/index.html");

        FitZoom();
        SizeChanged += (_, _) => FitZoom();
    }

    private void OnArtRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (web.CoreWebView2 is not { } core || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)
            || !uri.Host.Equals(GameLibrary.ArtHost, StringComparison.OrdinalIgnoreCase)) return;
        var name = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var path = Path.Combine(GameLibrary.ArtDir, name);
        if (name.Length == 0 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Contains("..") || !File.Exists(path))
        {
            e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        var type = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
        try
        {
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(path)), 200, "OK",
                $"Content-Type: {type}\r\nCache-Control: max-age=86400\r\nAccess-Control-Allow-Origin: https://{Host}");
        }
        catch (IOException) { e.Response = core.Environment.CreateWebResourceResponse(null, 503, "Busy", ""); }
    }

    /// <summary>
    /// The layout is drawn for ~1500 CSS px across. On a big monitor scale everything up together,
    /// like a console UI on a TV, instead of leaving small text in a sea of space.
    /// </summary>
    private void FitZoom()
    {
        if (web.CoreWebView2 is null || ClientSize.Width == 0) return;
        var cssWidth = ClientSize.Width * 96.0 / DeviceDpi;
        web.ZoomFactor = Math.Clamp(cssWidth / 1500, 1.0, 2.0);
    }

    /// <summary>Minimized: hide and suspend the page so BoostOS costs nothing while you play. Restored: it resumes by itself.</summary>
    private async Task OnResizedAsync()
    {
        if (web.CoreWebView2 is null) return;
        if (WindowState == FormWindowState.Minimized)
        {
            web.Visible = false;
            try { await web.CoreWebView2.TrySuspendAsync(); }
            catch (InvalidOperationException) { } // already suspended, or busy with a dialog
        }
        else if (!web.Visible)
        {
            web.Visible = true; // visibility resumes a suspended WebView2
            Post("resumed", new { });
        }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string cmd;
        JsonElement msg;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            msg = doc.RootElement.Clone();
            cmd = msg.GetProperty("cmd").GetString() ?? "";
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            switch (cmd)
            {
                case "hello":
                    Post("hello", new { settings, version = Application.ProductVersion.Split('+')[0], matches = MatchStore.Load(), gameMode = GameMode.Status(periodicWifiSpikes), fixesApplied = Fixes.Applied().Count });
                    if (startupRecoveryError is { } recoveryError)
                    {
                        startupRecoveryError = null;
                        Post("error", new { cmd = "gameModeRecovery", message = recoveryError });
                    }
                    Post("games", await Task.Run(() => GameLibrary.List(refresh: true)));
                    break;

                case "games":
                    Post("games", await Task.Run(() => GameLibrary.List(refresh: true)));
                    break;

                case "fixesApplied":
                    Post("fixesApplied", Fixes.Applied());
                    break;

                case "rlVideo":
                    Post("rlVideo", await Task.Run(() => RlVideo.Read(LastSessionHot())));
                    break;

                case "rlVideoSet":
                case "rlVideoUndo":
                    var changes = cmd == "rlVideoSet"
                        ? JsonSerializer.Deserialize<Dictionary<string, string>>(msg.GetProperty("changes").GetRawText()) ?? []
                        : [];
                    Post("rlVideo", await ChangeSystemAsync(() =>
                    {
                        if (cmd == "rlVideoSet") RlVideo.Apply(changes); else RlVideo.Undo();
                        return RlVideo.Read(LastSessionHot());
                    }));
                    Post("rlVideoSaved", new { undo = cmd == "rlVideoUndo" });
                    break;

                case "minimize":
                    WindowState = FormWindowState.Minimized;
                    break;

                case "saveSettings":
                    settings = JsonSerializer.Deserialize<AppSettings>(msg.GetProperty("settings").GetRawText(), Program.Json) ?? settings;
                    settings.Save();
                    I18n.English = settings.Language == "en";
                    Agent.Sync(settings);
                    Post("settings", settings);
                    break;

                case "scan":
                    Post("scan", await Scanner.RunAsync());
                    break;

                case "fix":
                case "undoFix":
                    var ids = msg.GetProperty("ids").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList();
                    var fixedResult = await ChangeSystemAsync(() =>
                    {
                        var outcome = Fixes.Run(ids, undo: cmd == "undoFix");
                        return new { outcome, applied = Fixes.Applied().Count };
                    });
                    Post("fixResult", fixedResult);
                    break;

                case "undoAllFixes":
                    var undone = await ChangeSystemAsync(() =>
                    {
                        var all = Fixes.Applied().Select(r => r.Id).ToList();
                        var outcome = Fixes.Run(all, undo: true);
                        return new { outcome, applied = Fixes.Applied().Count };
                    });
                    Post("fixResult", undone);
                    break;

                case "netTest":
                    netTest?.Cancel();
                    netTest = new CancellationTokenSource();
                    var seconds = msg.TryGetProperty("seconds", out var s) && s.TryGetInt32(out var n) ? Math.Clamp(n, 10, 300) : 60;
                    var token = netTest.Token;
                    var result = await NetTest.RunAsync(seconds, sample => OnUiThread(() => Post("netSample", sample)), token);
                    if (!token.IsCancellationRequested)
                    {
                        periodicWifiSpikes = result.Targets.Any(t => t.Id == "router" && t.PeriodSeconds is not null);
                        Directory.CreateDirectory(AppSettings.DataDir);
                        File.WriteAllText(LastNetPath, JsonSerializer.Serialize(new { periodic = periodicWifiSpikes, at = DateTime.Now }));
                        Post("netResult", result);
                    }
                    break;

                case "netCancel":
                    netTest?.Cancel();
                    break;

                case "gameModeStatus":
                    Post("gameMode", await Task.Run(() => GameMode.Status(periodicWifiSpikes)));
                    break;

                case "gameModeOn":
                    Post("gameMode", await ChangeSystemAsync(() => GameMode.Apply(periodicWifiSpikes)));
                    break;

                case "gameModeOff":
                    Post("gameMode", await ChangeSystemAsync(() => GameMode.Restore(periodicWifiSpikes)));
                    break;

                case "play":
                    // Console flow: get the PC ready, then start the game. Restores by itself when the game closes.
                    await PlayAsync(GameFor(msg), withGameMode: settings.GameModeOnPlay);
                    break;

                case "launch":
                    await PlayAsync(GameFor(msg), withGameMode: false);
                    break;

                case "matches":
                    Post("matches", MatchStore.Load());
                    break;

                case "toggleFullscreen":
                    SetFullscreen(!fullscreen);
                    break;

                case "exit":
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportError(cmd, ex);
        }
    }

    private async Task<T> ChangeSystemAsync<T>(Func<T> change)
    {
        await systemChanges.WaitAsync();
        try { return await Task.Run(change); }
        finally { systemChanges.Release(); }
    }

    /// <summary>The game a message names, else the favourite, else Rocket League, else the first game found.</summary>
    private GameInfo? GameFor(JsonElement msg)
    {
        var id = msg.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var games = GameLibrary.List();
        return GameLibrary.Find(id) ?? GameLibrary.Find(settings.FavoriteGame)
            ?? games.FirstOrDefault(g => g.IsRocketLeague) ?? games.FirstOrDefault();
    }

    private bool LastSessionHot() =>
        MatchStore.Load().FirstOrDefault() is { } m && (m.GpuThermalLimitedPct is >= 20 || m.GpuTempMax is >= 86);

    private async Task PlayAsync(GameInfo? game, bool withGameMode)
    {
        await systemChanges.WaitAsync();
        try
        {
            var wasActive = GameMode.IsActive;
            var launched = false;
            try
            {
                if (withGameMode) Post("gameMode", await Task.Run(() => GameMode.Apply(periodicWifiSpikes, game?.Store)));
                launched = game is not null && Launch(game.Launch);
                Post("launch", new { ok = launched, name = game?.Name });
                if (launched) WindowState = FormWindowState.Minimized; // out of the game's way; the page suspends itself
            }
            finally
            {
                // A failed launch must not strand changes made for this attempt, or undo a manual session.
                if (!launched && !wasActive && GameMode.IsActive && GameLibrary.Running() is null)
                    Post("gameMode", await Task.Run(() => GameMode.Restore(periodicWifiSpikes)));
            }
        }
        finally { systemChanges.Release(); }
    }

    /// <summary>
    /// Every 4 seconds: is one of the library's games running? (The kernel's process snapshot only, never a handle
    /// to the game.) The shared watcher records and restores when this copy owns the session; the background
    /// recorder may own it instead, and then this window only shows what happened.
    /// </summary>
    private async void WatchGame()
    {
        if (checkingGame || IsDisposed || Disposing) return;
        checkingGame = true;
        try
        {
            await systemChanges.WaitAsync();
            try { await Task.Run(watcher.Tick); }
            finally { systemChanges.Release(); }
        }
        catch (Exception ex) { ReportError("gameWatch", ex); }
        finally { checkingGame = false; }
    }

    private void OnGameStarted(GameInfo game, GameModeStatus? applied) => OnUiThread(() =>
    {
        Post("gameRunning", new { id = game.Id, name = game.Name });
        if (applied is not null) Post("gameMode", applied);
    });

    private void OnGameFinished(GameWatcher.Ended e) => OnUiThread(async () =>
    {
        Post("gameRunning", new { id = (string?)null, name = (string?)null });
        // Come back to the front only if BoostOS stepped aside for this match.
        if (WindowState == FormWindowState.Minimized && !IsDisposed && !Disposing)
        {
            WindowState = FormWindowState.Normal;
            SetFullscreen(fullscreen);
            Activate();
        }
        var report = e.Report;
        if (!e.Owner)
        {
            // The background recorder owned this session: its report lands in a few seconds.
            var since = DateTime.Now.AddMinutes(-1);
            for (var i = 0; i < 12 && report is null; i++)
            {
                await Task.Delay(1000);
                report = MatchStore.Load().FirstOrDefault(m => m.Start.AddMinutes(m.Minutes) >= since);
            }
        }
        Post("gameMode", GameMode.Status(periodicWifiSpikes));
        if (report is { Minutes: >= 1 }) Post("matchReport", new { report, previous = MatchStore.Load().Skip(1).FirstOrDefault() });
        if (e.Restored is { } status)
            Post("notice", new { message = status.Active
                ? I18n.L("המשחק נסגר. חלק מהשינויים עדיין ממתינים לשחזור; אפשר לנסות שוב בלחצן השחזור.", "The game closed. Some changes still need recovery; use the restore button to retry.")
                : I18n.L("המשחק נסגר. המחשב חזר למצב הרגיל.", "The game closed. Your PC is back to normal.") });
        if (e.Error is not null) ReportError("matchReport", e.Error);
    });

    private void ReportError(string cmd, Exception error)
    {
        Debug.WriteLine($"{cmd}: {error}");
        try { Post("error", new { cmd, message = error.Message }); }
        catch (Exception postError) { Debug.WriteLine(postError); } // WebView2 may already be closing; never escape an async event handler.
    }

    private void Post(string type, object data)
    {
        if (IsDisposed || web.CoreWebView2 is null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, data }, Program.Json));
    }

    private void OnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { } // window closing
    }

    /// <summary>
    /// Starts a game through its own store launcher (Epic or Steam), which also starts its anti-cheat.
    /// We never start a game's executable directly.
    /// </summary>
    private static bool Launch(string uri)
    {
        if (!uri.StartsWith("steam://rungameid/", StringComparison.Ordinal)
            && !uri.StartsWith("com.epicgames.launcher://apps/", StringComparison.Ordinal)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetFullscreen(bool on)
    {
        fullscreen = on;
        if (on)
        {
            // A borderless window that covers the whole monitor: Windows treats it as full screen and hides the taskbar.
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = (IsHandleCreated ? Screen.FromHandle(Handle) : Screen.PrimaryScreen!).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Size = new Size(1440, 900);
            CenterToScreen();
        }
    }
}

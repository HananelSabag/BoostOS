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
    private AppSettings settings = AppSettings.Load();
    private CancellationTokenSource? netTest;
    private MatchRecorder? recorder;
    private bool fullscreen;
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
        Load += async (_, _) => await InitWebAsync();
        Resize += async (_, _) => await OnResizedAsync();
        FormClosing += (_, _) =>
        {
            netTest?.Cancel();
            recorder?.Dispose();
        };
        gameWatch.Tick += (_, _) => WatchGame();
        gameWatch.Start();
    }

    private async Task InitWebAsync()
    {
        // UI sounds play from controller input too, which a browser wouldn't count as a user gesture.
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.DataDir, "WebView2"), options);
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
        core.WebMessageReceived += OnMessage;
        core.Navigate($"https://{Host}/index.html");

        FitZoom();
        SizeChanged += (_, _) => FitZoom();
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
                    Post("hello", new { settings, version = Application.ProductVersion.Split('+')[0], matches = MatchStore.Load().Take(5), gameMode = GameMode.Status(periodicWifiSpikes), fixesApplied = Fixes.Applied().Count });
                    break;

                case "saveSettings":
                    settings = JsonSerializer.Deserialize<AppSettings>(msg.GetProperty("settings").GetRawText(), Program.Json) ?? settings;
                    settings.Save();
                    I18n.English = settings.Language == "en";
                    Post("settings", settings);
                    break;

                case "scan":
                    Post("scan", await Scanner.RunAsync());
                    break;

                case "fix":
                case "undoFix":
                    var ids = msg.GetProperty("ids").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList();
                    var outcome = await Task.Run(() => Fixes.Run(ids, undo: cmd == "undoFix"));
                    Post("fixResult", new { outcome, applied = Fixes.Applied().Count });
                    break;

                case "undoAllFixes":
                    var all = Fixes.Applied().Select(r => r.Id).ToList();
                    var undone = await Task.Run(() => Fixes.Run(all, undo: true));
                    Post("fixResult", new { outcome = undone, applied = Fixes.Applied().Count });
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
                    Post("gameMode", await Task.Run(() => GameMode.Apply(periodicWifiSpikes)));
                    break;

                case "gameModeOff":
                    Post("gameMode", await Task.Run(() => GameMode.Restore(periodicWifiSpikes)));
                    break;

                case "play":
                    // Console flow: get the PC ready, then start the game. Restores by itself when the game closes.
                    Post("gameMode", await Task.Run(() => GameMode.Apply(periodicWifiSpikes)));
                    Launch();
                    break;

                case "launchRocketLeague":
                    Launch();
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
            Post("error", new { cmd, message = ex.Message });
        }
    }

    private void Launch()
    {
        var launched = LaunchRocketLeague();
        Post("launch", new { ok = launched });
        if (launched) WindowState = FormWindowState.Minimized; // out of the game's way; the page suspends itself
    }

    /// <summary>
    /// Every 4 seconds: is Rocket League running? (A process-list snapshot only, never a handle to the game.)
    /// Start recording when a match starts; when it ends, save the report, undo Game Mode and come back.
    /// </summary>
    private async void WatchGame()
    {
        if (RocketLeague.IsRunning())
        {
            sawGame = true;
            if (recorder is null && settings.TrackMatches) recorder = new MatchRecorder(GameMode.IsActive);
            return;
        }
        if (!sawGame) return;
        sawGame = false;

        MatchReport? report = null;
        if (recorder is not null)
        {
            var r = recorder;
            recorder = null;
            report = await Task.Run(r.Finish);
            MatchStore.Add(report);
        }

        GameModeStatus? status = null;
        if (GameMode.IsActive) status = await Task.Run(() => GameMode.Restore(periodicWifiSpikes));

        // Come back to the front only if BoostOS stepped aside for this match.
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
            SetFullscreen(fullscreen);
            Activate();
        }
        if (status is not null) Post("gameMode", status);
        if (report is { Minutes: >= 1 }) Post("matchReport", new { report, previous = MatchStore.Load().Skip(1).FirstOrDefault() });
        if (status is not null) Post("notice", new { message = I18n.L("המשחק נסגר. המחשב חזר למצב הרגיל.", "The game closed. Your PC is back to normal.") });
    }

    private bool sawGame;

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
    /// Starts the game through its own store launcher, which also starts Easy Anti-Cheat.
    /// We never start RocketLeague.exe directly.
    /// </summary>
    private static bool LaunchRocketLeague()
    {
        var install = RocketLeague.Find();
        if (install is null) return false;
        var uri = install.Store == "Steam"
            ? "steam://rungameid/252950"
            : $"com.epicgames.launcher://apps/{install.EpicAppName ?? "Sugar"}?action=launch&silent=true";
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

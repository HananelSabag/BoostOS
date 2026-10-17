# BoostOS

**A console-style home screen for Windows that gets your PC ready for Rocket League, and puts it back afterwards.**

[![CI](https://github.com/HananelSabag/BoostOS/actions/workflows/ci.yml/badge.svg)](https://github.com/HananelSabag/BoostOS/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![Hebrew + English](https://img.shields.io/badge/UI-Hebrew%20%2B%20English-FF8B2B)
[![License: MIT](https://img.shields.io/badge/License-MIT-43D79A)](LICENSE)

![BoostOS home screen](docs/home-en.png)

BoostOS opens full screen like a PlayStation or Xbox dashboard and is driven with a controller, the keyboard or a mouse.
Press **Rocket League** and it closes what would get in the way mid-match, switches power to performance and starts the
game through its own launcher. When Rocket League closes, BoostOS attempts to restore the changes and shows a session report.
Failed recovery items stay saved for retry. Reports include menus and training as well as matches; they do not measure FPS.

> Made by **Hananel Sabag**.

---

## What's inside

### Session recording in v0.3.0

Reports now separate observed GPU clock-limit signals from load-based bottleneck estimates. Power, thermal and hardware
signals are counted separately when the driver supplies them; older reports retain their aggregate and explicitly say
that the reason was not recorded. Missing telemetry is shown as unavailable.

With session reports enabled, new recordings retain timestamped samples and the original clock-reason mask under
`%LOCALAPPDATA%\BoostOS\sessions`. CPU sampling continues if GPU telemetry stops. A session stays on one GPU UUID,
and stale GPU values are discarded. Reports include coverage counts and separate temperature/load charts. Summary and
chart exclude the first 20 seconds; the detailed recording retains them. Up to 20 detailed recordings are kept, with a
43,200-sample memory cap per session; truncation and save failures are visible. The recorder still does not measure FPS
or frame times, and these changes do not establish an FPS gain.

| | |
|---|---|
| **Play** | Turns on Game Mode, then starts Rocket League through Epic Games or Steam. BoostOS minimizes and attempts to suspend its page; the process watcher and optional recorder still run. |
| **Game Mode** | Closes background sync, updaters and phone extras (OneDrive, Steam, Phone Link, Widgets, Teams, Copilot, Adobe) and sets *Best performance*. A snapshot on disk lets it restore even after a crash. Browsers, Discord, Spotify and your controller software are never touched. |
| **Diagnostics** | A folder that unfolds into four tools. |
| ↳ Full scan | Ten areas: PC, displays, graphics, power, network, Rocket League, Windows gaming settings, background apps, controllers, storage. Every problem BoostOS can fix has a **Fix** button, and every fix can be undone. |
| ↳ Network test | Router and internet pinged side by side, drawn live: blue is the Wi-Fi link, orange is the way out. It tells loss from a router that only limits pings, and finds spikes on a fixed beat (Windows' background Wi-Fi scans). |
| ↳ Controller test | Live sticks, triggers and buttons, plus a 3-second drift test that recommends a Controller Deadzone for Rocket League. |
| ↳ Mouse test | Estimated event rate from raw pointer updates, subject to browser and scheduling limits. |
| **Session report** | After Rocket League closes: system GPU load, temperature, clocks, power, limiting samples and the busiest CPU core. Possible bottlenecks are estimates from system load, without frame-time data. |
| **Settings** | Hebrew or English, interface sounds, controller rumble, start full screen, match reports, and *Undo all fixes*. |

| Diagnostics folder | Full scan |
|---|---|
| ![Diagnostics folder](docs/diagnostics-folder.png) | ![Full scan](docs/scan.png) |
| **Network test** | **Game Mode** |
| ![Network test](docs/network.png) | ![Game Mode](docs/game-mode.png) |
| **Controller test** | **Settings** |
| ![Controller test](docs/controller-test.png) | ![Settings](docs/settings-en.png) |

## Game process isolation

BoostOS uses the game's launcher and keeps its monitoring outside the game process:

- It never opens a handle to the game process, never reads its memory, and injects nothing.
- It finds the game from the system's process snapshot only (`Process.GetProcessesByName`).
- Match reports use driver data (`nvidia-smi`) and Windows' own per-core CPU counters, not the game.
- It starts the game only through Epic or Steam, so Easy Anti-Cheat starts with it.
- It reacts to the controller only while its own window has focus, so presses in the game never reach it.

Everything it changes is a Windows setting, a driver profile or the game's settings file: the same things you could change by hand.

## What a fix changes

| Fix | What changes | Admin |
|---|---|:-:|
| Power mode | Windows power mode → *Best performance* | |
| Background recording | Game Bar *Record what happened* → off | |
| Windows Game Mode | Game Mode → on | |
| Windowed optimizations | *Optimizations for windowed games* → on | |
| Rocket League GPU | Graphics preference for `RocketLeague.exe` → *High performance* | |
| VSync | `UseVsync=False` in `TASystemSettings.ini` (backed up first) | |
| Wi-Fi power | Wireless adapter power saving on AC → *Maximum Performance* | |
| Wi-Fi roaming | Intel *Roaming Aggressiveness* → *Lowest* | ✓ |
| NVIDIA profile | Rocket League profile: *Prefer maximum performance* + *Low Latency Mode: On* (via NVAPI) | ✓ |

Each successful fix records the value it replaced in `%LOCALAPPDATA%\BoostOS\fixes.json`; *Undo all fixes* attempts to write it back.
Wi-Fi backups now include the original adapter or power-plan identity. Older scalar Wi-Fi backups cannot identify that target;
they are retained and report that manual recovery is needed. Permanent fixes still need stronger transaction handling for a crash
between changing a setting and saving its backup; see the [code review](docs/CODE_REVIEW.he.md).
Admin fixes run in a second copy of BoostOS started elevated, so a batch asks for approval once.
What BoostOS deliberately leaves alone, because the effect is unproven or a trade-off: HAGS, MIMO power save, monitor overclocking, Windows security features.

## Design

- **The arena:** night navy under two floodlights that take the colour of the focused tile. Blue team, orange team, and boost yellow.
- **The boost meter:** the readiness score is the round boost gauge from the Rocket League HUD, and it fills like a boost pickup at start-up.
- **Console motion:** a boot sequence, tiles that spring into focus with a light sweep, a folder that unfolds its tools, view transitions between screens, short Web Audio interface sounds and a light controller rumble. `prefers-reduced-motion` is honoured.
- **Less work while you play:** the host attempts to suspend WebView2 when minimized. UI sound processing sleeps between sounds, controller drawing shares the focused input poll, and the watcher and optional system recorder continue in the host.
- **Type:** Karantina (scoreboard-style Hebrew display face) and Assistant, bundled so the app works offline.

## How it's built

```
src/BoostOS/
  Program.cs, MainForm.cs   WinForms host: fullscreen window, WebView2, message bridge, game watcher
  Core/                     models, settings, Hebrew/English strings, Win32 and WMI helpers
  Diagnostics/              one class per scan area, run in parallel
  Network/NetTest.cs        live ping test and its analysis
  Game/                     Game Mode (apply/restore) and the match recorder
  Tuning/                   fixes with undo, NVIDIA driver profile through NVAPI
  wwwroot/                  the UI: vanilla HTML/CSS and ES modules, no framework
tests/BoostOS.Tests/        xUnit tests for parsing, network analysis, reports, settings
tools/                      icon generator, standalone Wi-Fi tweak script
```

The page and the host talk through `chrome.webview.postMessage({ cmd })` and `{ type, data }` replies. Opened in a normal browser,
the UI falls back to a small fake host, which makes design work possible without the app (`wwwroot/dev/scan.sample.json` is
created with `BoostOS.exe --scan-json`).

## Build and run

Requires Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the WebView2 runtime (part of Windows 11).

```
dotnet run --project src/BoostOS                 # full screen
dotnet run --project src/BoostOS -- --windowed   # in a window
dotnet test BoostOS.slnx
dotnet publish src/BoostOS -c Release -o publish
```

Browser-only preview, with Python 3 installed:

```
python tools/serve-preview.py
```

Open `http://127.0.0.1:8744/`. The preview uses a fake host and optional sample data, and does not apply Windows fixes.
An optional ignored `wwwroot/dev/matches.sample.json` array supplies session report fixtures for UI checks.
The [17 October 2026 review](docs/CODE_REVIEW.he.md) records implemented fixes, remaining limitations and validation.

Headless modes, handy for checking a machine:

```
BoostOS.exe --scan-json scan.json [en]   # full diagnostics as JSON
BoostOS.exe --net-json net.json 60       # 60-second network test with every sample
BoostOS.exe --nvidia-json nv.json        # raw NVIDIA profile fields
```

## Things learned along the way

- **A router can fake packet loss.** The home router used for testing answers a ~15 second burst of pings and then drops exactly one a second once the total passes ~7/s. Real Wi-Fi loss comes in bursts, so the test now runs at 2.5 pings/s per target and recognizes the metronome pattern instead of reporting a problem.
- **NVAPI struct versions matter.** Newer NVAPI headers add a 64-bit member to `NVDRS_SETTING` (12,328 bytes); the driver tested here expects the older 12,320-byte layout. BoostOS tries both and reads the value offset from the size it settled on.
- **Measure before you look.** Idle CPU load is sampled before the scan starts, otherwise the scan measures itself.

## License

[MIT](LICENSE) © 2026 Hananel Sabag. Rocket League is a trademark of Psyonix; BoostOS is an independent project and isn't affiliated with Psyonix, Epic Games or NVIDIA.

---

## בעברית

BoostOS הוא מסך בית בסגנון קונסולה לווינדוס, שמכין את המחשב לרוקט ליג ומחזיר אותו למצב הרגיל אחרי המשחק.

- **רוקט ליג:** מפעיל מצב משחק ופותח את המשחק דרך Epic או Steam. BoostOS מוקטן ומנסה להקפיא את הממשק; ניטור המשחק והקלטת נתוני המערכת ממשיכים.
- **מצב משחק:** סוגר תוכנות רקע (OneDrive, Steam, Phone Link ועוד) ומעביר את החשמל לביצועים. כשהמשחק נסגר, הכל חוזר לבד.
- **אבחון:** תיקייה עם ארבעה כלים: סריקה מלאה עם כפתור "תקן" לכל בעיה, בדיקת רשת, בדיקת שלט עם המלצה ל-deadzone, ובדיקת עכבר.
- **דוח הפעלה:** אחרי סגירת רוקט ליג מוצגים נתוני המערכת לכל ההפעלה, כולל תפריטים ואימונים. ההערכה לגבי מגבלת מעבד או כרטיס מסך אינה מדידת FPS.
- **בטוח עם האנטי-צ'יט:** BoostOS לא נוגע בתהליך של המשחק ולא בזיכרון שלו. הוא משנה רק הגדרות של ווינדוס, פרופיל בדרייבר וקובץ ההגדרות של המשחק, ואת כל השינויים אפשר להחזיר בלחיצה אחת.
- **שפה:** עברית או אנגלית, לפי הבחירה בהגדרות.

נבנה על ידי **חננאל סבג**. קוד פתוח ברישיון [MIT](LICENSE).

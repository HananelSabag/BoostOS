# BoostOS

**A console-style home screen for Windows that gets your PC ready to play, and puts it back afterwards.**

[![CI](https://github.com/HananelSabag/BoostOS/actions/workflows/ci.yml/badge.svg)](https://github.com/HananelSabag/BoostOS/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![Hebrew + English](https://img.shields.io/badge/UI-Hebrew%20%2B%20English-FF8B2B)
[![License: MIT](https://img.shields.io/badge/License-MIT-43D79A)](LICENSE)

![BoostOS home screen](docs/home.png)

BoostOS opens full screen like a PlayStation or Xbox dashboard and is driven with a controller, the keyboard or a mouse.
Your favourite game sits on the first tile with its own artwork. Press it and BoostOS closes what would get in the way,
switches power to performance and starts the game through its own launcher. When you close the game, everything goes
back by itself and you get a short report on how the PC held up.

> Made by **Hananel Sabag**.

---

## What's inside

| | |
|---|---|
| **Favourite game** | The big first tile, with the game's real art (from the launchers' own local caches, nothing is downloaded). △ / Y on any game puts it first. |
| **Games** | A folder that unfolds a row of every installed game underneath it: Epic Games and Steam, found from the launchers' files. Up returns to the rail, Back closes the row. |
| **Game Mode** | One pill next to the power button. Focus shows what it will do; a press switches it. It closes background sync, updaters and phone extras (OneDrive, Steam unless the game is on Steam, Phone Link, Widgets, Teams, Copilot, Adobe) and sets *Best performance*. Heavy apps that stay open (browsers, chat and AI apps, editors) go into Windows' efficiency mode instead of being closed, and every minute of the game it measures the CPU and quiets any other app working hard in the background (never sound, voice chat, recording, controllers, remote desktop or Windows itself). Before the match it says what only you can change: a laptop held to its base GPU power (not in Performance mode) or a driver profile a driver update reset. It turns on by itself when a game starts and is restored when the game closes, even after a crash. |
| **Background recorder** | A small part with no window that starts with Windows (about 50 MB, ~0.03% CPU). Every game is recorded and gets Game Mode, however it was started, even with BoostOS closed. One copy owns each session, so nothing is recorded twice. Besides the GPU, CPU and network it keeps the facts advice depends on: the Wi-Fi band, channel and width, whether sound goes over Bluetooth, the GPU's power limit, the power plan, and on Clevo-built laptops the fans and CPU temperature (read-only, through the vendor's own DLL). Each session's archive carries its summary, so one that never reached the list comes back by itself; anything that goes wrong is written to `%LOCALAPPDATA%\BoostOS\log.txt` (or `%TEMP%\BoostOS-log.txt`, with the reason, when that file can't be written). |
| **Diagnostics** | One screen with five tabs, switched with L1/R1 (LB/RB, Q/E): |
| ↳ Overview | The readiness score and eleven areas of the PC. The last, *From your games*, is what really happened while you played (heat and what it cost, GPU speed, CPU, fans, Wi-Fi, background), judged by the same rules as Game data. Areas with something to improve open by themselves; the rest fold to one line. |
| ↳ Fixes | Everything BoostOS can change, grouped into *worth fixing*, *changed by BoostOS* (with Undo), *already good* and *not needed here*. |
| ↳ Network | Router and internet pinged side by side, drawn live. It tells loss from a router that only limits pings, and finds spikes on a fixed beat. |
| ↳ Devices | What's plugged in, plus a controller test (a drawn controller that lights up, and a drift test that recommends a deadzone), a keyboard test (every key, and how many register at once) and a mouse test (polling rate, buttons, wheel). |
| ↳ Game data | Every recorded session: what it means in plain words first, then GPU heat and whether it actually cost frames (slowed while maxed out, not just sitting at its temperature target), a GPU clock held down by something other than heat, the CPU's real clock while the game used it, router and internet ping during play, Wi-Fi signal, memory, and which other apps used the CPU. What to try comes from that session's own facts (nobody on strong 5 GHz is told to move to 5 GHz). |
| **Rocket League video** | The game's own Video settings, read from its settings file and saved back to it while the game is closed. When heat cost frames in the last session, BoostOS suggests lighter settings. Everything it changes can be put back. |
| **Settings** | Language, favourite game, Game Mode on start, sounds, rumble, full screen, game data, and *Undo all fixes*. Exit asks first, and offers to just go to the desktop. |

| Games row | Diagnostics |
|---|---|
| ![Games row](docs/games-row.png) | ![Diagnostics overview](docs/diagnostics.png) |
| **Fixes** | **Game data** |
| ![Fixes](docs/fixes.png) | ![Game data](docs/game-data.png) |

## Safe with Easy Anti-Cheat

Rocket League requires Easy Anti-Cheat online since 28 April 2026. BoostOS is built so there is nothing for it to see:

- It never opens a handle to a game process, never reads its memory, and injects nothing.
- It finds running games from the kernel's process snapshot, and confirms one by its image path from the same snapshot
  (`NtQuerySystemInformation`, `SystemProcessIdInformation`): the information Task Manager shows, read without opening the process.
- Session reports use driver data (`nvidia-smi`), Windows' own CPU and clock counters, ping and the Wi-Fi API, never the game.
- It starts games only through Epic or Steam, so their anti-cheat starts with them.
- It reacts to the controller only while its own window has focus, so presses in the game never reach it.

Everything it changes is a Windows setting, a driver profile or a game's settings file: the same things you could change by hand.

## What a fix changes

| Fix | What changes | Admin |
|---|---|:-:|
| Rocket League FPS cap | NVIDIA *Max Frame Rate* for the game: the screen's refresh rate, or 3 below it with G-SYNC | ✓ |
| NVIDIA on the desktop | Global *Power management mode* back to the driver default, so the card rests outside games | ✓ |
| NVIDIA profile | Rocket League profile: *Prefer maximum performance* + *Low Latency Mode: On* (via NVAPI) | ✓ |
| Rocket League GPU | Graphics preference for `RocketLeague.exe` → *High performance* | |
| VSync | `UseVsync=False` in `TASystemSettings.ini` (backed up first) | |
| BoostOS power plan | A copy of Balanced: on the charger aggressive CPU boost and no PCI Express / USB / Wi-Fi power saving; on battery boost off and every saving on | |
| Power mode | Windows power mode → *Best performance* | |
| Windows Game Mode | Game Mode → on | |
| Windowed optimizations | *Optimizations for windowed games* → on | |
| Background recording | Game Bar *Record what happened* → off | |
| Wi-Fi power | Wireless adapter power saving on AC → *Maximum Performance* | |
| Wi-Fi roaming | Intel *Roaming Aggressiveness* → *Lowest* | ✓ |

Each fix stores the value it replaced in `%LOCALAPPDATA%\BoostOS\fixes.json`; *Undo* writes it back.
Admin fixes run in a second copy of BoostOS started elevated, so a batch asks for approval once.
What BoostOS deliberately leaves alone, because the effect is unproven or a trade-off: HAGS, MIMO power save, monitor overclocking, Windows security features.

## Design

- **The arena:** night navy under two floodlights that take the colour of the focused tile, with the focused game's art behind the home screen. Blue team, orange team, and boost yellow.
- **Readable where it counts:** every screen except Home dims the arena and puts text on solid cards.
- **The boost meter:** the readiness score is the round boost gauge from the Rocket League HUD.
- **Console motion:** a boot sequence, tiles that spring into focus with a light sweep, a games row that unfolds under its folder, tabs, dialogs, short Web Audio interface sounds and a light controller rumble. `prefers-reduced-motion` is honoured.
- **Free while you play:** animations use transform and opacity, loops pause when the page is hidden, and the host suspends the whole WebView2 once the window is minimized.
- **Type:** Karantina (scoreboard-style Hebrew display face) and Assistant, bundled so the app works offline.

## How it's built

```
src/BoostOS/
  Program.cs, MainForm.cs   WinForms host: fullscreen window, WebView2, message bridge, game watcher
  Core/                     models, settings, strings, Win32/WMI helpers, the handle-free process snapshot
  Diagnostics/              one class per scan area, run in parallel
  Network/NetTest.cs        live ping test and its analysis
  Game/                     game library, Game Mode, the session recorder and its probes
  Tuning/                   fixes with undo, NVIDIA profiles through NVAPI, Rocket League video settings
  wwwroot/                  the UI: vanilla HTML/CSS and ES modules, no framework (screens/, panels/)
tests/BoostOS.Tests/        xUnit tests for parsing, network analysis, reports, recovery, settings
tools/                      icon generator, standalone Wi-Fi tweak script
```

The page and the host talk through `chrome.webview.postMessage({ cmd })` and `{ type, data }` replies. Opened in a normal browser,
the UI falls back to a small fake host fed from `wwwroot/dev/*.sample.json`, which makes design work possible without the app.

## Build and run

Requires Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the WebView2 runtime (part of Windows 11).

```
dotnet run --project src/BoostOS                 # full screen
dotnet run --project src/BoostOS -- --windowed   # in a window
dotnet test BoostOS.slnx
dotnet publish src/BoostOS -c Release -o publish
```

Headless modes, handy for checking a machine:

```
BoostOS.exe --scan-json scan.json [en]   # full diagnostics as JSON
BoostOS.exe --games-json games.json      # installed games, art, running game, Rocket League video settings
BoostOS.exe --net-json net.json 60       # 60-second network test with every sample
BoostOS.exe --efficiency-test            # efficiency mode on the heavy background apps for 5 s, then back
BoostOS.exe --record-json rec.json 40    # a 40-second session recording without a game: checks every probe
BoostOS.exe --findings-json f.json [en]  # what every recorded session means, as Game data shows it
BoostOS.exe --busy-test 20 [2]           # which apps Game Mode would quiet as busy over 20 s (changes nothing)
BoostOS.exe --nvidia-json nv.json        # raw NVIDIA profile fields, global profile included
```

## Things learned along the way

- **The global NVIDIA profile matters on a laptop.** On the test machine the global *Power management mode* was *Prefer maximum performance*, so the GPU sat at P0 drawing ~29 W at 63 °C on an idle desktop. Games keep their own profile; the desktop doesn't need full clocks.
- **At the heat limit is not the same as held back.** A laptop GPU boosts until it reaches its temperature target (87 °C on the test machine, usually within 1–3 minutes) and then trims its clock to stay there. Across 19 recorded sessions the card sat at that limit up to 90 % of the time, yet it was never more than 90 % busy while doing so: the frame cap was reached anyway, so heat cost nothing. Reports now count heat only when it slows a maxed-out card.
- **A low clock can hide behind a cool-looking report.** In 5 of those sessions the GPU stayed at ~780 MHz the whole game while 70–85 % busy, with no heat or power reason from the driver. That is now its own warning, and sessions record the card's power limit so the cause shows next time.
- **The GPU's power limit gives away the laptop's mode.** Control Center's mode lives in the laptop's controller and can't be read safely, but the RTX 3060 Laptop on the test machine runs at its 80 W base in the quiet modes and gets up to 105 W with Dynamic Boost in Performance. `nvidia-smi` reports both, so BoostOS can say which mode the laptop is really in.
- **Bluetooth sound shares the air with Wi-Fi.** On the Intel AX201 Wi-Fi and Bluetooth share an antenna, and the test machine plays its sound through a Bluetooth receiver all the time. With ~8 ping spikes an hour on an otherwise clean 5 GHz link, that's the first thing BoostOS suggests testing.
- **A router can fake packet loss.** The home router used for testing answers a ~15 second burst of pings and then drops exactly one a second once the total passes ~7/s. The test runs at 2.5 pings/s per target, and the in-game probe at one a second.
- **NVAPI struct versions matter.** Newer NVAPI headers add a 64-bit member to `NVDRS_SETTING` (12,328 bytes); the driver tested here expects the older 12,320-byte layout. BoostOS tries both.
- **Measure before you look.** Idle CPU load is sampled before the scan starts, otherwise the scan measures itself.

## License

[MIT](LICENSE) © 2026 Hananel Sabag. Rocket League is a trademark of Psyonix; BoostOS is an independent project and isn't affiliated with Psyonix, Epic Games, Valve or NVIDIA.

---

## בעברית

BoostOS הוא מסך בית בסגנון קונסולה לווינדוס, שמכין את המחשב למשחק ומחזיר אותו למצב הרגיל אחרי.

- **משחק מועדף:** הריבוע הגדול הראשון, עם התמונה של המשחק. △ על כל משחק שם אותו בהתחלה.
- **משחקים:** תיקייה שנפרסת לשורה מתחת עם כל המשחקים המותקנים מ-Epic ומ-Steam. למעלה חוזרים, חזרה סוגרת.
- **מצב משחק:** כפתור קטן ליד כפתור היציאה. נדלק לבד כשמפעילים משחק, וכשהמשחק נסגר הכל חוזר לבד. לאורך המשחק הוא מודד את המעבד ומשתיק כל תוכנה שעובדת קשה ברקע, ולפני המשחק אומר מה רק אתה יכול לשנות (לפטופ שלא במצב Performance, פרופיל דרייבר שאופס).
- **אבחון:** מסך אחד עם לשוניות: סקירה (מה תקין ומה לא, כולל "מהמשחקים שלך": מה באמת קרה בזמן המשחקים), תיקונים, רשת, התקנים (בדיקת שלט, מקלדת ועכבר) ונתוני משחק.
- **נתוני משחק:** אחרי כל משחק: חום כרטיס המסך והאם הוא באמת עלה בפריימים (האטה כשהכרטיס במלוא הכוח, לא רק ישיבה בגבול החום שלו), כרטיס שנתקע על מהירות נמוכה בלי קשר לחום, מהירות המעבד כשהמשחק השתמש בו, פינג ו-Wi-Fi בזמן המשחק, ואיזה תוכנות עבדו ברקע. מה לנסות נקבע לפי העובדות של אותו משחק (תדר, רוחב ערוץ, סאונד ב-Bluetooth, מאווררים, גבול ההספק של הכרטיס), ולא נאמר לכולם.
- **הגדרות וידאו לרוקט ליג:** ההגדרות של המשחק עצמו, עם המלצות לפי מה שנמדד במחשב, ושמירה ישר לקובץ של המשחק.
- **בטוח עם האנטי-צ'יט:** BoostOS לא פותח את תהליך המשחק ולא נוגע בזיכרון שלו. הוא משנה רק הגדרות של ווינדוס, פרופיל בדרייבר וקובץ ההגדרות של המשחק, ואת כל השינויים אפשר להחזיר.
- **שפה:** עברית או אנגלית.

נבנה על ידי **חננאל סבג**. קוד פתוח ברישיון [MIT](LICENSE).

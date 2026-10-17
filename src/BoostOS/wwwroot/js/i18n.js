// Interface strings in both languages. Diagnostic texts come from the C# side already in the chosen language.

const STRINGS = {
  he: {
    'tile.play': 'רוקט ליג', 'tile.play.sub': 'שחק',
    'tile.game': 'מצב משחק', 'tile.game.on': 'פעיל', 'tile.game.off': 'כבוי',
    'tile.diag': 'אבחון', 'tile.diag.sub': '4 כלים',
    'tile.settings': 'הגדרות',
    'tool.scan': 'סריקה מלאה', 'tool.net': 'בדיקת רשת', 'tool.pad': 'בדיקת שלט', 'tool.mouse': 'בדיקת עכבר',

    'hub.play': 'מכין את המחשב ופותח את המשחק דרך {store}, יחד עם Easy Anti‑Cheat. כשתסיים לשחק, הכל חוזר לבד.',
    'hub.game': 'סוגר תוכנות רקע שמסנכרנות ומעדכנות באמצע משחק, ומעביר את החשמל לביצועים. כשהמשחק נסגר, הכל חוזר כמו שהיה.',
    'hub.diag': 'כל מה שבודק את המחשב, במקום אחד: סריקה של כל המערכת, רשת, שלט ועכבר.',
    'hub.settings': 'שפה, צלילים, רטט, ומה שהוחל על המחשב.',
    'hub.scan': 'בודק מסכים, כרטיס מסך, חשמל, רשת, שלטים ומה שרץ ברקע, ומתקן בלחיצה את מה שאפשר.',
    'hub.net': 'פינג לנתב ולאינטרנט באותו זמן: מגלה אם הלאג מגיע מה-Wi-Fi או מספק האינטרנט.',
    'hub.pad': 'סטיקים, הדקים וכפתורים בזמן אמת, ומדידת drift שאומרת לך איזה deadzone לשים במשחק.',
    'hub.mouse': 'כמה פעמים בשנייה העכבר מדווח למחשב.',
    'hub.lastMatch': 'המשחק האחרון', 'hub.minutes': 'דקות', 'hub.gpuMax': 'כרטיס מסך, שיא', 'hub.limit': 'מה הגביל',
    'hub.willClose': 'ייסגרו', 'hub.nothingToClose': 'אין מה לסגור',
    'hub.fixable': 'BoostOS יכול לתקן {n} מהם לבד', 'hub.fixTitle': 'הכי שווה לתקן', 'hub.allGood': 'הכל מוכן. אפשר לשחק.',
    'hub.scanning': 'סורק את המחשב…', 'hub.lang': 'שפה', 'hub.sounds': 'צלילים',
    'bottleneck.cpu': 'המעבד', 'bottleneck.gpu': 'כרטיס המסך', 'bottleneck.none': 'כלום', 'bottleneck.unknown': 'לא ידוע',

    'gauge.label': 'מוכנות למשחק', 'gauge.scanning': 'סורק',
    'status.bad': 'לתקן', 'status.warn': 'לשיפור', 'status.ok': 'תקין', 'status.info': 'מידע',

    'scan.title': 'אבחון המערכת', 'scan.rescan': 'סרוק שוב', 'scan.only': 'רק מה שצריך טיפול',
    'scan.meta': '{machine}, נסרק ב-{time}', 'scan.loading': 'סורק את המחשב. זה לוקח כמה שניות.',
    'scan.empty': 'אין מה לתקן. הכל במצב טוב.', 'scan.fix': 'תקן', 'scan.fixAll': 'תקן הכל ({n})',
    'scan.admin': 'דורש אישור מנהל', 'scan.fixing': 'מתקן…', 'scan.fixed': 'תוקן',
    'scan.fixedToast': 'תוקנו {n}. סורק שוב…', 'scan.fixFailed': 'לא תוקן: {msg}',

    'net.title': 'בדיקת רשת', 'net.meta': 'פינג לנתב ולאינטרנט באותו זמן', 'net.30': '30 שניות', 'net.60': 'דקה', 'net.120': '2 דקות',
    'net.start': 'התחל בדיקה', 'net.stop': 'עצור', 'net.again': 'בדוק שוב', 'net.stopped': 'בדיקת הרשת נעצרה',
    'net.keyRouter': 'נתב: מודד רק את ה-Wi-Fi, מהמחשב עד הנתב', 'net.keyInternet': 'אינטרנט: מהמחשב עד 1.1.1.1', 'net.keyLost': 'פס אדום למעלה: חבילה שלא חזרה',
    'net.router': 'נתב', 'net.internet': 'אינטרנט', 'net.median': 'ms חציון', 'net.jitter': 'רעד (jitter)', 'net.loss': 'איבוד חבילות', 'net.max': 'הכי גבוה', 'net.spikes': 'קפיצות',
    'net.every': '{n}, כל {s} שניות', 'net.running': 'אל תוריד משהו ואל תפתח וידאו בזמן הבדיקה, כדי שהמדידה תראה את הרשת כמו בזמן משחק.',
    'net.idle': 'אם הקו הכחול נקי והכתום קופץ, הבעיה אצל ספק האינטרנט. אם הכחול קופץ, הבעיה ב-Wi-Fi עצמו, ואת זה אפשר לתקן.',
    'net.waiting': 'הגרף יתמלא כשהבדיקה תתחיל', 'net.measuring': 'מתחיל למדוד…',

    'pad.title': 'בדיקת שלט', 'pad.meta': 'סטיקים, הדקים וכפתורים בזמן אמת', 'pad.left': 'סטיק שמאלי', 'pad.right': 'סטיק ימני',
    'pad.none': 'לחץ על כפתור כלשהו בשלט כדי שהבדיקה תתחיל.', 'pad.driftTitle': 'מדידת drift',
    'pad.driftText': 'עזוב את השלט על השולחן ואל תיגע בסטיקים. נמדוד 3 שניות כמה הם זזים לבד.',
    'pad.measure': 'מדוד drift', 'pad.hands': 'אל תיגע בשלט', 'pad.result': 'drift במנוחה', 'pad.leftShort': 'שמאל', 'pad.rightShort': 'ימין',
    'pad.recommend': 'ברוקט ליג: Settings → Controls → Controller Deadzone. אפשר לרדת עד {dz}. deadzone נמוך = תגובה מהירה יותר, אבל מתחת ל-drift המכונית תזוז לבד.',
    'pad.recommendBad': 'drift של {d} זה הרבה. כדאי Controller Deadzone של {dz} לפחות, ולשקול ניקוי או תיקון של הסטיק.',
    'pad.hold': 'החזק ○ כדי לצאת', 'pad.ring': 'הקו המקווקו: ה-deadzone המומלץ',

    'mouse.title': 'בדיקת עכבר', 'mouse.meta': 'כמה פעמים בשנייה העכבר מדווח למחשב (polling rate)',
    'mouse.pad': 'הזז את העכבר מהר בעיגולים בתוך המסגרת, כמה שניות.', 'mouse.now': 'Hz עכשיו', 'mouse.peak': 'Hz שיא',
    'mouse.none': 'עוד אין מדידה.', 'mouse.reset': 'אפס מדידה', 'mouse.faster': 'תזיז מהר יותר, בעיגולים, כדי לקבל מדידה אמיתית.',
    'mouse.125': 'בערך 125Hz. זה מה שמקבלים ב-Bluetooth או בעכבר משרדי. אם לעכבר יש דונגל אלחוטי, חבר אותו: הוא מגיע ל-1000Hz.',
    'mouse.500': 'בערך 500Hz. סביר. בתוכנה של העכבר אפשר בדרך כלל להעלות ל-1000Hz.',
    'mouse.1000': 'בערך 1000Hz. מצוין, זה הסטנדרט לגיימינג.', 'mouse.fast': '{hz}Hz. עכבר מהיר מאוד. אם יש גמגום במשחקים, שווה לנסות 1000Hz.',

    'game.title': 'מצב משחק', 'game.meta': 'מכין את המחשב לרוקט ליג, ומחזיר הכל כשמסיימים',
    'game.on': 'פעיל', 'game.off': 'כבוי', 'game.since': 'פעיל מ-{time}', 'game.play': 'הפעל ושחק', 'game.open': 'פתח את רוקט ליג',
    'game.only': 'רק להפעיל מצב משחק', 'game.restore': 'החזר הכל עכשיו', 'game.will': 'מה יקרה', 'game.did': 'מה נעשה',
    'game.nothing': 'לא היה מה לשנות', 'game.ready': 'המחשב כבר היה מוכן.', 'game.after': 'אחרי המשחק', 'game.afterText': 'כשרוקט ליג נסגר, הכל חוזר לבד.',
    'game.note': 'BoostOS לא נוגע במשחק עצמו, וגם לא בדפדפן, בדיסקורד או בספוטיפיי.',
    'game.applying': 'מפעיל מצב משחק…', 'game.restoring': 'מחזיר הכל למצב הרגיל…', 'game.launching': 'מכין את המחשב ופותח את רוקט ליג…',
    'report.title': 'המשחק האחרון', 'report.none': 'עוד אין דוח. הדוח נוצר אוטומטית אחרי משחק, מנתוני המעבד והכרטיס בלבד. המשחק עצמו לא נקרא.',
    'report.minutes': 'דקות', 'report.gpuUtil': 'עומס כרטיס', 'report.gpuTemp': 'טמפ׳ כרטיס, שיא', 'report.gpuClock': 'מהירות כרטיס', 'report.power': 'הספק',
    'report.limited': 'זמן שהכרטיס הוגבל', 'report.core': 'הליבה העמוסה', 'report.withGame': 'עם מצב משחק', 'report.withoutGame': 'בלי מצב משחק',
    'report.cpu': 'המעבד הוא שהגביל: ליבה אחת עבדה כמעט במלואה. כאן עוזרים פחות תוכנות ברקע, מצב Turbo בלפטופ, והגבלת FPS קצת מתחת למה שהמחשב מחזיק.',
    'report.gpu': 'כרטיס המסך עבד במלואו. אם רוצים יותר FPS, מורידים איכות גרפית או רזולוציה במשחק.',
    'report.none2': 'לא המעבד ולא הכרטיס עבדו במלואם, כנראה הגבלת FPS או VSync. המחשב מחזיק בנוחות.',
    'report.hot': 'הכרטיס הגיע ל-{t}°C. מעל 85°C הוא מוריד מהירות. מעמד מאוורר ללפטופ וניקוי אבק עוזרים.',
    'report.compare': 'לעומת המשחק הקודם', 'report.fresh': 'דוח חדש מהמשחק שהסתיים',

    'settings.title': 'הגדרות', 'settings.lang': 'שפה', 'settings.langSub': 'עברית או English',
    'settings.sounds': 'צלילי ממשק', 'settings.soundsSub': 'צליל קצר בכל תזוזה ובחירה',
    'settings.rumble': 'רטט בשלט', 'settings.rumbleSub': 'רטט עדין כשבוחרים',
    'settings.fullscreen': 'פתיחה במסך מלא', 'settings.fullscreenSub': 'BoostOS נפתח כמו קונסולה, בלי שורת המשימות',
    'settings.track': 'דוח אחרי כל משחק', 'settings.trackSub': 'נתוני מעבד וכרטיס מסך בלבד. המשחק עצמו לא נקרא.',
    'settings.fixes': 'תיקונים שהוחלו', 'settings.fixesSub': '{n} שינויים במחשב. אפשר להחזיר את כולם.', 'settings.fixesNone': 'עוד לא הוחל שום תיקון',
    'settings.undo': 'החזר הכל', 'settings.undone': 'כל התיקונים הוחזרו', 'settings.exit': 'יציאה מ-BoostOS',
    'settings.about': 'מסך משחק שמכין את המחשב לרוקט ליג. קוד פתוח.',

    'hint.select': 'בחירה', 'hint.back': 'חזרה', 'hint.full': 'חלון או מסך מלא', 'hint.close': 'סגירת התיקייה',
    'launch.ok': 'רוקט ליג נפתח', 'launch.fail': 'לא מצאתי איך לפתוח את רוקט ליג',
    'pad.connected': 'שלט PlayStation מחובר', 'pad.connectedGeneric': 'שלט מחובר', 'chip.game': 'מצב משחק פעיל',
    'error': 'משהו נכשל: {msg}', 'preview': 'בתצוגה המקדימה בדפדפן אין את הפעולה הזו', 'exit': 'יציאה',
  },
  en: {
    'tile.play': 'Rocket League', 'tile.play.sub': 'Play',
    'tile.game': 'Game Mode', 'tile.game.on': 'On', 'tile.game.off': 'Off',
    'tile.diag': 'Diagnostics', 'tile.diag.sub': '4 tools',
    'tile.settings': 'Settings',
    'tool.scan': 'Full scan', 'tool.net': 'Network test', 'tool.pad': 'Controller test', 'tool.mouse': 'Mouse test',

    'hub.play': 'Gets the PC ready, then starts the game through {store} with Easy Anti‑Cheat. When you finish, everything goes back by itself.',
    'hub.game': 'Closes background apps that sync and update mid-match, and switches power to performance. When the game closes, it all goes back.',
    'hub.diag': 'Everything that checks the PC, in one place: a full system scan, network, controller and mouse.',
    'hub.settings': 'Language, sounds, rumble, and what has been changed on this PC.',
    'hub.scan': 'Checks displays, graphics, power, network, controllers and background apps, and fixes what it can in one press.',
    'hub.net': 'Pings the router and the internet side by side, so you know whether lag comes from Wi-Fi or your provider.',
    'hub.pad': 'Sticks, triggers and buttons live, plus a drift test that tells you which deadzone to use in game.',
    'hub.mouse': 'How many times a second your mouse reports to the PC.',
    'hub.lastMatch': 'Last match', 'hub.minutes': 'minutes', 'hub.gpuMax': 'GPU peak', 'hub.limit': 'Held back by',
    'hub.willClose': 'Will close', 'hub.nothingToClose': 'Nothing to close',
    'hub.fixable': 'BoostOS can fix {n} of them by itself', 'hub.fixTitle': 'Worth fixing', 'hub.allGood': 'All set. Go play.',
    'hub.scanning': 'Scanning the PC…', 'hub.lang': 'Language', 'hub.sounds': 'Sounds',
    'bottleneck.cpu': 'the CPU', 'bottleneck.gpu': 'the GPU', 'bottleneck.none': 'nothing', 'bottleneck.unknown': 'unknown',

    'gauge.label': 'match ready', 'gauge.scanning': 'scanning',
    'status.bad': 'Fix', 'status.warn': 'Improve', 'status.ok': 'Good', 'status.info': 'Info',

    'scan.title': 'System diagnostics', 'scan.rescan': 'Scan again', 'scan.only': 'Only what needs attention',
    'scan.meta': '{machine}, scanned at {time}', 'scan.loading': 'Scanning the PC. This takes a few seconds.',
    'scan.empty': 'Nothing to fix. Everything looks good.', 'scan.fix': 'Fix', 'scan.fixAll': 'Fix all ({n})',
    'scan.admin': 'Needs admin approval', 'scan.fixing': 'Fixing…', 'scan.fixed': 'Fixed',
    'scan.fixedToast': '{n} fixed. Scanning again…', 'scan.fixFailed': 'Not fixed: {msg}',

    'net.title': 'Network test', 'net.meta': 'Router and internet ping, side by side', 'net.30': '30 seconds', 'net.60': '1 minute', 'net.120': '2 minutes',
    'net.start': 'Start test', 'net.stop': 'Stop', 'net.again': 'Test again', 'net.stopped': 'Network test stopped',
    'net.keyRouter': 'Router: just the Wi-Fi, PC to router', 'net.keyInternet': 'Internet: PC to 1.1.1.1', 'net.keyLost': 'Red tick on top: a packet that never came back',
    'net.router': 'Router', 'net.internet': 'Internet', 'net.median': 'ms median', 'net.jitter': 'Jitter', 'net.loss': 'Packet loss', 'net.max': 'Highest', 'net.spikes': 'Spikes',
    'net.every': '{n}, every {s} s', 'net.running': "Don't download or stream during the test, so it sees the network the way a match does.",
    'net.idle': "If blue is clean and orange jumps, it's your provider. If blue jumps, it's the Wi-Fi itself, and that can be fixed.",
    'net.waiting': 'The chart fills in once the test starts', 'net.measuring': 'Starting to measure…',

    'pad.title': 'Controller test', 'pad.meta': 'Sticks, triggers and buttons, live', 'pad.left': 'Left stick', 'pad.right': 'Right stick',
    'pad.none': 'Press any button on the controller to start.', 'pad.driftTitle': 'Drift test',
    'pad.driftText': "Put the controller down and don't touch the sticks. For 3 seconds we measure how much they move on their own.",
    'pad.measure': 'Measure drift', 'pad.hands': 'Hands off', 'pad.result': 'Drift at rest', 'pad.leftShort': 'left', 'pad.rightShort': 'right',
    'pad.recommend': 'In Rocket League: Settings → Controls → Controller Deadzone. You can go as low as {dz}. Lower = more responsive, but below the drift your car moves on its own.',
    'pad.recommendBad': '{d} of drift is a lot. Use a Controller Deadzone of at least {dz}, and consider cleaning or repairing the stick.',
    'pad.hold': 'Hold ○ to leave', 'pad.ring': 'Dashed ring: the recommended deadzone',

    'mouse.title': 'Mouse test', 'mouse.meta': 'How many times a second the mouse reports (polling rate)',
    'mouse.pad': 'Move the mouse fast in circles inside the frame for a few seconds.', 'mouse.now': 'Hz now', 'mouse.peak': 'Hz peak',
    'mouse.none': 'No measurement yet.', 'mouse.reset': 'Reset', 'mouse.faster': 'Move faster, in circles, for a real reading.',
    'mouse.125': "About 125Hz. That's Bluetooth or an office mouse. If it has a wireless dongle, use it: up to 1000Hz.",
    'mouse.500': 'About 500Hz. Fine. The mouse software can usually raise it to 1000Hz.',
    'mouse.1000': "About 1000Hz. Excellent, that's the gaming standard.", 'mouse.fast': '{hz}Hz. A very fast mouse. If games stutter, try 1000Hz.',

    'game.title': 'Game Mode', 'game.meta': 'Gets the PC ready for Rocket League, and puts it all back afterwards',
    'game.on': 'On', 'game.off': 'Off', 'game.since': 'On since {time}', 'game.play': 'Start and play', 'game.open': 'Open Rocket League',
    'game.only': 'Just turn on Game Mode', 'game.restore': 'Put everything back now', 'game.will': 'What happens', 'game.did': 'What was done',
    'game.nothing': 'Nothing needed changing', 'game.ready': 'The PC was already ready.', 'game.after': 'After the match', 'game.afterText': 'When Rocket League closes, everything goes back by itself.',
    'game.note': "BoostOS never touches the game itself, nor your browser, Discord or Spotify.",
    'game.applying': 'Turning on Game Mode…', 'game.restoring': 'Putting everything back…', 'game.launching': 'Getting the PC ready and starting Rocket League…',
    'report.title': 'Last match', 'report.none': "No report yet. One is made automatically after a match, from CPU and GPU numbers only. The game itself is never read.",
    'report.minutes': 'minutes', 'report.gpuUtil': 'GPU load', 'report.gpuTemp': 'GPU temp, peak', 'report.gpuClock': 'GPU clock', 'report.power': 'Power',
    'report.limited': 'GPU held back', 'report.core': 'Busiest core', 'report.withGame': 'with Game Mode', 'report.withoutGame': 'without Game Mode',
    'report.cpu': 'The CPU held you back: one core ran almost flat out. Fewer background apps, the laptop\'s Turbo mode, and an FPS cap a little below what the PC holds all help here.',
    'report.gpu': 'The GPU was maxed out. For more FPS, lower graphics quality or resolution in game.',
    'report.none2': 'Neither the CPU nor the GPU was maxed, likely an FPS cap or VSync. The PC is comfortable.',
    'report.hot': 'The GPU reached {t}°C. Above 85°C it clocks down. A laptop stand with fans and a dust clean help.',
    'report.compare': 'vs. the previous match', 'report.fresh': 'Fresh report from the match that just ended',

    'settings.title': 'Settings', 'settings.lang': 'Language', 'settings.langSub': 'עברית or English',
    'settings.sounds': 'Interface sounds', 'settings.soundsSub': 'A short sound on every move and selection',
    'settings.rumble': 'Controller rumble', 'settings.rumbleSub': 'A light rumble when you select',
    'settings.fullscreen': 'Start full screen', 'settings.fullscreenSub': 'BoostOS opens like a console, no taskbar',
    'settings.track': 'Report after every match', 'settings.trackSub': 'CPU and GPU numbers only. The game itself is never read.',
    'settings.fixes': 'Applied fixes', 'settings.fixesSub': '{n} changes to this PC. They can all be undone.', 'settings.fixesNone': 'No fixes applied yet',
    'settings.undo': 'Undo all', 'settings.undone': 'All fixes undone', 'settings.exit': 'Exit BoostOS',
    'settings.about': 'A game screen that gets your PC ready for Rocket League. Open source.',

    'hint.select': 'Select', 'hint.back': 'Back', 'hint.full': 'Window or full screen', 'hint.close': 'Close folder',
    'launch.ok': 'Rocket League is starting', 'launch.fail': "Couldn't find a way to start Rocket League",
    'pad.connected': 'PlayStation controller connected', 'pad.connectedGeneric': 'Controller connected', 'chip.game': 'Game Mode on',
    'error': 'Something failed: {msg}', 'preview': "Not available in the browser preview", 'exit': 'Exit',
  },
};

let lang = 'he';

export const getLang = () => lang;

export function setLang(next) {
  lang = next === 'en' ? 'en' : 'he';
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === 'en' ? 'ltr' : 'rtl';
}

export function t(key, vars = {}) {
  const s = STRINGS[lang][key] ?? STRINGS.he[key] ?? key;
  return s.replace(/\{(\w+)\}/g, (_, k) => vars[k] ?? '');
}

export const locale = () => (lang === 'en' ? 'en-GB' : 'he-IL');

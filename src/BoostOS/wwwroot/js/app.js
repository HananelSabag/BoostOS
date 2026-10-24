// BoostOS front end: boot sequence, screen switching, settings and the shared app state.

import { setLang, t } from './i18n.js';
import { on, send } from './bridge.js';
import { $, $$, toast, setAccent, aimLight, reduceMotion } from './ui.js';
import { initNav, focusFirst, focusEl, renderHints } from './nav.js';
import { configure, sfx } from './sound.js';
import { initChrome, onGameMode, gameModeFailed, renderPill, dialogOpen, closeDialog, startClock, pillHints } from './chrome.js';
import { initFixes } from './panels/fixrun.js';
import home from './screens/home.js';
import diag from './screens/diag.js';
import pad from './screens/pad.js';
import keys from './screens/keys.js';
import mouse from './screens/mouse.js';
import rlvideo from './screens/rlvideo.js';
import settings from './screens/settings.js';

const screens = { home, diag, pad, keys, mouse, rlvideo, settings };
const ACCENT = { home: 'play', diag: 'diag', pad: 'diag', keys: 'diag', mouse: 'diag', rlvideo: 'play', settings: 'settings' };
const TESTS = ['pad', 'keys', 'mouse'];

const app = {
  settings: { language: 'he', sounds: true, rumble: true, startFullscreen: true, trackMatches: true, gameModeOnPlay: true, backgroundRecorder: true, favoriteGame: null },
  report: null,
  scanning: false,
  scanError: null,
  game: null,
  games: [],
  matches: [],
  applied: [],
  rlVideo: null,
  running: null,
  version: '',
  fixesApplied: 0,
  current: 'home',
};
let rescanAfterCurrent = false;

const ctx = { app, show, back, startScan, saveSettings, refreshAll, renderHints };
Object.values(screens).forEach(s => s.init(ctx));
initFixes(ctx);

function buildAll() {
  for (const [name, s] of Object.entries(screens)) s.build($(`.screen[data-screen="${name}"]`));
  $('.power').setAttribute('aria-label', t('exit'));
  $('#gm-pill').setAttribute('aria-label', t('gm.name'));
  renderPill();
}

/** Switch screens. opts: { focus, instant, tab, from, newest }. */
function show(name, opts = {}) {
  const swap = () => {
    const changed = name !== app.current;
    if (changed) screens[app.current]?.leave?.();
    $$('.screen').forEach(s => { s.hidden = s.dataset.screen !== name; });
    app.current = name;
    document.body.dataset.screen = name;
    if (changed || opts.tab) screens[name].enter?.(opts);
    renderHints();
    (opts.focus ?? (name === 'diag' ? diag.focusTarget(opts) : focusFirst))();
    if (name !== 'home') { setAccent(ACCENT[name]); aimLight(null); }
  };
  if (document.startViewTransition && !reduceMotion && !opts.instant && name !== app.current) document.startViewTransition(swap);
  else swap();
}

/** Back: the screen first (close a folder, a tab's own back), then one level up, like a console. */
function back() {
  if (dialogOpen()) { closeDialog(null); return; }
  if (screens[app.current].back?.()) return;
  const from = app.current;
  if (from === 'home') return;
  if (TESTS.includes(from)) { show('diag', { tab: 'devices', focus: () => focusEl($(`[data-test="${from}"]`)) }); return; }
  if (from === 'rlvideo' && rlvideo.returnTo() === 'diag') { show('diag', { tab: 'fixes', focus: () => focusEl($('#fx-video')) }); return; }
  show('home', { focus: () => home.focusTile(from) });
}

function startScan() {
  if (app.scanning) return;
  app.scanning = true;
  app.scanError = null;
  refreshAll();
  send('scan');
}

function refreshAll() {
  const before = document.activeElement;
  Object.values(screens).forEach(s => s.refresh?.());
  renderPill();
  // A redraw must never leave the D-pad with nothing focused: fall back to the screen's natural spot.
  if (before && before !== document.body && !before.isConnected && !dialogOpen()) {
    if (app.current === 'diag') diag.focusTarget()();
    else focusFirst();
  }
}

function saveSettings(next) {
  send('saveSettings', { settings: next });
  applySettings(next);
}

function applySettings(next) {
  const langChanged = next.language !== app.settings.language;
  app.settings = next;
  configure(next);
  if (!langChanged) { refreshAll(); return; }
  setLang(next.language);
  buildAll();
  show(app.current, { instant: true, focus: () => $('[data-key="language"]')?.focus({ preventScroll: true }) });
  app.report = null;
  if (app.scanning) rescanAfterCurrent = true;
  else startScan(); // diagnostic texts come from the host, in the new language
}

// ---- Messages from the host ----

on('hello', h => {
  app.version = h.version;
  app.matches = h.matches ?? [];
  app.game = h.gameMode;
  app.fixesApplied = h.fixesApplied ?? 0;
  app.settings = { ...app.settings, ...h.settings };
  configure(app.settings);
  setLang(app.settings.language);
  buildAll();
  boot();
  send('fixesApplied');
});
on('games', list => { app.games = list ?? []; refreshAll(); });
on('settings', s => { if (JSON.stringify(s) !== JSON.stringify(app.settings)) applySettings(s); });
on('scan', r => {
  app.scanning = false;
  if (rescanAfterCurrent) { rescanAfterCurrent = false; startScan(); return; }
  app.report = r;
  refreshAll();
});
on('gameMode', s => {
  app.game = s;
  onGameMode(s);
  refreshAll();
});
on('gameRunning', g => { app.running = g.id; refreshAll(); });
on('launch', r => toast(r.ok ? t('launch.ok', { name: r.name ?? '' }) : t('launch.fail')));
on('notice', n => toast(n.message));
on('matches', list => { app.matches = list ?? []; refreshAll(); });
on('matchReport', ({ report }) => {
  app.running = null;
  app.matches = [report, ...(app.matches ?? []).filter(m => m.start !== report.start)].slice(0, 20);
  refreshAll();
  show('diag', { tab: 'sessions', newest: true });
  toast(t('ses.fresh'));
});
on('error', e => {
  if (e.cmd === 'scan') {
    app.scanning = false;
    app.scanError = e.message;
    if (rescanAfterCurrent) { rescanAfterCurrent = false; startScan(); }
    else refreshAll();
  }
  if (e.cmd === 'gameModeOn' || e.cmd === 'gameModeOff') gameModeFailed();
  sfx.error();
  toast(t('error', { msg: e.message }));
});
on('preview', () => toast(t('preview')));
on('resumed', () => { send('gameModeStatus'); send('matches'); });

// ---- Boot: the boost meter fills, the arena opens, the tiles arrive ----

function boot() {
  $$('.screen').forEach(s => { s.hidden = s.dataset.screen !== 'home'; });
  app.current = 'home';
  document.body.dataset.screen = 'home';
  initNav({
    back,
    tab: dir => { if (!dialogOpen()) screens[app.current].tab?.(dir); },
    options: () => { if (!dialogOpen()) screens[app.current].options?.(); },
    current: () => app.current,
    hints: () => dialogOpen() ? [['select', t('hint.select')], ['back', t('hint.cancel')]]
      : document.activeElement?.id === 'gm-pill' ? [...pillHints(), ['back', t('hint.back')]]
      : screens[app.current].hints(),
  });
  sfx.boot();
  home.enter();

  const overlay = $('#boot');
  const finish = () => {
    if (overlay.classList.contains('done')) return;
    overlay.classList.add('done');
    const homeEl = $('.screen[data-screen="home"]');
    homeEl.classList.add('entering');
    focusFirst();
    // Drop the entrance class once it has played, so focus transforms are not held by the animation.
    setTimeout(() => homeEl.classList.remove('entering'), 1100);
    setTimeout(() => overlay.remove(), 700);
  };
  const timer = setTimeout(finish, reduceMotion ? 150 : 1250);
  const skip = () => { clearTimeout(timer); finish(); };
  addEventListener('keydown', skip, { once: true });
  addEventListener('pointerdown', skip, { once: true });

  // Give the window a moment to settle so its own start-up work doesn't count as background CPU load.
  setTimeout(startScan, 1500);
}

document.addEventListener('visibilitychange', () => document.body.classList.toggle('paused', document.hidden));
document.addEventListener('keydown', e => {
  if (e.key === 'F11' && app.current !== 'keys') { e.preventDefault(); send('toggleFullscreen'); }
});
// The pill's flyout and the hints follow the focus in the top bar.
document.addEventListener('focusin', e => { if (e.target.closest?.('.topbar')) renderHints(); });

initChrome(ctx);
startClock();
send('hello');

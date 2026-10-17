// BoostOS front end: boot sequence, screen switching, settings and the shared app state.

import { setLang, t } from './i18n.js';
import { on, send } from './bridge.js';
import { $, $$, toast, setAccent, aimLight, reduceMotion, icon } from './ui.js';
import { initNav, focusFirst, renderHints } from './nav.js';
import { configure, sfx } from './sound.js';
import home from './screens/home.js';
import scan from './screens/scan.js';
import net from './screens/net.js';
import pad from './screens/pad.js';
import mouse from './screens/mouse.js';
import game from './screens/game.js';
import settings from './screens/settings.js';

const screens = { home, scan, net, pad, mouse, game, settings };
const ACCENT = { home: 'play', scan: 'diag', net: 'diag', pad: 'diag', mouse: 'diag', game: 'game', settings: 'settings' };

const app = {
  settings: { language: 'he', sounds: true, rumble: true, startFullscreen: true, trackMatches: true },
  report: null,
  scanning: false,
  game: null,
  matches: [],
  version: '',
  fixesApplied: 0,
  current: 'home',
};

const ctx = { app, show, back, startScan, saveSettings, refreshAll, renderHints };
Object.values(screens).forEach(s => s.init(ctx));

function buildAll() {
  for (const [name, s] of Object.entries(screens)) s.build($(`.screen[data-screen="${name}"]`));
  $('.power').setAttribute('aria-label', t('exit'));
}

function show(name, { focus, instant } = {}) {
  const swap = () => {
    const changed = name !== app.current;
    if (changed) screens[app.current]?.leave?.();
    $$('.screen').forEach(s => { s.hidden = s.dataset.screen !== name; });
    app.current = name;
    if (changed) screens[name].enter?.();
    renderHints();
    (focus ?? focusFirst)();
    if (name !== 'home') { setAccent(ACCENT[name]); aimLight(null); }
  };
  if (document.startViewTransition && !reduceMotion && !instant && name !== app.current) document.startViewTransition(swap);
  else swap();
}

function back() {
  if (screens[app.current].back?.()) return;
  if (app.current === 'home') return;
  const from = app.current;
  show('home', { focus: () => home.focusTile(from) });
}

function startScan() {
  if (app.scanning) return;
  app.scanning = true;
  refreshAll();
  send('scan');
}

function refreshAll() {
  Object.values(screens).forEach(s => s.refresh?.());
}

function saveSettings(next) {
  send('saveSettings', { settings: next });
  applySettings(next);
}

function applySettings(next) {
  const langChanged = next.language !== app.settings.language;
  app.settings = next;
  configure(next);
  if (!langChanged) { screens.settings.refresh(); return; }
  setLang(next.language);
  buildAll();
  show(app.current, { instant: true, focus: () => $('[data-key="language"]')?.focus({ preventScroll: true }) });
  app.report = null;
  startScan(); // diagnostic texts come from the host, in the new language
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
});
on('settings', s => { if (JSON.stringify(s) !== JSON.stringify(app.settings)) applySettings(s); });
on('scan', r => { app.scanning = false; app.report = r; refreshAll(); });
on('gameMode', s => {
  app.game = s;
  $('#game-chip').hidden = !s.active;
  $('#game-chip').textContent = t('chip.game');
  refreshAll();
});
on('launch', r => toast(r.ok ? t('launch.ok') : t('launch.fail')));
on('notice', n => toast(n.message));
on('error', e => { sfx.error(); toast(t('error', { msg: e.message })); });
on('preview', () => toast(t('preview')));
on('resumed', () => send('gameModeStatus'));

// ---- Boot: the boost meter fills, the arena opens, the tiles arrive ----

function boot() {
  $$('.screen').forEach(s => { s.hidden = s.dataset.screen !== 'home'; });
  app.current = 'home';
  initNav({ back, current: () => app.current, hints: () => screens[app.current].hints() });
  sfx.boot();

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
  if (e.key === 'F11') { e.preventDefault(); send('toggleFullscreen'); }
});
$('.power').innerHTML = icon('power');
$('.power').addEventListener('click', () => send('exit'));
setInterval(() => { $('#clock').textContent = new Date().toLocaleTimeString(app.settings.language === 'en' ? 'en-GB' : 'he-IL', { hour: '2-digit', minute: '2-digit' }); }, 10000);
$('#clock').textContent = new Date().toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit' });

send('hello');

// Moving around like a console: D-pad / stick / arrows move the focus spatially, ✕ / A / Enter selects,
// ○ / B / Esc goes back. A screen can "capture" the controller (the controller test) and get raw input instead.

import { $, $$, reduceMotion } from './ui.js';
import { sfx, rememberPad } from './sound.js';
import { t } from './i18n.js';

const nav = {
  kind: 'keys',            // 'keys' | 'ps' | 'xbox'
  capture: null,           // fn(gamepad, now) while a screen wants raw controller input
  back: () => {},
  current: () => 'home',
  hints: () => [],
};

export function initNav({ back, current, hints }) {
  Object.assign(nav, { back, current, hints });
  if ([...(navigator.getGamepads?.() ?? [])].some(Boolean)) startPolling();
  renderHints();
}

// Poll at 60 Hz with a timer, and only while a controller is connected. A requestAnimationFrame loop would ask the
// compositor for a new frame on every refresh (155 times a second here) even when nothing on screen changes.
let pollTimer = 0;
function startPolling() {
  if (!pollTimer) pollTimer = setInterval(() => pollPad(performance.now()), 16);
}
function stopPolling() {
  clearInterval(pollTimer);
  pollTimer = 0;
}

export const setCapture = fn => { nav.capture = fn; };
export const inputKind = () => nav.kind;

export function navItems() {
  const screen = $(`.screen[data-screen="${nav.current()}"]`);
  const inScreen = screen ? $$('[data-nav]', screen) : [];
  return [...inScreen, ...$$('.topbar [data-nav]')].filter(el => !el.disabled && el.offsetParent !== null);
}

export function focusFirst() {
  const items = navItems();
  (items.find(el => el.hasAttribute('data-default')) ?? items[0])?.focus({ preventScroll: true });
}

export function focusEl(el) {
  if (!el) return;
  el.focus({ preventScroll: true });
  el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: reduceMotion ? 'auto' : 'smooth' });
}

export function moveFocus(dir) {
  const items = navItems();
  if (!items.length) return;
  const cur = items.includes(document.activeElement) ? document.activeElement : null;
  if (!cur) { focusFirst(); return; }

  const a = cur.getBoundingClientRect();
  const ax = a.left + a.width / 2, ay = a.top + a.height / 2;
  let best = null, bestScore = Infinity;
  for (const el of items) {
    if (el === cur) continue;
    const b = el.getBoundingClientRect();
    const dx = b.left + b.width / 2 - ax, dy = b.top + b.height / 2 - ay;
    let along, across;
    if (dir === 'left') { if (dx > -4) continue; along = -dx; across = Math.abs(dy); }
    else if (dir === 'right') { if (dx < 4) continue; along = dx; across = Math.abs(dy); }
    else if (dir === 'up') { if (dy > -4) continue; along = -dy; across = Math.abs(dx); }
    else { if (dy < 4) continue; along = dy; across = Math.abs(dx); }
    const score = along + across * 3;
    if (score < bestScore) { bestScore = score; best = el; }
  }
  if (best) { focusEl(best); sfx.move(); }
}

function goBack() {
  sfx.back();
  nav.back();
}

// Every press of a navigable control gets the selection sound, unless it asks for its own (data-sfx).
document.addEventListener('click', e => {
  const el = e.target.closest('[data-nav]');
  if (!el || el.disabled) return;
  const kind = el.dataset.sfx ?? 'select';
  if (kind !== 'none') sfx[kind]?.();
});

document.addEventListener('keydown', e => {
  const dirs = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down' };
  if (dirs[e.key]) { e.preventDefault(); setKind('keys'); moveFocus(dirs[e.key]); }
  else if (e.key === 'Escape' || e.key === 'Backspace') { e.preventDefault(); setKind('keys'); goBack(); }
  else if (e.key === 'Enter' && document.activeElement?.matches('[data-nav]:not(button)')) { e.preventDefault(); document.activeElement.click(); }
});

// Hovering moves the selection, like a console cursor. Ignore the fake mouseover Chrome fires while a list scrolls.
let lastRealMouse = 0;
document.addEventListener('mousemove', e => {
  if (e.movementX || e.movementY) {
    lastRealMouse = performance.now();
    document.body.classList.remove('pad-mode');
  }
});
document.addEventListener('mouseover', e => {
  if (performance.now() - lastRealMouse > 120) return;
  const el = e.target.closest('[data-nav]');
  if (el && el !== document.activeElement && !el.disabled && !el.closest('[data-no-hover]')) el.focus({ preventScroll: true });
});

// ---- Controller ----

const pad = { prev: [], held: null, repeatAt: 0 };

function kindOf(gp) {
  const id = gp.id.toLowerCase();
  return id.includes('054c') || id.includes('dualsense') || id.includes('wireless controller') || id.includes('dualshock') ? 'ps' : 'xbox';
}

function pollPad(now) {
  // Only while BoostOS is visible and focused, so presses inside the game never reach us.
  if (document.hidden || !document.hasFocus()) return;
  const gp = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
  if (!gp) return;
  rememberPad(gp);

  const down = i => !!gp.buttons[i]?.pressed;
  const pressed = i => down(i) && !pad.prev[i];
  const anyPress = gp.buttons.some((b, i) => b.pressed && !pad.prev[i]);
  if (anyPress) { setKind(kindOf(gp)); document.body.classList.add('pad-mode'); }

  if (nav.capture) {
    nav.capture(gp, now);
  } else {
    if (pressed(0)) document.activeElement?.click?.();
    if (pressed(1)) goBack();
    let dir = down(12) ? 'up' : down(13) ? 'down' : down(14) ? 'left' : down(15) ? 'right' : null;
    const x = gp.axes[0] ?? 0, y = gp.axes[1] ?? 0;
    if (!dir && Math.hypot(x, y) > 0.55) dir = Math.abs(x) > Math.abs(y) ? (x > 0 ? 'right' : 'left') : (y > 0 ? 'down' : 'up');
    if (dir && dir !== pad.held) { moveFocus(dir); pad.repeatAt = now + 380; }
    else if (dir && now >= pad.repeatAt) { moveFocus(dir); pad.repeatAt = now + 115; }
    pad.held = dir;
  }
  pad.prev = gp.buttons.map(b => b.pressed);
}

export const padSnapshot = () => [...(navigator.getGamepads?.() ?? [])].find(Boolean) ?? null;
export const padKind = gp => kindOf(gp);

window.addEventListener('gamepadconnected', e => {
  const kind = kindOf(e.gamepad);
  $('#pad-status').textContent = kind === 'ps' ? t('pad.connected') : t('pad.connectedGeneric');
  setKind(kind);
  startPolling();
});
window.addEventListener('gamepaddisconnected', () => {
  if ([...(navigator.getGamepads?.() ?? [])].some(Boolean)) return;
  $('#pad-status').textContent = '';
  setKind('keys');
  stopPolling();
});

function setKind(kind) {
  if (nav.kind === kind) return;
  nav.kind = kind;
  renderHints();
}

const GLYPHS = {
  select: { ps: '<span class="glyph ps ps-cross"><svg viewBox="0 0 24 24" class="ic"><path d="m6 6 12 12M18 6 6 18"/></svg></span>', xbox: '<span class="glyph xb-a">A</span>', keys: '<span class="glyph key">Enter</span>' },
  back: { ps: '<span class="glyph ps ps-circle"><svg viewBox="0 0 24 24" class="ic"><circle cx="12" cy="12" r="7"/></svg></span>', xbox: '<span class="glyph xb-b">B</span>', keys: '<span class="glyph key">Esc</span>' },
  full: { ps: '<span class="glyph key">F11</span>', xbox: '<span class="glyph key">F11</span>', keys: '<span class="glyph key">F11</span>' },
};

/** Hints for the current screen: [['select', label], ['back', label], ...] */
export function renderHints() {
  const list = nav.hints();
  $('#hints').innerHTML = list.map(([g, label]) => `<span>${GLYPHS[g][nav.kind]} ${label}</span>`).join('');
}

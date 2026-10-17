// Keyboard test: a drawn keyboard. A key glows while held and stays marked once it has worked, so a dead key
// stands out. It also counts how many keys register at once (rollover). Hold Esc to leave.

import { $, $$, icon } from '../ui.js';
import { t } from '../i18n.js';
import { setKeyCapture } from '../nav.js';
import { sfx } from '../sound.js';

let ctx;
let root;
const held = new Set();
const seen = new Set();
let maxHeld = 0;
let escSince = 0;
let escTimer = 0;

// [code, label, width in key units]; a null code is a gap.
const F = n => [`F${n}`, `F${n}`, 1];
const K = c => [`Key${c}`, c, 1];
const D = d => [`Digit${d}`, d, 1];
const MAIN = [
  [['Escape', 'Esc', 1], [null, '', 1], F(1), F(2), F(3), F(4), [null, '', 0.5], F(5), F(6), F(7), F(8), [null, '', 0.5], F(9), F(10), F(11), F(12)],
  [['Backquote', '`', 1], ...'1234567890'.split('').map(D), ['Minus', '-', 1], ['Equal', '=', 1], ['Backspace', '⌫', 2]],
  [['Tab', 'Tab', 1.5], ...'QWERTYUIOP'.split('').map(K), ['BracketLeft', '[', 1], ['BracketRight', ']', 1], ['Backslash', '\\', 1.5]],
  [['CapsLock', 'Caps', 1.75], ...'ASDFGHJKL'.split('').map(K), ['Semicolon', ';', 1], ['Quote', "'", 1], ['Enter', 'Enter', 2.25]],
  [['ShiftLeft', 'Shift', 2.25], ...'ZXCVBNM'.split('').map(K), ['Comma', ',', 1], ['Period', '.', 1], ['Slash', '/', 1], ['ShiftRight', 'Shift', 2.75]],
  [['ControlLeft', 'Ctrl', 1.25], ['MetaLeft', 'Win', 1.25], ['AltLeft', 'Alt', 1.25], ['Space', '', 6.25], ['AltRight', 'Alt', 1.25], ['ContextMenu', '☰', 1.25], ['ControlRight', 'Ctrl', 2.5]],
];
const SIDE = [
  [['PrintScreen', 'PrtSc', 1], ['ScrollLock', 'ScrLk', 1], ['Pause', 'Pause', 1]],
  [['Insert', 'Ins', 1], ['Home', 'Home', 1], ['PageUp', 'PgUp', 1]],
  [['Delete', 'Del', 1], ['End', 'End', 1], ['PageDown', 'PgDn', 1]],
  [[null, '', 3]],
  [[null, '', 1], ['ArrowUp', '↑', 1], [null, '', 1]],
  [['ArrowLeft', '←', 1], ['ArrowDown', '↓', 1], ['ArrowRight', '→', 1]],
];
const ALL = [...MAIN, ...SIDE].flat().filter(k => k[0]).map(k => k[0]);

const rowHtml = row => `<div class="krow">${row.map(([code, label, w]) => code
  ? `<span class="k" data-code="${code}" style="--w:${w}">${label}</span>`
  : `<span class="k gap" style="--w:${w}"></span>`).join('')}</div>`;

const screen = {
  init(c) {
    ctx = c;
    addEventListener('blur', () => { held.clear(); $$('.k.on', root ?? document).forEach(k => k.classList.remove('on')); });
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('keys.title')}</h2>
        <span class="head-meta">${t('keys.meta')}</span>
      </div>
      <div class="keys-body">
        <div class="card kb-card"><div class="kb" dir="ltr">
          <div class="kb-main">${MAIN.map(rowHtml).join('')}</div>
          <div class="kb-side">${SIDE.map(rowHtml).join('')}</div>
        </div></div>
        <div class="keys-stats">
          <div class="card readout"><b id="k-last">—</b><span>${t('keys.last')}</span></div>
          <div class="card readout"><b id="k-held">0</b><span>${t('keys.held')}</span></div>
          <div class="card readout"><b id="k-seen">0</b><span>${t('keys.seen', { n: ALL.length })}</span></div>
          <p class="note">${t('keys.note')}</p>
          <button class="btn" data-nav id="k-reset">${t('keys.reset')}</button>
        </div>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    $('#k-reset', root).addEventListener('click', reset);
    paint();
  },

  enter() {
    held.clear();
    setKeyCapture(onKey);
    paint();
  },

  leave() {
    setKeyCapture(null);
    clearTimeout(escTimer);
    escSince = 0;
  },

  hints: () => [['holdBack', t('keys.hold')]],
};

export default screen;

function onKey(e) {
  // Everything is a key under test here: no browser shortcuts, no navigation.
  e.preventDefault();
  const code = e.code || e.key;
  if (e.type === 'keydown') {
    if (code === 'Escape' && !e.repeat) {
      escSince = performance.now();
      clearTimeout(escTimer);
      escTimer = setTimeout(() => { if (escSince) { escSince = 0; sfx.back(); ctx.back(); } }, 800);
    }
    if (!held.has(code)) {
      held.add(code);
      maxHeld = Math.max(maxHeld, held.size);
      if (!seen.has(code)) { seen.add(code); sfx.move(); }
    }
    $('#k-last', root).textContent = keyName(e);
  } else {
    if (code === 'Escape') { escSince = 0; clearTimeout(escTimer); }
    held.delete(code);
    seen.add(code); // Print Screen only reports its release
  }
  paint();
}

function keyName(e) {
  if (e.key === ' ') return 'Space';
  return e.key.length === 1 ? e.key.toUpperCase() : e.key;
}

function paint() {
  if (!root) return;
  $$('.k[data-code]', root).forEach(k => {
    k.classList.toggle('on', held.has(k.dataset.code));
    k.classList.toggle('seen', seen.has(k.dataset.code));
  });
  $('#k-held', root).textContent = `${held.size} · ${t('keys.max', { n: maxHeld })}`;
  $('#k-seen', root).textContent = String(ALL.filter(c => seen.has(c)).length);
}

function reset() {
  seen.clear();
  held.clear();
  maxHeld = 0;
  $('#k-last', root).textContent = '—';
  paint();
}

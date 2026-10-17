// Controller test: live sticks, triggers and buttons, and a drift test that recommends a Rocket League deadzone.
// While this screen is open the controller is "captured": its input feeds the test instead of the menus.
// Hold ○ / B (or press Esc) to leave.

import { $, esc, icon } from '../ui.js';
import { t } from '../i18n.js';
import { setCapture, padKind, inputKind } from '../nav.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let raf = 0;
let holdBackSince = 0;
let drift = null;        // { until, left: 0, right: 0 } while measuring
let result = null;       // { left, right, dz }
let prevButtons = [];

const PS = ['✕', '○', '□', '△', 'L1', 'R1', 'L2', 'R2', 'Create', 'Options', 'L3', 'R3', '↑', '↓', '←', '→', 'PS', 'Pad'];
const XB = ['A', 'B', 'X', 'Y', 'LB', 'RB', 'LT', 'RT', 'View', 'Menu', 'LS', 'RS', '↑', '↓', '←', '→', 'Xbox', ''];

const screen = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    const stick = (id, label) => `
      <div class="stick" id="${id}">
        <svg viewBox="-110 -110 220 220" aria-hidden="true">
          <circle class="ring" r="100"/>
          <line class="cross" x1="-100" y1="0" x2="100" y2="0"/><line class="cross" x1="0" y1="-100" x2="0" y2="100"/>
          <circle class="dz" r="0"/>
          <circle class="pos" r="9" cx="0" cy="0"/>
        </svg>
        <span>${label}</span>
      </div>`;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('pad.title')}</h2>
        <span class="head-meta">${t('pad.meta')}</span>
      </div>
      <div class="pad-body">
        <div class="pad-live">
          <div class="sticks">${stick('stick-l', t('pad.left'))}${stick('stick-r', t('pad.right'))}</div>
          <div class="triggers">
            <div class="trigger"><span id="tl-name">L2</span><div class="bar"><div id="tl"></div></div><output id="tl-v">0%</output></div>
            <div class="trigger"><span id="tr-name">R2</span><div class="bar"><div id="tr"></div></div><output id="tr-v">0%</output></div>
          </div>
          <div class="buttons" id="pad-buttons"></div>
        </div>
        <aside class="pad-side panel">
          <h3>${t('pad.driftTitle')}</h3>
          <p id="pad-help">${t('pad.driftText')}</p>
          <div id="drift-out" class="drift-result"></div>
          <button class="btn primary" data-nav data-default id="drift-btn">${t('pad.measure')}</button>
          <p class="note">${t('pad.ring')}</p>
        </aside>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    $('#drift-btn', root).addEventListener('click', () => {
      drift = { until: performance.now() + 3000, left: 0, right: 0 };
      result = null;
      renderResult();
    });
    renderResult();
  },

  enter() {
    holdBackSince = 0;
    setCapture(capture);
    cancelAnimationFrame(raf);
    const loop = () => {
      raf = requestAnimationFrame(loop);
      if (document.hidden) return;
      const gp = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
      draw(gp);
    };
    loop();
  },

  leave() {
    setCapture(null);
    cancelAnimationFrame(raf);
    drift = null;
  },

  hints: () => [['back', inputKind() === 'keys' ? t('hint.back') : t('pad.hold')]],
};

export default screen;

/** Raw controller input while on this screen. Only a long ○ / B press leaves; everything else is the test. */
function capture(gp, now) {
  const backDown = !!gp.buttons[1]?.pressed;
  if (backDown && !holdBackSince) holdBackSince = now;
  if (!backDown) holdBackSince = 0;
  if (holdBackSince && now - holdBackSince > 700) { holdBackSince = 0; sfx.back(); ctx.back(); return; }
  // ✕ / A starts the drift test when nothing is running, so the whole screen works from the couch.
  if (gp.buttons[0]?.pressed && !prevButtons[0] && !drift) $('#drift-btn', root)?.click();
  prevButtons = gp.buttons.map(b => b.pressed);
}

function draw(gp) {
  if (!root) return;
  const help = $('#pad-help', root);
  if (!gp) {
    help.textContent = t('pad.none');
    return;
  }
  const names = padKind(gp) === 'ps' ? PS : XB;
  $('#tl-name', root).textContent = names[6];
  $('#tr-name', root).textContent = names[7];

  const [lx = 0, ly = 0, rx = 0, ry = 0] = gp.axes;
  stickAt('#stick-l', lx, ly);
  stickAt('#stick-r', rx, ry);

  const tl = gp.buttons[6]?.value ?? 0, tr = gp.buttons[7]?.value ?? 0;
  $('#tl', root).style.width = `${tl * 100}%`;
  $('#tr', root).style.width = `${tr * 100}%`;
  $('#tl-v', root).textContent = `${Math.round(tl * 100)}%`;
  $('#tr-v', root).textContent = `${Math.round(tr * 100)}%`;

  const box = $('#pad-buttons', root);
  if (box.childElementCount !== gp.buttons.length) {
    box.innerHTML = gp.buttons.map((_, i) => names[i] ? `<span data-b="${i}">${esc(names[i])}</span>` : '').join('');
  }
  gp.buttons.forEach((b, i) => box.querySelector(`[data-b="${i}"]`)?.classList.toggle('on', b.pressed));

  if (drift) {
    drift.left = Math.max(drift.left, Math.hypot(lx, ly));
    drift.right = Math.max(drift.right, Math.hypot(rx, ry));
    const left = Math.ceil((drift.until - performance.now()) / 1000);
    if (left <= 0) {
      const worst = Math.max(drift.left, drift.right);
      // Deadzone just above the measured drift, with a small margin, in Rocket League's 0.00–1.00 scale.
      result = { left: drift.left, right: drift.right, worst, dz: Math.max(0.02, Math.ceil((worst + 0.02) * 100) / 100) };
      drift = null;
      sfx.on();
      renderResult();
    } else {
      $('#drift-out', root).innerHTML = `<span class="countdown">${left}</span><span>${t('pad.hands')}</span>`;
    }
  }
}

function stickAt(sel, x, y) {
  const el = $(sel, root);
  const pos = el.querySelector('.pos');
  pos.setAttribute('cx', (x * 100).toFixed(1));
  pos.setAttribute('cy', (y * 100).toFixed(1));
  const dz = result?.dz ?? 0;
  el.querySelector('.dz').setAttribute('r', String(dz * 100));
  pos.classList.toggle('out', Math.hypot(x, y) > (dz || 0.05));
}

function renderResult() {
  const out = root && $('#drift-out', root);
  if (!out) return;
  if (!result) { out.innerHTML = ''; return; }
  const f = v => v.toFixed(2);
  out.innerHTML = `
    <span>${t('pad.result')}</span>
    <b><bdi>${f(result.left)} · ${f(result.right)}</bdi></b>
    <span>${t('pad.leftShort')} · ${t('pad.rightShort')}</span>
    <p>${result.worst > 0.15 ? t('pad.recommendBad', { d: f(result.worst), dz: f(result.dz) }) : t('pad.recommend', { dz: f(result.dz) })}</p>`;
}

// Controller test: a drawn controller that lights up live (sticks, triggers, every button), and a drift test that
// recommends a Rocket League deadzone. While this screen is open the controller is "captured": its input feeds the
// test instead of the menus. Hold ○ / B (or press Esc) to leave.
// The drawing is an SVG with its own left and right, so the left stick stays on the left in Hebrew too.

import { $, $$, esc, icon } from '../ui.js';
import { t } from '../i18n.js';
import { setCapture, padKind, inputKind } from '../nav.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let holdBackSince = 0;
let drift = null;        // { until, left: 0, right: 0 } while measuring
let result = null;       // { left, right, dz }
let prevButtons = [];
let activePadIndex = null;
let drawnKind = null;

const LABELS = {
  ps: { 4: 'L1', 5: 'R1', 6: 'L2', 7: 'R2' },
  xbox: { 4: 'LB', 5: 'RB', 6: 'LT', 7: 'RT' },
};

// Face buttons: index → [cx, cy, PS symbol, Xbox letter, Xbox colour]
const FACE = { 3: [595, 152, 'triangle', 'Y', '#E8B83A'], 1: [643, 200, 'circle', 'B', '#D9433C'], 0: [595, 248, 'cross', 'A', '#3EA84B'], 2: [547, 200, 'square', 'X', '#3B7BD9'] };
const PS_COLORS = { triangle: '#4FD1B5', circle: '#FF6B7A', cross: '#8FB4FF', square: '#E58BD8' };
const SYMBOL = {
  triangle: '<path d="M0,-10 9,6 -9,6Z"/>',
  circle: '<circle r="8"/>',
  cross: '<path d="M-8,-8 8,8M8,-8 -8,8"/>',
  square: '<rect x="-7.5" y="-7.5" width="15" height="15" rx="1.5"/>',
};

function controllerSvg(kind) {
  const L = LABELS[kind];
  const face = Object.entries(FACE).map(([i, [cx, cy, ps, xb, color]]) => `
    <g class="btn-face" data-b="${i}" transform="translate(${cx} ${cy})">
      <circle class="cap" r="23"/>
      ${kind === 'ps'
        ? `<g class="sym" style="--c:${PS_COLORS[ps]}">${SYMBOL[ps]}</g>`
        : `<text class="sym letter" style="--c:${color}" text-anchor="middle" dominant-baseline="central">${xb}</text>`}
    </g>`).join('');
  return `<svg class="ctl" viewBox="0 0 800 470" role="img" aria-label="${esc(t('pad.title'))}">
    <defs>
      <clipPath id="clip-l2"><rect x="158" y="2" width="104" height="36" rx="12"/></clipPath>
      <clipPath id="clip-r2"><rect x="538" y="2" width="104" height="36" rx="12"/></clipPath>
    </defs>
    <!-- triggers fill from the bottom as they're pressed -->
    <g class="trig" data-b="6"><rect class="cap" x="158" y="2" width="104" height="36" rx="12"/><rect class="fill" id="fill-6" clip-path="url(#clip-l2)" x="158" y="38" width="104" height="0"/><text x="210" y="21" text-anchor="middle" dominant-baseline="central">${L[6]}</text></g>
    <g class="trig" data-b="7"><rect class="cap" x="538" y="2" width="104" height="36" rx="12"/><rect class="fill" id="fill-7" clip-path="url(#clip-r2)" x="538" y="38" width="104" height="0"/><text x="590" y="21" text-anchor="middle" dominant-baseline="central">${L[7]}</text></g>
    <g class="btn-pill" data-b="4"><rect class="cap" x="152" y="46" width="116" height="20" rx="10"/><text x="210" y="56.5" text-anchor="middle" dominant-baseline="central">${L[4]}</text></g>
    <g class="btn-pill" data-b="5"><rect class="cap" x="532" y="46" width="116" height="20" rx="10"/><text x="590" y="56.5" text-anchor="middle" dominant-baseline="central">${L[5]}</text></g>

    <path class="body" d="M168 92C230 66 570 66 632 92C712 116 752 186 770 290C788 392 760 446 704 450C656 454 632 418 606 372C588 340 560 330 520 330L280 330C240 330 212 340 194 372C168 418 144 454 96 450C40 446 12 392 30 290C48 186 88 116 168 92Z"/>

    <g class="dpad">
      <rect class="cap" data-b="12" x="189" y="152" width="32" height="38" rx="7"/>
      <rect class="cap" data-b="13" x="189" y="210" width="32" height="38" rx="7"/>
      <rect class="cap" data-b="14" x="151" y="184" width="38" height="32" rx="7"/>
      <rect class="cap" data-b="15" x="221" y="184" width="38" height="32" rx="7"/>
    </g>
    ${face}
    <rect class="cap pad-touch" data-b="17" x="320" y="94" width="160" height="98" rx="18"/>
    <rect class="cap" data-b="8" x="280" y="96" width="18" height="34" rx="9"/>
    <rect class="cap" data-b="9" x="502" y="96" width="18" height="34" rx="9"/>
    <circle class="cap" data-b="16" cx="400" cy="232" r="15"/>

    <g class="stick" id="stick-l" data-b="10" transform="translate(296 288)">
      <circle class="well" r="46"/><circle class="dz" r="0"/><circle class="thumb" r="16"/>
    </g>
    <g class="stick" id="stick-r" data-b="11" transform="translate(504 288)">
      <circle class="well" r="46"/><circle class="dz" r="0"/><circle class="thumb" r="16"/>
    </g>
  </svg>`;
}

const screen = {
  init(c) {
    ctx = c;
    window.addEventListener('gamepaddisconnected', e => {
      if (ctx.app.current !== 'pad' || e.gamepad.index !== activePadIndex) return;
      drift = null;
      result = null;
      activePadIndex = null;
      holdBackSince = 0;
      draw(null);
      renderResult();
    });
  },

  build(el) {
    root = el;
    drawnKind = null;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('pad.title')}</h2>
        <span class="head-meta" id="pad-name">${t('pad.meta')}</span>
      </div>
      <div class="pad-body">
        <div class="card pad-stage">
          <div id="pad-svg"></div>
          <p class="pad-none" id="pad-none">${t('pad.none')}</p>
          <div class="pad-readout" dir="ltr" id="pad-readout"></div>
          <div class="pad-extra" id="pad-extra" dir="ltr"></div>
        </div>
        <aside class="card pad-side">
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
    const gp = [...(navigator.getGamepads?.() ?? [])].find(Boolean);
    if (activePadIndex !== (gp?.index ?? null)) result = null;
    activePadIndex = gp?.index ?? null;
    prevButtons = gp?.buttons.map(b => b.pressed) ?? [];
    draw(gp);
    // Navigation already polls at 60Hz while focused; share it instead of rendering a second loop.
    setCapture(capture);
  },

  leave() {
    setCapture(null);
    drift = null;
  },

  hints: () => [[inputKind() === 'keys' ? 'back' : 'holdBack', inputKind() === 'keys' ? t('hint.back') : t('pad.hold')]],
};

export default screen;

/** Raw controller input while on this screen. Only a long ○ / B press leaves; everything else is the test. */
function capture(gp, now) {
  if (gp.index !== activePadIndex) {
    activePadIndex = gp.index;
    drift = null;
    result = null;
    prevButtons = gp.buttons.map(b => b.pressed);
    renderResult();
  }
  draw(gp);
  const backDown = !!gp.buttons[1]?.pressed;
  if (backDown && !holdBackSince) holdBackSince = now;
  if (!backDown) holdBackSince = 0;
  root.style.setProperty('--hold', holdBackSince ? String(Math.min(1, (now - holdBackSince) / 700)) : '0');
  if (holdBackSince && now - holdBackSince > 700) { holdBackSince = 0; sfx.back(); ctx.back(); return; }
  // ✕ / A starts the drift test when nothing is running, so the whole screen works from the couch.
  if (gp.buttons[0]?.pressed && !prevButtons[0] && !drift) $('#drift-btn', root)?.click();
  prevButtons = gp.buttons.map(b => b.pressed);
}

function draw(gp) {
  if (!root) return;
  const help = $('#pad-help', root);
  $('#pad-none', root).hidden = !!gp;
  if (!gp) {
    help.textContent = t('pad.none');
    $('#pad-svg', root).innerHTML = '';
    drawnKind = null;
    return;
  }
  help.textContent = t('pad.driftText');
  const kind = padKind(gp);
  if (drawnKind !== kind) {
    $('#pad-svg', root).innerHTML = controllerSvg(kind);
    drawnKind = kind;
    $('#pad-name', root).textContent = gp.id.replace(/\s*\(.*$/, '');
  }
  const svg = $('#pad-svg svg', root);

  gp.buttons.forEach((b, i) => {
    for (const el of svg.querySelectorAll(`[data-b="${i}"]`)) el.classList.toggle('on', b.pressed);
  });
  for (const i of [6, 7]) {
    const v = gp.buttons[i]?.value ?? 0;
    const fill = svg.querySelector(`#fill-${i}`);
    fill.setAttribute('y', String(38 - 36 * v));
    fill.setAttribute('height', String(36 * v));
  }

  const [lx = 0, ly = 0, rx = 0, ry = 0] = gp.axes;
  stickAt(svg.querySelector('#stick-l'), lx, ly);
  stickAt(svg.querySelector('#stick-r'), rx, ry);
  const f = v => (v >= 0 ? ' ' : '') + v.toFixed(2);
  $('#pad-readout', root).textContent = `L ${f(lx)} ${f(ly)}   ·   R ${f(rx)} ${f(ry)}   ·   ${LABELS[kind][6]} ${Math.round((gp.buttons[6]?.value ?? 0) * 100)}%   ${LABELS[kind][7]} ${Math.round((gp.buttons[7]?.value ?? 0) * 100)}%`;

  // Back paddles and other extra buttons (DualSense Edge, Elite) have no standard place: list them.
  const extra = $('#pad-extra', root);
  const layout = `${gp.buttons.length}`;
  if (extra.dataset.layout !== layout) {
    extra.innerHTML = gp.buttons.length > 18 ? `<span>${t('pad.extra')}</span>${gp.buttons.slice(18).map((_, j) => `<span class="chip-btn" data-x="${18 + j}">${18 + j}</span>`).join('')}` : '';
    extra.dataset.layout = layout;
  }
  $$('[data-x]', extra).forEach(el => el.classList.toggle('on', !!gp.buttons[Number(el.dataset.x)]?.pressed));

  if (drift) {
    drift.left = Math.max(drift.left, Math.hypot(lx, ly));
    drift.right = Math.max(drift.right, Math.hypot(rx, ry));
    const left = Math.ceil((drift.until - performance.now()) / 1000);
    if (left <= 0) {
      const worst = Math.max(drift.left, drift.right);
      // Deadzone just above the measured drift, with a small margin, in Rocket League's 0.00–1.00 scale.
      result = { left: drift.left, right: drift.right, worst, dz: Math.min(1, Math.max(0.02, Math.ceil((worst + 0.02) * 100) / 100)) };
      drift = null;
      sfx.on();
      renderResult();
    } else {
      $('#drift-out', root).innerHTML = `<span class="countdown">${left}</span><span>${t('pad.hands')}</span>`;
    }
  }
}

function stickAt(g, x, y) {
  const thumb = g.querySelector('.thumb');
  thumb.setAttribute('cx', (x * 30).toFixed(1));
  thumb.setAttribute('cy', (y * 30).toFixed(1));
  const dz = result?.dz ?? 0;
  g.querySelector('.dz').setAttribute('r', String(dz * 46));
  thumb.classList.toggle('out', Math.hypot(x, y) > (dz || 0.05));
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

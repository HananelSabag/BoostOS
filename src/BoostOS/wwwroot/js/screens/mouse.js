// Mouse test: an estimate of the polling rate from raw pointer updates, and every button and the wheel, live.

import { $, $$, icon } from '../ui.js';
import { t } from '../i18n.js';

let ctx;
let root;
let count = 0;
let peak = 0;
let timer = 0;
let windowStart = 0;
let wheel = 0;

const BUTTONS = [[0, 'mouse.left'], [2, 'mouse.right'], [1, 'mouse.middle'], [3, 'mouse.back'], [4, 'mouse.forward']];

export default {
  init(c) {
    ctx = c;
    // Releases can happen outside the pad; listen once for the whole page (build runs again on a language change).
    addEventListener('mouseup', e => { if (ctx.app.current === 'mouse') $(`[data-mb="${e.button}"]`, root)?.classList.remove('on'); });
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav data-default aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('mouse.title')}</h2>
        <span class="head-meta">${t('mouse.meta')}</span>
      </div>
      <div class="mouse-body">
        <div class="mouse-pad" id="mouse-pad" data-no-hover><p>${t('mouse.pad')}</p>
          <div class="mouse-btns">${BUTTONS.map(([b, k]) => `<span class="chip-btn" data-mb="${b}">${t(k)}</span>`).join('')}<span class="chip-btn" id="wheel">${t('mouse.wheel')} <bdi id="wheel-n">0</bdi></span></div>
        </div>
        <div class="mouse-side">
          <div class="card readout"><b id="hz-now">0</b><span>${t('mouse.now')}</span></div>
          <div class="card readout"><b id="hz-peak">${peak}</b><span>${t('mouse.peak')}</span></div>
          <p id="mouse-verdict">${peak ? verdict(peak) : t('mouse.none')}</p>
          <button class="btn" data-nav id="mouse-reset">${t('mouse.reset')}</button>
        </div>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    const pad = $('#mouse-pad', root);
    const add = n => { count += n; pad.classList.add('active'); };
    if ('onpointerrawupdate' in window) pad.addEventListener('pointerrawupdate', e => { if (e.pointerType === 'mouse') add(e.getCoalescedEvents?.().length || 1); });
    else pad.addEventListener('pointermove', e => { if (e.pointerType === 'mouse') add(e.getCoalescedEvents?.().length || 1); });
    pad.addEventListener('pointerleave', () => pad.classList.remove('active'));
    const press = (e, on) => {
      $(`[data-mb="${e.button}"]`, root)?.classList.toggle('on', on);
      if (e.button >= 1) e.preventDefault(); // no autoscroll, no browser back
    };
    pad.addEventListener('mousedown', e => press(e, true));
    pad.addEventListener('mouseup', e => press(e, false));
    pad.addEventListener('contextmenu', e => e.preventDefault());
    pad.addEventListener('auxclick', e => e.preventDefault());
    pad.addEventListener('wheel', e => {
      e.preventDefault();
      wheel += e.deltaY > 0 ? -1 : 1;
      $('#wheel-n', root).textContent = String(wheel);
      const w = $('#wheel', root);
      w.classList.add('on');
      clearTimeout(w.timer);
      w.timer = setTimeout(() => w.classList.remove('on'), 140);
    }, { passive: false });
    $('#mouse-reset', root).addEventListener('click', () => {
      peak = 0;
      count = 0;
      wheel = 0;
      windowStart = performance.now();
      $('#hz-now', root).textContent = '0';
      $('#hz-peak', root).textContent = '0';
      $('#wheel-n', root).textContent = '0';
      $('#mouse-verdict', root).textContent = t('mouse.none');
    });
  },

  enter() {
    clearInterval(timer);
    count = 0;
    windowStart = performance.now();
    timer = setInterval(() => {
      const now = performance.now();
      const hz = Math.round(count * 1000 / Math.max(1, now - windowStart));
      count = 0;
      windowStart = now;
      $('#hz-now', root).textContent = hz;
      if (hz > peak) {
        peak = hz;
        $('#hz-peak', root).textContent = hz;
        $('#mouse-verdict', root).textContent = verdict(hz);
      }
    }, 250);
  },

  leave() { clearInterval(timer); $$('[data-mb]', root).forEach(b => b.classList.remove('on')); },
  hints: () => [['back', t('hint.back')]],
};

function verdict(hz) {
  if (hz < 60) return t('mouse.faster');
  if (hz <= 160) return t('mouse.125');
  if (hz <= 600) return t('mouse.500');
  if (hz <= 1300) return t('mouse.1000');
  return t('mouse.fast', { hz });
}

// Mouse polling rate, counted from raw pointer updates (pointerrawupdate), four windows a second.

import { $, icon } from '../ui.js';
import { t } from '../i18n.js';

let ctx;
let root;
let count = 0;
let peak = 0;
let timer = 0;

export default {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav data-default aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('mouse.title')}</h2>
        <span class="head-meta">${t('mouse.meta')}</span>
      </div>
      <div class="mouse-body">
        <div class="mouse-pad" id="mouse-pad" data-no-hover><p>${t('mouse.pad')}</p></div>
        <div class="mouse-side">
          <div class="readout"><b id="hz-now">0</b><span>${t('mouse.now')}</span></div>
          <div class="readout"><b id="hz-peak">${peak}</b><span>${t('mouse.peak')}</span></div>
          <p id="mouse-verdict">${peak ? verdict(peak) : t('mouse.none')}</p>
          <button class="btn" data-nav id="mouse-reset">${t('mouse.reset')}</button>
        </div>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    const pad = $('#mouse-pad', root);
    const add = n => { count += n; pad.classList.add('active'); };
    if ('onpointerrawupdate' in window) pad.addEventListener('pointerrawupdate', e => { if (e.pointerType === 'mouse') add(1); });
    else pad.addEventListener('pointermove', e => { if (e.pointerType === 'mouse') add(e.getCoalescedEvents?.().length || 1); });
    pad.addEventListener('pointerleave', () => pad.classList.remove('active'));
    $('#mouse-reset', root).addEventListener('click', () => {
      peak = 0;
      $('#hz-now', root).textContent = '0';
      $('#hz-peak', root).textContent = '0';
      $('#mouse-verdict', root).textContent = t('mouse.none');
    });
  },

  enter() {
    clearInterval(timer);
    count = 0;
    timer = setInterval(() => {
      const hz = count * 4;
      count = 0;
      $('#hz-now', root).textContent = hz;
      if (hz > peak) {
        peak = hz;
        $('#hz-peak', root).textContent = hz;
        $('#mouse-verdict', root).textContent = verdict(hz);
      }
    }, 250);
  },

  leave() { clearInterval(timer); },
  hints: () => [['back', t('hint.back')]],
};

function verdict(hz) {
  if (hz < 60) return t('mouse.faster');
  if (hz <= 160) return t('mouse.125');
  if (hz <= 600) return t('mouse.500');
  if (hz <= 1300) return t('mouse.1000');
  return t('mouse.fast', { hz });
}

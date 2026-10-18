// Rocket League video settings, read from the game's own settings file and saved back to it (game closed only).
// What the last session showed decides the suggestions: a card held back by heat gets lighter settings.

import { $, $$, esc, icon, toast } from '../ui.js';
import { send, on } from '../bridge.js';
import { t } from '../i18n.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let from = 'home';
let pending = {};
let saving = false;
let firstPaint = false;

const screen = {
  init(c) {
    ctx = c;
    on('rlVideo', state => { ctx.app.rlVideo = state; saving = false; pending = {}; screen.refresh(); });
    on('rlVideoSaved', ({ undo }) => { sfx.on(); toast(undo ? t('vid.undone') : t('vid.saved')); });
    on('error', e => { if (e.cmd === 'rlVideoSet' || e.cmd === 'rlVideoUndo') { saving = false; screen.refresh(); } });
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('vid.title')}</h2>
        <span class="head-meta" id="vid-meta"></span>
      </div>
      <div class="vid-body">
        <div class="vid-list" id="vid-list"></div>
        <aside class="vid-side">
          <div class="card vid-advice" id="vid-advice"></div>
          <button class="btn primary" data-nav id="vid-save" data-sfx="none"></button>
          <button class="btn" data-nav id="vid-suggest">${icon('star')}${t('vid.suggest')}</button>
          <button class="btn" data-nav id="vid-undo" data-sfx="none">${icon('undo')}${t('vid.undo')}</button>
          <p class="note">${t('vid.fps')}</p>
        </aside>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    root.onclick = e => {
      const b = e.target.closest('[data-opt]');
      if (!b || b.disabled) return;
      const [id, value] = [b.dataset.set, b.dataset.opt];
      const current = ctx.app.rlVideo?.settings.find(s => s.id === id)?.value;
      if (value === current) delete pending[id]; else pending[id] = value;
      sfx.select();
      paint();
    };
    $('#vid-suggest', root).addEventListener('click', () => {
      for (const s of ctx.app.rlVideo?.settings ?? []) if (s.recommended && s.recommended !== s.value) pending[s.id] = s.recommended;
      paint();
      toast(Object.keys(pending).length ? t('vid.suggested', { n: Object.keys(pending).length }) : t('vid.nothing'));
    });
    $('#vid-save', root).addEventListener('click', () => {
      if (saving || !Object.keys(pending).length) return;
      saving = true;
      sfx.select();
      send('rlVideoSet', { changes: pending });
      paint();
    });
    $('#vid-undo', root).addEventListener('click', () => {
      if (saving) return;
      saving = true;
      send('rlVideoUndo');
      paint();
    });
    this.refresh();
  },

  enter(opts = {}) {
    from = opts.from ?? from;
    pending = {};
    firstPaint = true;
    send('rlVideo');
    this.refresh();
  },

  returnTo: () => from,
  hints: () => [['select', t('hint.select')], ['back', t('hint.back')]],

  refresh() {
    if (!root) return;
    const s = ctx.app.rlVideo;
    const list = $('#vid-list', root);
    if (!s) { list.innerHTML = `<p class="empty">${t('vid.loading')}</p>`; paint(); return; }
    if (!s.found) { list.innerHTML = `<p class="empty">${t('vid.notFound')}</p>`; paint(); return; }
    const focusKey = document.activeElement?.dataset?.set ? `${document.activeElement.dataset.set}|${document.activeElement.dataset.opt}` : null;
    list.innerHTML = s.settings.map(x => `
      <div class="vid-row" data-row="${x.id}">
        <span class="vid-name"><b>${t(`vid.${x.id}`)}</b><small>${t(`vid.${x.id}.what`)}</small></span>
        <span class="seg">${x.options.map(o => `<button class="seg-btn" data-nav data-set="${x.id}" data-opt="${o}" data-sfx="none">${t(`vid.opt.${x.id === 'worldDetail' ? `detail${o}` : o}`)}${o === x.recommended ? `<span class="rec" title="${esc(t('vid.rec'))}">${icon('star')}</span>` : ''}</button>`).join('')}</span>
      </div>`).join('');
    if (focusKey) {
      const [id, opt] = focusKey.split('|');
      $(`[data-set="${id}"][data-opt="${opt}"]`, list)?.focus({ preventScroll: true });
    }
    paint();
    // The settings arrive after the screen opened: start on the first setting, not on the back button.
    if (firstPaint && (document.activeElement === $('.back', root) || document.activeElement === document.body)) {
      firstPaint = false;
      $('.seg-btn[aria-pressed="true"]', list)?.focus({ preventScroll: true });
    }
  },
};

export default screen;

/** Pressed state, pending marks and the buttons, without rebuilding the list (keeps the focus where it is). */
function paint() {
  const s = ctx.app.rlVideo;
  const running = !!s?.running;
  $('#vid-meta', root).innerHTML = s?.found ? `<bdi>${esc(s.resolution ?? '')}</bdi> · ${running ? t('vid.running') : t('vid.closed')}` : '';
  for (const x of s?.settings ?? []) {
    const shown = pending[x.id] ?? x.value;
    $$(`[data-set="${x.id}"]`, root).forEach(b => {
      b.setAttribute('aria-pressed', String(b.dataset.opt === shown));
      b.classList.toggle('changed', pending[x.id] === b.dataset.opt);
      b.disabled = running || saving;
    });
  }
  const n = Object.keys(pending).length;
  const save = $('#vid-save', root);
  save.disabled = running || saving || n === 0;
  save.innerHTML = saving ? `<span class="spinner"></span>${t('vid.saving')}` : `${icon('check')}${n ? t('vid.saveN', { n }) : t('vid.save')}`;
  $('#vid-undo', root).hidden = !s?.changed;
  $('#vid-undo', root).disabled = running || saving;
  $('#vid-suggest', root).disabled = running || saving;
  const last = ctx.app.matches?.find(m => !m.gameId || m.gameId === 'epic:Sugar' || m.gameId === 'steam:252950');
  $('#vid-advice', root).innerHTML = running
    ? `<h3>${t('vid.closeFirst')}</h3><p>${t('vid.closeFirstText')}</p>`
    : s?.hot && last
      ? `<h3 class="s-warn">${t('vid.hotTitle')}</h3><p>${t('vid.hotText', { temp: Math.round(last.gpuTempMax ?? 0), pct: Math.round(last.gpuThermalLimitedPct ?? 0) })}</p>`
      : `<h3>${t('vid.okTitle')}</h3><p>${t('vid.okText')}</p>`;
}

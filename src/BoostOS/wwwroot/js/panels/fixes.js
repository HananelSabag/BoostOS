// Fixes: everything BoostOS can change on this PC, in one list. What needs fixing, what BoostOS already changed
// (with Undo), what is already good and what doesn't apply here. Every change keeps the value it replaced.

import { $, esc, icon, valueHtml } from '../ui.js';
import { t } from '../i18n.js';
import { runFix, undoAll, fixBusy } from './fixrun.js';

let ctx;
let root;

// The order of the list inside each group: game first, then Windows, then the network.
const ORDER = ['nvidia-fps-cap', 'nvidia-global-power', 'nvidia-profile', 'rl-gpu-pref', 'rl-vsync', 'power-plan', 'power-overlay', 'windows-game-mode',
  'windowed-opt', 'game-dvr-history', 'wifi-power-ac', 'wifi-roaming'];

const panel = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="fx">
        <div class="fx-top">
          <p class="fx-intro">${t('fixes.intro')}</p>
          <div class="fx-actions">
            <button class="btn primary" data-nav id="fx-all" data-sfx="none" hidden></button>
            <button class="btn" data-nav id="fx-undo-all" data-sfx="none" hidden></button>
          </div>
        </div>
        <div class="fx-list" id="fx-list"></div>
        <button class="link-card" data-nav id="fx-video">
          ${icon('sliders')}<span><b>${t('fixes.video')}</b><small>${t('fixes.videoSub')}</small></span>
          <svg class="ic chev-side" viewBox="0 0 24 24" aria-hidden="true"><path d="m15 5-7 7 7 7"/></svg>
        </button>
      </div>`;
    $('#fx-all', root).addEventListener('click', e => runFix(rows().filter(r => r.state === 'needed').map(r => r.id), e.currentTarget));
    $('#fx-undo-all', root).addEventListener('click', e => undoAll(e.currentTarget));
    $('#fx-video', root).addEventListener('click', () => ctx.show('rlvideo', { from: 'diag' }));
    root.onclick = e => {
      const b = e.target.closest('[data-fix-id]');
      if (b) runFix([b.dataset.fixId], b, b.dataset.undo === '1');
    };
    this.refresh();
  },

  refresh() {
    if (!root) return;
    const list = rows();
    const needed = list.filter(r => r.state === 'needed');
    const applied = list.filter(r => r.applied);
    const all = $('#fx-all', root), undo = $('#fx-undo-all', root);
    all.hidden = needed.length === 0;
    undo.hidden = applied.length === 0;
    if (!fixBusy()) {
      all.disabled = undo.disabled = false;
      all.innerHTML = `${icon('wrench')}${t('fixes.fixAll', { n: needed.length })}`;
      undo.innerHTML = `${icon('undo')}${t('fixes.undoAll', { n: applied.length })}`;
    }
    $('#fx-video', root).hidden = !ctx.app.games.some(g => g.isRocketLeague);

    const focusId = document.activeElement?.closest?.('[data-row]')?.dataset.row;
    const groups = [['needed', t('fixes.gNeeded')], ['applied', t('fixes.gApplied')], ['good', t('fixes.gGood')], ['na', t('fixes.gNa')]];
    const report = ctx.app.report;
    $('#fx-list', root).innerHTML = !report && ctx.app.scanning ? `<p class="empty">${t('scan.loading')}</p>` : groups.map(([g, title]) => {
      const items = list.filter(r => (g === 'applied' ? r.applied && r.state !== 'needed' : r.state === g && !(r.applied && g !== 'needed')));
      if (!items.length) return '';
      return `<h3 class="fx-group">${title}<span>${items.length}</span></h3>${items.map(item).join('')}`;
    }).join('');
    if (focusId) $(`[data-row="${focusId}"]`, root)?.focus({ preventScroll: true });
  },

  hints: () => [],
};

export default panel;

/** One row per known fix, from the latest scan and the record of what BoostOS changed. */
function rows() {
  const checks = (ctx.app.report?.sections ?? []).flatMap(s => s.checks).filter(c => c.fix);
  const appliedIds = new Set((ctx.app.applied ?? []).map(r => r.id));
  return ORDER.map(id => {
    const check = checks.find(c => c.fix.id === id);
    const issue = check && (check.status === 'warn' || check.status === 'bad');
    return {
      id, check, admin: check?.fix.needsAdmin ?? ['nvidia-fps-cap', 'nvidia-global-power', 'nvidia-profile', 'wifi-roaming'].includes(id),
      applied: appliedIds.has(id),
      state: issue ? 'needed' : check ? 'good' : 'na',
    };
  });
}

function item(r) {
  const status = r.state === 'needed' ? (r.check.status === 'bad' ? 'bad' : 'warn') : r.applied ? 'boost' : r.state === 'good' ? 'ok' : 'idle';
  const chip = r.state === 'needed' ? t('fixes.needed') : r.applied ? t('fixes.applied') : r.state === 'good' ? t('fixes.good') : t('fixes.na');
  const actions = [
    r.state === 'needed' ? `<button class="fix-btn" data-nav data-fix-id="${r.id}" data-sfx="none">${icon(r.admin ? 'shield' : 'wrench')}${t('scan.fix')}</button>` : '',
    r.applied ? `<button class="fix-btn ghost" data-nav data-fix-id="${r.id}" data-undo="1" data-sfx="none">${icon('undo')}${t('fixes.undo')}</button>` : '',
  ].join('');
  return `<div class="fx-row s-${status}" data-nav data-row="${r.id}" tabindex="0">
    <span class="dot"></span>
    <span class="fx-name"><b>${esc(t(`fix.${r.id}`))}</b><small>${esc(t(`fix.${r.id}.what`))}</small></span>
    <span class="fx-value">${r.check ? valueHtml(r.check.value) : ''}</span>
    <span class="fx-chip">${chip}${r.admin && r.state === 'needed' ? ` · ${t('fixes.admin')}` : ''}</span>
    <span class="fx-btns">${actions}</span>
    ${r.check?.advice && r.state === 'needed' ? `<p class="c-advice">${esc(r.check.advice)}</p>` : ''}
  </div>`;
}

// Running fixes from any tab: one request at a time, a spinner on the button that asked, a rescan afterwards.

import { toast } from '../ui.js';
import { send, on } from '../bridge.js';
import { t } from '../i18n.js';
import { sfx } from '../sound.js';

let ctx;
let pending = null; // { ids, undo }

export function initFixes(c) {
  ctx = c;
  on('fixResult', ({ outcome, applied }) => {
    ctx.app.fixesApplied = applied ?? ctx.app.fixesApplied;
    send('fixesApplied');
    const mine = pending;
    pending = null;
    const done = outcome?.done ?? [], failed = outcome?.failed ?? [];
    if (failed.length) { sfx.error(); toast(t('fix.failed', { msg: failed[0].message })); }
    if (done.length) { sfx.on(); toast(mine?.undo ? t('fix.undoneToast', { n: done.length }) : t('fix.doneToast', { n: done.length })); }
    ctx.startScan();
  });
  on('fixesApplied', list => { ctx.app.applied = list ?? []; ctx.app.fixesApplied = ctx.app.applied.length; ctx.refreshAll(); });
  on('error', e => {
    if (!pending || !['fix', 'undoFix', 'undoAllFixes'].includes(e.cmd)) return;
    pending = null;
    ctx.refreshAll();
  });
}

export const fixBusy = () => !!pending;

/** Starts a batch. Admin fixes make Windows ask once for the whole batch. */
export function runFix(ids, button, undo = false) {
  if (pending || !ids.length) return;
  pending = { ids, undo };
  sfx.select();
  if (button) {
    button.classList.add('busy');
    button.disabled = true;
    button.innerHTML = `<span class="spinner"></span>${undo ? t('fix.undoing') : t('fix.fixing')}`;
  }
  send(undo ? 'undoFix' : 'fix', { ids });
}

export function undoAll(button) {
  if (pending || !ctx.app.fixesApplied) return;
  pending = { ids: [], undo: true };
  if (button) { button.disabled = true; button.innerHTML = `<span class="spinner"></span>${t('fix.undoing')}`; }
  send('undoAllFixes');
}

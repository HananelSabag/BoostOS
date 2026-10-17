// The top bar (Game Mode pill, clock, power) and the dialogs: a console never quits on a single press.

import { $, esc, icon, toast } from './ui.js';
import { send } from './bridge.js';
import { t } from './i18n.js';
import { sfx } from './sound.js';
import { focusFirst, focusEl, renderHints } from './nav.js';

let ctx;
let pending = false; // waiting for the host to switch Game Mode
let dialog = null;   // { el, onClose, returnFocus }

export function initChrome(c) {
  ctx = c;
  const pill = $('#gm-pill');
  pill.addEventListener('click', toggleGameMode);
  $('.power').innerHTML = icon('power');
  $('.power').addEventListener('click', powerMenu);
  renderPill();
}

/** One press: on or off. The flyout under the pill says what it will do (or did) without leaving the screen. */
function toggleGameMode() {
  if (pending) return;
  const active = !!ctx.app.game?.active;
  pending = true;
  (active ? sfx.off : sfx.on)();
  send(active ? 'gameModeOff' : 'gameModeOn');
  renderPill();
}

export function onGameMode(status) {
  const wasPending = pending;
  pending = false;
  renderPill();
  if (!wasPending) return;
  const done = (status.steps ?? []).filter(s => s.result === 'done').length;
  const failed = (status.steps ?? []).filter(s => s.result === 'failed');
  if (failed.length) { sfx.error(); toast(`${failed[0].title}: ${failed[0].detail}`); }
  else toast(status.active ? t('gm.onToast', { n: done }) : t('gm.offToast'));
}

export function gameModeFailed() { pending = false; renderPill(); }

export function renderPill() {
  const pill = $('#gm-pill');
  if (!pill || !ctx) return;
  const s = ctx.app.game;
  const active = !!s?.active;
  pill.classList.toggle('on', active);
  pill.classList.toggle('busy', pending);
  pill.setAttribute('aria-pressed', String(active));
  pill.innerHTML = `${icon('rocket')}<span class="gm-name">${t('gm.name')}</span>
    <span class="gm-state">${pending ? '<span class="spinner"></span>' : `<span class="gm-dot"></span>${active ? t('gm.on') : t('gm.off')}`}</span>`;
  const lines = active
    ? (s.steps ?? []).filter(x => x.result !== 'skipped').map(x => [x.result === 'failed' ? 's-bad' : 's-ok', `${x.title}: ${x.detail}`])
    : (s?.plan ?? []).filter(x => x.willRun).map(x => ['s-ok', `${x.title}: ${x.detail}`]);
  if (!lines.length) lines.push(['s-idle', t('gm.nothing')]);
  $('#gm-flyout').innerHTML = `<b>${active ? t('gm.didTitle') : t('gm.willTitle')}</b>
    <ul>${lines.slice(0, 4).map(([cls, text]) => `<li class="${cls}"><span class="dot"></span><span>${esc(text)}</span></li>`).join('')}</ul>
    <p>${active ? t('gm.backNote') : t('gm.autoNote')}</p>`;
}

// ---- Dialogs ----

/**
 * A small console dialog. actions: [{ id, label, icon?, primary?, danger? }]. Resolves with the chosen id, or null
 * when dismissed with Back. While it's open the D-pad stays inside it.
 */
export function openDialog({ title, text = '', actions, focus }) {
  closeDialog(null);
  return new Promise(resolve => {
    const el = $('#modal');
    el.innerHTML = `<div class="modal-card" role="dialog" aria-modal="true" aria-labelledby="modal-title">
      <h2 id="modal-title">${esc(title)}</h2>${text ? `<p>${esc(text)}</p>` : ''}
      <div class="modal-actions">${actions.map(a => `<button class="btn${a.primary ? ' primary' : ''}${a.danger ? ' danger' : ''}" data-nav data-act="${esc(a.id)}"${a.id === focus ? ' data-default' : ''}>${a.icon ? icon(a.icon) : ''}${esc(a.label)}</button>`).join('')}</div>
    </div>`;
    el.hidden = false;
    $('main').inert = true;
    $('.topbar').inert = true;
    dialog = { el, resolve, returnFocus: document.activeElement };
    el.onclick = e => {
      const b = e.target.closest('[data-act]');
      if (b) closeDialog(b.dataset.act);
      else if (e.target === el) closeDialog(null);
    };
    sfx.open();
    focusFirst(el);
    renderHints();
  });
}

export const dialogOpen = () => !!dialog;

export function closeDialog(result) {
  if (!dialog) return;
  const { el, resolve, returnFocus } = dialog;
  dialog = null;
  el.hidden = true;
  el.innerHTML = '';
  $('main').inert = false;
  $('.topbar').inert = false;
  if (result === null) sfx.close();
  if (returnFocus?.isConnected) focusEl(returnFocus);
  renderHints();
  resolve(result);
}

async function powerMenu() {
  const choice = await openDialog({
    title: t('power.title'),
    text: ctx.app.game?.active ? t('power.textGame') : t('power.text'),
    actions: [
      { id: 'cancel', label: t('power.cancel') },
      { id: 'desktop', label: t('power.desktop'), icon: 'desktop' },
      { id: 'exit', label: t('power.exit'), icon: 'power', danger: true },
    ],
    focus: 'cancel',
  });
  if (choice === 'exit') send('exit');
  else if (choice === 'desktop') send('minimize');
}

export { powerMenu };

/** Keeps the clock in the top bar current, in the interface language. */
export function startClock() {
  const tick = () => { $('#clock').textContent = new Date().toLocaleTimeString(ctx.app.settings.language === 'en' ? 'en-GB' : 'he-IL', { hour: '2-digit', minute: '2-digit' }); };
  tick();
  setInterval(tick, 10000);
}

export const pillHints = () => [['select', ctx.app.game?.active ? t('gm.turnOff') : t('gm.turnOn')]];

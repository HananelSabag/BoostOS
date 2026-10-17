// Devices: what's plugged in (controllers, mice, keyboards) and the three input tests.

import { $, esc, icon, valueHtml } from '../ui.js';
import { t } from '../i18n.js';
import { padSnapshot, padKind } from '../nav.js';

let ctx;
let root;

const TESTS = [
  { id: 'pad', icon: 'gamepad' },
  { id: 'keys', icon: 'keyboard' },
  { id: 'mouse', icon: 'mouse' },
];

const panel = {
  init(c) {
    ctx = c;
    addEventListener('gamepadconnected', () => panel.refresh());
    addEventListener('gamepaddisconnected', () => panel.refresh());
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="dev">
        <div class="dev-tests">
          ${TESTS.map((x, i) => `<button class="test-tile" data-nav data-test="${x.id}"${i === 0 ? ' data-default' : ''}>
            ${icon(x.icon)}<span><b>${t(`test.${x.id}`)}</b><small>${t(`test.${x.id}.sub`)}</small></span></button>`).join('')}
        </div>
        <div>
          <h3 class="fx-group">${t('dev.connected')}</h3>
          <div class="dev-list" id="dev-list"></div>
        </div>
      </div>`;
    root.onclick = e => {
      const b = e.target.closest('[data-test]');
      if (b) ctx.show(b.dataset.test, { from: 'devices' });
    };
    this.refresh();
  },

  refresh() {
    if (!root) return;
    const sec = ctx.app.report?.sections.find(s => s.id === 'input');
    const gp = padSnapshot();
    const live = gp ? `<div class="dev-row s-ok" data-nav tabindex="0">${icon('gamepad')}<span class="fx-name"><b>${t(padKind(gp) === 'ps' ? 'pad.connected' : 'pad.connectedGeneric')}</b><small>${esc(gp.id.replace(/\s*\(.*$/, ''))}</small></span><span class="fx-chip">${t('dev.live')}</span></div>` : '';
    const rows = (sec?.checks ?? []).map(c => {
      const ic = /mouse|עכבר/i.test(c.title) ? 'mouse' : /key|מקלד/i.test(c.title) ? 'keyboard' : 'gamepad';
      return `<div class="dev-row s-${c.status}" data-nav tabindex="0">${icon(ic)}<span class="fx-name"><b>${esc(c.title)}</b><small>${valueHtml(c.value)}</small></span>
        ${c.status === 'warn' || c.status === 'bad' ? `<span class="fx-chip">${t(`status.${c.status}`)}</span>` : ''}
        ${c.advice ? `<p class="c-advice">${esc(c.advice)}</p>` : ''}</div>`;
    }).join('');
    $('#dev-list', root).innerHTML = live + rows || `<p class="empty">${ctx.app.scanning ? t('scan.loading') : t('dev.none')}</p>`;
  },

  hints: () => [],
};

export default panel;

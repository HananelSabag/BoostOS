// Diagnostics: one screen, five tabs. Overview (what's good and what isn't), Fixes, Network, Devices, Sessions.
// L1/R1 (LB/RB, Q/E) switch tabs from anywhere; moving along the tab bar switches too.

import { $, $$, icon, fmtTime } from '../ui.js';
import { t, locale } from '../i18n.js';
import { focusEl } from '../nav.js';
import { sfx } from '../sound.js';
import overview from '../panels/overview.js';
import fixes from '../panels/fixes.js';
import net from '../panels/net.js';
import devices from '../panels/devices.js';
import sessions from '../panels/sessions.js';
import { issuesOf } from './home.js';

const TABS = [
  { id: 'overview', icon: 'pulse', panel: overview },
  { id: 'fixes', icon: 'wrench', panel: fixes },
  { id: 'net', icon: 'wifi', panel: net },
  { id: 'devices', icon: 'gamepad', panel: devices },
  { id: 'sessions', icon: 'chart', panel: sessions },
];

let ctx;
let root;
let current = 'overview';

const screen = {
  init(c) {
    ctx = c;
    TABS.forEach(x => x.panel.init(c));
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('diag.title')}</h2>
        <span class="head-meta" id="diag-meta"></span>
      </div>
      <nav class="tabs" role="tablist" aria-label="${t('diag.title')}">
        ${TABS.map(x => `<button class="tab" role="tab" data-nav data-tab="${x.id}" data-sfx="none" aria-selected="${x.id === current}">
          ${icon(x.icon)}<span>${t(`tab.${x.id}`)}</span><span class="tab-badge" id="badge-${x.id}"></span></button>`).join('')}
      </nav>
      ${TABS.map(x => `<section class="view" data-view="${x.id}" role="tabpanel" ${x.id === current ? '' : 'hidden'}></section>`).join('')}`;
    TABS.forEach(x => x.panel.build($(`[data-view="${x.id}"]`, root)));
    $('.back', root).addEventListener('click', () => ctx.back());
    $$('.tab', root).forEach(b => {
      b.addEventListener('focus', () => select(b.dataset.tab));
      b.addEventListener('click', () => select(b.dataset.tab));
    });
    this.refresh();
  },

  enter(opts = {}) {
    select(opts.tab ?? current, { force: true });
    if (opts.tab === 'sessions' && opts.newest) sessions.showNewest();
  },

  leave() { TABS.find(x => x.id === current)?.panel.leave?.(); },

  /** Bumpers: the next or previous tab, keeping the focus on the tab bar so Down enters the content. */
  tab(dir) {
    const i = TABS.findIndex(x => x.id === current);
    const next = TABS[(i + dir + TABS.length) % TABS.length];
    sfx.move();
    focusEl($(`[data-tab="${next.id}"]`, root));
  },

  focusTarget(opts = {}) {
    return () => focusEl($(`[data-tab="${opts.tab ?? current}"]`, root));
  },

  back() { return TABS.find(x => x.id === current)?.panel.back?.() ?? false; },

  hints: () => [['select', t('hint.select')], ['tabs', t('hint.tabs')], ['back', t('hint.back')], ...(TABS.find(x => x.id === current)?.panel.hints() ?? [])],

  refresh() {
    if (!root) return;
    TABS.forEach(x => x.panel.refresh?.());
    const { report, scanning } = ctx.app;
    $('#diag-meta', root).textContent = report && !scanning ? t('scan.meta', { machine: report.machine, time: fmtTime(report.generatedAt, locale()) }) : scanning ? t('hub.scanning') : '';
    const issues = report ? issuesOf(report) : [];
    badge('overview', issues.length);
    badge('fixes', issues.filter(c => c.fix).length);
  },
};

export default screen;

function badge(id, n) {
  const el = $(`#badge-${id}`, root);
  if (!el) return;
  el.textContent = n ? String(n) : '';
  el.hidden = !n;
}

// The sound comes from whatever moved the focus (D-pad or bumper), so switching itself stays silent.
function select(id, { force = false } = {}) {
  if (id === current && !force) return;
  const prev = TABS.find(x => x.id === current);
  const changed = id !== current;
  if (changed) prev?.panel.leave?.();
  current = id;
  $$('.tab', root).forEach(b => b.setAttribute('aria-selected', String(b.dataset.tab === id)));
  $$('.view', root).forEach(v => { v.hidden = v.dataset.view !== id; });
  const view = $(`[data-view="${id}"]`, root);
  view.classList.remove('enter');
  if (changed) { void view.offsetWidth; view.classList.add('enter'); }
  TABS.find(x => x.id === id).panel.enter?.();
  ctx.renderHints();
}

export const diagTab = () => current;

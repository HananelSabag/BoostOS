// Overview: the readiness score and every area of the PC. Areas with something to improve open by themselves;
// areas where everything is fine stay folded to one line, so the screen says "what's good, what isn't" at a glance.

import { $, esc, icon, gauge, setGauge, valueHtml } from '../ui.js';
import { t } from '../i18n.js';
import { runFix, fixBusy } from './fixrun.js';

let ctx;
let root;
const opened = new Set();   // areas the player opened
const closed = new Set();   // areas with issues the player folded

const panel = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="ov">
        <aside class="ov-side">
          <div id="ov-gauge">${gauge(176, t('gauge.label'))}</div>
          <ul class="tally" id="ov-tally"></ul>
          <button class="btn primary" data-nav id="ov-fix-all" data-sfx="none" hidden></button>
          <button class="btn" data-nav id="ov-rescan">${icon('pulse')}${t('scan.rescan')}</button>
        </aside>
        <div class="ov-list" id="ov-list"></div>
      </div>`;
    $('#ov-rescan', root).addEventListener('click', () => ctx.startScan());
    $('#ov-fix-all', root).addEventListener('click', e => runFix(fixable(ctx.app.report).map(c => c.fix.id), e.currentTarget));
    root.onclick = e => {
      const fix = e.target.closest('[data-fix]');
      if (fix) { runFix([fix.dataset.fix], fix); return; }
      const head = e.target.closest('[data-sec]');
      if (head) toggle(head.dataset.sec, head.dataset.issues === '1');
    };
    this.refresh();
  },

  enter() { if (!ctx.app.report && !ctx.app.scanning) ctx.startScan(); },

  refresh() {
    if (!root) return;
    const { report, scanning, scanError } = ctx.app;
    const list = $('#ov-list', root);
    const busy = scanning || !report;
    $('#ov-rescan', root).disabled = scanning;
    setGauge($('#ov-gauge', root), busy ? null : report.score, scanning ? t('gauge.scanning') : t('gauge.label'));

    if (busy) {
      $('#ov-tally', root).innerHTML = '';
      $('#ov-fix-all', root).hidden = true;
      list.innerHTML = `<p class="empty">${scanError ? esc(t('error', { msg: scanError })) : t('scan.loading')}</p>`;
      return;
    }

    const checks = report.sections.flatMap(s => s.checks);
    const count = s => checks.filter(c => c.status === s).length;
    $('#ov-tally', root).innerHTML = ['bad', 'warn', 'ok']
      .map(s => `<li class="s-${s}"><span class="dot"></span><b>${count(s)}</b>${t(`status.${s}`)}</li>`).join('');

    const nFix = fixable(report).length;
    const fixAll = $('#ov-fix-all', root);
    fixAll.hidden = nFix === 0;
    if (!fixBusy()) { fixAll.disabled = false; fixAll.innerHTML = `${icon('wrench')}${t('scan.fixAll', { n: nFix })}`; }

    const focused = document.activeElement?.closest?.('[data-sec],[data-check],[data-fix]');
    const focusKey = focused?.dataset.sec ?? focused?.dataset.check ?? null;
    list.innerHTML = report.sections.map((s, si) => {
      const issues = s.checks.filter(c => c.status === 'bad' || c.status === 'warn');
      const worst = issues.some(c => c.status === 'bad') ? 'bad' : issues.length ? 'warn' : 'ok';
      const open = issues.length ? !closed.has(s.id) : opened.has(s.id);
      const good = s.checks.filter(c => c.status === 'ok').length;
      return `<section class="sec s-${worst}${open ? ' open' : ''}">
        <button class="sec-head" data-nav data-sec="${esc(s.id)}" data-issues="${issues.length ? 1 : 0}" aria-expanded="${open}">
          ${icon(s.icon)}<span class="sec-title">${esc(s.title)}</span>
          <span class="sec-sum">${issues.length ? `<span class="pill s-${worst}"><span class="dot"></span>${t('scan.toImprove', { n: issues.length })}</span>` : ''}
            ${good ? `<span class="pill s-ok">${icon('check')}${t('scan.good', { n: good })}</span>` : ''}</span>
          <svg class="chev ic" viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>
        </button>
        ${open ? `<div class="sec-body">${s.checks.map((c, ci) => row(c, `${si}-${ci}`)).join('')}</div>` : ''}
      </section>`;
    }).join('');
    if (focusKey) ($(`[data-sec="${focusKey}"]`, list) ?? $(`[data-check="${focusKey}"]`, list))?.focus({ preventScroll: true });
  },

  hints: () => [],
};

export default panel;

export const fixable = r => (r?.sections ?? []).flatMap(s => s.checks).filter(c => c.fix && (c.status === 'warn' || c.status === 'bad'));

function toggle(id, hasIssues) {
  if (hasIssues) closed.has(id) ? closed.delete(id) : closed.add(id);
  else opened.has(id) ? opened.delete(id) : opened.add(id);
  panel.refresh();
  $(`[data-sec="${id}"]`, root)?.focus({ preventScroll: true });
}

function row(c, key) {
  const issue = c.status === 'warn' || c.status === 'bad';
  const fix = c.fix && issue
    ? `<button class="fix-btn" data-nav data-fix="${esc(c.fix.id)}" data-sfx="none">${icon(c.fix.needsAdmin ? 'shield' : 'wrench')}${t('scan.fix')}</button>`
    : '<span></span>';
  return `
    <div class="check s-${c.status}" data-nav data-check="${key}" tabindex="0">
      <span class="dot"></span>
      <span class="c-title">${esc(c.title)}</span>
      <span class="c-value">${valueHtml(c.value)}</span>
      ${fix}
      ${c.advice ? `<p class="c-advice">${esc(c.advice)}</p>` : ''}
    </div>`;
}

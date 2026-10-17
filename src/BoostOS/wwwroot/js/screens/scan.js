// Full system scan: every check, grouped, with a Fix button wherever BoostOS can fix it by itself.

import { $, $$, esc, icon, gauge, setGauge, valueHtml, reduceMotion, toast, fmtTime } from '../ui.js';
import { send, on } from '../bridge.js';
import { t, locale } from '../i18n.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let onlyIssues = false;
let pendingFix = null;

export default {
  init(c) {
    ctx = c;
    on('fixResult', ({ outcome, applied }) => {
      const done = outcome.done ?? [], failed = outcome.failed ?? [];
      ctx.app.fixesApplied = applied ?? ctx.app.fixesApplied;
      const mine = pendingFix !== null; // "undo all" from Settings reports itself
      pendingFix = null;
      if (!mine) return;
      if (failed.length) { sfx.error(); toast(t('scan.fixFailed', { msg: failed[0].message })); }
      if (done.length) { sfx.on(); toast(t('scan.fixedToast', { n: done.length })); }
      ctx.startScan();
    });
    on('error', e => {
      if (!pendingFix || !['fix', 'undoFix'].includes(e.cmd)) return;
      pendingFix = null;
      this.refresh();
    });
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('scan.title')}</h2>
        <span class="head-meta" id="scan-meta"></span>
        <button class="btn" data-nav id="rescan">${t('scan.rescan')}</button>
      </div>
      <div class="scan-body">
        <aside class="scan-side">
          <div id="scan-gauge">${gauge(196, t('gauge.label'))}</div>
          <ul class="tally" id="scan-tally"></ul>
          <button class="btn primary" data-nav id="fix-all" hidden></button>
          <button class="btn" data-nav id="only-issues" aria-pressed="${onlyIssues}">${t('scan.only')}</button>
          <nav class="scan-index" id="scan-index"></nav>
        </aside>
        <div class="scan-list" id="scan-list"></div>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    $('#rescan', root).addEventListener('click', () => ctx.startScan());
    $('#only-issues', root).addEventListener('click', e => {
      onlyIssues = !onlyIssues;
      e.currentTarget.setAttribute('aria-pressed', String(onlyIssues));
      this.refresh();
    });
    $('#fix-all', root).addEventListener('click', () => {
      const ids = fixable(ctx.app.report).map(c => c.fix.id);
      if (ids.length) runFix(ids, $('#fix-all', root));
    });
    root.onclick = e => {
      const b = e.target.closest('[data-fix]');
      if (b) runFix([b.dataset.fix], b);
    };
    this.refresh();
  },

  enter() {
    if (!ctx.app.report && !ctx.app.scanning) ctx.startScan();
  },

  hints: () => [['select', t('hint.select')], ['back', t('hint.back')], ['full', t('hint.full')]],

  refresh() {
    if (!root) return;
    const { report, scanning, scanError } = ctx.app;
    const list = $('#scan-list', root);
    const busy = scanning || !report;
    $('#rescan', root).disabled = scanning;
    setGauge($('#scan-gauge', root), busy ? null : report.score, scanning ? t('gauge.scanning') : t('gauge.label'));

    if (busy) {
      $('#scan-meta', root).textContent = '';
      $('#scan-tally', root).innerHTML = '';
      $('#scan-index', root).innerHTML = '';
      $('#fix-all', root).hidden = true;
      list.innerHTML = `<p class="empty">${scanError ? esc(t('error', { msg: scanError })) : t('scan.loading')}</p>`;
      return;
    }

    $('#scan-meta', root).textContent = t('scan.meta', { machine: report.machine, time: fmtTime(report.generatedAt, locale()) });
    const checks = report.sections.flatMap(s => s.checks);
    const count = s => checks.filter(c => c.status === s).length;
    $('#scan-tally', root).innerHTML = ['bad', 'warn', 'ok']
      .map(s => `<li class="s-${s}"><span class="dot"></span><b>${count(s)}</b>${t(`status.${s}`)}</li>`).join('');

    const fixAll = $('#fix-all', root);
    const nFix = fixable(report).length;
    fixAll.hidden = nFix === 0;
    fixAll.innerHTML = `${icon('wrench')} ${t('scan.fixAll', { n: nFix })}`;

    const sections = report.sections
      .map(s => ({ ...s, shown: onlyIssues ? s.checks.filter(c => c.status === 'bad' || c.status === 'warn') : s.checks }))
      .filter(s => s.shown.length);

    $('#scan-index', root).innerHTML = sections.map(s => {
      const n = s.checks.filter(c => c.status === 'bad' || c.status === 'warn').length;
      return `<button class="index-btn" data-nav data-jump="${esc(s.id)}">${icon(s.icon)}<span>${esc(s.title)}</span>${n ? `<span class="count">${n}</span>` : ''}</button>`;
    }).join('');
    $$('[data-jump]', root).forEach(b => b.addEventListener('click', () => {
      const sec = $(`#sec-${b.dataset.jump}`, root);
      sec?.scrollIntoView({ block: 'start', behavior: reduceMotion ? 'auto' : 'smooth' });
      sec?.querySelector('.check')?.focus({ preventScroll: true });
    }));

    list.innerHTML = sections.length ? sections.map(s => `
      <section class="sec" id="sec-${esc(s.id)}">
        <h3 class="sec-head">${icon(s.icon)}${esc(s.title)}</h3>
        ${s.shown.map(row).join('')}
      </section>`).join('') : `<p class="empty">${t('scan.empty')}</p>`;
  },
};

const fixable = r => (r?.sections ?? []).flatMap(s => s.checks).filter(c => c.fix && (c.status === 'warn' || c.status === 'bad'));

function row(c) {
  const fix = c.fix && (c.status === 'warn' || c.status === 'bad')
    ? `<button class="fix-btn" data-nav data-fix="${esc(c.fix.id)}" data-sfx="none" title="${c.fix.needsAdmin ? esc(t('scan.admin')) : ''}">
         ${icon(c.fix.needsAdmin ? 'shield' : 'wrench')}${t('scan.fix')}</button>`
    : '<span></span>';
  return `
    <div class="check s-${c.status}" data-nav tabindex="0">
      <span class="dot"></span>
      <span class="c-title">${esc(c.title)}</span>
      <span class="c-value">${valueHtml(c.value)}</span>
      ${fix}
      <span class="tag">${t(`status.${c.status}`)}</span>
      ${c.advice ? `<p class="c-advice">${esc(c.advice)}</p>` : ''}
    </div>`;
}

function runFix(ids, button) {
  if (pendingFix) return;
  pendingFix = ids;
  sfx.select();
  button.classList.add('busy');
  button.disabled = true;
  button.innerHTML = `<span class="spinner"></span>${t('scan.fixing')}`;
  send('fix', { ids });
}

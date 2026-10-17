// Game Mode: the plan (or what was done), the big switch, and the report from the last match.

import { $, esc, icon, toast, fmtTime } from '../ui.js';
import { send, on } from '../bridge.js';
import { t, locale } from '../i18n.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let fresh = false; // the report on screen is from a match that just ended

const screen = {
  init(c) {
    ctx = c;
    on('matchReport', ({ report }) => {
      ctx.app.matches = [report, ...(ctx.app.matches ?? []).filter(m => m.start !== report.start)].slice(0, 20);
      fresh = true;
      ctx.refreshAll();
      if (ctx.app.current !== 'game') ctx.show('game');
    });
    on('matches', list => { ctx.app.matches = list; screen.refresh(); });
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('game.title')}</h2>
        <span class="head-meta">${t('game.meta')}</span>
      </div>
      <div class="game-body">
        <div class="game-main">
          <section><h3 id="game-list-title"></h3><ul class="steps" id="game-list"></ul></section>
          <section class="report" id="game-report"></section>
        </div>
        <aside class="game-side">
          <p class="game-state" id="game-state"><span class="led"></span><span id="game-state-text"></span></p>
          <button class="btn primary big" data-nav data-default id="game-play"></button>
          <button class="btn" data-nav id="game-toggle" data-sfx="none"></button>
          <p class="note">${t('game.note')}</p>
        </aside>
      </div>`;
    $('.back', root).addEventListener('click', () => ctx.back());
    $('#game-play', root).addEventListener('click', () => {
      send(ctx.app.game?.active ? 'launchRocketLeague' : 'play');
      toast(t('game.launching'));
    });
    $('#game-toggle', root).addEventListener('click', () => {
      const active = ctx.app.game?.active;
      (active ? sfx.off : sfx.on)();
      send(active ? 'gameModeOff' : 'gameModeOn');
      toast(active ? t('game.restoring') : t('game.applying'));
    });
    this.refresh();
  },

  enter() { send('gameModeStatus'); send('matches'); },
  leave() { fresh = false; },
  hints: () => [['select', t('hint.select')], ['back', t('hint.back')], ['full', t('hint.full')]],

  refresh() {
    if (!root) return;
    const s = ctx.app.game;
    const active = !!s?.active;
    $('#game-state', root).classList.toggle('on', active);
    $('#game-state .led', root).classList.toggle('on', active);
    $('#game-state-text', root).textContent = active ? (s.since ? t('game.since', { time: fmtTime(s.since, locale()) }) : t('game.on')) : t('game.off');
    $('#game-play', root).textContent = active ? t('game.open') : t('game.play');
    $('#game-toggle', root).textContent = active ? t('game.restore') : t('game.only');

    const list = $('#game-list', root);
    if (active) {
      $('#game-list-title', root).textContent = t('game.did');
      const cls = { done: 's-ok', skipped: 's-idle', failed: 's-bad' };
      list.innerHTML = (s.steps.length
        ? s.steps.map(x => item(cls[x.result] ?? 's-idle', x.title, x.detail)).join('')
        : item('s-idle', t('game.nothing'), t('game.ready'))) + item('s-idle', t('game.after'), t('game.afterText'));
    } else {
      $('#game-list-title', root).textContent = t('game.will');
      list.innerHTML = (s?.plan ?? []).map(x => item(x.willRun ? 's-ok' : 's-idle', x.title, x.detail)).join('');
    }
    renderReport();
  },
};

export default screen;

const item = (cls, title, detail) => `<li class="${cls}"><span class="dot"></span><span><b>${esc(title)}</b><span>${esc(detail)}</span></span></li>`;

function renderReport() {
  const box = $('#game-report', root);
  const [m, prev] = ctx.app.matches ?? [];
  if (!m) {
    box.innerHTML = `<h3>${t('report.title')}</h3><p>${t('report.none')}</p>`;
    return;
  }
  const n = (v, unit = '', digits = 0) => finite(v) ? `${Number(v.toFixed(digits))}${unit}` : '—';
  const hasCpu = m.schemaVersion < 2 || !finite(m.schemaVersion) || m.cpuSamples !== 0;
  const previousCore = prev?.schemaVersion >= 2 && prev.cpuSamples === 0 ? null : prev?.busiestCoreAvg;
  // Different scenes and session lengths can change load and clocks in either direction.
  const delta = (cur, before, unit = '') => {
    if (!finite(cur) || !finite(before)) return '';
    const d = Math.round(cur - before);
    if (d === 0) return '';
    return `<span class="delta"><bdi>${d > 0 ? '+' : ''}${d}${unit}</bdi></span>`;
  };
  const cells = [
    [n(m.minutes, '', 1), t('report.minutes'), ''],
    [n(m.gpuUtil, '%'), t('report.gpuUtil'), ''],
    [n(m.gpuTempAvg, '°C'), t('report.gpuTempAvg'), ''],
    [n(m.gpuTempMax, '°C'), t('report.gpuTemp'), delta(m.gpuTempMax, prev?.gpuTempMax, '°')],
    [n(m.gpuClock), `${t('report.gpuClock')} · MHz`, delta(m.gpuClock, prev?.gpuClock)],
    [n(m.gpuPower, 'W'), t('report.power'), ''],
    [n(hasCpu ? m.cpuAvg : null, '%'), t('report.cpuUtil'), ''],
    [n(hasCpu ? m.busiestCoreAvg : null, '%'), t('report.core'), hasCpu ? delta(m.busiestCoreAvg, previousCore, '%') : ''],
  ];
  const load = hasCpu
    ? t(({ cpu: 'report.cpu', gpu: 'report.gpu', none: 'report.none2' })[m.bottleneck] ?? 'report.unknown')
    : `${m.bottleneck === 'gpu' ? `${t('report.gpu')} ` : ''}${t('report.cpuMissing')}`;

  box.innerHTML = `
    <h3>${t('report.title')}</h3>
    <p>${esc(fresh ? t('report.fresh') : fmtTime(m.start, locale()))} · ${m.gameMode ? t('report.withGame') : t('report.withoutGame')}</p>
    ${renderLimits(m, n)}
    <div class="report-grid">${cells.map(([b, s, d]) => `<div><b><bdi>${esc(b)}</bdi>${d}</b><span>${esc(s)}</span></div>`).join('')}</div>
    ${prev ? `<p class="report-comparison">${esc(t('report.compare'))} · ${esc(t('report.compareNote'))}</p>` : ''}
    <div class="report-load"><h4>${t('report.loadTitle')}</h4><p>${esc(load)}</p></div>
    ${renderTimeline(m)}
    <div class="report-context"><p>${esc(t('report.scope'))}</p>${renderCoverage(m)}</div>`;
}

const finite = v => typeof v === 'number' && Number.isFinite(v);

function renderLimits(m, n) {
  const hasReasons = m.schemaVersion >= 2 && m.clockReasonSamples > 0;
  const reasonFields = [
    ['gpuThermalLimitedPct', 'report.reasonThermal'],
    ['gpuPowerLimitedPct', 'report.reasonPower'],
    ['gpuHardwareSlowdownPct', 'report.reasonHardware'],
  ];
  const limited = (finite(m.gpuLimitedPct) && m.gpuLimitedPct > 0)
    || (hasReasons && reasonFields.some(([key]) => finite(m[key]) && m[key] > 0));
  const observed = finite(m.gpuLimitedPct);
  const state = limited ? 'observed' : observed ? 'clear' : 'unavailable';
  const title = limited ? 'report.limitDetected' : observed ? 'report.limitNotSeen' : 'report.limitUnavailable';
  return `<div class="report-limits${limited ? ' has-limits' : ''}" data-report-limit="${state}">
    <div class="report-limit-heading">${icon(limited ? 'pulse' : 'gpu')}<h4>${t(title)}</h4>
      ${observed ? `<b class="report-limit-number"><bdi>${esc(n(m.gpuLimitedPct, '%'))}</bdi><small>${t('report.observedSamples')}</small></b>` : ''}</div>
    <p>${esc(t('report.limitMeaning'))}</p>
    ${hasReasons ? `<dl class="report-reasons">${reasonFields.map(([key, label]) => `<div><dt>${t(label)}</dt><dd><bdi>${esc(n(m[key], '%'))}</bdi></dd></div>`).join('')}</dl><p class="report-reason-note">${esc(t('report.reasonCoverage', { n: m.clockReasonSamples }))} ${esc(t('report.reasonOverlap'))}</p>`
    : `<p class="report-reason-missing">${esc(t(m.schemaVersion >= 2 ? 'report.reasonUnavailable' : 'report.reasonLegacy'))}</p>`}
    ${hasReasons && m.gpuThermalLimitedPct > 0 ? `<p class="report-next">${esc(t('report.nextThermal'))}</p>` : ''}
    ${hasReasons && m.gpuPowerLimitedPct > 0 ? `<p class="report-next">${esc(t('report.nextPower'))}</p>` : ''}
    ${hasReasons && m.gpuHardwareSlowdownPct > 0 ? `<p class="report-next">${esc(t('report.nextHardware'))}</p>` : ''}
  </div>`;
}

function renderCoverage(m) {
  if (m.schemaVersion < 2 || !finite(m.schemaVersion)) return `<p>${esc(t('report.coverageLegacy', { n: finite(m.samples) ? m.samples : '—' }))}</p>`;
  return `<p class="report-coverage">${esc(t('report.coverage', { gpu: finite(m.gpuSamples) ? m.gpuSamples : '—', cpu: finite(m.cpuSamples) ? m.cpuSamples : '—' }))}</p>
    <p>${esc(t('report.warmup'))}${m.telemetryFile && !m.telemetrySaveFailed && !m.telemetryTruncated ? ` ${esc(t('report.warmupSaved'))}` : ''}</p>
    ${m.telemetrySaveFailed ? `<p class="report-storage-warning">${esc(t('report.detailFailed'))}</p>` : m.telemetryFile ? `<p>${esc(t('report.detailSaved'))}</p>` : ''}
    ${m.telemetryTruncated ? `<p class="report-storage-warning">${esc(t('report.detailTruncated'))}</p>` : ''}`;
}

function renderTimeline(m) {
  const points = (Array.isArray(m.timeline) ? m.timeline : []).filter(p => p && finite(p.seconds) && p.seconds >= 0).sort((a, b) => a.seconds - b.seconds);
  if (points.length < 2 || points.at(-1).seconds <= points[0].seconds) {
    return `<p class="report-timeline-missing">${esc(t(m.schemaVersion >= 2 ? 'report.timelineUnavailable' : 'report.timelineLegacy'))}</p>`;
  }
  // Two separate scales keep temperature and utilisation distinct. Missing values break the line.
  const duration = Math.max(finite(m.minutes) ? m.minutes * 60 : 0, points.at(-1).seconds, 1);
  const minute = seconds => Number((seconds / 60).toFixed(1));
  const charts = [['gpuTemp', 'report.timelineTemp', 'temperature', '°C'], ['gpuUtil', 'report.timelineLoad', 'utilisation', '%']].map(([key, label, cls, unit]) => {
    const values = points.map(p => p[key]).filter(finite);
    if (values.length < 2) return '';
    const min = key === 'gpuTemp' ? Math.max(0, Math.floor(Math.min(...values) / 10) * 10 - 10) : 0;
    const max = key === 'gpuTemp' ? Math.max(min + 20, Math.ceil(Math.max(...values) / 10) * 10) : Math.max(100, Math.ceil(Math.max(...values) / 10) * 10);
    const x = seconds => 38 + seconds / duration * 672;
    const y = value => 88 - (value - min) / (max - min) * 76;
    let drawing = false;
    const path = points.map(p => {
      if (!finite(p[key])) { drawing = false; return ''; }
      const move = drawing ? 'L' : 'M';
      drawing = true;
      return `${move}${x(p.seconds).toFixed(1)},${y(p[key]).toFixed(1)}`;
    }).join(' ');
    const range = t('report.timelineRange', { min: Math.round(Math.min(...values)), max: Math.round(Math.max(...values)), unit });
    return `<div class="report-trace ${cls}"><div class="report-trace-heading"><b>${t(label)}</b><span><bdi>${esc(range)}</bdi></span></div>
      <svg viewBox="0 0 720 104" role="img" aria-label="${esc(t(label))}: ${esc(range)}" preserveAspectRatio="none">
        <path class="report-trace-grid" d="M38,12H710 M38,50H710 M38,88H710"/>
        <text x="30" y="17" text-anchor="end">${max}</text><text x="30" y="93" text-anchor="end">${min}</text>
        <path class="report-trace-line" d="${path}"/>
      </svg></div>`;
  }).join('');
  if (!charts) return `<p class="report-timeline-missing">${esc(t('report.timelineUnavailable'))}</p>`;
  return `<figure class="report-timeline"><figcaption><h4>${t('report.timelineTitle')}</h4><p>${t('report.timelineSaved')}</p></figcaption>${charts}
    <div class="report-trace-time" dir="ltr"><bdi>0</bdi><span>${t('report.elapsedMinutes')}</span><bdi>${minute(duration)}</bdi></div></figure>`;
}

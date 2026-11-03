// Sessions: one place for everything recorded while you played. Pick a session on the side; the detail starts
// with what it means in plain words, then the numbers, the charts and what else was busy on the PC.

import { $, esc, icon, finite, gameArt, fmtTime, fmtDate } from '../ui.js';
import { t, locale } from '../i18n.js';

let ctx;
let root;
let selected = 0;

const panel = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    root.innerHTML = `<div class="ses-wrap"><section class="card ses-sum" id="ses-sum" data-nav tabindex="0"></section>
      <div class="ses"><nav class="ses-list" id="ses-list" aria-label="${t('ses.list')}"></nav><article class="ses-detail" id="ses-detail"></article></div></div>`;
    root.addEventListener('focusin', e => {
      const item = e.target.closest('[data-ses]');
      if (item && Number(item.dataset.ses) !== selected) {
        selected = Number(item.dataset.ses);
        $('#ses-list', root).dataset.key = `${(ctx.app.matches ?? []).length}|${ctx.app.matches?.[0]?.start}|${ctx.app.games.length}|${selected}|${ctx.app.summary?.main?.text ?? ''}`;
        renderDetail();
        markSelected();
      }
    });
    this.refresh();
  },

  /** A session that just ended opens here, selected. */
  showNewest() { selected = 0; delete $('#ses-list', root).dataset.key; this.refresh(); },

  refresh() {
    if (!root) return;
    const list = ctx.app.matches ?? [];
    if (selected >= list.length) selected = 0;
    // Redraw only when the sessions (or the art for them) changed, and keep the focus where it was.
    const key = `${list.length}|${list[0]?.start}|${ctx.app.games.length}|${selected}|${ctx.app.summary?.main?.text ?? ''}`;
    const box = $('#ses-list', root);
    if (box.dataset.key === key) return;
    box.dataset.key = key;
    renderSummary(list);
    const focused = document.activeElement?.closest?.('[data-ses]')?.dataset.ses;
    box.innerHTML = list.length ? list.map((m, i) => {
      const game = gameOf(m);
      const worst = (m.findings ?? []).find(f => f.status !== 'ok')?.status ?? 'ok';
      return `<button class="ses-item s-${worst}" data-nav data-ses="${i}"${i === 0 ? ' data-default' : ''} aria-current="${i === selected}">
        <span class="ses-thumb">${gameArt(game, { logo: false })}</span>
        <span class="ses-text"><b>${esc(m.gameName ?? game?.name ?? 'Rocket League')}</b>
          <small><bdi>${esc(fmtDate(m.start, locale()))} · ${esc(fmtTime(m.start, locale()))}</bdi> · ${t('ses.minutes', { n: Math.round(m.minutes) })}</small></span>
        <span class="dot"></span>
      </button>`;
    }).join('') : '';
    if (focused != null) $(`[data-ses="${focused}"]`, box)?.focus({ preventScroll: true });
    renderDetail();
  },

  hints: () => [],
};

export default panel;

function markSelected() {
  root.querySelectorAll('[data-ses]').forEach(b => b.setAttribute('aria-current', String(Number(b.dataset.ses) === selected)));
}

const gameOf = m => ctx.app.games.find(g => g.id === m.gameId) ?? (m.gameId ? null : ctx.app.games.find(g => g.isRocketLeague));

function renderDetail() {
  const box = $('#ses-detail', root);
  const list = ctx.app.matches ?? [];
  const m = list[selected];
  if (!m) {
    box.innerHTML = `<div class="ses-empty">${icon('chart')}<h3>${t('ses.noneTitle')}</h3><p>${t('ses.none')}</p></div>`;
    return;
  }
  const prev = list.slice(selected + 1).find(x => (x.gameId ?? 'epic:Sugar') === (m.gameId ?? 'epic:Sugar'));
  const notes = m.findings ?? [];
  box.innerHTML = `
    <header class="ses-head">
      <h3>${esc(m.gameName ?? gameOf(m)?.name ?? 'Rocket League')}</h3>
      <span><bdi>${esc(fmtDate(m.start, locale()))} · ${esc(fmtTime(m.start, locale()))}</bdi> · ${t('ses.minutes', { n: Math.round(m.minutes) })} · ${m.gameMode ? t('ses.withGm') : t('ses.withoutGm')}</span>
    </header>
    <section class="card ses-notes" data-nav tabindex="0">
      <h4>${t('ses.learned')}</h4>
      <ul>${notes.map(f => `<li class="s-${f.status}"><span class="dot"></span><span>${esc(f.text)}${f.tip && f.status !== 'ok' ? `<small>${esc(f.tip)}</small>` : ''}</span></li>`).join('')}</ul>
    </section>
    <section class="ses-metrics" data-nav tabindex="0">${metrics(m, prev)}</section>
    ${charts(m)}
    ${background(m)}`;
}

// ---- All sessions at once ----

const avg = xs => xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null;
const of = (list, f) => list.map(f).filter(finite);

/** The pattern across every session: the one thing to fix first, what is fine, and Game Mode on vs off. */
function renderSummary(list) {
  const box = $('#ses-sum', root);
  if (list.length < 2) { box.hidden = true; return; }
  box.hidden = false;
  const minutes = of(list, m => m.minutes).reduce((a, b) => a + b, 0);
  // The judgment (the one thing to fix first, what is fine) comes from the host, the same rules as the scan.
  const main = ctx.app.summary?.main ?? { status: 'ok', text: '' };
  const good = ctx.app.summary?.good ?? [];

  // Game Mode: compare when both kinds exist, otherwise say what would make the comparison possible.
  const withGm = list.filter(m => m.gameMode), without = list.filter(m => !m.gameMode);
  const cmp = withGm.length && without.length
    ? `<div class="sum-cmp"><b>${t('sum.gmTitle')}</b>${[[t('sum.gmOn'), withGm], [t('sum.gmOff'), without]].map(([label, g]) => `
        <span><i>${label}</i><bdi>${Math.round(avg(of(g, m => m.gpuThermalLimitedPct)) ?? 0)}%</bdi> ${t('sum.heldShort')} · <bdi>${(avg(g.filter(m => m.router).map(m => m.router.spikes / Math.max(1, m.minutes) * 60)) ?? 0).toFixed(1)}</bdi> ${t('sum.spikesShort')}</span>`).join('')}</div>`
    : `<p class="note">${t('sum.gmAll', { n: list.length })}</p>`;

  // One bar per session, oldest on the left: how much of it the card spent at its heat limit, coloured by what that
  // cost (red: frames lost to heat; amber: the clock was held down by something else).
  const bars = [...list].reverse().map(m => {
    const v = finite(m.gpuThermalLimitedPct) ? m.gpuThermalLimitedPct : 0;
    const c = m.heatCost ?? 0;
    return `<span class="${c >= 15 ? 's-bad' : c >= 5 || m.held ? 's-warn' : 's-ok'}" style="--v:${Math.max(4, v)}%" title="${Math.round(v)}%"></span>`;
  }).join('');

  box.innerHTML = `
    <div class="sum-head"><h3>${t('sum.title', { n: list.length })}</h3><span>${t('sum.hours', { h: (minutes / 60).toFixed(1) })}</span></div>
    <div class="sum-body">
      <div class="sum-main s-${main.status}"><span class="dot"></span><div><b>${esc(main.text)}</b>${main.tip ? `<small>${esc(main.tip)}</small>` : ''}</div></div>
      <figure class="sum-bars" dir="ltr" aria-label="${esc(t('sum.barsLabel'))}"><div>${bars}</div><figcaption>${t('sum.barsLabel')}</figcaption></figure>
    </div>
    ${good.length ? `<p class="sum-good">${icon('check')}${good.map(esc).join(' · ')}</p>` : ''}
    ${cmp}`;
}

// ---- What it means ----

const NAMES = {
  chrome: 'Chrome', msedge: 'Edge', msedgewebview2: 'WebView2', discord: 'Discord', spotify: 'Spotify', onedrive: 'OneDrive',
  steam: 'Steam', steamwebhelper: 'Steam', epicgameslauncher: 'Epic Games Launcher', epicwebhelper: 'Epic Games Launcher',
  'eosoverlayrenderer-win64-shipping': 'Epic overlay', dwm: 'Desktop Window Manager', system: 'Windows (System)', svchost: 'Windows services',
  msmpeng: 'Microsoft Defender', searchhost: 'Windows Search', explorer: 'Explorer', boostos: 'BoostOS', claude: 'Claude', chatgpt: 'ChatGPT',
  'whatsapp.root': 'WhatsApp', whatsapp: 'WhatsApp', audiodg: 'Windows audio', nvcontainer: 'NVIDIA Container', 'nvidia-smi': 'nvidia-smi',
  phoneexperiencehost: 'Phone Link', crossdeviceresume: 'Phone Link', intelgraphicssoftware: 'Intel Graphics Software', playstationaccessories: 'PlayStation Accessories',
  'ms-teams': 'Teams', obs64: 'OBS', nvidia_overlay: 'NVIDIA overlay', 'nvidia app': 'NVIDIA app',
};
export const appName = n => NAMES[n] ?? n;

const ghz = mhz => (mhz / 1000).toFixed(1);

// ---- The numbers ----

function metrics(m, prev) {
  const n = (v, unit = '', d = 0) => finite(v) ? `${Number(v.toFixed(d))}${unit}` : '—';
  const delta = (cur, before, unit = '', better = 'lower') => {
    if (!finite(cur) || !finite(before)) return '';
    const d = Math.round(cur - before);
    if (!d) return '';
    const good = better === 'lower' ? d < 0 : d > 0;
    return `<span class="delta ${good ? 'up' : 'down'}"><bdi>${d > 0 ? '+' : ''}${d}${unit}</bdi></span>`;
  };
  const tiles = [
    [icon('temp'), n(m.gpuTempMax, '°'), t('ses.gpuPeak'), delta(m.gpuTempMax, prev?.gpuTempMax, '°'), m.gpuTempMax >= 92 ? 'warn' : ''],
    [icon('gpu'), n(m.gpuThermalLimitedPct, '%'), t('ses.atLimit'), delta(m.gpuThermalLimitedPct, prev?.gpuThermalLimitedPct, '%'), ''],
    [icon('gpu'), n(m.heatCost, '%'), t('ses.heatCost'), '', m.heatCost >= 5 ? 'warn' : ''],
    [icon('gpu'), n(m.gpuUtil, '%'), t('ses.gpuLoad'), finite(m.gpuBusyPct) && m.gpuBusyPct >= 1 ? `<span class="delta"><bdi>${t('ses.maxed', { pct: Math.round(m.gpuBusyPct) })}</bdi></span>` : '', ''],
    [icon('gpu'), n(m.gpuClock), t('ses.gpuClock'), '', m.held ? 'warn' : ''],
    [icon('chip'), finite(m.cpuMhzAvg) ? `${ghz(m.cpuMhzAvg)}` : '—', t('ses.cpuSpeed'), finite(m.cpuMhzLow) ? `<span class="delta"><bdi>${t('ses.low', { v: ghz(m.cpuMhzLow) })}</bdi></span>` : '', ''],
    [icon('chip'), n(m.cpuSamples === 0 ? null : m.busiestCoreAvg, '%'), t('ses.core'), '', m.busiestCoreAvg >= 85 ? 'warn' : ''],
    [icon('wifi'), m.router ? n(m.router.medianMs, '') : '—', t('ses.router'), m.router ? `<span class="delta ${m.router.spikes ? 'down' : ''}"><bdi>${t('ses.spikes', { n: m.router.spikes })}</bdi></span>` : '', m.router?.spikes ? 'warn' : ''],
    [icon('wifi'), m.internet ? n(m.internet.medianMs, '') : '—', t('ses.internet'), '', ''],
    [icon('wifi'), m.wifi ? n(m.wifi.qualityAvg, '%') : '—', t('ses.signal'), m.wifi?.rssiAvg ? `<span class="delta"><bdi>${Math.round(m.wifi.rssiAvg)} dBm</bdi></span>` : '', ''],
    [icon('layers'), n(m.ramMaxPct, '%'), t('ses.ram'), finite(m.ramTotalGb) ? `<span class="delta"><bdi>${t('ses.of', { v: `${Math.round(m.ramTotalGb)} GB` })}</bdi></span>` : '', m.ramMaxPct >= 90 ? 'warn' : ''],
    [icon('layers'), finite(m.vramMaxMb) ? (m.vramMaxMb / 1024).toFixed(1) : '—', t('ses.vram'), finite(m.vramTotalMb) ? `<span class="delta"><bdi>${t('ses.of', { v: `${Math.round(m.vramTotalMb / 1024)} GB` })}</bdi></span>` : '', ''],
  ];
  return tiles.filter(x => x[1] !== '—').map(([ic, v, label, d, s]) =>
    `<div class="metric${s ? ` s-${s}` : ''}">${ic}<b><bdi>${esc(v)}</bdi></b><span>${esc(label)}</span>${d}</div>`).join('');
}

function charts(m) {
  const points = (Array.isArray(m.timeline) ? m.timeline : []).filter(p => p && finite(p.seconds)).sort((a, b) => a.seconds - b.seconds);
  if (points.length < 3) return '';
  const duration = Math.max(points.at(-1).seconds, 1);
  const series = [
    ['gpuTemp', t('ses.chartTemp'), 'temperature', v => `${Math.round(v)}°`, 40, null],
    ['cpuMhz', t('ses.chartCpu'), 'cpu', v => `${ghz(v)}`, 0, null],
    ['routerMs', t('ses.chartPing'), 'ping', v => `${Math.round(v)} ms`, 0, 20],
  ].map(([key, label, cls, fmt, floor, minTop]) => {
    const values = points.map(p => p[key]).filter(finite);
    if (values.length < 3) return '';
    const lo = Math.min(floor, Math.min(...values)), hi = Math.max(minTop ?? 0, Math.max(...values)) * 1.05 || 1;
    const x = s => (s / duration) * 600;
    const y = v => 70 - ((v - lo) / (hi - lo || 1)) * 62;
    let pen = false;
    const d = points.map(p => {
      if (!finite(p[key])) { pen = false; return ''; }
      const cmd = pen ? 'L' : 'M';
      pen = true;
      return `${cmd}${x(p.seconds).toFixed(1)},${y(p[key]).toFixed(1)}`;
    }).join('');
    return `<div class="trace ${cls}"><div class="trace-head"><b>${label}</b><span><bdi>${fmt(Math.min(...values))} – ${fmt(Math.max(...values))}</bdi></span></div>
      <svg viewBox="0 0 600 74" preserveAspectRatio="none" aria-hidden="true"><path class="trace-grid" d="M0,8H600M0,39H600M0,70H600"/><path class="trace-line" d="${d}"/></svg></div>`;
  }).join('');
  if (!series) return '';
  return `<section class="card ses-charts" data-nav tabindex="0"><h4>${t('ses.across')}</h4>${series}
    <div class="trace-time" dir="ltr"><bdi>0</bdi><span>${t('ses.elapsed')}</span><bdi>${Math.round(duration / 60)}′</bdi></div></section>`;
}

function background(m) {
  const apps = m.background ?? [];
  if (!apps.length) return '';
  const max = Math.max(...apps.map(a => a.cpuPct), 1);
  return `<section class="card ses-apps" data-nav tabindex="0"><h4>${t('ses.busy')}</h4>
    <ul>${apps.map(a => `<li><span>${esc(appName(a.name))}</span><span class="bar"><span style="width:${Math.max(3, (a.cpuPct / max) * 100)}%"></span></span><b><bdi>${a.cpuPct.toFixed(1)}%</bdi></b></li>`).join('')}</ul>
    <p class="note">${t('ses.busyNote')}</p></section>`;
}

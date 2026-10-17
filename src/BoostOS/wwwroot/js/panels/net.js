// Network test: router and internet pinged side by side, drawn live. Blue = router (the Wi-Fi link), orange = internet.

import { $, $$, esc, cssVar, toast } from '../ui.js';
import { send, on } from '../bridge.js';
import { t } from '../i18n.js';

let ctx;
let root;
const net = { running: false, secs: 60, samples: { router: [], internet: [] }, result: null };

const panel = {
  init(c) {
    ctx = c;
    let queued = false;
    on('netSample', s => {
      if (!net.running) return;
      (net.samples[s.target] ??= []).push(s);
      if (queued) return;
      queued = true;
      requestAnimationFrame(() => { queued = false; draw(); renderStats(); progress(); });
    });
    on('netResult', r => {
      net.running = false;
      net.result = r;
      panel.refresh();
      draw();
    });
    on('error', e => {
      if (e.cmd !== 'netTest') return;
      net.running = false;
      panel.refresh();
    });
    window.addEventListener('resize', () => requestAnimationFrame(draw));
  },

  build(el) {
    root = el;
    root.innerHTML = `
      <div class="net">
        <div class="net-controls">
          <div class="seg" role="group" aria-label="${t('net.length')}">
            ${[30, 60, 120].map(s => `<button class="seg-btn" data-nav data-secs="${s}" aria-pressed="${net.secs === s}">${t(`net.${s}`)}</button>`).join('')}
          </div>
          <button class="btn primary" data-nav id="net-start"></button>
          <div class="progress" aria-hidden="true"><div id="net-bar"></div></div>
        </div>
        <div class="card chart-panel">
          <canvas id="net-chart" role="img" aria-label="${t('net.chart')}"></canvas>
          <div class="legend">
            <span class="key key-router">${t('net.keyRouter')}</span>
            <span class="key key-internet">${t('net.keyInternet')}</span>
            <span class="key key-lost">${t('net.keyLost')}</span>
          </div>
        </div>
        <div class="net-bottom">
          <div class="stats" id="net-stats"></div>
          <div class="card verdict" id="net-verdict" aria-live="polite"></div>
        </div>
      </div>`;
    $$('[data-secs]', root).forEach(b => b.addEventListener('click', () => {
      if (net.running) return;
      net.secs = Number(b.dataset.secs);
      $$('[data-secs]', root).forEach(o => o.setAttribute('aria-pressed', String(o === b)));
      draw();
    }));
    $('#net-start', root).addEventListener('click', () => {
      if (net.running) { stop(true); return; }
      net.running = true;
      net.result = null;
      net.samples = { router: [], internet: [] };
      send('netTest', { seconds: net.secs });
      panel.refresh();
      draw();
    });
    this.refresh();
  },

  enter() { requestAnimationFrame(draw); },
  leave() { if (net.running) stop(true); },
  hints: () => [],

  refresh() {
    if (!root) return;
    const b = $('#net-start', root);
    b.textContent = net.running ? t('net.stop') : net.result ? t('net.again') : t('net.start');
    b.classList.toggle('primary', !net.running);
    $$('[data-secs]', root).forEach(x => { x.disabled = net.running; });
    renderStats();
    progress();
    const v = $('#net-verdict', root);
    const r = net.result;
    v.className = 'card verdict' + (r ? ` s-${r.status}` : '');
    v.innerHTML = r
      ? `<h3>${esc(r.headline)}</h3><ul>${r.findings.map(f => `<li class="s-${f.status}"><span class="dot"></span><span>${esc(f.text)}</span></li>`).join('')}</ul>`
      : `<p>${net.running ? t('net.running') : t('net.idle')}</p>`;
  },
};

export default panel;

function stop(notify) {
  send('netCancel');
  net.running = false;
  if (notify) toast(t('net.stopped'));
  panel.refresh();
}

const fmt = v => v == null ? '—' : v < 10 ? v.toFixed(1) : String(Math.round(v));

function live(arr) {
  if (!arr?.length) return null;
  const ok = arr.filter(s => s.ms != null).map(s => s.ms);
  const sorted = [...ok].sort((a, b) => a - b);
  let jitter = null;
  if (ok.length > 1) {
    let sum = 0;
    for (let i = 1; i < ok.length; i++) sum += Math.abs(ok[i] - ok[i - 1]);
    jitter = sum / (ok.length - 1);
  }
  return { median: sorted.length ? sorted[Math.floor(sorted.length / 2)] : null, jitter, lossPct: 100 * (arr.length - ok.length) / arr.length, max: sorted.at(-1) ?? null };
}

function renderStats() {
  if (!root) return;
  const r = net.result;
  $('#net-stats', root).innerHTML = [['router', t('net.router')], ['internet', t('net.internet')]].map(([id, label]) => {
    const s = r?.targets.find(x => x.id === id) ?? live(net.samples[id]);
    if (!s) return `<div class="card stat ${id} empty-stat"><h3>${label}</h3><div class="stat-main">—</div></div>`;
    const rows = [[t('net.jitter'), `${fmt(s.jitter)} ms`], [t('net.loss'), `${(s.lossPct ?? 0).toFixed(1)}%`], [t('net.max'), `${fmt(s.max)} ms`]];
    if (s.spikeEvents != null) rows.push([t('net.spikes'), s.periodSeconds ? t('net.every', { n: s.spikeEvents, s: Math.round(s.periodSeconds) }) : String(s.spikeEvents)]);
    return `<div class="card stat ${id}">
      <h3>${label}${s.address ? ` <bdi>${esc(s.address)}</bdi>` : ''}</h3>
      <div class="stat-main"><bdi>${fmt(s.median)}</bdi><small>${t('net.median')}</small></div>
      <dl class="stat-rows">${rows.map(([k, v]) => `<dt>${k}</dt><dd><bdi>${esc(v)}</bdi></dd>`).join('')}</dl>
    </div>`;
  }).join('');
}

function progress() {
  if (!root) return;
  const all = [...net.samples.router, ...net.samples.internet];
  const tmax = all.length ? Math.max(...all.map(s => s.t)) : 0;
  $('#net-bar', root).style.width = net.result ? '100%' : `${Math.min(100, (100 * tmax) / net.secs)}%`;
}

const niceCeil = v => [10, 20, 25, 50, 100, 200, 250, 500, 1000].find(s => v <= s) ?? Math.ceil(v / 500) * 500;

function draw() {
  const c = root && $('#net-chart', root);
  if (!c || c.offsetParent === null) return;
  const w = c.clientWidth, h = c.clientHeight, dpr = window.devicePixelRatio || 1;
  if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
  const g = c.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  g.direction = 'ltr';

  const padL = 8, padR = 62, padT = 18, padB = 26;
  const secs = net.secs;
  const series = [['internet', cssVar('--orange')], ['router', cssVar('--blue')]];
  const values = series.flatMap(([id]) => (net.samples[id] ?? []).filter(s => s.ms != null).map(s => s.ms)).sort((a, b) => a - b);
  const top = values.length ? values[Math.floor(values.length * 0.995)] : 0;
  const ymax = niceCeil(Math.max(20, top * 1.15));
  const X = tt => padL + (tt / secs) * (w - padL - padR);
  const Y = ms => padT + (1 - Math.min(ms, ymax) / ymax) * (h - padT - padB);

  g.font = '600 13px Assistant, "Segoe UI", sans-serif';
  g.textBaseline = 'middle';
  g.lineWidth = 1;
  for (let i = 0; i <= 4; i++) {
    const v = (ymax * i) / 4, y = Y(v);
    g.strokeStyle = 'rgba(255,255,255,0.07)';
    g.beginPath(); g.moveTo(padL, y); g.lineTo(w - padR, y); g.stroke();
    g.fillStyle = cssVar('--ink-faint');
    g.textAlign = 'left';
    g.fillText(`${v} ms`, w - padR + 10, y);
  }
  g.textBaseline = 'top';
  g.textAlign = 'center';
  for (let i = 0; i <= 4; i++) { const tt = (secs * i) / 4; g.fillText(`${Math.round(tt)}s`, X(tt), h - padB + 8); }

  for (const [id, color] of series) {
    const samples = net.samples[id] ?? [];
    g.strokeStyle = color;
    g.lineWidth = id === 'router' ? 2.2 : 1.6;
    g.lineJoin = 'round';
    g.beginPath();
    let pen = false;
    for (const s of samples) {
      if (s.ms == null) { pen = false; continue; }
      const x = X(s.t), y = Y(s.ms);
      if (pen) g.lineTo(x, y); else { g.moveTo(x, y); pen = true; }
    }
    g.stroke();
    g.fillStyle = cssVar('--bad');
    for (const s of samples) if (s.ms == null) g.fillRect(X(s.t) - 1.5, padT - 14, 3, 10);
  }

  if (!values.length) {
    g.fillStyle = cssVar('--ink-faint');
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.font = '600 16px Assistant, "Segoe UI", sans-serif';
    g.direction = document.documentElement.dir;
    g.fillText(net.running ? t('net.measuring') : t('net.waiting'), w / 2, h / 2);
  }
}

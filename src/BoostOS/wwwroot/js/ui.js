// Small shared pieces: DOM helpers, icons, game art, the boost gauge, toasts, dialogs and the arena lights.

import { inHost } from './bridge.js';

export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
export const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
export const cssVar = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
export const finite = v => typeof v === 'number' && Number.isFinite(v);

const ICONS = {
  chip: '<rect x="6" y="6" width="12" height="12" rx="2"/><rect x="9.5" y="9.5" width="5" height="5" rx="1"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/>',
  display: '<rect x="2.5" y="4" width="19" height="12.5" rx="2"/><path d="M8.5 20.5h7M12 16.5v4"/>',
  gpu: '<rect x="2" y="6" width="20" height="11" rx="2"/><circle cx="9" cy="11.5" r="3"/><path d="M15 9.5h4M15 13.5h4M5 17v3"/>',
  bolt: '<path d="M13 2 4.5 13.5H11L10 22l8.5-11.5H12L13 2Z"/>',
  wifi: '<path d="M2.5 9a14 14 0 0 1 19 0M5.5 12.5a9.5 9.5 0 0 1 13 0M8.6 16a5 5 0 0 1 6.8 0"/><circle cx="12" cy="19.3" r="1.1"/>',
  car: '<path d="M5.2 16.5H4a1 1 0 0 1-1-1V13l2.2-4.3A2 2 0 0 1 7 7.6h8.6a2 2 0 0 1 1.6.8L20 12l1 1v2.5a1 1 0 0 1-1 1h-1.2"/><circle cx="7.5" cy="16.5" r="2"/><circle cx="16.5" cy="16.5" r="2"/><path d="M9.5 16.5h5M6 12h14"/>',
  windows: '<rect x="3" y="3" width="8" height="8" rx="1"/><rect x="13" y="3" width="8" height="8" rx="1"/><rect x="3" y="13" width="8" height="8" rx="1"/><rect x="13" y="13" width="8" height="8" rx="1"/>',
  layers: '<path d="m12 3 9 5-9 5-9-5 9-5Z"/><path d="m3 13 9 5 9-5"/>',
  gamepad: '<path d="M7 7h10a5 5 0 0 1 4.9 6l-.7 3.4a2.6 2.6 0 0 1-4.5 1.2L14.6 15H9.4l-2.1 2.6a2.6 2.6 0 0 1-4.5-1.2L2.1 13A5 5 0 0 1 7 7Z"/><path d="M7.5 10v3M6 11.5h3"/><circle cx="16" cy="10.5" r=".7"/><circle cx="17.6" cy="12.6" r=".7"/>',
  keyboard: '<rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M6 14h.01M18 14h.01M9 14h6"/>',
  disk: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="12" cy="11" r="4"/><path d="M7 17h.01"/>',
  ball: '<circle cx="12" cy="12" r="9"/><path d="M12 7.5 15.5 10l-1.3 4h-4.4L8.5 10 12 7.5Z"/><path d="M12 3v4.5M15.5 10l4.6-1.5M14.2 14l2.9 3.8M9.8 14l-2.9 3.8M8.5 10 3.9 8.5"/>',
  pulse: '<path d="M3 12h4l2.5-6 5 12 2.5-6h4"/>',
  mouse: '<rect x="6" y="2.5" width="12" height="19" rx="6"/><path d="M12 6.5v3.5"/>',
  rocket: '<path d="M5 15c-1.5 1.5-2 5-2 5s3.5-.5 5-2"/><path d="M9 13.5 6.5 11l2-4c2.5-4.5 7-5.5 12.5-5.5 0 5.5-1 10-5.5 12.5l-4 2L9 13.5Z"/><circle cx="15" cy="9" r="1.6"/>',
  power: '<path d="M12 3v8"/><path d="M6.3 6.3a8 8 0 1 0 11.4 0"/>',
  back: '<path d="M9 5l7 7-7 7"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z"/>',
  wrench: '<path d="M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 0 0 5.4-5.4l-2.6 2.6-2.4-.6-.6-2.4 2.6-2.6Z"/>',
  shield: '<path d="M12 3 4.5 6v5.5c0 4.6 3.2 8.4 7.5 9.5 4.3-1.1 7.5-4.9 7.5-9.5V6L12 3Z"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  cross: '<path d="m6 6 12 12M18 6 6 18"/>',
  circle: '<circle cx="12" cy="12" r="7"/>',
  grid: '<rect x="3" y="3" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="2"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="2"/>',
  star: '<path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9L12 3Z"/>',
  sliders: '<path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/>',
  chart: '<path d="M4 20V4M4 20h16"/><path d="m7 15 4-5 3 3 5-6"/>',
  undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>',
  temp: '<path d="M14 14.8V4.5a2 2 0 1 0-4 0v10.3a4 4 0 1 0 4 0Z"/><path d="M12 9v7"/>',
  play: '<path d="M7 4.5v15l12-7.5-12-7.5Z"/>',
  desktop: '<rect x="2.5" y="4" width="19" height="12.5" rx="2"/><path d="M8.5 20.5h7M12 16.5v4M8 10l4 3 4-3"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
};

export const icon = name => `<svg class="ic" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] ?? ICONS.chip}</svg>`;

// Values arrive as "part · part · part". Each part is isolated so English and Hebrew inside it keep their own order.
export const valueHtml = v => String(v ?? '').split(' · ').map(p => `<bdi>${esc(p)}</bdi>`).join('<span class="sep" aria-hidden="true"></span>');

/** Game pictures: served by the host from its art cache; the browser preview reads copies in dev/art. */
export const artUrl = name => name ? (inHost ? `https://art.boostos.example/${encodeURIComponent(name)}` : `dev/art/${encodeURIComponent(name)}`) : '';

/** The picture of a game: its art and, for Steam, the title logo laid over it. */
export function gameArt(g, { logo = true } = {}) {
  if (!g) return `<span class="art-fallback">${icon('ball')}</span>`;
  const wide = g.wide ? `<img class="art-img" src="${artUrl(g.wide)}" alt="" loading="lazy" decoding="async">` : `<span class="art-fallback">${g.icon ? `<img src="${artUrl(g.icon)}" alt="">` : icon('gamepad')}</span>`;
  const mark = logo && g.logo ? `<img class="art-logo" src="${artUrl(g.logo)}" alt="" decoding="async">` : '';
  return wide + mark;
}

let gaugeIds = 0;
const gaugeFrames = new WeakMap();
export function gauge(size, label) {
  const id = `g${++gaugeIds}`;
  return `<div class="gauge" style="--size:${size}px" role="img">
    <svg viewBox="0 0 200 200" aria-hidden="true">
      <defs><linearGradient id="${id}" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#FF7A1A"/><stop offset="1" stop-color="#FFD23F"/></linearGradient></defs>
      <circle class="g-track" cx="100" cy="100" r="84"/>
      <circle class="g-fill" cx="100" cy="100" r="84" pathLength="100" stroke="url(#${id})"/>
    </svg>
    <div class="g-center"><span class="g-num">--</span><span class="g-label">${esc(label)}</span></div>
  </div>`;
}

/** Fills the ring and counts the number up, like the boost meter after a pickup. */
export function setGauge(host, score, label) {
  const g = host?.querySelector('.gauge');
  if (!g) return;
  const fill = g.querySelector('.g-fill'), num = g.querySelector('.g-num');
  g.querySelector('.g-label').textContent = label;
  g.setAttribute('aria-label', score == null ? label : `${label}: ${score}/100`);
  g.classList.toggle('full', score === 100);
  if (score == null) {
    cancelAnimationFrame(gaugeFrames.get(g));
    gaugeFrames.delete(g);
    delete g.dataset.score;
    fill.style.strokeDashoffset = 100;
    num.textContent = '--';
    return;
  }
  if (g.dataset.score === String(score)) return;
  cancelAnimationFrame(gaugeFrames.get(g));
  const from = Number(num.textContent) || 0;
  g.dataset.score = score;
  fill.style.strokeDashoffset = 100 - score;
  if (reduceMotion) { num.textContent = score; return; }
  const start = performance.now();
  const step = now => {
    const p = Math.min(1, (now - start) / 1200);
    num.textContent = Math.round(from + (score - from) * (1 - Math.pow(1 - p, 3)));
    if (p < 1) gaugeFrames.set(g, requestAnimationFrame(step));
    else gaugeFrames.delete(g);
  };
  gaugeFrames.set(g, requestAnimationFrame(step));
}

let toastTimer;
export function toast(message) {
  const el = $('#toast');
  el.textContent = message;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 3400);
}

const ACCENTS = { play: '#FF8B2B', games: '#FF8B2B', game: '#FF8B2B', diag: '#3D8BFF', settings: '#7FA3DB', tool: '#3D8BFF', boost: '#FFD23F' };
/** The floodlights take the colour of what you're looking at. */
export const setAccent = key => document.documentElement.style.setProperty('--accent', ACCENTS[key] ?? ACCENTS.diag);

/** Swing the main floodlight over an element (or back to the top centre). One transition, then it rests. */
export function aimLight(el) {
  const flood = document.querySelector('.flood-a');
  if (!flood) return;
  const r = el?.getBoundingClientRect();
  const cx = r ? r.left + r.width / 2 : innerWidth / 2;
  const cy = r ? r.top + r.height / 2 : 0;
  flood.style.transform = `translate3d(${Math.round(cx - flood.offsetWidth / 2)}px, ${Math.round(cy - flood.offsetHeight * 0.66)}px, 0)`;
}

/** The arena backdrop shows the focused game's picture, cross-faded between two layers. null clears it. */
let artLayer = 0, artNow = '';
export function setBackdrop(name) {
  const url = artUrl(name);
  if (url === artNow) return;
  artNow = url;
  const layers = $$('.backdrop');
  if (layers.length < 2) return;
  const next = layers[artLayer = 1 - artLayer], prev = layers[1 - artLayer];
  if (url) {
    const img = new Image();
    img.onload = () => {
      if (artNow !== url) return;
      next.style.backgroundImage = `url("${url}")`;
      next.classList.add('on');
      prev.classList.remove('on');
    };
    img.src = url;
  } else {
    next.classList.remove('on');
    prev.classList.remove('on');
  }
}

export const fmtTime = (iso, locale) => new Date(iso).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });
export const fmtDate = (iso, locale) => new Date(iso).toLocaleDateString(locale, { day: 'numeric', month: 'short' });

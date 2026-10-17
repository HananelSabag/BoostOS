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
  const n = v => (v == null ? '—' : Math.round(v));
  // Lower is better for temperature and the busiest core; higher is better for GPU clock.
  const delta = (cur, before, lowerIsBetter, unit = '') => {
    if (cur == null || before == null) return '';
    const d = Math.round(cur - before);
    if (d === 0) return '';
    const better = lowerIsBetter ? d < 0 : d > 0;
    return `<span class="delta ${better ? 'better' : 'worse'}"><bdi>${d > 0 ? '+' : ''}${d}${unit}</bdi></span>`;
  };
  const cells = [
    [n(m.minutes), t('report.minutes'), ''],
    [`${n(m.gpuUtil)}%`, t('report.gpuUtil'), ''],
    [`${n(m.gpuTempMax)}°`, t('report.gpuTemp'), delta(m.gpuTempMax, prev?.gpuTempMax, true, '°')],
    [`${n(m.gpuClock)}`, `${t('report.gpuClock')} · MHz`, delta(m.gpuClock, prev?.gpuClock, false)],
    [`${n(m.gpuPower)}W`, t('report.power'), ''],
    [`${n(m.gpuLimitedPct)}%`, t('report.limited'), ''],
    [`${n(m.busiestCoreAvg)}%`, t('report.core'), delta(m.busiestCoreAvg, prev?.busiestCoreAvg, true, '%')],
  ];
  const lines = [t(m.bottleneck === 'cpu' ? 'report.cpu' : m.bottleneck === 'gpu' ? 'report.gpu' : 'report.none2')];
  if (m.gpuTempMax >= 85) lines.push(t('report.hot', { t: Math.round(m.gpuTempMax) }));

  box.innerHTML = `
    <h3>${t('report.title')}</h3>
    <p>${esc(fresh ? t('report.fresh') : fmtTime(m.start, locale()))} · ${m.gameMode ? t('report.withGame') : t('report.withoutGame')}${prev ? ` · ${t('report.compare')}` : ''}</p>
    <div class="report-grid">${cells.map(([b, s, d]) => `<div><b><bdi>${esc(b)}</bdi>${d}</b><span>${esc(s)}</span></div>`).join('')}</div>
    ${lines.map(l => `<p>${esc(l)}</p>`).join('')}`;
}

// Home: a short rail of tiles (Play, Game Mode, the Diagnostics folder, Settings) and a hub under the focused tile.

import { $, $$, esc, icon, gauge, setGauge, setAccent, aimLight, valueHtml, reduceMotion } from '../ui.js';
import { send } from '../bridge.js';
import { t } from '../i18n.js';
import { focusEl } from '../nav.js';
import { sfx } from '../sound.js';
import { toast } from '../ui.js';

const TOOLS = [
  { id: 'scan', icon: 'pulse' },
  { id: 'net', icon: 'wifi' },
  { id: 'pad', icon: 'gamepad' },
  { id: 'mouse', icon: 'mouse' },
];

let ctx;
let root;
let folderOpen = false;
let hubFor = null;

export default {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    folderOpen = false;
    hubFor = null;
    root.innerHTML = `
      <div class="rail" role="list">
        <button class="tile tile-play" data-nav data-default data-tile="play" role="listitem" style="--i:0">
          ${icon('ball')}<span class="tile-name">${t('tile.play')}<span class="tile-sub">${t('tile.play.sub')}</span></span>
        </button>
        <button class="tile tile-game" data-nav data-tile="game" role="listitem" style="--i:1">
          <span class="led"></span>${icon('rocket')}<span class="tile-name">${t('tile.game')}<span class="tile-sub" id="game-sub">${t('tile.game.off')}</span></span>
        </button>
        <button class="tile tile-folder" data-nav data-tile="diag" data-sfx="none" role="listitem" aria-expanded="false" style="--i:2">
          <span class="folder-glyphs">${TOOLS.map(x => `<span>${icon(x.icon)}</span>`).join('')}</span>
          <span class="tile-name">${t('tile.diag')}<span class="tile-sub">${t('tile.diag.sub')}</span></span>
        </button>
        <button class="tile tile-settings" data-nav data-tile="settings" role="listitem" style="--i:3">
          ${icon('gear')}<span class="tile-name">${t('tile.settings')}</span>
        </button>
      </div>
      <div class="hub">
        <div class="hub-main" id="hub-main"></div>
        <aside class="readiness">
          <div class="fix"><h2 id="fix-heading"></h2><ul class="fix-list" id="fix-list"></ul></div>
          <div id="home-gauge">${gauge(230, t('gauge.label'))}</div>
        </aside>
      </div>`;

    root.addEventListener('focusin', e => {
      const tile = e.target.closest('.tile');
      if (!tile) return;
      const id = tile.dataset.tile;
      if (folderOpen && id !== 'diag' && !tile.classList.contains('tile-tool')) closeFolder(false);
      setAccent(tile.classList.contains('tile-tool') ? 'tool' : id);
      aimLight(tile);
      renderHub(id);
    });
    root.addEventListener('click', e => {
      const tile = e.target.closest('.tile');
      if (tile) activate(tile.dataset.tile);
    });
    this.refresh();
  },

  /** Back on Home: close the folder first, like a console. */
  back() {
    if (!folderOpen) return false;
    closeFolder(true);
    return true;
  },

  focusTile(id) {
    if (TOOLS.some(x => x.id === id)) {
      if (!folderOpen) openFolder(false);
      focusEl($(`.tile[data-tile="${id}"]`, root));
    } else {
      focusEl($(`.tile[data-tile="${id === 'game' ? 'game' : id === 'settings' ? 'settings' : 'play'}"]`, root));
    }
  },

  hints: () => folderOpen ? [['select', t('hint.select')], ['back', t('hint.close')], ['full', t('hint.full')]] : [['select', t('hint.select')], ['full', t('hint.full')]],

  /** Called whenever app data changes (scan, Game Mode, matches). */
  refresh() {
    if (!root) return;
    const { report, scanning, game } = ctx.app;
    setGauge($('#home-gauge', root), scanning || !report ? null : report.score, scanning || !report ? t('gauge.scanning') : t('gauge.label'));

    const heading = $('#fix-heading', root), list = $('#fix-list', root);
    if (scanning || !report) {
      heading.textContent = t('hub.scanning');
      list.innerHTML = '';
    } else {
      const issues = issuesOf(report);
      heading.textContent = issues.length ? t('hub.fixTitle') : t('hub.allGood');
      list.innerHTML = issues.slice(0, 4).map(c =>
        `<li class="s-${c.status}"><span class="dot"></span><span><b>${esc(c.title)}</b> ${valueHtml(c.value)}</span></li>`).join('');
    }

    const on = !!game?.active;
    $('.tile-game .led', root)?.classList.toggle('on', on);
    const sub = $('#game-sub', root);
    if (sub) sub.textContent = on ? t('tile.game.on') : t('tile.game.off');
    if (hubFor) renderHub(hubFor, true);
  },
};

const issuesOf = r => r.sections.flatMap(s => s.checks).filter(c => c.status === 'bad' || c.status === 'warn')
  .sort((a, b) => (a.status === 'bad' ? 0 : 1) - (b.status === 'bad' ? 0 : 1));

function activate(id) {
  switch (id) {
    case 'play':
      send('play');
      toast(t('game.launching'));
      break;
    case 'diag':
      if (folderOpen) closeFolder(true); else openFolder(true);
      break;
    case 'game': case 'settings': case 'scan': case 'net': case 'pad': case 'mouse':
      ctx.show(id);
      break;
  }
}

function openFolder(focusFirstTool) {
  if (folderOpen) return;
  folderOpen = true;
  const folder = $('.tile-folder', root);
  folder.classList.add('open');
  folder.parentElement.classList.add('folder-open');
  folder.setAttribute('aria-expanded', 'true');
  folder.insertAdjacentHTML('afterend', TOOLS.map((x, i) => `
    <button class="tile tile-tool unfold" data-nav data-tile="${x.id}" role="listitem" style="--i:${i}">
      ${icon(x.icon)}<span class="tile-name">${t(`tool.${x.id}`)}</span>
    </button>`).join(''));
  sfx.open();
  ctx.renderHints();
  if (focusFirstTool) focusEl($('.tile-tool', root));
}

function closeFolder(refocus) {
  if (!folderOpen) return;
  folderOpen = false;
  const folder = $('.tile-folder', root);
  folder.classList.remove('open');
  folder.parentElement.classList.remove('folder-open');
  folder.setAttribute('aria-expanded', 'false');
  const tools = $$('.tile-tool', root);
  if (refocus) focusEl(folder);
  sfx.close();
  ctx.renderHints();
  if (reduceMotion) { tools.forEach(x => x.remove()); return; }
  tools.forEach(x => { x.classList.remove('unfold'); x.classList.add('fold'); x.disabled = true; });
  setTimeout(() => tools.forEach(x => x.remove()), 200);
}

/** The hub under the rail: what the focused tile does, plus the facts that matter for it. */
function renderHub(id, quiet = false) {
  const main = $('#hub-main', root);
  if (!main) return;
  const { report, game, matches } = ctx.app;
  const facts = [];
  let title = '', text = '';

  if (id === 'play') {
    title = t('tile.play');
    const store = report?.sections.find(s => s.id === 'rocketleague')?.checks[0]?.value?.split(' · ')[0] ?? 'Epic Games';
    text = t('hub.play', { store });
    const m = matches?.[0];
    if (m) {
      facts.push([Math.round(m.minutes), `${t('hub.lastMatch')} · ${t('hub.minutes')}`]);
      if (m.gpuTempMax != null) facts.push([`${Math.round(m.gpuTempMax)}°`, t('hub.gpuMax')]);
      facts.push([t(`bottleneck.${m.bottleneck}`), t('hub.limit')]);
    }
  } else if (id === 'game') {
    title = t('tile.game');
    text = t('hub.game');
    facts.push([game?.active ? t('game.on') : t('game.off'), t('tile.game')]);
    const close = game?.plan?.find(p => p.willRun)?.detail?.split('.')[0];
    if (close && !game?.active) facts.push([close, t('hub.willClose')]);
  } else if (id === 'diag') {
    title = t('tile.diag');
    text = t('hub.diag');
    if (report) {
      const issues = issuesOf(report);
      facts.push([report.score, t('gauge.label')]);
      const fixable = issues.filter(c => c.fix).length;
      if (fixable) facts.push([fixable, t('hub.fixable', { n: fixable })]);
    }
  } else if (id === 'settings') {
    title = t('tile.settings');
    text = t('hub.settings');
    facts.push([ctx.app.settings.language === 'en' ? 'English' : 'עברית', t('hub.lang')]);
  } else {
    title = t(`tool.${id}`);
    text = t(`hub.${id}`);
  }

  main.innerHTML = `
    <h1>${esc(title)}</h1>
    <p class="hub-text">${esc(text)}</p>
    ${facts.length ? `<div class="hub-facts">${facts.map(([b, s]) => `<div><b><bdi>${esc(b)}</bdi></b><span>${esc(s)}</span></div>`).join('')}</div>` : ''}`;
  if (!quiet && hubFor !== id && !reduceMotion) {
    main.classList.remove('swap');
    void main.offsetWidth; // restart the animation
    main.classList.add('swap');
  }
  hubFor = id;
}

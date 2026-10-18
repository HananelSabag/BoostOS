// Home: the favourite game up front, the Games folder (its cards unfold in a row underneath), Diagnostics and
// Settings. Under them a hub explains the focused tile, next to the readiness gauge.

import { $, esc, icon, gauge, setGauge, setAccent, aimLight, setBackdrop, valueHtml, reduceMotion, gameArt, artUrl, finite, toast } from '../ui.js';
import { send } from '../bridge.js';
import { t } from '../i18n.js';
import { focusEl } from '../nav.js';
import { sfx } from '../sound.js';
import { openDialog } from '../chrome.js';

let ctx;
let root;
let shelfOpen = false;
let hubFor = null;

export const favoriteOf = app => app.games.find(g => g.id === app.settings.favoriteGame)
  ?? app.games.find(g => g.isRocketLeague) ?? app.games[0] ?? null;

const screen = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    shelfOpen = false;
    hubFor = null;
    root.innerHTML = `
      <div class="rail">
        <button class="tile tile-fav" data-nav data-default data-tile="fav" style="--i:0"></button>
        <button class="tile tile-folder" data-nav data-tile="games" data-sfx="none" aria-expanded="false" aria-controls="shelf" style="--i:1"></button>
        <button class="tile" data-nav data-tile="diag" style="--i:2">
          ${icon('pulse')}<span class="tile-name">${t('tile.diag')}<span class="tile-sub" id="diag-sub"></span></span>
        </button>
        <button class="tile" data-nav data-tile="settings" style="--i:3">
          ${icon('gear')}<span class="tile-name">${t('tile.settings')}</span>
        </button>
      </div>
      <div class="shelf" id="shelf" hidden></div>
      <div class="hub">
        <div class="hub-main" id="hub-main"></div>
        <aside class="readiness">
          <div class="fix"><h2 id="fix-heading"></h2><ul class="fix-list" id="fix-list"></ul></div>
          <div id="home-gauge">${gauge(200, t('gauge.label'))}</div>
        </aside>
      </div>`;

    // build runs again after a language change; replace delegated handlers instead of stacking them.
    root.removeEventListener('focusin', handleFocus);
    root.addEventListener('focusin', handleFocus);
    root.onclick = e => {
      const tile = e.target.closest('[data-tile]');
      if (tile) activate(tile, e.detail === 0);
    };
    $('#hub-main', root).onanimationend = e => e.currentTarget.classList.remove('swap');
    this.refresh();
  },

  enter() { setBackdrop(favoriteOf(ctx.app)?.wide); },
  leave() { setBackdrop(null); },

  /** Back on Home closes the games row first, like a console. */
  back() {
    if (!shelfOpen) return false;
    closeShelf(true);
    return true;
  },

  /** △ / Y / O on a game: its options. */
  options() {
    const id = document.activeElement?.closest?.('[data-game]')?.dataset.game;
    const game = ctx.app.games.find(g => g.id === id);
    if (game) gameOptions(game);
  },

  focusTile(id) {
    focusEl($(`[data-tile="${['diag', 'settings', 'games'].includes(id) ? id : 'fav'}"]`, root));
  },

  hints() {
    const el = document.activeElement;
    const onGame = !!el?.closest?.('[data-game]');
    const list = [['select', onGame ? t('hint.play') : el?.dataset?.tile === 'games' ? (shelfOpen ? t('hint.close') : t('hint.open')) : t('hint.select')]];
    if (onGame) list.push(['options', t('hint.options')]);
    if (shelfOpen) list.push(['back', t('hint.close')]);
    list.push(['full', t('hint.full')]);
    return list;
  },

  /** Called whenever app data changes (scan, games, Game Mode, matches). */
  refresh() {
    if (!root) return;
    const { report, scanning, games } = ctx.app;
    renderFavorite();
    renderFolder();
    if (shelfOpen) renderShelf();

    setGauge($('#home-gauge', root), scanning || !report ? null : report.score, scanning || !report ? t('gauge.scanning') : t('gauge.label'));
    const issues = report ? issuesOf(report) : [];
    $('#diag-sub', root).textContent = scanning || !report ? t('hub.scanning') : issues.length ? t('tile.diag.issues', { n: issues.length }) : t('tile.diag.ok');

    const heading = $('#fix-heading', root), list = $('#fix-list', root);
    if (scanning || !report) {
      heading.textContent = t('hub.scanning');
      list.innerHTML = '';
    } else {
      heading.textContent = issues.length ? t('hub.fixTitle') : t('hub.allGood');
      list.innerHTML = issues.slice(0, 3).map(c =>
        `<li class="s-${c.status}"><span class="dot"></span><span><b>${esc(c.title)}</b> ${valueHtml(c.value)}</span></li>`).join('');
    }
    // The games can arrive after the focus has settled: show the focused game's picture (or the favourite's).
    if (ctx.app.current === 'home') {
      const focusedId = document.activeElement?.closest?.('[data-game]')?.dataset.game;
      setBackdrop((games.find(g => g.id === focusedId) ?? favoriteOf(ctx.app))?.wide);
    }
    if (hubFor) renderHub(hubFor, true);
  },
};

export default screen;

export const issuesOf = r => r.sections.flatMap(s => s.checks).filter(c => c.status === 'bad' || c.status === 'warn')
  .sort((a, b) => (a.status === 'bad' ? 0 : 1) - (b.status === 'bad' ? 0 : 1));

function renderFavorite() {
  const fav = favoriteOf(ctx.app);
  const tile = $('.tile-fav', root);
  const key = fav ? `${fav.id}|${fav.wide}|${ctx.app.running}` : 'none';
  if (tile.dataset.key === key) return;
  tile.dataset.key = key;
  if (fav) tile.dataset.game = fav.id; else delete tile.dataset.game;
  const running = fav && ctx.app.running === fav.id;
  tile.innerHTML = `<span class="tile-art">${gameArt(fav)}</span>
    <span class="tile-name">${esc(fav?.name ?? t('tile.noGames'))}<span class="tile-sub">${running ? t('tile.running') : fav ? `${t('tile.play')} · ${esc(fav.store)}` : t('tile.noGamesSub')}</span></span>
    <span class="play-badge" aria-hidden="true">${icon('play')}</span>`;
}

function renderFolder() {
  const { games } = ctx.app;
  const tile = $('.tile-folder', root);
  const covers = games.filter(g => g.wide).slice(0, 3);
  const key = `${covers.map(g => g.id).join()}|${games.length}|${shelfOpen}`;
  if (tile.dataset.key === key) return;
  tile.dataset.key = key;
  tile.classList.toggle('open', shelfOpen);
  tile.setAttribute('aria-expanded', String(shelfOpen));
  tile.innerHTML = `<span class="folder-stack">${covers.length
      ? covers.map((g, i) => `<span style="--k:${i}"><img src="${artUrl(g.wide)}" alt="" loading="lazy"></span>`).join('')
      : `<span class="folder-empty">${icon('grid')}</span>`}</span>
    <span class="tile-name">${t('tile.games')}<span class="tile-sub">${games.length ? t('tile.games.sub', { n: games.length }) : t('tile.games.none')}</span></span>`;
}

function renderShelf() {
  const shelf = $('#shelf', root);
  const { games, settings } = ctx.app;
  const fav = favoriteOf(ctx.app);
  // Redraw only when something changed, and keep the focus on the same game when it does.
  const key = `${games.map(g => `${g.id}:${g.wide}`).join()}|${fav?.id}|${ctx.app.running}`;
  if (shelf.dataset.key === key) return;
  shelf.dataset.key = key;
  const focused = document.activeElement?.closest?.('.game-card')?.dataset.game;
  shelf.innerHTML = games.length ? games.map((g, i) => `
    <button class="game-card" data-nav data-tile="game" data-game="${esc(g.id)}" style="--i:${i}" aria-label="${esc(g.name)}">
      <span class="card-art">${gameArt(g)}</span>
      ${g.id === fav?.id ? `<span class="fav-badge" title="${esc(t('games.favorite'))}">${icon('star')}</span>` : ''}
      ${ctx.app.running === g.id ? `<span class="run-badge">${t('tile.running')}</span>` : ''}
    </button>`).join('')
    : `<p class="shelf-empty">${t('games.empty')}</p>`;
  shelf.dataset.fav = settings.favoriteGame ?? '';
  if (focused) $(`.game-card[data-game="${CSS.escape(focused)}"]`, shelf)?.focus({ preventScroll: true });
}

function handleFocus(e) {
  const tile = e.target.closest('[data-tile]');
  if (!tile) return;
  const id = tile.dataset.tile;
  const gameId = tile.dataset.game;
  setAccent(id === 'fav' || id === 'game' || id === 'games' ? 'play' : id);
  if (id !== 'game') aimLight(tile);
  const game = ctx.app.games.find(g => g.id === gameId);
  setBackdrop((game ?? favoriteOf(ctx.app))?.wide);
  renderHub(id === 'game' ? `game:${gameId}` : id);
  ctx.renderHints();
}

function activate(tile, viaPad) {
  const id = tile.dataset.tile;
  // Collapsing on focus moves the target between pointer-down and click, cancelling mouse activation.
  if (shelfOpen && id !== 'games' && id !== 'game') closeShelf(false);
  switch (id) {
    case 'fav': case 'game':
      play(tile.dataset.game);
      break;
    case 'games':
      if (shelfOpen) closeShelf(true); else openShelf(viaPad);
      break;
    case 'diag': case 'settings':
      ctx.show(id);
      break;
  }
}

function play(id) {
  const game = ctx.app.games.find(g => g.id === id);
  send('play', id ? { id } : {});
  toast(ctx.app.settings.gameModeOnPlay ? t('game.launching', { name: game?.name ?? t('tile.play') }) : t('game.launchingPlain', { name: game?.name ?? '' }));
}

async function gameOptions(game) {
  const isFav = favoriteOf(ctx.app)?.id === game.id;
  const actions = [
    { id: 'play', label: t('games.play'), icon: 'play', primary: true },
    { id: 'launch', label: ctx.app.settings.gameModeOnPlay ? t('games.playPlain') : t('games.playGm'), icon: 'rocket' },
  ];
  if (!isFav) actions.push({ id: 'fav', label: t('games.makeFavorite'), icon: 'star' });
  if (game.isRocketLeague) actions.push({ id: 'video', label: t('games.video'), icon: 'sliders' });
  const choice = await openDialog({ title: game.name, text: `${game.store}`, actions, focus: 'play' });
  if (choice === 'play') play(game.id);
  else if (choice === 'launch') {
    // The opposite of the Settings default, for this one start.
    send(ctx.app.settings.gameModeOnPlay ? 'launch' : 'play', { id: game.id });
    toast(t('game.launchingPlain', { name: game.name }));
  } else if (choice === 'fav') {
    ctx.saveSettings({ ...ctx.app.settings, favoriteGame: game.id });
    sfx.on();
    toast(t('games.favoriteSet', { name: game.name }));
  } else if (choice === 'video') ctx.show('rlvideo', { from: 'home' });
}

function openShelf(focusFirstCard) {
  if (shelfOpen) return;
  shelfOpen = true;
  send('games');
  const shelf = $('#shelf', root);
  renderShelf();
  renderFolder();
  shelf.hidden = false;
  root.classList.add('shelf-open');
  if (!reduceMotion) { shelf.classList.remove('unfold'); void shelf.offsetWidth; shelf.classList.add('unfold'); }
  sfx.open();
  ctx.renderHints();
  if (focusFirstCard) focusEl($('.game-card', shelf));
}

function closeShelf(refocus) {
  if (!shelfOpen) return;
  shelfOpen = false;
  const shelf = $('#shelf', root);
  shelf.hidden = true;
  shelf.classList.remove('unfold');
  root.classList.remove('shelf-open');
  renderFolder();
  if (refocus) focusEl($('.tile-folder', root));
  sfx.close();
  ctx.renderHints();
}

/** The hub under the rail: what the focused tile does, plus the facts that matter for it. */
function renderHub(id, quiet = false) {
  const main = $('#hub-main', root);
  if (!main) return;
  const { report, matches, games } = ctx.app;
  const facts = [];
  let title = '', text = '';

  if (id === 'fav' || id.startsWith('game:')) {
    const game = id === 'fav' ? favoriteOf(ctx.app) : games.find(g => `game:${g.id}` === id);
    title = game?.name ?? t('tile.noGames');
    text = !game ? t('hub.noGames')
      : ctx.app.settings.gameModeOnPlay ? t('hub.play', { store: game.store }) : t('hub.playPlain', { store: game.store });
    const m = matches.find(x => x.gameId ? x.gameId === game?.id : game?.isRocketLeague);
    if (m) {
      facts.push([Math.round(m.minutes), t('hub.lastMinutes')]);
      if (finite(m.gpuTempMax)) facts.push([`${Math.round(m.gpuTempMax)}°`, t('hub.gpuMax')]);
      if (m.router && finite(m.router.medianMs)) facts.push([`${Math.round(m.router.medianMs)} ms`, t('hub.wifiPing')]);
    }
    if (game && favoriteOf(ctx.app)?.id === game.id && id !== 'fav') facts.push(['★', t('games.favorite')]);
  } else if (id === 'games') {
    title = t('tile.games');
    text = games.length ? t('hub.games', { n: games.length }) : t('games.empty');
    const stores = [...new Set(games.map(g => g.store))];
    if (stores.length) facts.push([stores.join(' · '), t('hub.stores')]);
  } else if (id === 'diag') {
    title = t('tile.diag');
    text = t('hub.diag');
    if (report) {
      const issues = issuesOf(report);
      facts.push([report.score, t('gauge.label')]);
      facts.push([issues.length, t('hub.issues')]);
      if (matches.length) facts.push([matches.length, t('hub.sessions')]);
    }
  } else if (id === 'settings') {
    title = t('tile.settings');
    text = t('hub.settings');
    facts.push([ctx.app.settings.language === 'en' ? 'English' : 'עברית', t('hub.lang')]);
  }

  main.innerHTML = `
    <h1>${esc(title)}</h1>
    <p class="hub-text">${esc(text)}</p>
    ${facts.length ? `<div class="hub-facts">${facts.map(([b, s]) => `<div><b><bdi>${esc(b)}</bdi></b><span>${esc(s)}</span></div>`).join('')}</div>` : ''}`;
  if (!quiet && hubFor !== id && !reduceMotion) main.classList.add('swap');
  hubFor = id;
}

export { gameOptions };

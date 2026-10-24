// Settings: deliberately short. Language, favourite game, Game Mode on start, sounds, rumble, full screen,
// session reports, undo fixes, exit, about.

import { $, $$, esc, icon, toast } from '../ui.js';
import { t } from '../i18n.js';
import { sfx } from '../sound.js';
import { openDialog, powerMenu } from '../chrome.js';
import { undoAll, fixBusy } from '../panels/fixrun.js';
import { favoriteOf } from './home.js';

let ctx;
let root;

const TOGGLES = [
  ['gameModeOnPlay', 'settings.gmOnPlay', 'settings.gmOnPlaySub'],
  ['sounds', 'settings.sounds', 'settings.soundsSub'],
  ['rumble', 'settings.rumble', 'settings.rumbleSub'],
  ['startFullscreen', 'settings.fullscreen', 'settings.fullscreenSub'],
  ['trackMatches', 'settings.track', 'settings.trackSub'],
  ['backgroundRecorder', 'settings.recorder', 'settings.recorderSub'],
];

const screen = {
  init(c) { ctx = c; },

  build(el) {
    root = el;
    const s = ctx.app.settings;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('settings.title')}</h2>
      </div>
      <div class="settings-body">
        <div class="card rows">
          <button class="row" data-nav data-default data-key="language" data-sfx="none">
            <span><b>${t('settings.lang')}</b><small>${t('settings.langSub')}</small></span>
            <span class="choice"><span class="${s.language === 'he' ? 'on' : ''}">עברית</span><span class="${s.language === 'en' ? 'on' : ''}">English</span></span>
          </button>
          <button class="row" data-nav id="fav-row">
            <span><b>${t('settings.favorite')}</b><small>${t('settings.favoriteSub')}</small></span>
            <span class="row-value" id="fav-value"></span>
          </button>
          ${TOGGLES.map(([key, title, sub]) => `
            <button class="row" data-nav data-key="${key}" role="switch" aria-checked="${!!s[key]}" data-sfx="none">
              <span><b>${t(title)}</b><small>${t(sub)}</small></span>
              <span class="switch ${s[key] ? 'on' : ''}" aria-hidden="true"></span>
            </button>`).join('')}
          <button class="row" data-nav id="undo-row" data-sfx="none">
            <span><b>${t('settings.fixes')}</b><small id="fixes-sub"></small></span>
            <span class="btn small" id="undo-label">${icon('undo')}${t('settings.undo')}</span>
          </button>
          <button class="row" data-nav id="exit-row">
            <span><b>${t('settings.exit')}</b><small>${t('settings.exitSub')}</small></span>
            ${icon('power')}
          </button>
        </div>
        <section class="card about">
          <h3>BoostOS</h3>
          <p>${t('settings.about')}</p>
          <p class="by">Made by Hananel Sabag</p>
          <p class="ver"><bdi>v${esc(ctx.app.version ?? '')}</bdi> · <bdi>github.com/HananelSabag/BoostOS</bdi></p>
        </section>
      </div>`;

    $('.back', root).addEventListener('click', () => ctx.back());
    $$('[data-key]', root).forEach(row => row.addEventListener('click', () => {
      const key = row.dataset.key;
      const next = { ...ctx.app.settings };
      if (key === 'language') next.language = next.language === 'he' ? 'en' : 'he';
      else next[key] = !next[key];
      (key === 'language' || next[key] ? sfx.on : sfx.off)();
      ctx.saveSettings(next);
    }));
    $('#fav-row', root).addEventListener('click', chooseFavorite);
    $('#undo-row', root).addEventListener('click', () => {
      if (!ctx.app.fixesApplied || fixBusy()) return;
      undoAll(null);
      toast(t('fix.undoing'));
      this.refresh();
    });
    $('#exit-row', root).addEventListener('click', powerMenu);
    this.refresh();
  },

  hints: () => [['select', t('hint.select')], ['back', t('hint.back')], ['full', t('hint.full')]],

  refresh() {
    if (!root) return;
    const n = ctx.app.fixesApplied ?? 0;
    $('#fixes-sub', root).textContent = n ? t('settings.fixesSub', { n }) : t('settings.fixesNone');
    $('#undo-row', root).disabled = n === 0 || fixBusy();
    $('#undo-label', root).style.visibility = n ? 'visible' : 'hidden';
    $('#fav-value', root).textContent = favoriteOf(ctx.app)?.name ?? '—';
    $('#fav-row', root).disabled = ctx.app.games.length < 2;
    const s = ctx.app.settings;
    $$('[role="switch"]', root).forEach(row => {
      const on = !!s[row.dataset.key];
      row.setAttribute('aria-checked', String(on));
      row.querySelector('.switch').classList.toggle('on', on);
    });
  },
};

export default screen;

async function chooseFavorite() {
  const { games } = ctx.app;
  if (games.length < 2) return;
  const fav = favoriteOf(ctx.app);
  const id = await openDialog({
    title: t('settings.favorite'),
    actions: games.slice(0, 8).map(g => ({ id: g.id, label: g.name, icon: g.id === fav?.id ? 'star' : 'gamepad' })),
    focus: fav?.id,
  });
  if (!id || id === fav?.id) return;
  ctx.saveSettings({ ...ctx.app.settings, favoriteGame: id });
  sfx.on();
  toast(t('games.favoriteSet', { name: games.find(g => g.id === id)?.name ?? '' }));
}

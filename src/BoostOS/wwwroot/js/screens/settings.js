// Settings: deliberately short. Language, sounds, rumble, full screen, match reports, undo fixes, about.

import { $, $$, esc, icon, toast } from '../ui.js';
import { send, on } from '../bridge.js';
import { t } from '../i18n.js';
import { sfx } from '../sound.js';

let ctx;
let root;
let undoing = false;

const TOGGLES = [
  ['sounds', 'settings.sounds', 'settings.soundsSub'],
  ['rumble', 'settings.rumble', 'settings.rumbleSub'],
  ['startFullscreen', 'settings.fullscreen', 'settings.fullscreenSub'],
  ['trackMatches', 'settings.track', 'settings.trackSub'],
];

const screen = {
  init(c) {
    ctx = c;
    on('fixResult', ({ outcome, applied }) => {
      if (!undoing) return;
      undoing = false;
      ctx.app.fixesApplied = applied;
      const failed = outcome?.failed ?? [];
      if (failed.length) { sfx.error(); toast(t('scan.fixFailed', { msg: failed[0].message })); }
      else toast(t('settings.undone'));
      screen.refresh();
      ctx.startScan();
    });
    on('error', e => {
      if (e.cmd !== 'undoAllFixes') return;
      undoing = false;
      screen.refresh();
    });
  },

  build(el) {
    root = el;
    const s = ctx.app.settings;
    root.innerHTML = `
      <div class="screen-head">
        <button class="back" data-nav aria-label="${t('hint.back')}" data-sfx="none">${icon('back')}</button>
        <h2>${t('settings.title')}</h2>
      </div>
      <div class="settings-body">
        <div class="rows">
          <button class="row" data-nav data-default data-key="language" data-sfx="none">
            <span><b>${t('settings.lang')}</b><small>${t('settings.langSub')}</small></span>
            <span class="choice"><span class="${s.language === 'he' ? 'on' : ''}">עברית</span><span class="${s.language === 'en' ? 'on' : ''}">English</span></span>
          </button>
          ${TOGGLES.map(([key, title, sub]) => `
            <button class="row" data-nav data-key="${key}" role="switch" aria-checked="${!!s[key]}" data-sfx="none">
              <span><b>${t(title)}</b><small>${t(sub)}</small></span>
              <span class="switch ${s[key] ? 'on' : ''}" aria-hidden="true"></span>
            </button>`).join('')}
          <button class="row" data-nav id="undo-row">
            <span><b>${t('settings.fixes')}</b><small id="fixes-sub"></small></span>
            <span class="btn" id="undo-label">${t('settings.undo')}</span>
          </button>
          <button class="row" data-nav id="exit-row">
            <span><b>${t('settings.exit')}</b></span>
            ${icon('power')}
          </button>
        </div>
        <section class="panel about">
          <h3>BoostOS</h3>
          <p>${t('settings.about')}</p>
          <p class="by">Made by Hananel Sabag</p>
          <p><bdi>v${esc(ctx.app.version ?? '')}</bdi> · <bdi>github.com/HananelSabag/BoostOS</bdi></p>
        </section>
      </div>`;

    $('.back', root).addEventListener('click', () => ctx.back());
    $$('[data-key]', root).forEach(row => row.addEventListener('click', () => {
      const key = row.dataset.key;
      const next = { ...ctx.app.settings };
      if (key === 'language') next.language = next.language === 'he' ? 'en' : 'he';
      else next[key] = !next[key];
      (key === 'language' || next[key] ? sfx.on : sfx.off)();
      ctx.saveSettings(next, key);
    }));
    $('#undo-row', root).addEventListener('click', () => {
      if (!ctx.app.fixesApplied || undoing) return;
      undoing = true;
      this.refresh();
      send('undoAllFixes');
    });
    $('#exit-row', root).addEventListener('click', () => send('exit'));
    this.refresh();
  },

  hints: () => [['select', t('hint.select')], ['back', t('hint.back')], ['full', t('hint.full')]],

  refresh() {
    if (!root) return;
    const n = ctx.app.fixesApplied ?? 0;
    $('#fixes-sub', root).textContent = n ? t('settings.fixesSub', { n }) : t('settings.fixesNone');
    $('#undo-row', root).disabled = n === 0 || undoing;
    $('#undo-label', root).style.visibility = n ? 'visible' : 'hidden';
    const s = ctx.app.settings;
    $$('[role="switch"]', root).forEach(row => {
      const on = !!s[row.dataset.key];
      row.setAttribute('aria-checked', String(on));
      row.querySelector('.switch').classList.toggle('on', on);
    });
  },
};

export default screen;

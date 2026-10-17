// Messages to and from the C# host. In a plain browser (design preview) a small fake host answers instead,
// from sample files in wwwroot/dev (created with BoostOS.exe --scan-json / --games-json; not shipped).

const host = window.chrome?.webview;
const listeners = {};

export const on = (type, fn) => (listeners[type] ??= []).push(fn);
export const emit = (type, data) => (listeners[type] ?? []).forEach(fn => fn(data));
export const inHost = !!host;

host?.addEventListener('message', e => emit(e.data.type, e.data.data));

export function send(cmd, extra = {}) {
  if (host) host.postMessage({ cmd, ...extra });
  else Preview.handle(cmd, extra);
}

const json = async (path, fallback) => {
  try {
    const r = await fetch(path, { cache: 'no-store' });
    return r.ok ? await r.json() : fallback;
  } catch { return fallback; }
};

const Preview = {
  timers: [],
  settings: { language: new URLSearchParams(location.search).get('lang') ?? 'he', sounds: true, rumble: true, startFullscreen: true, trackMatches: true, gameModeOnPlay: true, favoriteGame: null },
  gameMode: { active: false, plan: [{ title: 'סגירת תוכנות רקע', detail: 'OneDrive, Phone Link', willRun: true }], steps: [] },
  video: null,
  async handle(cmd, extra) {
    const later = (type, data, ms = 300) => setTimeout(() => emit(type, data), ms);
    switch (cmd) {
      case 'hello':
        later('hello', { settings: this.settings, version: 'preview', matches: await json('dev/matches.sample.json', []), gameMode: this.gameMode, fixesApplied: 1 }, 0);
        later('games', (await json('dev/games.sample.json', { games: [] })).games, 50);
        break;
      case 'games': later('games', (await json('dev/games.sample.json', { games: [] })).games, 50); break;
      case 'saveSettings':
        this.settings = extra.settings;
        later('settings', this.settings, 0);
        break;
      case 'scan':
        later('scan', await json('dev/scan.sample.json', { generatedAt: new Date().toISOString(), score: 0, machine: 'Preview', sections: [] }), 700);
        break;
      case 'fixesApplied': later('fixesApplied', [{ id: 'wifi-roaming', at: new Date().toISOString(), before: '1' }], 50); break;
      case 'netTest': this.fakeNet(extra.seconds); break;
      case 'netCancel': this.stop(); break;
      case 'gameModeStatus': later('gameMode', this.gameMode); break;
      case 'gameModeOn': case 'gameModeOff':
        this.gameMode = cmd === 'gameModeOn'
          ? { active: true, since: new Date().toISOString(), plan: this.gameMode.plan, steps: [{ title: 'OneDrive', detail: 'נסגר', result: 'done' }, { title: 'מצב חשמל', detail: 'עבר ל-Best performance', result: 'done' }] }
          : { active: false, plan: this.gameMode.plan, steps: [] };
        later('gameMode', this.gameMode, 600);
        break;
      case 'matches': later('matches', await json('dev/matches.sample.json', [])); break;
      case 'rlVideo': {
        this.video ??= (await json('dev/games.sample.json', {})).video ?? null;
        later('rlVideo', this.video);
        break;
      }
      case 'rlVideoSet':
        for (const [id, v] of Object.entries(extra.changes)) { const s = this.video.settings.find(x => x.id === id); if (s) s.value = v; }
        this.video.changed = true;
        later('rlVideo', this.video, 400); later('rlVideoSaved', { undo: false }, 420);
        break;
      case 'fix': case 'undoFix': case 'undoAllFixes':
        later('fixResult', { outcome: { done: extra.ids ?? [], failed: [] }, applied: 0 }, 600);
        break;
      case 'play': case 'launch': later('launch', { ok: true, name: 'Rocket League' }); break;
      default: emit('preview', cmd);
    }
  },
  stop() { this.timers.forEach(clearInterval); this.timers = []; },
  fakeNet(secs) {
    this.stop();
    const t0 = performance.now();
    const elapsed = () => (performance.now() - t0) / 1000;
    let i = 0, j = 0;
    this.timers.push(setInterval(() => {
      const t = elapsed();
      if (t > secs) {
        this.stop();
        emit('netResult', {
          seconds: secs, status: 'ok', headline: 'Preview',
          targets: [{ id: 'router', address: '192.168.1.1', median: 3.1, jitter: 1.4, lossPct: 0, max: 14, spikeEvents: 0 },
                    { id: 'internet', address: '1.1.1.1', median: 6.5, jitter: 1.9, lossPct: 0, max: 15, spikeEvents: 0 }],
          findings: [{ status: 'ok', text: 'Preview data' }],
        });
        return;
      }
      emit('netSample', { target: 'router', index: i++, t, ms: 2.2 + Math.random() * 2.4 });
      emit('netSample', { target: 'internet', index: j++, t, ms: 5 + Math.random() * 4 });
    }, 400));
  },
};

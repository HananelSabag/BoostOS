// Messages to and from the C# host. In a plain browser (design preview) a small fake host answers instead.

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

const Preview = {
  timers: [],
  settings: { language: 'he', sounds: true, rumble: true, startFullscreen: true, trackMatches: true },
  async handle(cmd, extra) {
    const later = (type, data, ms = 300) => setTimeout(() => emit(type, data), ms);
    switch (cmd) {
      case 'hello':
        later('hello', { settings: this.settings, version: 'preview', matches: [], gameMode: { active: false, plan: [], steps: [] }, fixesApplied: 0 }, 0);
        break;
      case 'saveSettings':
        this.settings = extra.settings;
        later('settings', this.settings, 0);
        break;
      case 'scan': {
        const report = await fetch('dev/scan.sample.json').then(r => (r.ok ? r.json() : null)).catch(() => null);
        later('scan', report ?? { generatedAt: new Date().toISOString(), score: 0, machine: 'Preview', sections: [] }, 700);
        break;
      }
      case 'netTest': this.fakeNet(extra.seconds); break;
      case 'netCancel': this.stop(); break;
      case 'gameModeStatus': case 'gameModeOn': case 'gameModeOff':
        later('gameMode', { active: cmd === 'gameModeOn', since: new Date().toISOString(), plan: [{ title: 'Background apps', detail: 'OneDrive, Phone Link', willRun: true }], steps: [{ title: 'OneDrive', detail: 'Closed', result: 'done' }] });
        break;
      case 'matches': later('matches', []); break;
      case 'fix': case 'undoFix': case 'undoAllFixes':
        later('fixResult', { outcome: { done: extra.ids ?? [], failed: [] }, applied: 0 }, 600);
        break;
      case 'play': case 'launchRocketLeague': later('launch', { ok: true }); break;
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

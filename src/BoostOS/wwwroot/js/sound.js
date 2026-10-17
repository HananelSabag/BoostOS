// Interface sounds, synthesised with Web Audio (no audio files), and a light controller rumble.
// Both are tiny, both can be turned off in Settings, and the audio engine is suspended whenever the page is hidden.

let ctx = null;
let master = null;
let soundsOn = true;
let rumbleOn = true;
let lastPad = null;

export function configure({ sounds, rumble }) {
  soundsOn = sounds;
  rumbleOn = rumble;
  if (!soundsOn) ctx?.suspend().catch(() => {});
}

export const rememberPad = pad => { lastPad = pad; };

function audio() {
  if (!soundsOn) return null;
  if (!ctx) {
    ctx = new AudioContext({ latencyHint: 'interactive' });
    master = ctx.createGain();
    master.gain.value = 0.9;
    master.connect(ctx.destination);
  }
  if (ctx.state === 'suspended') ctx.resume().catch(() => {});
  return ctx;
}

function tone(from, to, dur, { type = 'sine', gain = 0.04, delay = 0 } = {}) {
  const a = audio();
  if (!a) return;
  const t = a.currentTime + delay;
  const o = a.createOscillator();
  const g = a.createGain();
  o.type = type;
  o.frequency.setValueAtTime(from, t);
  o.frequency.exponentialRampToValueAtTime(to, t + dur);
  g.gain.setValueAtTime(0.0001, t);
  g.gain.exponentialRampToValueAtTime(gain, t + 0.008);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  o.connect(g).connect(master);
  o.start(t);
  o.stop(t + dur + 0.03);
  o.onended = () => { o.disconnect(); g.disconnect(); };
  // A running AudioContext keeps the audio thread busy even in silence, so put it to sleep between sounds.
  clearTimeout(idleTimer);
  idleTimer = setTimeout(() => ctx?.suspend().catch(() => {}), 2500);
}

let idleTimer = 0;

function rumble(ms, weak, strong = 0) {
  if (!rumbleOn || !lastPad?.vibrationActuator) return;
  try {
    lastPad.vibrationActuator.playEffect('dual-rumble', { duration: ms, weakMagnitude: weak, strongMagnitude: strong })?.catch(() => {});
  } catch { /* not every controller supports it */ }
}

export const sfx = {
  move: () => tone(1700, 1450, 0.04, { gain: 0.03 }),
  select: () => { tone(660, 990, 0.08, { type: 'triangle', gain: 0.045 }); tone(990, 1320, 0.09, { type: 'triangle', gain: 0.03, delay: 0.05 }); rumble(45, 0.45); },
  back: () => tone(880, 560, 0.08, { type: 'triangle', gain: 0.035 }),
  open: () => { [520, 780, 1040].forEach((f, i) => tone(f, f * 1.01, 0.18, { type: 'sine', gain: 0.025, delay: i * 0.035 })); rumble(30, 0.3); },
  close: () => { [1040, 780].forEach((f, i) => tone(f, f * 0.98, 0.1, { gain: 0.022, delay: i * 0.03 })); },
  on: () => { tone(740, 1110, 0.1, { type: 'triangle', gain: 0.04 }); rumble(60, 0.5, 0.15); },
  off: () => tone(1110, 620, 0.12, { type: 'triangle', gain: 0.035 }),
  error: () => { tone(300, 260, 0.09, { type: 'square', gain: 0.02 }); tone(300, 240, 0.12, { type: 'square', gain: 0.02, delay: 0.11 }); },
  // The boot chord: three notes rising with the boost meter.
  boot: () => { [392, 523, 784].forEach((f, i) => tone(f, f * 1.005, 0.7, { gain: 0.03, delay: i * 0.12 })); rumble(120, 0.25, 0.1); },
};

document.addEventListener('visibilitychange', () => {
  if (!ctx) return;
  if (document.hidden) ctx.suspend().catch(() => {});
  // Resume only when the next sound is requested; returning to an idle window needs no audio thread.
});

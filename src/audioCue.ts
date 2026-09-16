// Shared Web Audio cue engine: the declarative `SoundPlan` model plus the one
// player that schedules a plan onto an AudioContext.
//
// Every synthesised cue in the app goes through here — the startup cue that
// matches the boot animation (src/SPEC.md [S-26], plans in startupSound.ts) and
// the Pomodoro phase alarm (widgets/others/pomodoro, plans in alarmSound.ts).
// Cues are SYNTHESISED, never sampled: no audio assets to ship or licence, and a
// plan is a plain data structure so it can be unit-pinned with no AudioContext.
//
// The split is deliberate: callers own a PURE builder that turns their inputs
// into a `SoundPlan`; this module owns the imperative half. All browser-API
// access is lazy + wrapped, so importing this module touches no globals and an
// audio failure can never break the caller (boot, or a running timer).

// ── Plan model ──────────────────────────────────────────────────────────────
// A SoundPlan is a tiny declarative score: each voice is an oscillator (`tone`)
// or filtered white-noise (`noise`) burst with [timeMs, value] envelopes that
// the player replays as Web Audio param ramps. Times in a voice's envelopes are
// RELATIVE to that voice's `startMs`. Closed unions so the player dispatch is
// exhaustive (no `any` in this synthesis path).

export type ToneType = 'sine' | 'square' | 'sawtooth' | 'triangle';

/** [timeMsFromVoiceStart, value]; envelopes are non-decreasing in time. */
export type EnvPoint = [number, number];

export interface ToneVoice {
  kind: 'tone';
  type: ToneType;
  startMs: number;
  endMs: number;
  /** Pitch envelope (Hz), always > 0. */
  freq: EnvPoint[];
  /** Amplitude envelope, 0..1 (the player applies the master gain on top). */
  gain: EnvPoint[];
}

export interface NoiseVoice {
  kind: 'noise';
  startMs: number;
  endMs: number;
  /** Band-pass centre frequency envelope (Hz), always > 0. */
  filterHz: EnvPoint[];
  /** Amplitude envelope, 0..1. */
  gain: EnvPoint[];
}

export type Voice = ToneVoice | NoiseVoice;

export interface SoundPlan {
  /** Total length of the cue (ms): the longest voice end plus its tail. */
  totalMs: number;
  voices: Voice[];
}

/** Quiet tail after the last sounding event, so a release ramp isn't clipped. */
export const SOUND_TAIL_MS = 250;

/** Longest voice end + tail → the plan's total length. */
export function planFrom(voices: Voice[]): SoundPlan {
  const end = voices.reduce((m, v) => Math.max(m, v.endMs), 0);
  return { totalMs: end ? end + SOUND_TAIL_MS : 0, voices };
}

/** One voice's gain at an ABSOLUTE plan time; 0 outside the voice, and held at
 *  the nearest endpoint's value outside the envelope (matching AudioParam). */
function gainAt(v: Voice, atMs: number): number {
  if (atMs < v.startMs || atMs > v.endMs) return 0;
  const rel = atMs - v.startMs;
  const env = v.gain;
  if (rel <= env[0][0]) return env[0][1];
  for (let i = 1; i < env.length; i++) {
    const [t1, g1] = env[i];
    if (rel > t1) continue;
    const [t0, g0] = env[i - 1];
    return t1 === t0 ? g1 : g0 + ((g1 - g0) * (rel - t0)) / (t1 - t0);
  }
  return env[env.length - 1][1];
}

/**
 * Upper bound on a plan's instantaneous amplitude: the largest sum of concurrent
 * voice gains. The real waveform peak is at or below this (voices only reach the
 * sum when they happen to align in phase). Every envelope is piecewise linear,
 * so their sum peaks at a breakpoint — evaluating the union of breakpoints is
 * exact for the bound, not a sampling approximation.
 */
export function peakGainBound(plan: SoundPlan): number {
  const times = new Set<number>();
  for (const v of plan.voices) {
    times.add(v.endMs);
    for (const [ms] of v.gain) times.add(v.startMs + ms);
  }
  let peak = 0;
  for (const t of times) {
    let sum = 0;
    for (const v of plan.voices) sum += gainAt(v, t);
    if (sum > peak) peak = sum;
  }
  return peak;
}

/**
 * Scale every voice gain so the plan's peak bound lands on `targetPeak`.
 *
 * Cues whose loudness the user controls need this: a plan hand-tuned by ear
 * against one set of voices clips as soon as the master gain reaches 1.0, and
 * plans that stack simultaneous partials end up far louder than plans that don't
 * (measured: a 3x spread across the Pomodoro alarms, with the most insistent one
 * the quietest). Normalising to a shared bound fixes both at once.
 *
 * NOT applied to the startup cue ([S-26]): that one ships at a fixed level with
 * no volume control, and rescaling it would change a sound users already have.
 */
export function normalizePlan(plan: SoundPlan, targetPeak: number): SoundPlan {
  const bound = peakGainBound(plan);
  if (bound <= 0) return plan;
  const factor = targetPeak / bound;
  return {
    totalMs: plan.totalMs,
    voices: plan.voices.map((v) => ({
      ...v,
      gain: v.gain.map(([ms, g]): EnvPoint => [ms, g * factor]),
    })),
  };
}

// ── Player (Web Audio, lazy + failure-isolated) ──────────────────────────────

/** Master gain a plan mixes through when the caller names no volume. Voice
 *  gains are 0..1 pre-master, so this is the headroom that keeps a dense plan
 *  from clipping. */
export const DEFAULT_CUE_VOLUME = 0.5;

let audioCtx: AudioContext | null = null;
let noiseBuffer: AudioBuffer | null = null;

// The currently-sounding cue. The player is SINGLE-FLIGHT ([S-26]): a new cue
// silences the previous one first, so re-triggers (rapid settings changes, or a
// change landing while an earlier cue still plays) never stack and overlap.
interface ActiveCue {
  master: GainNode;
  sources: AudioScheduledSourceNode[];
  teardown: number; // window.setTimeout id for the node-cleanup
}
let activeCue: ActiveCue | null = null;

/**
 * Silence + tear down the in-flight cue, if any. The hard no-overlap guarantee
 * is the fast master-gain fade to ~0; stopping the sources early is best-effort
 * (re-stopping an already-scheduled source is a no-op or throws on some engines,
 * so it's wrapped; the mute is what actually prevents the overlap).
 */
function stopActiveCue(ctx: AudioContext): void {
  const cue = activeCue;
  activeCue = null;
  if (!cue) return;
  const now = ctx.currentTime;
  try {
    cue.master.gain.cancelScheduledValues(now);
    cue.master.gain.setValueAtTime(Math.max(0.0001, cue.master.gain.value), now);
    cue.master.gain.linearRampToValueAtTime(0.0001, now + 0.04);
  } catch { /* ignore: muting is best-effort */ }
  for (const s of cue.sources) {
    try { s.stop(now + 0.05); } catch { /* already stopped / cannot re-stop */ }
  }
  window.clearTimeout(cue.teardown);
  window.setTimeout(() => { try { cue.master.disconnect(); } catch { /* gone */ } }, 90);
}

/** Lazily create (and reuse) one AudioContext; null when Web Audio is absent. */
function getCtx(): AudioContext | null {
  if (audioCtx) return audioCtx;
  const Ctor: typeof AudioContext | undefined =
    typeof AudioContext !== 'undefined'
      ? AudioContext
      : (globalThis as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctor) return null;
  try {
    audioCtx = new Ctor();
  } catch {
    audioCtx = null; // construction can throw if the host blocks audio
  }
  return audioCtx;
}

/** One reusable 2 s white-noise buffer for the noise voices. */
function getNoiseBuffer(ctx: AudioContext): AudioBuffer {
  if (noiseBuffer) return noiseBuffer;
  const len = Math.floor(ctx.sampleRate * 2);
  const buf = ctx.createBuffer(1, len, ctx.sampleRate);
  const data = buf.getChannelData(0);
  for (let i = 0; i < len; i++) data[i] = Math.random() * 2 - 1;
  noiseBuffer = buf;
  return buf;
}

/** Replay one [timeMs, value] envelope onto an AudioParam, relative to `t0` (s). */
function applyEnv(param: AudioParam, env: EnvPoint[], t0: number): void {
  env.forEach(([ms, val], i) => {
    const t = t0 + ms / 1000;
    // Guard against a non-positive setValueAtTime time on an already-running ctx.
    const at = Math.max(t, t0);
    if (i === 0) param.setValueAtTime(val, at);
    else param.linearRampToValueAtTime(val, at);
  });
}

/** Schedule every voice of `plan` onto `ctx`, mixed through one master gain, and
 *  register it as the in-flight cue so the next call can silence it. */
function scheduleSoundPlan(ctx: AudioContext, plan: SoundPlan, volume: number): void {
  const t0 = ctx.currentTime + 0.02; // tiny lead so the first ramp isn't in the past
  const master = ctx.createGain();
  master.gain.value = volume;
  master.connect(ctx.destination);
  const sources: AudioScheduledSourceNode[] = [];

  for (const v of plan.voices) {
    const vStart = t0 + v.startMs / 1000;
    const vEnd = t0 + v.endMs / 1000;
    const gain = ctx.createGain();
    gain.gain.value = 0;
    gain.connect(master);

    if (v.kind === 'tone') {
      const osc = ctx.createOscillator();
      osc.type = v.type;
      applyEnv(osc.frequency, v.freq, vStart);
      applyEnv(gain.gain, v.gain, vStart);
      osc.connect(gain);
      osc.start(vStart);
      osc.stop(vEnd + 0.02);
      osc.onended = () => { osc.disconnect(); gain.disconnect(); };
      sources.push(osc);
    } else {
      const src = ctx.createBufferSource();
      src.buffer = getNoiseBuffer(ctx);
      src.loop = true;
      const filter = ctx.createBiquadFilter();
      filter.type = 'bandpass';
      filter.Q.value = 0.8;
      applyEnv(filter.frequency, v.filterHz, vStart);
      applyEnv(gain.gain, v.gain, vStart);
      src.connect(filter).connect(gain);
      src.start(vStart);
      src.stop(vEnd + 0.02);
      src.onended = () => { src.disconnect(); filter.disconnect(); gain.disconnect(); };
      sources.push(src);
    }
  }

  // Drop the master once the tail has run, so a long session doesn't leak nodes.
  const teardown = window.setTimeout(
    () => { try { master.disconnect(); } catch { /* already gone */ } },
    plan.totalMs + 200,
  );
  activeCue = { master, sources, teardown };
}

export interface PlaySoundPlanOpts {
  /** Master gain, 0..1. Defaults to `DEFAULT_CUE_VOLUME`. */
  volume?: number;
}

/**
 * Play `plan` (no-op for an empty plan or when Web Audio is unavailable). Fully
 * wrapped: audio is never load-bearing, so a failure here must not propagate to
 * the caller.
 */
export function playSoundPlan(plan: SoundPlan, opts: PlaySoundPlanOpts = {}): void {
  try {
    if (!plan.voices.length) return;
    const volume = Math.min(1, Math.max(0, opts.volume ?? DEFAULT_CUE_VOLUME));
    if (volume <= 0) return;
    const ctx = getCtx();
    if (!ctx) return;
    // Autoplay policy may park the context as 'suspended'; resume is best-effort
    // (the WebView2 --autoplay-policy arg makes the boot case reliable, and a cue
    // triggered from a settings control always has a user gesture).
    if (ctx.state === 'suspended') void ctx.resume().catch(() => { /* ignore */ });
    // Single-flight: silence any cue still playing so re-triggers never overlap.
    stopActiveCue(ctx);
    scheduleSoundPlan(ctx, plan, volume);
  } catch {
    /* ignore: audio is purely cosmetic, never let it break the caller */
  }
}

/** Test seam: drop the cached context + in-flight cue between cases. */
export function resetAudioCueForTest(): void {
  activeCue = null;
  audioCtx = null;
  noiseBuffer = null;
}

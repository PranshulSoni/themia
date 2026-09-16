// Startup sound: a procedural Web Audio cue that MATCHES the per-style startup
// animation (src/SPEC.md [S-26]). Opt-in (`globalDesign.widgetStartupSound`,
// default off); plays once per window boot and on the settings ▶ preview,
// alongside the [S-15] intro / [S-16] overlay, and is suppressed under
// `prefers-reduced-motion` exactly like the animation: no animation, nothing to
// match, no sound.
//
// Like the hyperspace starfield, the sound is SYNTHESISED (no audio assets to
// ship/licence) and timed off the SAME source of truth the visuals use
// (`HYPERSPACE_PROFILES`), so it can never drift from the animation. This module
// is the PURE half — `buildStartupSoundPlan` turns a style + duration into a
// `SoundPlan` of oscillator/noise voices with time-ordered envelopes, unit-
// pinnable with no AudioContext (see startupSound.test.ts). The imperative half
// (the one player every synthesised cue in the app shares) lives in audioCue.ts.

import { HYPERSPACE_PROFILES } from './hyperspace.ts';
import { type WidgetStartupAnimation } from './design.ts';
import { planFrom, playSoundPlan, SOUND_TAIL_MS, type SoundPlan, type Voice } from './audioCue.ts';

// The startup cue's own name for the shared tail, kept so the plan tests read in
// startup terms.
export const STARTUP_SOUND_TAIL_MS = SOUND_TAIL_MS;

// Re-exported so a caller that only deals in startup cues keeps a single import
// site: the plan vocabulary it builds against, and the engine reset its tests need.
export type { EnvPoint, NoiseVoice, SoundPlan, ToneType, ToneVoice, Voice } from './audioCue.ts';
export { resetAudioCueForTest as resetStartupSoundForTest } from './audioCue.ts';

// ── Builders (pure) ──────────────────────────────────────────────────────────

/**
 * The matching sound cue for a startup `style` and the per-widget intro duration
 * `widgetDurationMs` (`globalDesign.widgetStartupAnimationMs`). Warp styles read
 * their shape from `HYPERSPACE_PROFILES` (the duration slider only retimes the
 * widget LANDING, not the warp), so a warp plan ignores `widgetDurationMs`;
 * `hacker` and the simple styles scale with it. `none` → empty plan.
 */
export function buildStartupSoundPlan(
  style: WidgetStartupAnimation,
  widgetDurationMs: number,
): SoundPlan {
  switch (style) {
    case 'none':
      return { totalMs: 0, voices: [] };
    case 'hyperspace':
      return warpPlan();
    case 'hacker':
      return hackerPlan(widgetDurationMs);
    default:
      return simplePlan(style, widgetDurationMs);
  }
}

/**
 * Hyperspace warp ([S-16]): a rising engine whine that spools up through the
 * accel phase, a noise wind-rush that builds with it, a low sustained drone,
 * and a bass BOOM punched at the jump point that decays through the decel,
 * with every boundary lifted straight from the profile so audio == visual. Harsh
 * and low-pitched to match the violent warp.
 */
function warpPlan(): SoundPlan {
  const p = HYPERSPACE_PROFILES.hyperspace;
  const peak = 0.95;   // engine whine peak amplitude
  const topHz = 1500;  // pitch at full warp
  const baseHz = 70;   // pitch at spool-up start
  const boomHz = 58;   // bass impact start pitch
  const jump = p.jumpMs;
  const decelEnd = jump + p.decelMs;

  const voices: Voice[] = [];

  // Engine whine: silent through static, ramps in pitch + level across accel,
  // holds at full warp through cruise, then drops away as the ship punches out.
  voices.push({
    kind: 'tone',
    type: 'sawtooth',
    startMs: p.staticMs,
    endMs: jump + 120,
    freq: [
      [0, baseHz],
      [p.accelMs, topHz],
      [jump - p.staticMs, topHz],
      [jump - p.staticMs + 120, baseHz],
    ],
    gain: [
      [0, 0],
      [p.accelMs, peak * 0.7],
      [jump - p.staticMs, peak],
      [jump - p.staticMs + 120, 0],
    ],
  });

  // Wind rush: band-pass noise that opens up (filter sweeps up) and loudens with
  // the warp, then falls away over the decel: the airy layer under the whine.
  voices.push({
    kind: 'noise',
    startMs: p.staticMs,
    endMs: decelEnd,
    filterHz: [
      [0, 320],
      [p.accelMs, 2600],
      [jump - p.staticMs, 2600],
      [decelEnd - p.staticMs, 500],
    ],
    gain: [
      [0, 0],
      [p.accelMs, 0.42],
      [jump - p.staticMs, 0.5],
      [decelEnd - p.staticMs, 0],
    ],
  });

  // Bass BOOM at the jump: a quick low sine punch that drops in pitch and decays
  // across the decel window (the widgets crash in over this tail).
  voices.push({
    kind: 'tone',
    type: 'sine',
    startMs: jump,
    endMs: decelEnd,
    freq: [
      [0, boomHz],
      [p.decelMs, boomHz * 0.45],
    ],
    gain: [
      [0, 0],
      [40, 1],
      [p.decelMs, 0],
    ],
  });

  // Sub drone through the whole show so the build has weight even on small
  // speakers: quietly present from spool-up, lifts at the boom, fades with decel.
  voices.push({
    kind: 'tone',
    type: 'sine',
    startMs: p.staticMs,
    endMs: decelEnd,
    freq: [
      [0, 46],
      [decelEnd - p.staticMs, 40],
    ],
    gain: [
      [0, 0],
      [p.accelMs, 0.18],
      [jump - p.staticMs, 0.32],
      [decelEnd - p.staticMs, 0],
    ],
  });

  // Make sure the explicit total spans jump+decel+tail regardless of which voice
  // happens to end last (the whine ends slightly before decelEnd).
  return { totalMs: decelEnd + STARTUP_SOUND_TAIL_MS, voices };
}

// A short pentatonic-ish ladder the hacker blips climb so the churn reads as
// "working" rather than random, and deterministic (no RNG) so the plan is testable.
const HACKER_BLIP_HZ = [440, 523, 587, 659, 784, 880, 988, 1175];

/**
 * Hacker churn ([S-23]): a stream of short digital blips over the configured
 * churn `d`, then a two-note "access granted" confirm at the end. The blip count
 * scales with `d` so a longer slider = a longer decode.
 */
function hackerPlan(widgetDurationMs: number): SoundPlan {
  const d = Math.max(1, widgetDurationMs);
  const stepMs = 110;
  const count = Math.max(5, Math.min(28, Math.floor(d / stepMs)));
  const voices: Voice[] = [];

  for (let i = 0; i < count; i++) {
    const hz = HACKER_BLIP_HZ[i % HACKER_BLIP_HZ.length];
    const at = Math.round((i / count) * (d - 80));
    voices.push({
      kind: 'tone',
      type: 'square',
      startMs: at,
      endMs: at + 45,
      freq: [[0, hz]],
      gain: [[0, 0.16], [38, 0.16], [45, 0]],
    });
  }

  // Confirm chime: two clean notes landing right at the end of the churn.
  const confirmAt = d;
  voices.push({
    kind: 'tone', type: 'triangle', startMs: confirmAt, endMs: confirmAt + 160,
    freq: [[0, 784]], gain: [[0, 0], [12, 0.3], [160, 0]],
  });
  voices.push({
    kind: 'tone', type: 'triangle', startMs: confirmAt + 90, endMs: confirmAt + 320,
    freq: [[0, 1175]], gain: [[0, 0], [12, 0.34], [230, 0]],
  });

  return planFrom(voices);
}

// Each simple style gets its own base note so it "matches" with a distinct
// colour while sharing one gentle power-on shape (one path, parametrized).
type SimpleStyle = 'fade' | 'scale' | 'slide' | 'sides' | 'topbottom' | 'flip' | 'blur';

const SIMPLE_BASE_HZ: Record<SimpleStyle, number> = {
  fade: 392,      // soft, low
  scale: 523,     // bright pop
  slide: 440,
  sides: 466,     // a semitone off slide: same family, its own colour
  topbottom: 494, // the other sheet axis, another semitone up
  flip: 587,
  blur: 330,      // muted
};

/**
 * Simple intros (fade/scale/slide/sides/topbottom/blur/flip): a soft
 * two-partial "power-on" swell whose length tracks the duration slider (clamped
 * so it never drags), with a per-style base note so each animation has its own
 * colour.
 */
function simplePlan(
  style: SimpleStyle,
  widgetDurationMs: number,
): SoundPlan {
  const base = SIMPLE_BASE_HZ[style];
  const len = Math.max(180, Math.min(1100, widgetDurationMs));
  const rising = style === 'slide' || style === 'scale'; // upward styles lift in pitch
  const endHz = rising ? base * 1.5 : base;
  const voices: Voice[] = [
    {
      kind: 'tone', type: 'sine', startMs: 0, endMs: len,
      freq: [[0, base], [len, endHz]],
      gain: [[0, 0], [Math.round(len * 0.35), 0.26], [len, 0]],
    },
    // A quiet octave shimmer on top for a little sparkle.
    {
      kind: 'tone', type: 'triangle', startMs: 20, endMs: len,
      freq: [[0, base * 2], [len - 20, endHz * 2]],
      gain: [[0, 0], [Math.round(len * 0.3), 0.1], [len - 20, 0]],
    },
  ];
  return planFrom(voices);
}

// ── Player entry point ──────────────────────────────────────────

export interface PlayStartupSoundOpts {
  /** `globalDesign.widgetStartupSound`. */
  enabled: boolean;
  /** `prefers-reduced-motion`; suppresses the sound with the animation ([S-26]). */
  reducedMotion: boolean;
}

/**
 * Play the matching startup sound for `style` (no-op when disabled, under reduced
 * motion, for style `none`, or when Web Audio is unavailable). The shared player
 * is single-flight, so a re-trigger silences the cue still in flight rather than
 * stacking on it ([S-26]).
 */
export function playStartupSound(
  style: WidgetStartupAnimation,
  widgetDurationMs: number,
  opts: PlayStartupSoundOpts,
): void {
  if (!opts.enabled || opts.reducedMotion || style === 'none') return;
  playSoundPlan(buildStartupSoundPlan(style, widgetDurationMs));
}

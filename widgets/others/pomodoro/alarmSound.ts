// Pomodoro phase alarm ([S-06]/[S-07]): the audible cue that fires when the
// countdown rolls from focus into break or back (themia-releases#77 — "I don't
// glance at it often enough if I am deep into something and miss it rolling
// into the next one").
//
// Like every other cue in the app the alarm is SYNTHESISED (no audio assets to
// ship or licence) and split into a PURE plan builder here plus the shared
// player in src/audioCue.ts, so the whole sound design is unit-pinnable with no
// AudioContext (see alarmSound.test.ts).
//
// Each alarm carries the phase it announces in its MELODIC DIRECTION: the cue
// for entering focus ascends (get going), the cue for entering a break descends
// (wind down). That is the whole point of the feature — the user is not looking
// at the widget, so the sound alone has to say which way the timer just rolled.
// Every sound is therefore described as ONE note sequence (the focus cue) that
// gets reversed for a break, so the rule lives in `buildDesign` rather than
// being restated, and possibly fumbled, once per sound.

import {
  normalizePlan,
  planFrom,
  playSoundPlan,
  type SoundPlan,
  type ToneType,
  type ToneVoice,
  type Voice,
} from '../../../src/audioCue.ts';
import type { Phase } from './pomodoroTick.ts';

/**
 * Closed set; `off` is the one value that yields an empty plan. Ordered
 * roughly gentle → insistent, which is the axis a user actually picks along,
 * and the order the config dropdown shows.
 */
export const POMODORO_ALARM_SOUNDS = [
  'off',
  'chime',
  'marimba',
  'crystal',
  'pluck',
  'droplet',
  'birdsong',
  'zen',
  'bell',
  'gong',
  'arcade',
  'digital',
  'buzzer',
] as const;
export type PomodoroAlarmSound = (typeof POMODORO_ALARM_SOUNDS)[number];

/** Every sound except `off` — the ones that have a design behind them. */
export type SoundingAlarm = Exclude<PomodoroAlarmSound, 'off'>;

export const DEFAULT_ALARM_SOUND: PomodoroAlarmSound = 'chime';
export const DEFAULT_ALARM_VOLUME = 70;

/**
 * Persisted config is a trust boundary: an old, hand-edited or corrupt
 * `alarmSound` must resolve to the default, never reach the plan switch as a
 * bare string.
 */
export function resolveAlarmSound(value: unknown): PomodoroAlarmSound {
  return (POMODORO_ALARM_SOUNDS as readonly unknown[]).includes(value)
    ? (value as PomodoroAlarmSound)
    : DEFAULT_ALARM_SOUND;
}

/** Same boundary for the volume: clamp to 0..100, default on anything else. */
export function resolveAlarmVolume(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return DEFAULT_ALARM_VOLUME;
  return Math.min(100, Math.max(0, Math.round(value)));
}

// ── Voice helpers ────────────────────────────────────────────────────────────

/** Near-instant attack, so a strike reads as struck rather than swelled. */
const ATTACK_MS = 6;

/** A struck, decaying partial: fast attack then a long fall to silence. */
function struck(
  startMs: number,
  hz: number,
  decayMs: number,
  peak: number,
  type: ToneType = 'sine',
): ToneVoice {
  return {
    kind: 'tone',
    type,
    startMs,
    endMs: startMs + decayMs,
    freq: [[0, hz]],
    gain: [[0, 0], [ATTACK_MS, peak], [decayMs, 0]],
  };
}

/** A flat-topped square blip: the digital alarm's building block. */
function beep(startMs: number, hz: number, lenMs: number, peak: number): ToneVoice {
  return {
    kind: 'tone',
    type: 'square',
    startMs,
    endMs: startMs + lenMs,
    freq: [[0, hz]],
    gain: [[0, 0], [4, peak], [lenMs - 8, peak], [lenMs, 0]],
  };
}

// ── Sound designs ────────────────────────────────────────────────────────────

/**
 * The voices ONE note of a sound is made of. `index` is the note's position in
 * the sequence, for sounds whose notes are not interchangeable — a gong's
 * answering strike is quieter than the first, an arcade jingle lands on a long
 * final note.
 */
type Timbre = (startMs: number, hz: number, index: number) => Voice[];

interface AlarmDesign {
  /** When each note starts, ms from the top of the cue. */
  offsets: number[];
  /**
   * The note sequence for the FOCUS cue, which must rise overall; a break plays
   * the same notes reversed. Same length as `offsets`.
   */
  notes: number[];
  timbre: Timbre;
}

// Inharmonic partials of a struck bell; the 2.76 / 5.40 ratios are what make a
// bell read as a bell rather than as a sine beep.
const BELL_PARTIALS: Array<[ratio: number, peak: number, decayMs: number]> = [
  [1, 0.55, 1600],
  [2.76, 0.18, 900],
  [5.4, 0.08, 520],
];

const ALARM_DESIGNS: Record<SoundingAlarm, AlarmDesign> = {
  /** Soft three-note major triad with an octave shimmer: the gentle default. */
  chime: {
    offsets: [0, 170, 340],
    notes: [523.25, 659.25, 783.99], // C5 E5 G5
    timbre: (at, hz) => [
      struck(at, hz, 900, 0.5, 'triangle'),
      // A quiet octave on top, decaying faster, for a little sparkle.
      struck(at, hz * 2, 480, 0.11),
    ],
  },

  /** Two strikes of an inharmonic bell a fifth apart: the longest-ringing option. */
  bell: {
    offsets: [0, 620],
    notes: [523.25, 523.25 * 1.5],
    timbre: (at, hz) =>
      BELL_PARTIALS.map(([ratio, peak, decayMs]) => struck(at, hz * ratio, decayMs, peak)),
  },

  /** Three double-beeps: the insistent alarm-clock pattern, for when a gentle
   *  chime is too easy to miss. */
  digital: {
    offsets: [0, 130, 340, 470, 680, 810],
    notes: [698.46, 880, 698.46, 880, 698.46, 880],
    timbre: (at, hz) => [beep(at, hz, 85, 0.4)],
  },

  /** A deep struck gong plus a quieter answering strike: low enough to carry
   *  through headphones without being shrill. */
  gong: {
    offsets: [0, 700],
    notes: [110, 164.81],
    timbre: (at, hz, i) =>
      i === 0
        ? [
            // Mallet contact: a short band-passed noise transient so the strike
            // has an attack instead of appearing out of nowhere.
            {
              kind: 'noise',
              startMs: at,
              endMs: at + 110,
              filterHz: [[0, 1400], [110, 600]],
              gain: [[0, 0], [6, 0.3], [110, 0]],
            },
            struck(at, hz, 2000, 0.9),
            struck(at, hz * 2, 1300, 0.24),
            struck(at, hz * 3, 800, 0.1),
          ]
        : [struck(at, hz, 1600, 0.4), struck(at, hz * 2, 900, 0.12)],
  },

  /** Wooden mallet bars: short, warm and dry. The tuned 4th partial is what
   *  separates a marimba from a plain sine. */
  marimba: {
    offsets: [0, 110, 220],
    notes: [523.25, 698.46, 880], // C5 F5 A5
    timbre: (at, hz) => [
      struck(at, hz, 380, 0.55),
      struck(at, hz * 4, 150, 0.2),
      struck(at, hz * 10, 60, 0.06),
    ],
  },

  /** High glass: a slow-beating detuned pair that shimmers as it rings out. */
  crystal: {
    offsets: [0, 150, 300],
    notes: [1046.5, 1318.51, 1567.98], // C6 E6 G6
    timbre: (at, hz) => [
      struck(at, hz, 1200, 0.4),
      // A few cents sharp: the two voices drift in and out of phase, which is
      // the shimmer. Kept above the fundamental so the note still reads as `hz`.
      struck(at, hz * 1.003, 1100, 0.3),
      struck(at, hz * 2, 600, 0.12, 'triangle'),
    ],
  },

  /** Plucked string: a fast four-note run, like brushing a harp. */
  pluck: {
    offsets: [0, 80, 160, 240],
    notes: [392, 523.25, 659.25, 783.99], // G4 C5 E5 G5
    timbre: (at, hz) => [
      struck(at, hz, 450, 0.5, 'triangle'),
      // A brief sawtooth partial for the bite of the pluck itself.
      struck(at, hz * 3, 120, 0.1, 'sawtooth'),
    ],
  },

  /** Two water drops: the most minimal cue here — a rising bloop, nothing else. */
  droplet: {
    offsets: [0, 260],
    notes: [880, 1174.66],
    timbre: (at, hz) => [
      {
        kind: 'tone',
        type: 'sine',
        startMs: at,
        endMs: at + 240,
        // The bloop IS the pitch rise; it lands above where it started.
        freq: [[0, hz * 0.55], [70, hz * 1.25], [240, hz * 1.35]],
        gain: [[0, 0], [8, 0.6], [240, 0]],
      },
      struck(at, hz * 3, 60, 0.08),
    ],
  },

  /** Three chirps: a nature cue for people who want the desk to stay quiet. */
  birdsong: {
    offsets: [0, 190, 380],
    notes: [1567.98, 2093, 2637.02],
    timbre: (at, hz) => [
      {
        kind: 'tone',
        type: 'sine',
        startMs: at,
        endMs: at + 95,
        freq: [[0, hz * 0.8], [45, hz * 1.35], [95, hz * 1.1]],
        gain: [[0, 0], [10, 0.55], [80, 0.4], [95, 0]],
      },
      {
        kind: 'tone',
        type: 'sine',
        startMs: at,
        endMs: at + 95,
        freq: [[0, hz * 1.6], [45, hz * 2.7], [95, hz * 2.2]],
        gain: [[0, 0], [10, 0.14], [95, 0]],
      },
    ],
  },

  /** Singing bowl: a slow swell with a long beating tail, for a cue that ends a
   *  focus block without jolting you out of it. */
  zen: {
    offsets: [0, 900],
    notes: [261.63, 392], // C4 G4
    timbre: (at, hz) => [
      {
        kind: 'tone', type: 'sine', startMs: at, endMs: at + 1600,
        freq: [[0, hz]], gain: [[0, 0], [40, 0.6], [1600, 0]],
      },
      {
        kind: 'tone', type: 'sine', startMs: at, endMs: at + 1600,
        freq: [[0, hz * 1.004]], gain: [[0, 0], [45, 0.45], [1600, 0]],
      },
      struck(at, hz * 2.4, 900, 0.18),
      struck(at, hz * 4.6, 500, 0.07),
    ],
  },

  /** Chiptune fanfare: four square notes landing on a held final one. */
  arcade: {
    offsets: [0, 90, 180, 270],
    notes: [523.25, 659.25, 783.99, 1046.5],
    timbre: (at, hz, i) =>
      i === 3
        ? [beep(at, hz, 320, 0.45), beep(at, hz * 1.5, 320, 0.2)]
        : [beep(at, hz, 80, 0.4)],
  },

  /** Harsh two-tone buzz: the last resort when nothing gentler gets through. */
  buzzer: {
    offsets: [0, 420],
    notes: [220, 293.66],
    timbre: (at, hz) => [
      {
        kind: 'tone',
        type: 'sawtooth',
        startMs: at,
        endMs: at + 340,
        freq: [[0, hz]],
        // Chopped, not sustained: the gaps are what make it read as a buzzer
        // rather than a drone.
        gain: [
          [0, 0], [6, 0.5], [70, 0.5], [80, 0.05], [95, 0.05],
          [105, 0.5], [175, 0.5], [185, 0.05], [200, 0.05],
          [210, 0.5], [300, 0.5], [340, 0],
        ],
      },
      {
        kind: 'tone', type: 'square', startMs: at, endMs: at + 340,
        freq: [[0, hz * 2]], gain: [[0, 0], [6, 0.16], [300, 0.16], [340, 0]],
      },
    ],
  },
};

// ── Builders (pure) ──────────────────────────────────────────────────────────

/**
 * Peak every alarm is normalised to, leaving 10% headroom at volume 100. Without
 * it the hand-tuned plans both clipped at full volume and sat three times apart
 * in level, so "insistent" (digital, one square voice at a time) came out
 * quietest and "gong" (four stacked partials) loudest. Sounds are chosen for
 * character, not because one happens to be louder.
 */
const ALARM_PEAK = 0.9;

/** Render a design in the direction `entering` demands ([S-06]). */
function buildDesign(design: AlarmDesign, entering: Phase): SoundPlan {
  const notes = entering === 'work' ? design.notes : [...design.notes].reverse();
  const voices: Voice[] = [];
  notes.forEach((hz, i) => voices.push(...design.timbre(design.offsets[i], hz, i)));
  return planFrom(voices);
}

/**
 * The alarm plan for `sound` announcing entry into `entering`. `off` (and any
 * value that failed to resolve) yields an empty plan, which the player treats
 * as a no-op. Every sounding plan is level-matched to `ALARM_PEAK`.
 */
export function buildAlarmPlan(sound: PomodoroAlarmSound, entering: Phase): SoundPlan {
  if (sound === 'off') return { totalMs: 0, voices: [] };
  return normalizePlan(buildDesign(ALARM_DESIGNS[sound], entering), ALARM_PEAK);
}

// ── Player entry point ───────────────────────────────────────────────────────

/**
 * Sound the alarm for a phase flip. No-op for `off`, for a zero volume, or when
 * Web Audio is unavailable. The shared player is single-flight, so a preview
 * fired while an earlier one still rings silences it rather than stacking.
 */
export function playPomodoroAlarm(
  sound: PomodoroAlarmSound,
  entering: Phase,
  volumePct: number,
): void {
  playSoundPlan(buildAlarmPlan(sound, entering), { volume: volumePct / 100 });
}

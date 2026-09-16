// [S-06]/[S-07] the Pomodoro alarm PLAN builder + the config-value resolvers.
// The plans are pure data, so the whole sound design is pinnable with no
// AudioContext (the shared player itself is pinned in src/startupSound.player.test.ts).

import { describe, it, expect } from 'vitest';
import {
  buildAlarmPlan,
  DEFAULT_ALARM_SOUND,
  DEFAULT_ALARM_VOLUME,
  POMODORO_ALARM_SOUNDS,
  resolveAlarmSound,
  resolveAlarmVolume,
  type PomodoroAlarmSound,
} from './alarmSound.ts';
import { peakGainBound, type Voice } from '../../../src/audioCue.ts';
import { en } from '../../../src/locales/en.ts';

const SOUNDING = POMODORO_ALARM_SOUNDS.filter((s) => s !== 'off');

/**
 * The plan's melodic line: one pitch per strike, in time order. Voices sharing a
 * start time are one struck note (fundamental + its partials/shimmer), so the
 * fundamental is the lowest of the group — the note a listener actually hears.
 */
function melody(voices: Voice[]): number[] {
  const byStart = new Map<number, number>();
  for (const v of voices) {
    if (v.kind !== 'tone') continue;
    const hz = v.freq[0][1];
    const seen = byStart.get(v.startMs);
    byStart.set(v.startMs, seen === undefined ? hz : Math.min(seen, hz));
  }
  return [...byStart.entries()].sort((a, b) => a[0] - b[0]).map(([, hz]) => hz);
}

describe('S-07 config values resolve at the trust boundary', () => {
  it('an absent, unknown or wrong-typed sound falls back to the default', () => {
    expect(resolveAlarmSound(undefined)).toBe(DEFAULT_ALARM_SOUND);
    expect(resolveAlarmSound('cowbell')).toBe(DEFAULT_ALARM_SOUND);
    expect(resolveAlarmSound(7)).toBe(DEFAULT_ALARM_SOUND);
    expect(resolveAlarmSound(null)).toBe(DEFAULT_ALARM_SOUND);
  });

  it('keeps every value that IS in the closed set (including off)', () => {
    for (const s of POMODORO_ALARM_SOUNDS) expect(resolveAlarmSound(s)).toBe(s);
  });

  it('clamps the volume to 0..100 and defaults on anything non-numeric', () => {
    expect(resolveAlarmVolume(0)).toBe(0);
    expect(resolveAlarmVolume(55)).toBe(55);
    expect(resolveAlarmVolume(-20)).toBe(0);
    expect(resolveAlarmVolume(400)).toBe(100);
    expect(resolveAlarmVolume(undefined)).toBe(DEFAULT_ALARM_VOLUME);
    expect(resolveAlarmVolume('80')).toBe(DEFAULT_ALARM_VOLUME);
    expect(resolveAlarmVolume(Number.NaN)).toBe(DEFAULT_ALARM_VOLUME);
  });
});

describe('S-06 alarm plans', () => {
  it('off is silent in both directions', () => {
    for (const entering of ['work', 'break'] as const) {
      expect(buildAlarmPlan('off', entering)).toEqual({ totalMs: 0, voices: [] });
    }
  });

  it('every other sound produces audible voices in both directions', () => {
    for (const sound of SOUNDING) {
      for (const entering of ['work', 'break'] as const) {
        const plan = buildAlarmPlan(sound, entering);
        expect(plan.voices.length, `${sound}/${entering}`).toBeGreaterThan(0);
        expect(plan.totalMs, `${sound}/${entering}`).toBeGreaterThan(0);
      }
    }
  });

  // This is the feature's whole point: the user is not looking at the widget,
  // so the cue alone must say which way the timer rolled.
  it('the motif ASCENDS into focus and DESCENDS into a break', () => {
    for (const sound of SOUNDING) {
      const work = melody(buildAlarmPlan(sound, 'work').voices);
      const brk = melody(buildAlarmPlan(sound, 'break').voices);
      expect(work.length, `${sound} needs >1 note to have a direction`).toBeGreaterThan(1);
      expect(work.at(-1), `${sound} into focus`).toBeGreaterThan(work[0]);
      expect(brk.at(-1), `${sound} into break`).toBeLessThan(brk[0]);
      // Same notes, opposite order: one sound, two readings.
      expect([...brk].sort(), sound).toEqual([...work].sort());
    }
  });

  it('a plan is a mirror of its opposite direction, not a different sound', () => {
    for (const sound of SOUNDING) {
      const work = buildAlarmPlan(sound, 'work');
      const brk = buildAlarmPlan(sound, 'break');
      expect(brk.voices.length, sound).toBe(work.voices.length);
      expect(brk.totalMs, sound).toBe(work.totalMs);
    }
  });

  it('stays short enough to be a cue rather than a track (<= 3 s)', () => {
    for (const sound of SOUNDING) {
      for (const entering of ['work', 'break'] as const) {
        expect(buildAlarmPlan(sound, entering).totalMs, `${sound}/${entering}`)
          .toBeLessThanOrEqual(3000);
      }
    }
  });

  it('every voice is well formed: positive pitch, time-ordered envelopes, gain in 0..1', () => {
    for (const sound of SOUNDING) {
      for (const entering of ['work', 'break'] as const) {
        for (const v of buildAlarmPlan(sound, entering).voices) {
          const where = `${sound}/${entering}`;
          expect(v.endMs, where).toBeGreaterThan(v.startMs);
          const freqEnv = v.kind === 'tone' ? v.freq : v.filterHz;
          for (const [, hz] of freqEnv) expect(hz, where).toBeGreaterThan(0);
          for (const env of [freqEnv, v.gain]) {
            const times = env.map(([ms]) => ms);
            expect([...times].sort((a, b) => a - b), where).toEqual(times);
            // Envelopes are relative to the voice start and must fit the voice.
            expect(times[0], where).toBeGreaterThanOrEqual(0);
            expect(times.at(-1)!, where).toBeLessThanOrEqual(v.endMs - v.startMs);
          }
          for (const [, g] of v.gain) {
            expect(g, where).toBeGreaterThanOrEqual(0);
            expect(g, where).toBeLessThanOrEqual(1);
          }
          // A cue that never returns to silence would be clipped mid-ring.
          expect(v.gain.at(-1)![1], where).toBe(0);
        }
      }
    }
  });

  // Regression: the plans were hand-tuned per sound, so at volume 100 chime,
  // bell and gong all pushed the master gain past 1.0 and clipped (rendered
  // through the real player into an OfflineAudioContext: peaks 1.13 / 1.18 /
  // 1.27, gong clipping 1287 samples), while `digital` -- the option you pick
  // BECAUSE the chime is too easy to miss -- peaked at 0.40, a third of the
  // others. Every plan is now normalised to one bound.
  it('every sound is level-matched and cannot clip at volume 100', () => {
    const bounds = SOUNDING.flatMap((sound) =>
      (['work', 'break'] as const).map((entering) =>
        peakGainBound(buildAlarmPlan(sound, entering))),
    );
    for (const b of bounds) {
      expect(b).toBeLessThanOrEqual(1);   // master gain is 1.0 at volume 100
      expect(b).toBeGreaterThan(0.5);     // ...and still actually audible
    }
    // One target, not four: the loudest and quietest plan agree.
    expect(Math.max(...bounds) - Math.min(...bounds)).toBeLessThan(1e-9);
  });

  // A missing key makes t() return the key itself, so the dropdown would quietly
  // offer "pomodoro.alarmSound.marimba" as a ringtone name. The dropdown is
  // derived from this set, so adding a sound without a label is otherwise silent.
  it('[S-07] every sound in the set has a translated label', () => {
    const labels = en.pomodoro.alarmSound as Record<string, string>;
    const missing = POMODORO_ALARM_SOUNDS.filter((s) => !labels[s]);
    expect(missing).toEqual([]);
  });

  it('the builder covers the whole closed set (no sound falls through)', () => {
    const missing = POMODORO_ALARM_SOUNDS.filter(
      (s: PomodoroAlarmSound) => buildAlarmPlan(s, 'work') === undefined,
    );
    expect(missing).toEqual([]);
  });
});

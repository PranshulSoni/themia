// [S-26] Startup sound: procedural Web Audio that MATCHES the per-style startup
// animation. The pure schedule BUILDER (`buildStartupSoundPlan`) is the only
// part exercisable without a real AudioContext (jsdom has none), so it owns all
// the testable invariants; the thin scheduler that feeds a plan to the
// AudioContext is cosmetic browser glue, like the canvas draw in
// HyperspaceOverlay. The plan is timed off the SAME source of truth the visuals
// use (`HYPERSPACE_PROFILES`), so the sound can never drift from the animation.

import { describe, it, expect } from 'vitest';
import {
  buildStartupSoundPlan,
  STARTUP_SOUND_TAIL_MS,
  type SoundPlan,
  type Voice,
} from './startupSound.ts';
import { HYPERSPACE_PROFILES } from './hyperspace.ts';
import type { WidgetStartupAnimation } from './design.ts';

// Type-only import above (erased) keeps this test in the plain-node env like
// hyperspace.test.ts; importing design.ts as a VALUE would run its top-level
// applyGlobalDesign() (touches document/window) and blow up without jsdom.
const ALL_STYLES: WidgetStartupAnimation[] =
  ['none', 'fade', 'scale', 'slide', 'sides', 'topbottom', 'flip', 'blur', 'hyperspace', 'hacker'];

/** First / max value of an envelope ([timeMs, value] pairs, time-ordered). */
function envFirst(env: Array<[number, number]>): number { return env[0][1]; }
function envMax(env: Array<[number, number]>): number { return Math.max(...env.map(([, v]) => v)); }

/** Peak gain across every voice in a plan: a rough "loudness/harshness" proxy. */
function peakGain(plan: SoundPlan): number {
  return Math.max(0, ...plan.voices.flatMap(v => v.gain.map(([, g]) => g)));
}

describe('S-26 startup sound: no sound when there is no animation', () => {
  it("style 'none' yields an empty plan (nothing to schedule)", () => {
    const plan = buildStartupSoundPlan('none', 500);
    expect(plan.voices).toEqual([]);
    expect(plan.totalMs).toBe(0);
  });
});

describe('S-26 warp sound: matches the hyperspace profile', () => {
  it('spans the whole warp (jump + decel) plus a short tail', () => {
    const p = HYPERSPACE_PROFILES['hyperspace'];
    const plan = buildStartupSoundPlan('hyperspace', 500);
    expect(plan.totalMs).toBe(p.jumpMs + p.decelMs + STARTUP_SOUND_TAIL_MS);
  });

  it('has an engine tone that rises in pitch as the warp spools up', () => {
    const plan = buildStartupSoundPlan('hyperspace', 500);
    const rising = plan.voices.some(v => v.kind === 'tone' && envMax(v.freq) > envFirst(v.freq));
    expect(rising).toBe(true);
  });

  it('is harsh and loud, a near-full-gain sawtooth engine whine', () => {
    const plan = buildStartupSoundPlan('hyperspace', 500);
    // Harsh: the engine whine is a sawtooth, not a soft triangle.
    expect(plan.voices.some(v => v.kind === 'tone' && v.type === 'sawtooth')).toBe(true);
    // Loud: at least one voice nearly maxes out the gain envelope.
    expect(peakGain(plan)).toBeGreaterThan(0.9);
  });

  it("the warp shape is fixed by the profile, not the per-widget duration slider", () => {
    // The warp visual timing comes from HYPERSPACE_PROFILES, so the sound must
    // not stretch/shrink with the per-widget intro duration.
    const a = buildStartupSoundPlan('hyperspace', 200);
    const b = buildStartupSoundPlan('hyperspace', 4000);
    expect(a.totalMs).toBe(b.totalMs);
  });
});

describe('S-26 hacker sound: digital churn scaled to the duration slider', () => {
  it('emits more blips for a longer churn', () => {
    const short = buildStartupSoundPlan('hacker', 500);
    const long = buildStartupSoundPlan('hacker', 2500);
    expect(long.voices.length).toBeGreaterThan(short.voices.length);
  });

  it('lasts roughly as long as the configured churn', () => {
    const d = 1800;
    const plan = buildStartupSoundPlan('hacker', d);
    expect(plan.totalMs).toBeGreaterThanOrEqual(d);
  });
});

describe('S-26 simple styles: a soft power-on that tracks the duration slider', () => {
  for (const style of ['fade', 'scale', 'slide', 'sides', 'topbottom', 'flip', 'blur'] as const) {
    it(`${style}: non-empty and longer for a longer animation`, () => {
      const short = buildStartupSoundPlan(style, 300);
      const long = buildStartupSoundPlan(style, 1200);
      expect(short.voices.length).toBeGreaterThan(0);
      expect(long.totalMs).toBeGreaterThan(short.totalMs);
    });
  }
});

describe('S-26 plan invariants: every style produces a schedulable plan', () => {
  const ok = (v: number) => Number.isFinite(v);

  function assertVoiceWellFormed(v: Voice, totalMs: number) {
    expect(v.startMs).toBeGreaterThanOrEqual(0);
    expect(v.endMs).toBeGreaterThan(v.startMs);
    expect(v.endMs).toBeLessThanOrEqual(totalMs);
    const envs = v.kind === 'tone' ? [v.freq] : [v.filterHz];
    envs.push(v.gain);
    for (const env of envs) {
      expect(env.length).toBeGreaterThan(0);
      let prev = -1;
      for (const [t, val] of env) {
        expect(ok(t)).toBe(true);
        expect(t).toBeGreaterThanOrEqual(prev); // time-ordered, non-decreasing
        prev = t;
        expect(ok(val)).toBe(true);
      }
    }
    // Gains are a normalized 0..1 amplitude; the player applies the master gain.
    for (const [, g] of v.gain) {
      expect(g).toBeGreaterThanOrEqual(0);
      expect(g).toBeLessThanOrEqual(1);
    }
    // Frequencies (tone pitch / noise filter centre) must be audible & positive.
    const freqEnv = v.kind === 'tone' ? v.freq : v.filterHz;
    for (const [, hz] of freqEnv) expect(hz).toBeGreaterThan(0);
  }

  for (const style of ALL_STYLES) {
    for (const d of [120, 500, 3000]) {
      it(`${style} @ ${d}ms: voices are well-formed`, () => {
        const plan = buildStartupSoundPlan(style, d);
        expect(plan.totalMs).toBeGreaterThanOrEqual(0);
        for (const v of plan.voices) assertVoiceWellFormed(v, plan.totalMs);
      });
    }
  }
});

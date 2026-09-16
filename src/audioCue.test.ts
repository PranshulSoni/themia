// The shared cue engine's PURE half: the peak-gain bound and the plan
// normalisation built on it. The imperative player is pinned in
// startupSound.player.test.ts.

import { describe, it, expect } from 'vitest';
import { normalizePlan, peakGainBound, planFrom, type SoundPlan, type Voice } from './audioCue.ts';

const tone = (startMs: number, endMs: number, gain: [number, number][]): Voice => ({
  kind: 'tone', type: 'sine', startMs, endMs, freq: [[0, 440]], gain,
});

describe('peakGainBound', () => {
  it('is zero for an empty plan', () => {
    expect(peakGainBound({ totalMs: 0, voices: [] })).toBe(0);
  });

  it('is a single voice\'s own peak when nothing overlaps it', () => {
    const plan = planFrom([
      tone(0, 100, [[0, 0], [10, 0.4], [100, 0]]),
      tone(200, 300, [[0, 0], [10, 0.6], [100, 0]]),
    ]);
    expect(peakGainBound(plan)).toBeCloseTo(0.6, 6);
  });

  it('SUMS voices that sound at the same instant', () => {
    const plan = planFrom([
      tone(0, 100, [[0, 0], [10, 0.4], [100, 0]]),
      tone(0, 100, [[0, 0], [10, 0.6], [100, 0]]),
    ]);
    expect(peakGainBound(plan)).toBeCloseTo(1.0, 6);
  });

  it('accounts for a decaying tail still sounding under a new strike', () => {
    // Strike 1 is at 0.5 and decays over 200 ms; strike 2 lands at 100 ms while
    // strike 1 is still at 0.25. The bound must see 0.75, not 0.5.
    const plan = planFrom([
      tone(0, 200, [[0, 0.5], [200, 0]]),
      tone(100, 300, [[0, 0.5], [200, 0]]),
    ]);
    expect(peakGainBound(plan)).toBeCloseTo(0.75, 6);
  });

  it('ignores a voice outside its own start/end window', () => {
    const plan = planFrom([tone(500, 600, [[0, 0], [10, 0.8], [100, 0]])]);
    expect(peakGainBound(plan)).toBeCloseTo(0.8, 6);
  });
});

describe('normalizePlan', () => {
  const plan = (): SoundPlan => planFrom([
    tone(0, 200, [[0, 0], [10, 0.5], [200, 0]]),
    tone(0, 200, [[0, 0], [10, 0.9], [200, 0]]),
  ]);

  it('scales the plan so its bound lands exactly on the target', () => {
    expect(peakGainBound(plan())).toBeCloseTo(1.4, 6);
    expect(peakGainBound(normalizePlan(plan(), 0.9))).toBeCloseTo(0.9, 6);
  });

  it('brings a too-QUIET plan UP as well as a too-loud one down', () => {
    const quiet = planFrom([tone(0, 100, [[0, 0], [10, 0.1], [100, 0]])]);
    expect(peakGainBound(normalizePlan(quiet, 0.9))).toBeCloseTo(0.9, 6);
  });

  it('preserves timing, voice count and relative balance', () => {
    const out = normalizePlan(plan(), 0.7);
    expect(out.totalMs).toBe(plan().totalMs);
    expect(out.voices.length).toBe(2);
    for (let i = 0; i < 2; i++) {
      expect(out.voices[i].startMs).toBe(plan().voices[i].startMs);
      expect(out.voices[i].endMs).toBe(plan().voices[i].endMs);
    }
    // The two voices keep their 0.5 : 0.9 ratio.
    const [a, b] = out.voices.map((v) => Math.max(...v.gain.map(([, g]) => g)));
    expect(b / a).toBeCloseTo(0.9 / 0.5, 6);
  });

  it('leaves a silent plan alone rather than dividing by zero', () => {
    const silent = planFrom([tone(0, 100, [[0, 0], [100, 0]])]);
    const out = normalizePlan(silent, 0.9);
    expect(out.voices[0].gain.every(([, g]) => g === 0)).toBe(true);
    expect(normalizePlan({ totalMs: 0, voices: [] }, 0.9).voices).toEqual([]);
  });

  it('does not mutate the input plan', () => {
    const original = plan();
    normalizePlan(original, 0.2);
    expect(peakGainBound(original)).toBeCloseTo(1.4, 6);
  });
});

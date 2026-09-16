// @vitest-environment jsdom
//
// [S-26] Startup-sound PLAYER, single-flight. The pure schedule builder is
// pinned in startupSound.test.ts; this file pins the imperative Web Audio glue
// that jsdom can't run for real, via a minimal fake AudioContext. The bug it
// guards: re-triggering a cue (rapid settings changes, or a change landing while
// the boot cue is still playing) used to stack a second cue on the first and they
// OVERLAPPED. A new cue must silence the previous one first.

import { describe, it, expect, beforeEach } from 'vitest';
import { playStartupSound, resetStartupSoundForTest } from './startupSound.ts';

// ── Minimal fake Web Audio graph: records the automation we assert on ──
type ParamEvent = [string, number, number?];
class FakeParam {
  value = 0;
  events: ParamEvent[] = [];
  setValueAtTime(v: number, t: number) { this.events.push(['set', v, t]); this.value = v; return this; }
  linearRampToValueAtTime(v: number, t: number) { this.events.push(['ramp', v, t]); this.value = v; return this; }
  cancelScheduledValues(t: number) { this.events.push(['cancel', t]); return this; }
}
class FakeGain { gain = new FakeParam(); connect(n: unknown) { return n; } disconnect() { /* noop */ } }
class FakeBiquad {
  type = 'bandpass'; Q = new FakeParam(); frequency = new FakeParam();
  connect(n: unknown) { return n; } disconnect() { /* noop */ }
}
class FakeOsc {
  type = 'sine'; frequency = new FakeParam();
  started = false; stopCount = 0; onended: (() => void) | null = null;
  connect(n: unknown) { return n; } disconnect() { /* noop */ }
  start() { this.started = true; }
  stop() { this.stopCount++; if (this.stopCount > 1) throw new Error('cannot re-stop'); }
}
class FakeBufferSrc {
  buffer: unknown = null; loop = false; stopCount = 0; onended: (() => void) | null = null;
  connect(n: unknown) { return n; } disconnect() { /* noop */ }
  start() { /* noop */ }
  stop() { this.stopCount++; if (this.stopCount > 1) throw new Error('cannot re-stop'); }
}
class FakeCtx {
  static last: FakeCtx | null = null;
  currentTime = 0; sampleRate = 48000; state = 'running';
  destination = {};
  gainNodes: FakeGain[] = [];
  constructor() { FakeCtx.last = this; }
  createGain() { const g = new FakeGain(); this.gainNodes.push(g); return g; }
  createOscillator() { return new FakeOsc(); }
  createBufferSource() { return new FakeBufferSrc(); }
  createBiquadFilter() { return new FakeBiquad(); }
  createBuffer(_c: number, len: number) { return { getChannelData() { return new Float32Array(len); } }; }
  resume() { return Promise.resolve(); }
}

const ON = { enabled: true, reducedMotion: false };

beforeEach(() => {
  resetStartupSoundForTest();
  FakeCtx.last = null;
  (globalThis as { AudioContext?: unknown }).AudioContext = FakeCtx;
});

describe('S-26 player single-flight: no overlapping cues', () => {
  it('a second cue silences the first (cancel + ramp to ~0) and uses its own master', () => {
    playStartupSound('hyperspace', 500, ON);
    const ctx = FakeCtx.last!;
    expect(ctx).toBeTruthy();
    const firstMaster = ctx.gainNodes[0]; // master is created before any voice gain
    const gainsAfterFirst = ctx.gainNodes.length;
    expect(firstMaster.gain.events).toHaveLength(0); // a lone cue never mutes itself

    playStartupSound('hyperspace', 500, ON);

    // The first cue's master was cancelled and ramped down to silence.
    const kinds = firstMaster.gain.events.map((e) => e[0]);
    expect(kinds).toContain('cancel');
    const ramps = firstMaster.gain.events.filter((e) => e[0] === 'ramp');
    expect(ramps.at(-1)![1]).toBeLessThanOrEqual(0.001);
    // The second cue mixes through a NEW master, not the muted one.
    expect(ctx.gainNodes.length).toBeGreaterThan(gainsAfterFirst);
  });

  it('reuses the one AudioContext across cues (no second context per replay)', () => {
    playStartupSound('scale', 400, ON);
    const first = FakeCtx.last;
    playStartupSound('hacker', 400, ON);
    expect(FakeCtx.last).toBe(first);
  });

  it('survives the engine refusing to re-stop a scheduled source', () => {
    // FakeOsc/FakeBufferSrc throw on a second stop() (like some real engines).
    // The mute is the guarantee; the early-stop is best-effort and must not throw.
    playStartupSound('hyperspace', 500, ON);
    expect(() => playStartupSound('hyperspace', 500, ON)).not.toThrow();
  });

  it('does nothing (no context) when disabled, reduced-motion, or style none', () => {
    playStartupSound('hyperspace', 500, { enabled: false, reducedMotion: false });
    expect(FakeCtx.last).toBeNull();
    playStartupSound('hyperspace', 500, { enabled: true, reducedMotion: true });
    expect(FakeCtx.last).toBeNull();
    playStartupSound('none', 500, ON);
    expect(FakeCtx.last).toBeNull();
  });
});

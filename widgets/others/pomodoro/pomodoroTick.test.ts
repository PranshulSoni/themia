// [S-03]/[S-06] the Pomodoro countdown reducer: the plain decrement, the phase
// flip that reloads the new phase's full duration, and the `flipped` signal the
// alarm hangs off. Pure, so the flip is reachable without running a widget for
// 25 minutes.

import { describe, it, expect } from 'vitest';
import { tickCountdown, type CountdownState } from './pomodoroTick.ts';

const WORK = 1500; // 25 min
const BREAK = 300; //  5 min

describe('S-03 countdown', () => {
  it('decrements without flipping while time remains', () => {
    expect(tickCountdown({ phase: 'work', remaining: 1500 }, WORK, BREAK))
      .toEqual({ phase: 'work', remaining: 1499, flipped: false });
    expect(tickCountdown({ phase: 'break', remaining: 2 }, WORK, BREAK))
      .toEqual({ phase: 'break', remaining: 1, flipped: false });
  });

  it('flips work → break at zero and loads the FULL break duration', () => {
    expect(tickCountdown({ phase: 'work', remaining: 1 }, WORK, BREAK))
      .toEqual({ phase: 'break', remaining: BREAK, flipped: true });
  });

  it('flips break → work at zero and loads the FULL work duration', () => {
    expect(tickCountdown({ phase: 'break', remaining: 1 }, WORK, BREAK))
      .toEqual({ phase: 'work', remaining: WORK, flipped: true });
  });

  it('flips exactly once per phase across a whole work→break→work cycle', () => {
    let state: CountdownState = { phase: 'work', remaining: 3 };
    const flips: string[] = [];
    for (let i = 0; i < 6; i++) {
      const next = tickCountdown(state, 2, 2);
      if (next.flipped) flips.push(next.phase);
      state = { phase: next.phase, remaining: next.remaining };
    }
    // 3 → 2 → 1 → flip(break,2) → 1 → flip(work,2) → 1
    expect(flips).toEqual(['break', 'work']);
  });

  it('a phase shorter than a tick still flips (remaining 1 is the last second)', () => {
    const next = tickCountdown({ phase: 'work', remaining: 1 }, 60, 60);
    expect(next.flipped).toBe(true);
    expect(next.remaining).toBe(60);
  });
});

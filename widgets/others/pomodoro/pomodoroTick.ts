// The Pomodoro countdown, as a pure reducer.
//
// The 1 Hz tick used to live inline in the component, which made [S-03] (the
// phase flip) only reachable through a running widget. It is now the one place
// that decides whether a tick rolled the timer into the next phase — the exact
// moment the alarm fires ([S-06]) — so both behaviours are unit-pinnable.

export type Phase = 'work' | 'break';

export interface CountdownState {
  phase: Phase;
  remaining: number;
}

export interface CountdownTick extends CountdownState {
  /** True only on the tick that rolled into the next phase ([S-03]). */
  flipped: boolean;
}

/**
 * Advance the countdown by one second. On reaching zero the phase flips and the
 * new phase's FULL duration is loaded, so the timer keeps running ([S-03]).
 * Durations are passed in (rather than read from config here) to keep this pure.
 */
export function tickCountdown(
  state: CountdownState,
  workSec: number,
  breakSec: number,
): CountdownTick {
  if (state.remaining > 1) {
    return { phase: state.phase, remaining: state.remaining - 1, flipped: false };
  }
  const next: Phase = state.phase === 'work' ? 'break' : 'work';
  return { phase: next, remaining: next === 'work' ? workSec : breakSec, flipped: true };
}

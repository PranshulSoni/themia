import { describe, it, expect } from 'vitest';
// Vite/Vitest `?raw` import (declared by vite/client): component source as a
// string, so the perf invariant can be asserted without node:fs types.
import SRC from './Clock.tsx?raw';

// Regression: the analog second hand once ran a free-running requestAnimationFrame
// loop (~60fps), repainting the blurred widget every frame, a heavy idle-CPU
// drain (~20% on the reporter's machine). [S-12] now caps the analog clock at one
// repaint per second: every hand derives from the single once-per-second `time()`
// signal and the second hand *steps* (`s * 6`) rather than sweeping.
describe('Clock analog repaint cadence (perf regression)', () => {
  it('[S-12] does not drive any hand with a requestAnimationFrame loop', () => {
    expect(SRC).not.toMatch(/requestAnimationFrame/);
  });

  it('[S-12] second hand reads the per-second angle, not a separate rAF signal', () => {
    expect(SRC).toMatch(/rotate\(\$\{angles\(\)\.second\}deg\)/);
    expect(SRC).not.toMatch(/secondAngle/);
  });
});

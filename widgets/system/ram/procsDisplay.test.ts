// [S-17] issue #55: the RAM top-process row count is configurable (parity with
// CPU's [S-26]/`clampProcCount`), but capped at 5 because the shared backend
// only returns up to 5 memory rows (`top_mem_procs` uses `.take(5)`).
import { describe, it, expect } from 'vitest';
import { clampRamProcCount, RAM_TOP_PROCS_DEFAULT, RAM_TOP_PROCS_MAX } from './procsDisplay.ts';

describe('clampRamProcCount [S-17]', () => {
  it('s55_defaults_to_3_when_unset', () => {
    expect(RAM_TOP_PROCS_DEFAULT).toBe(3);
    // Old persisted configs without the field must still load with the default.
    expect(clampRamProcCount(undefined)).toBe(3);
    expect(clampRamProcCount(null)).toBe(3);
  });

  it('s55_passes_through_integers_in_range', () => {
    expect(clampRamProcCount(1)).toBe(1);
    expect(clampRamProcCount(3)).toBe(3);
    expect(clampRamProcCount(5)).toBe(5);
  });

  it('s55_clamps_to_ram_max_of_5', () => {
    // Backend returns up to 5 memory rows; never surface a request for more.
    expect(RAM_TOP_PROCS_MAX).toBe(5);
    expect(clampRamProcCount(0)).toBe(1);
    expect(clampRamProcCount(-4)).toBe(1);
    expect(clampRamProcCount(6)).toBe(5);
    expect(clampRamProcCount(99)).toBe(5);
  });

  it('s55_rejects_non_numeric_junk_with_default', () => {
    expect(clampRamProcCount('4')).toBe(3);
    expect(clampRamProcCount(NaN)).toBe(3);
    expect(clampRamProcCount(Infinity)).toBe(3);
    expect(clampRamProcCount({})).toBe(3);
  });

  it('s55_rounds_fractional_persisted_values', () => {
    expect(clampRamProcCount(4.6)).toBe(5);
    expect(clampRamProcCount(2.4)).toBe(2);
  });
});

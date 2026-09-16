import { describe, it, expect } from 'vitest';
import { resolveGpuIndexes, selectedGpuIndexes, itemLayout } from './gpuSelection.ts';

// gpu [S-24]: the single `gpuIndex` picker was generalised to a `gpuIndexes`
// multi-select. Old configs (single number) must keep working after the change.
describe('resolveGpuIndexes — migration (gpu [S-24])', () => {
  it('uses the new gpuIndexes array when present', () => {
    expect(resolveGpuIndexes({ gpuIndexes: [0, 2] })).toEqual([0, 2]);
  });

  it('migrates the legacy single gpuIndex to a one-element array', () => {
    expect(resolveGpuIndexes({ gpuIndex: 1 })).toEqual([1]);
  });

  it('prefers the new field over the legacy field when both exist', () => {
    expect(resolveGpuIndexes({ gpuIndexes: [2, 3], gpuIndex: 0 })).toEqual([2, 3]);
  });

  it('preserves selection order (not sorted)', () => {
    expect(resolveGpuIndexes({ gpuIndexes: [2, 0, 1] })).toEqual([2, 0, 1]);
  });
});

// gpu [S-25]: config crosses the JSON trust boundary, so it is validated. Absent
// selection defaults to the primary GPU; corrupt entries are dropped, not fatal.
describe('resolveGpuIndexes — validation, absent vs corrupt (gpu [S-25])', () => {
  it('defaults to [0] for absent / null / undefined config', () => {
    expect(resolveGpuIndexes({})).toEqual([0]);
    expect(resolveGpuIndexes(null)).toEqual([0]);
    expect(resolveGpuIndexes(undefined)).toEqual([0]);
  });

  it('treats an empty gpuIndexes array as absent -> [0]', () => {
    expect(resolveGpuIndexes({ gpuIndexes: [] })).toEqual([0]);
  });

  it('drops non-integer / negative entries and dedupes, preserving order', () => {
    const cfg = { gpuIndexes: [0, -1, 2, 2, 1.5, 'x' as unknown as number] };
    expect(resolveGpuIndexes(cfg)).toEqual([0, 2]);
  });

  it('falls back to the legacy field when gpuIndexes is corrupt (not an array)', () => {
    expect(resolveGpuIndexes({ gpuIndexes: 'nope' as unknown, gpuIndex: 3 })).toEqual([3]);
  });

  it('falls back to [0] when both fields are corrupt', () => {
    expect(resolveGpuIndexes({ gpuIndexes: {} as unknown, gpuIndex: -5 })).toEqual([0]);
  });

  it('ignores a non-integer legacy gpuIndex', () => {
    expect(resolveGpuIndexes({ gpuIndex: 2.7 as unknown as number })).toEqual([0]);
  });
});

// gpu [S-28]: the config picker reflects the RAW selection — it must NOT apply
// resolveGpuIndexes' [0] default, or unticking the last GPU re-ticks the primary.
describe('selectedGpuIndexes — picker selection, no [0] default (gpu [S-28])', () => {
  it('reflects the current gpuIndexes selection', () => {
    expect(selectedGpuIndexes({ gpuIndexes: [1, 2] })).toEqual([1, 2]);
  });

  it('returns [] for an explicitly empty selection (last GPU unticked)', () => {
    expect(selectedGpuIndexes({ gpuIndexes: [], gpuIndex: undefined })).toEqual([]);
  });

  it('does not fall back to [0] for absent / null / undefined config', () => {
    expect(selectedGpuIndexes({})).toEqual([]);
    expect(selectedGpuIndexes(null)).toEqual([]);
    expect(selectedGpuIndexes(undefined)).toEqual([]);
  });

  it('migrates a legacy scalar when gpuIndexes is absent', () => {
    expect(selectedGpuIndexes({ gpuIndex: 1 })).toEqual([1]);
  });

  it('drops invalid entries and dedupes, preserving order', () => {
    expect(selectedGpuIndexes({ gpuIndexes: [2, -1, 2, 0] })).toEqual([2, 0]);
  });
});

// gpu [S-26]: one item stretches to fill (as before); many items overflow-scroll.
describe('itemLayout — stretch vs overflow (gpu [S-26])', () => {
  it('a single item stretches to fill', () => {
    expect(itemLayout(1)).toBe('stretch');
  });

  it('zero items still stretch (widget always renders the primary GPU)', () => {
    expect(itemLayout(0)).toBe('stretch');
  });

  it('two or more items overflow-scroll', () => {
    expect(itemLayout(2)).toBe('overflow');
    expect(itemLayout(5)).toBe('overflow');
  });
});

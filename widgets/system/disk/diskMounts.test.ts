import { describe, it, expect } from 'vitest';
import { resolveMounts, diskLayout, selectedDisks } from './diskMounts.ts';

// disk [S-23] config migration + [S-24] layout mode.
//
// #67: the Disk Monitor moved from a single `mount?: string` to `mounts?: string[]`.
// Old persisted configs must keep working (migrate `mount` -> `[mount]`), and the
// render layout switches on how many drives are selected (1 = stretch as today,
// N = stacked + overflow).

describe('resolveMounts (disk S-23 migration)', () => {
  it('S-23_ migrates a legacy single mount to a one-element array', () => {
    expect(resolveMounts({ mount: 'C:\\' })).toEqual(['C:\\']);
  });

  it('prefers an explicit mounts[] over a stale legacy mount', () => {
    expect(resolveMounts({ mount: 'C:\\', mounts: ['D:\\', 'E:\\'] })).toEqual(['D:\\', 'E:\\']);
  });

  it('respects an explicit empty mounts[] (absent vs empty are distinct)', () => {
    // mounts present but empty => user cleared the selection, do NOT re-migrate mount.
    expect(resolveMounts({ mount: 'C:\\', mounts: [] })).toEqual([]);
  });

  it('returns [] when neither mount nor mounts is present', () => {
    expect(resolveMounts({})).toEqual([]);
    expect(resolveMounts(null)).toEqual([]);
    expect(resolveMounts(undefined)).toEqual([]);
  });

  it('treats an empty legacy mount string as no selection', () => {
    expect(resolveMounts({ mount: '' })).toEqual([]);
  });

  it('drops non-string entries from a corrupt mounts array', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(resolveMounts({ mounts: ['C:\\', 42, null, 'D:\\'] as any })).toEqual(['C:\\', 'D:\\']);
  });
});

describe('diskLayout (disk S-24)', () => {
  it('S-24_ 0 or 1 selected drive => single (stretch to fill)', () => {
    expect(diskLayout(0)).toBe('single');
    expect(diskLayout(1)).toBe('single');
  });

  it('S-24_ 2+ selected drives => multi (stacked + overflow)', () => {
    expect(diskLayout(2)).toBe('multi');
    expect(diskLayout(5)).toBe('multi');
  });
});

describe('selectedDisks (disk S-23)', () => {
  const stats = {
    disks: [
      { mount: 'C:\\', used: 1, total: 2, read_per_sec: 100, write_per_sec: 10 },
      { mount: 'D:\\', used: 3, total: 4, read_per_sec: 200, write_per_sec: 20 },
      { mount: 'E:\\', used: 5, total: 6, read_per_sec: 300, write_per_sec: 30 },
    ],
  };

  it('resolves the selected mounts to their disks in selection order', () => {
    expect(selectedDisks(stats, ['E:\\', 'C:\\']).map(d => d.mount)).toEqual(['E:\\', 'C:\\']);
  });

  it('drops mounts with no matching backend disk', () => {
    expect(selectedDisks(stats, ['C:\\', 'Z:\\']).map(d => d.mount)).toEqual(['C:\\']);
  });

  it('returns [] when there are no disks', () => {
    expect(selectedDisks({ disks: [] }, ['C:\\'])).toEqual([]);
    expect(selectedDisks(null, ['C:\\'])).toEqual([]);
  });
});

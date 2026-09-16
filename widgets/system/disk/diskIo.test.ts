import { describe, it, expect } from 'vitest';
import { activeDisk, selectedDiskIo } from './diskIo.ts';

// disk [S-16], themia-releases#49: a single-partition Disk Monitor must show the
// read/write rate of the *selected* partition, not system-wide disk I/O. Before
// the fix the header + graph sourced `disk_read_per_sec`/`disk_write_per_sec`
// (system-wide), so four widgets on four partitions all mirrored the same value.

const stats = {
  // System-wide totals (deliberately different from every per-mount rate). The
  // bug returned these; the fix must ignore them for the header/graph.
  disk_read_per_sec: 9_000_000,
  disk_write_per_sec: 8_000_000,
  disks: [
    { mount: 'C:\\', used: 1, total: 2, read_per_sec: 100, write_per_sec: 10 },
    { mount: 'D:\\', used: 1, total: 2, read_per_sec: 200, write_per_sec: 20 },
    { mount: 'E:\\', used: 1, total: 2, read_per_sec: 300, write_per_sec: 30 },
  ],
};

describe('selectedDiskIo (disk S-16 / #49)', () => {
  it('returns the selected partition rate, not system-wide', () => {
    expect(selectedDiskIo(stats, 'D:\\')).toEqual({ read: 200, write: 20 });
  });

  it('each partition resolves to its own distinct rate (no mirroring)', () => {
    const rates = stats.disks.map(d => selectedDiskIo(stats, d.mount));
    expect(rates).toEqual([
      { read: 100, write: 10 },
      { read: 200, write: 20 },
      { read: 300, write: 30 },
    ]);
    // All distinct: the #49 symptom was all four being identical.
    expect(new Set(rates.map(r => r.read)).size).toBe(3);
  });

  it('unknown mount falls back to the first disk ([S-09])', () => {
    expect(selectedDiskIo(stats, 'Z:\\')).toEqual({ read: 100, write: 10 });
  });

  it('missing per-mount rates report 0 (no fallback to system-wide)', () => {
    const s = { disk_read_per_sec: 5_000, disk_write_per_sec: 4_000,
                disks: [{ mount: 'C:\\', used: 1, total: 2 }] };
    expect(selectedDiskIo(s, 'C:\\')).toEqual({ read: 0, write: 0 });
  });

  it('no disks -> 0/0', () => {
    expect(selectedDiskIo({ disks: [] }, 'C:\\')).toEqual({ read: 0, write: 0 });
    expect(selectedDiskIo(null, 'C:\\')).toEqual({ read: 0, write: 0 });
  });
});

describe('activeDisk (disk S-09)', () => {
  it('matches mount exactly', () => {
    expect(activeDisk(stats, 'E:\\')?.mount).toBe('E:\\');
  });
  it('falls back to first disk when no match', () => {
    expect(activeDisk(stats, 'nope')?.mount).toBe('C:\\');
  });
  it('null when no disks', () => {
    expect(activeDisk({ disks: [] }, 'C:\\')).toBeNull();
    expect(activeDisk(null, 'C:\\')).toBeNull();
  });
});

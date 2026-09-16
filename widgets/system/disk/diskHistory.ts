// Sparkline history for the Disk Monitor ([S-16], [S-22]).
//
// The read/write series track the *selected* partition's per-mount rate, not
// system-wide disk I/O (themia-releases#49). Switching the monitored mount
// clears both series so the graph never carries the previous partition's
// samples ([S-22]); the initial mount value never triggers a clear (`defer`),
// so a demo/seed set survives first render.

import { createSignal, createEffect, on, type Accessor } from 'solid-js';
import { selectedDiskIo, type DiskIoStats } from './diskIo.ts';

export interface DiskHistory {
  read: Accessor<number[]>;
  write: Accessor<number[]>;
  /** Append the selected partition's rate from a fresh stats sample. */
  push: (stats: DiskIoStats | null) => void;
}

export function createDiskHistory(
  mount: Accessor<string>,
  // `max` may be a live accessor so the series window can track a
  // configurable graph span ÷ poll interval, not just a fixed count.
  opts?: { max?: number | Accessor<number>; seed?: { read: number[]; write: number[] } },
): DiskHistory {
  const maxOf = () => {
    const m = typeof opts?.max === 'function' ? opts.max() : opts?.max;
    return typeof m === 'number' && Number.isFinite(m) && m >= 1 ? m : 40;
  };
  const [read, setRead]   = createSignal<number[]>(opts?.seed?.read ?? []);
  const [write, setWrite] = createSignal<number[]>(opts?.seed?.write ?? []);

  createEffect(on(mount, () => { setRead([]); setWrite([]); }, { defer: true }));

  function push(stats: DiskIoStats | null): void {
    const io = selectedDiskIo(stats, mount());
    setRead(h  => [...h, io.read].slice(-maxOf()));
    setWrite(h => [...h, io.write].slice(-maxOf()));
  }

  return { read, write, push };
}

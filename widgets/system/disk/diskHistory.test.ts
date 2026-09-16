// @vitest-environment jsdom
//
// disk [S-16] / [S-22] / themia-releases#49: widget-level wiring of the
// per-partition sparkline history:
//  - push() appends the *selected* partition's per-mount rate, so two widgets
//    on two mounts build different graphs (the #49 mirror is gone),
//  - the series is capped at `max`,
//  - changing the monitored mount clears both series [S-22],
//  - the initial mount value never clears a seed (`defer`).
//
// Mounted via solid-js/web `render` so the createEffect actually flushes (a
// bare createRoot under vitest never advances the scheduler, see
// manualRefresh.test.ts).

import { describe, it, expect, afterEach } from 'vitest';
import { render } from 'solid-js/web';
import { createSignal } from 'solid-js';
import { createDiskHistory, type DiskHistory } from './diskHistory.ts';

let dispose: (() => void) | null = null;
let host: HTMLDivElement | null = null;

afterEach(() => {
  dispose?.();
  dispose = null;
  host?.remove();
  host = null;
});

function mount(
  mountAcc: () => string,
  opts?: { max?: number; seed?: { read: number[]; write: number[] } },
): DiskHistory {
  host = document.createElement('div');
  document.body.appendChild(host);
  let hist!: DiskHistory;
  dispose = render(() => {
    hist = createDiskHistory(mountAcc, opts);
    return null;
  }, host);
  return hist;
}

const stats = {
  disk_read_per_sec: 9_000_000, // system-wide, must never appear in history
  disk_write_per_sec: 8_000_000,
  disks: [
    { mount: 'C:\\', used: 1, total: 2, read_per_sec: 100, write_per_sec: 10 },
    { mount: 'D:\\', used: 1, total: 2, read_per_sec: 200, write_per_sec: 20 },
  ],
};

describe('createDiskHistory (disk S-16 / #49)', () => {
  it('push appends the selected partition rate, not system-wide', () => {
    const hist = mount(() => 'D:\\');
    hist.push(stats);
    hist.push(stats);
    expect(hist.read()).toEqual([200, 200]);
    expect(hist.write()).toEqual([20, 20]);
  });

  it('two mounts build distinct series (no mirroring)', () => {
    const c = mount(() => 'C:\\');
    const d = mount(() => 'D:\\');
    c.push(stats);
    d.push(stats);
    expect(c.read()).toEqual([100]);
    expect(d.read()).toEqual([200]);
  });

  it('caps the series at max', () => {
    const hist = mount(() => 'C:\\', { max: 3 });
    for (let i = 0; i < 5; i++) hist.push(stats);
    expect(hist.read()).toEqual([100, 100, 100]);
  });

  it('S-22_ changing the mount clears both series', () => {
    const [m, setM] = createSignal('C:\\');
    const hist = mount(m);
    hist.push(stats);
    hist.push(stats);
    expect(hist.read().length).toBe(2);
    setM('D:\\');
    expect(hist.read()).toEqual([]);
    expect(hist.write()).toEqual([]);
    // and it resumes on the new partition
    hist.push(stats);
    expect(hist.read()).toEqual([200]);
  });

  it('the initial mount value does not clear a seed (defer)', () => {
    const hist = mount(() => 'C:\\', { seed: { read: [1, 2, 3], write: [4, 5, 6] } });
    expect(hist.read()).toEqual([1, 2, 3]);
    expect(hist.write()).toEqual([4, 5, 6]);
  });
});

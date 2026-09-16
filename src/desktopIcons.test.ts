// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest';
import { desktopIconsAdoption, watchDesktopIcons, DESKTOP_ICONS_POLL_MS } from './desktopIcons.ts';

function harness(opts?: { live?: (boolean | null)[]; fail?: boolean }) {
  const reads = opts?.live ?? [];
  let i = 0;
  let stored = true;
  let calls = 0;
  const applied: boolean[] = [];
  const errors: unknown[] = [];
  const w = watchDesktopIcons({
    read: () => {
      calls++;
      return opts?.fail
        ? Promise.reject(new Error('shell view not found'))
        : Promise.resolve(reads[Math.min(i++, reads.length - 1)] ?? null);
    },
    stored: () => stored,
    apply: (v) => { stored = v; applied.push(v); },
    onError: (e) => errors.push(e),
  });
  return {
    w, applied, errors,
    reads: () => calls,
    setStored: (v: boolean) => { stored = v; },
  };
}

function setVisibility(state: DocumentVisibilityState) {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

const flush = () => new Promise((r) => setTimeout(r, 0));

describe('[DI-04] desktopIconsAdoption', () => {
  it('adopts a live state that disagrees with the cache', () => {
    expect(desktopIconsAdoption(false, true)).toBe(false);
    expect(desktopIconsAdoption(true, false)).toBe(true);
  });

  it('does not write when the cache already agrees', () => {
    expect(desktopIconsAdoption(true, true)).toBeNull();
    expect(desktopIconsAdoption(false, false)).toBeNull();
  });

  it('keeps the cache when the shell state is unreadable', () => {
    expect(desktopIconsAdoption(null, true)).toBeNull();
    expect(desktopIconsAdoption(null, false)).toBeNull();
  });
});

describe('[DI-04] watchDesktopIcons', () => {
  it('adopts on explicit refresh', async () => {
    const h = harness({ live: [false] });
    h.w.refresh();
    await flush();
    expect(h.applied).toEqual([false]);
    h.w.dispose();
  });

  // Regression: the read used to happen only when the settings panel mounted.
  // The config window stays open while the user hides icons from the desktop
  // context menu, so the toggle kept showing the stale cached state.
  it('re-reads when the window regains focus', async () => {
    const h = harness({ live: [false] });
    window.dispatchEvent(new Event('focus'));
    await flush();
    expect(h.applied).toEqual([false]);
    h.w.dispose();
  });

  it('re-reads when the document becomes visible again', async () => {
    const h = harness({ live: [false] });
    setVisibility('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    await flush();
    expect(h.applied).toEqual([false]);
    h.w.dispose();
  });

  it('ignores a visibilitychange that hides the window', async () => {
    const h = harness({ live: [false] });
    setVisibility('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    await flush();
    expect(h.applied).toEqual([]);
    h.w.dispose();
  });

  it('logs and keeps the cache when the read fails', async () => {
    const h = harness({ fail: true });
    h.w.refresh();
    await flush();
    expect(h.applied).toEqual([]);
    expect(h.errors).toHaveLength(1);
    h.w.dispose();
  });

  it('stops listening after dispose', async () => {
    const h = harness({ live: [false] });
    h.w.dispose();
    window.dispatchEvent(new Event('focus'));
    await flush();
    expect(h.applied).toEqual([]);
  });
});

describe('[DI-04] desktop icon poll', () => {
  it('reads immediately on poll(true), then once per interval', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    const h = harness({ live: [true] });
    h.w.poll(true);
    expect(h.reads()).toBe(1);
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 3);
    expect(h.reads()).toBe(4);
    h.w.dispose();
  });

  it('picks up a change made while the page stays open and focused', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    // Icons shown, then hidden from the desktop context menu on tick 2.
    const h = harness({ live: [true, true, false] });
    h.w.poll(true);
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 2);
    expect(h.applied).toEqual([false]);
    h.w.dispose();
  });

  it('stops ticking when the page goes away', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    const h = harness({ live: [true] });
    h.w.poll(true);
    h.w.poll(false);
    const before = h.reads();
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 5);
    expect(h.reads()).toBe(before);
    h.w.dispose();
  });

  it('stops ticking while the window is hidden and resumes on return', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    const h = harness({ live: [true] });
    h.w.poll(true);
    setVisibility('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    const before = h.reads();
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 5);
    expect(h.reads()).toBe(before);

    setVisibility('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS);
    expect(h.reads()).toBeGreaterThan(before);
    h.w.dispose();
  });

  it('never stacks reads: a tick during a slow read is dropped', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    let calls = 0;
    let release: (v: boolean | null) => void = () => {};
    const w = watchDesktopIcons({
      read: () => { calls++; return new Promise((r) => { release = r; }); },
      stored: () => true,
      apply: () => {},
    });
    w.poll(true);
    expect(calls).toBe(1);
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 3);
    expect(calls).toBe(1);
    release(true);
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS);
    expect(calls).toBe(2);
    w.dispose();
  });

  it('leaves no timer behind after dispose', async () => {
    vi.useFakeTimers();
    setVisibility('visible');
    const h = harness({ live: [true] });
    h.w.poll(true);
    h.w.dispose();
    const before = h.reads();
    await vi.advanceTimersByTimeAsync(DESKTOP_ICONS_POLL_MS * 5);
    expect(h.reads()).toBe(before);
  });
});

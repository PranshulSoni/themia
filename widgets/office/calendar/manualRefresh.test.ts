// @vitest-environment jsdom
//
// [S-40] regression (manual Refresh was dead): the calendar context menu bumps
// the `_refresh` config token, but Calendar.tsx never tracked it, so Refresh
// did nothing. createManualRefresh pins the contract:
//  - fires once per token bump (caller re-fetches events + forces the
//    birthday cache),
//  - never fires on mount: a `_refresh` token persisted in config from a
//    previous session must not force contact enumeration on app start [S-38],
//  - the callback runs untracked: signals it reads must not become deps.
//
// Mounted via solid-js/web `render` so effects actually flush (bare createRoot
// under vitest never advances the scheduler; see createCachedFetch.test.ts).

import { describe, it, expect, afterEach } from 'vitest';
import { render } from 'solid-js/web';
import { createSignal } from 'solid-js';
import { createManualRefresh } from './manualRefresh.ts';

let dispose: (() => void) | null = null;
let host: HTMLDivElement | null = null;

afterEach(() => {
  dispose?.();
  dispose = null;
  host?.remove();
  host = null;
});

function mount(token: () => number | undefined, onRefresh: () => void): void {
  host = document.createElement('div');
  document.body.appendChild(host);
  dispose = render(() => {
    createManualRefresh(token, onRefresh);
    return null;
  }, host);
}

describe('createManualRefresh', () => {
  it('S-40_ does not fire on mount when no token is set', () => {
    const [tok] = createSignal<number | undefined>(undefined);
    let fired = 0;
    mount(tok, () => fired++);
    expect(fired).toBe(0);
  });

  it('S-40_ does not fire on mount with a persisted token, only on the next bump', () => {
    const [tok, setTok] = createSignal<number | undefined>(1_750_000_000_000);
    let fired = 0;
    mount(tok, () => fired++);
    expect(fired).toBe(0);
    setTok(1_750_000_000_001);
    expect(fired).toBe(1);
  });

  it('S-40_ fires once per bump, dedupes identical tokens', () => {
    const [tok, setTok] = createSignal<number | undefined>(undefined);
    let fired = 0;
    mount(tok, () => fired++);
    setTok(100);
    expect(fired).toBe(1);
    setTok(101);
    expect(fired).toBe(2);
    setTok(101);
    expect(fired).toBe(2);
  });

  it('S-40_ signals read inside the callback do not re-trigger it', () => {
    const [tok, setTok] = createSignal<number | undefined>(undefined);
    const [other, setOther] = createSignal(0);
    let fired = 0;
    mount(tok, () => { void other(); fired++; });
    setTok(100);
    expect(fired).toBe(1);
    setOther(1);
    expect(fired).toBe(1);
  });
});

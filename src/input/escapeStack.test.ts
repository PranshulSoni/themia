import { describe, it, expect, vi } from 'vitest';

vi.mock('../debugLog.ts', () => ({ debugLog: () => {} }));

import { pushEscape, onEscapeStackActive } from './escapeStack.ts';

describe('escapeStack active-change notifier (#14 set_escape_wanted)', () => {
  it('fires true only on 0->1 and false only on 1->0, once each', () => {
    const cb = vi.fn();
    onEscapeStackActive(cb);

    const offA = pushEscape(() => true); // 0 -> 1
    expect(cb.mock.calls).toEqual([[true]]);

    const offB = pushEscape(() => true); // 1 -> 2, no transition
    expect(cb.mock.calls).toEqual([[true]]);

    offB(); // 2 -> 1, no transition
    expect(cb.mock.calls).toEqual([[true]]);

    offA(); // 1 -> 0
    expect(cb.mock.calls).toEqual([[true], [false]]);

    onEscapeStackActive(() => {}); // reset the module callback for other tests
  });
});

// Tabs widget model: pins widgets/others/tabs/SPEC.md [S-02].

import { describe, it, expect } from 'vitest';
import { resolveActiveTabId } from './tabsModel.ts';

describe('tabs active resolution ([S-02])', () => {
  const kids = ['a', 'b', 'c'];

  it('a current child id wins', () => {
    expect(resolveActiveTabId(kids, 'c')).toBe('c');
  });

  it('S-02_stale/absent id → first child (a tab bar always has an active tab)', () => {
    expect(resolveActiveTabId(kids, 'gone')).toBe('a');
    expect(resolveActiveTabId(kids, undefined)).toBe('a');
    expect(resolveActiveTabId(kids, 42)).toBe('a');
  });

  it('S-02_empty container → null', () => {
    expect(resolveActiveTabId([], 'x')).toBe(null);
  });
});

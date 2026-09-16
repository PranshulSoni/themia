// The hidden-widget registry a self-hiding widget reports into (music [S-20]).
// The grid reads it for the tile's chrome; App.tsx reads it to drop the tile
// from the window hit-mask, so a stale id here means an invisible box that eats
// desktop clicks.

import { describe, it, expect, afterEach } from 'vitest';
import { hiddenWidgetIds, isWidgetHidden, setWidgetHidden } from './widgetVisibility.ts';

// Module state is shared across tests; clear whatever a case registered.
afterEach(() => {
  for (const id of [...hiddenWidgetIds()]) setWidgetHidden(id, false);
});

describe('S-20_widgetVisibility', () => {
  it('reports nothing hidden by default', () => {
    expect(hiddenWidgetIds()).toEqual([]);
    expect(isWidgetHidden('w1')).toBe(false);
  });

  it('records and clears a widget', () => {
    setWidgetHidden('w1', true);
    expect(isWidgetHidden('w1')).toBe(true);
    expect(hiddenWidgetIds()).toEqual(['w1']);

    setWidgetHidden('w1', false);
    expect(isWidgetHidden('w1')).toBe(false);
    expect(hiddenWidgetIds()).toEqual([]);
  });

  it('tracks widgets independently', () => {
    setWidgetHidden('w1', true);
    setWidgetHidden('w2', true);
    setWidgetHidden('w1', false);
    expect(isWidgetHidden('w1')).toBe(false);
    expect(isWidgetHidden('w2')).toBe(true);
  });

  it('is idempotent — a redundant call does not duplicate or churn the array', () => {
    setWidgetHidden('w1', true);
    const first = hiddenWidgetIds();
    setWidgetHidden('w1', true);
    // Same reference: dependents (the rect push) must not re-run on a no-op.
    expect(hiddenWidgetIds()).toBe(first);
    expect(hiddenWidgetIds()).toEqual(['w1']);
  });

  it('clearing an unknown id is a no-op', () => {
    const before = hiddenWidgetIds();
    setWidgetHidden('never-registered', false);
    expect(hiddenWidgetIds()).toBe(before);
  });
});

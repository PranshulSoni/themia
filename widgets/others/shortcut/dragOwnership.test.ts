// [S-35] The RECEIVER owns both halves of a cross-widget move; the drag SOURCE
// must never delete anything when `start_native_drag` resolves.
//
// The bug this pins: `startDrag(...).then(effect => { if (effect === 'move')
// remove the item })`. The OLE promise and the `native-drag` drop event are two
// independent messages into the webview, so the removal could run before the
// drop handler had re-added / reordered anything — dropping a shortcut back in
// its own list made it vanish. No amount of bookkeeping inside the drop handler
// fixes an ordering assumption, so the sender-side delete is gone entirely.
//
// Source-scanned rather than behavioural: the losing interleaving needs the real
// OLE loop, which neither vitest nor webdriver can drive (webdriver dispatches
// the router event directly and therefore always "wins" the race).

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const SRC = readFileSync(join(process.cwd(), 'widgets/others/shortcut/Shortcut.tsx'), 'utf8');

/** Strip comments, so prose ABOUT the old code can't satisfy (or trip) a scan. */
function code(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
}

/** Body of `startOleDrag`, i.e. everything the drag SOURCE runs. */
function startOleDragBody(): string {
  const start = SRC.indexOf('function startOleDrag(');
  expect(start, 'startOleDrag not found').toBeGreaterThan(-1);
  const end = SRC.indexOf('\n  function ', start + 1);
  return code(SRC.slice(start, end === -1 ? SRC.length : end));
}

describe('[S-35] the drag source never deletes items', () => {
  it('startOleDrag writes no item removal of its own', () => {
    const body = startOleDragBody();
    // `writeItems(items().filter(...))` in this body is the sender-side delete.
    expect(body).not.toMatch(/writeItems\s*\(\s*items\(\)\.filter/);
  });

  it('startOleDrag does not branch on the OLE move effect to mutate items', () => {
    const body = startOleDragBody();
    const movesOnEffect = /effect\s*===\s*'move'/.test(body);
    expect(movesOnEffect, "sender must not act on effect === 'move'").toBe(false);
  });

  it('the drag payload carries the source view, so the receiver can reach it', () => {
    // Without it the receiver cannot resolve the source widget's store when the
    // two widgets live in different monitor windows, and a move duplicates.
    expect(startOleDragBody()).toMatch(/sourceViewId:/);
  });

  it('the receiver removes the original', () => {
    expect(SRC).toMatch(/removeFromSource\(/);
  });
});

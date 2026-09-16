import { describe, it, expect } from 'vitest';
import {
  anyPinned, effectivePinned, isPinLayerLabel, isWidgetPinned, layerShowsWidget,
} from './pinLayer.ts';
import type { Widget } from './layout.ts';

const w = (id: string, extra: Partial<Widget> = {}): Widget => ({
  id, type: 'clock', x: 0, y: 0, w: 0.2, h: 0.2, ...extra,
});

describe('[S-45] pin layer labels', () => {
  it('recognises a pin overlay, not a base overlay', () => {
    expect(isPinLayerLabel('monitor-0-pin')).toBe(true);
    expect(isPinLayerLabel('monitor-12-pin')).toBe(true);
    expect(isPinLayerLabel('monitor-0')).toBe(false);
  });

  it('never claims a detached window', () => {
    for (const l of ['config', 'connect', 'widget-window', 'tray-flyout', 'layout']) {
      expect(isPinLayerLabel(l)).toBe(false);
    }
  });
});

describe('[S-45] pinned flag is strict', () => {
  it('only an exact true pins', () => {
    expect(isWidgetPinned(w('a', { config: { alwaysOnTop: true } }))).toBe(true);
    expect(isWidgetPinned(w('a', { config: { alwaysOnTop: 'yes' } }))).toBe(false);
    expect(isWidgetPinned(w('a', { config: { alwaysOnTop: 1 } }))).toBe(false);
    expect(isWidgetPinned(w('a', { config: {} }))).toBe(false);
    expect(isWidgetPinned(w('a'))).toBe(false);
    expect(isWidgetPinned(undefined)).toBe(false);
  });
});

describe('[S-45] children follow their top-level ancestor', () => {
  const group = w('g', { type: 'group', config: { alwaysOnTop: true } });
  const member = w('m', { parentId: 'g' });
  const loose = w('l', { config: { alwaysOnTop: true }, parentId: 'plain' });
  const plain = w('plain');
  const byId = new Map([group, member, loose, plain].map(x => [x.id, x]));

  it('a member of a pinned group is pinned', () => {
    expect(effectivePinned(member, byId)).toBe(true);
  });

  it("a child's own flag never overrides its unpinned ancestor", () => {
    expect(effectivePinned(loose, byId)).toBe(false);
  });

  it('a dangling parentId resolves to unpinned instead of throwing', () => {
    expect(effectivePinned(w('x', { parentId: 'gone', config: { alwaysOnTop: true } }), byId)).toBe(false);
  });

  it('a parent cycle terminates', () => {
    const a = w('a', { parentId: 'b' });
    const b = w('b', { parentId: 'a' });
    const cyc = new Map([a, b].map(x => [x.id, x]));
    expect(effectivePinned(a, cyc)).toBe(false);
  });
});

describe('[S-45] every widget lands on exactly one layer', () => {
  const cases: { pinned: boolean; editMode: boolean }[] = [
    { pinned: false, editMode: false },
    { pinned: false, editMode: true },
    { pinned: true,  editMode: false },
    { pinned: true,  editMode: true },
  ];

  it('is a partition: never both layers, never neither', () => {
    for (const c of cases) {
      const base = layerShowsWidget(c.pinned, false, c.editMode);
      const pin  = layerShowsWidget(c.pinned, true,  c.editMode);
      expect(base !== pin, `pinned=${c.pinned} edit=${c.editMode}`).toBe(true);
    }
  });

  it('pinned widgets float only outside edit mode', () => {
    expect(layerShowsWidget(true, true, false)).toBe(true);
    expect(layerShowsWidget(true, false, false)).toBe(false);
  });

  it('edit mode hands every widget back to the base layer', () => {
    expect(layerShowsWidget(true, false, true)).toBe(true);
    expect(layerShowsWidget(true, true, true)).toBe(false);
    expect(layerShowsWidget(false, true, true)).toBe(false);
  });
});

describe('[S-45] the pin overlay is built only when it has work', () => {
  it('is off for an empty or unpinned layout', () => {
    expect(anyPinned([])).toBe(false);
    expect(anyPinned([w('a'), w('b', { config: { alwaysOnTop: false } })])).toBe(false);
  });

  it('is on for one pinned top-level widget', () => {
    expect(anyPinned([w('a'), w('b', { config: { alwaysOnTop: true } })])).toBe(true);
  });

  it('a pinned CHILD alone does not build the layer (it follows its parent)', () => {
    expect(anyPinned([w('p'), w('c', { parentId: 'p', config: { alwaysOnTop: true } })])).toBe(false);
  });
});

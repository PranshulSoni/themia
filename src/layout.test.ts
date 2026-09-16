// @vitest-environment jsdom
//
// Characterization of the layout persistence schema: the data-integrity core
// that every persisted/imported/cross-window layout blob flows through. Pins
// [S-07] (percent geometry) + [S-13] (structural widget guard). Runs under jsdom
// because importing layout.ts pulls in sync.ts (window + BroadcastChannel).

import { describe, it, expect, vi } from 'vitest';
import {
  makeLayoutSchema, getViewLayoutStore, destroyViewLayoutStore, isContainerType, defaultLayout,
  isAreaGroupType, isGroupCollapsed, groupRenderRect, groupHeaderSide, groupBorderMode,
  areaGroupAtRect, areaChildRect, GROUP_HEADER_PX, TRAY_COLLECTION_ID, type Widget,
} from './layout.ts';
import { loadPersisted, type ReadableStorage } from './persistedStore.ts';

function fakeStorage(initial: Record<string, string> = {}): ReadableStorage {
  const map = new Map<string, string>(Object.entries(initial));
  return { getItem: (k: string) => (map.has(k) ? map.get(k)! : null) };
}
// Layout persists BARE (no envelope), so the stored value is the raw blob.
function stored(state: unknown): ReadableStorage {
  return fakeStorage({ k: JSON.stringify(state) });
}

describe('layout schema: load + migrate ([S-07] / [S-13])', () => {
  it('absent key → non-empty default layout, no corruption log', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', makeLayoutSchema(), fakeStorage(), { onCorrupt });
    expect(got.widgets.length).toBeGreaterThan(0);
    expect(onCorrupt).not.toHaveBeenCalled();
  });

  it('corrupt JSON → default + loud log (absent ≠ corrupt)', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted('k', makeLayoutSchema(), fakeStorage({ k: '{not json' }), { onCorrupt });
    expect(got.widgets.length).toBeGreaterThan(0);
    expect(onCorrupt).toHaveBeenCalledTimes(1);
  });

  it('keeps a structurally-valid percent widget verbatim', () => {
    const w = { id: 'a', type: 'clock', x: 0.1, y: 0.2, w: 0.3, h: 0.4 };
    const got = loadPersisted('k', makeLayoutSchema(), stored({ widgets: [w] }));
    expect(got.widgets).toEqual([w]);
  });

  it('drops widgets with missing / non-finite geometry or no id ([S-13])', () => {
    const good = { id: 'a', type: 'clock', x: 0.1, y: 0.2, w: 0.3, h: 0.4 };
    const missingH = { id: 'b', type: 'clock', x: 0.1, y: 0.2, w: 0.3 };
    const nanX = { id: 'c', type: 'clock', x: NaN, y: 0, w: 0.3, h: 0.4 };
    const noId = { type: 'clock', x: 0, y: 0, w: 0.3, h: 0.4 };
    const got = loadPersisted(
      'k',
      makeLayoutSchema(),
      stored({ widgets: [good, missingH, nanX, noId, null, 'nope'] }),
    );
    expect(got.widgets.map((w) => w.id)).toEqual(['a']);
  });

  it('migrates legacy cell geometry to finite fractions, dropping cell fields', () => {
    const legacy = { id: 'a', type: 'clock', col: 0, row: 0, colSpan: 6, rowSpan: 4 };
    const got = loadPersisted('k', makeLayoutSchema(), stored({ cols: 12, rows: 8, widgets: [legacy] }));
    expect(got.widgets).toHaveLength(1);
    const w = got.widgets[0];
    expect(w.id).toBe('a');
    for (const v of [w.x, w.y, w.w, w.h]) {
      expect(Number.isFinite(v)).toBe(true);
    }
    expect(w.x).toBeGreaterThanOrEqual(0);
    expect(w.w).toBeGreaterThan(0);
    expect(w.w).toBeLessThanOrEqual(1);
    expect('col' in w).toBe(false);
    expect('colSpan' in w).toBe(false);
  });

  it('non-array widgets → falls back to default widgets', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({ widgets: 'nope' }));
    expect(got.widgets.length).toBeGreaterThan(0);
  });
});

// ── Widget stacking: parentId model ([S-19]/[S-20]) ────────────────────────

const stackW  = { id: 's1', type: 'stack', x: 0.1, y: 0.1, w: 0.3, h: 0.5 };
const tabsW   = { id: 't1', type: 'tabs',  x: 0.6, y: 0.1, w: 0.3, h: 0.5 };
const childW  = (id: string, extra: Record<string, unknown> = {}) =>
  ({ id, type: 'clock', x: 0.2, y: 0.2, w: 0.1, h: 0.1, ...extra });

describe('isContainerType: the closed set of parent types ([S-19])', () => {
  it('S-19_stack, tabs and group are containers; nothing else is', () => {
    expect(isContainerType('stack')).toBe(true);
    expect(isContainerType('tabs')).toBe(true);
    expect(isContainerType('group')).toBe(true);
    for (const t of ['clock', 'folder', 'email', '', 'Stack', 'tab', 'groups']) {
      expect(isContainerType(t)).toBe(false);
    }
  });

  it('S-36_only group is an AREA container (the one that does not render its children)', () => {
    expect(isAreaGroupType('group')).toBe(true);
    for (const t of ['stack', 'tabs', 'clock', '']) expect(isAreaGroupType(t)).toBe(false);
  });
});

describe('layout schema: parentId sanitize ([S-20])', () => {
  it('keeps a parentId that references a stack widget in the same blob', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [stackW, childW('c1', { parentId: 's1' })],
    }));
    expect(got.widgets.find(w => w.id === 'c1')?.parentId).toBe('s1');
  });

  it('strips an orphan parentId (referenced widget missing), child survives top-level', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [childW('c1', { parentId: 'gone' })],
    }));
    const c = got.widgets.find(w => w.id === 'c1');
    expect(c).toBeDefined();
    expect(c && 'parentId' in c).toBe(false);
  });

  it('strips a parentId that references a non-stack widget', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [childW('not-a-stack'), childW('c1', { parentId: 'not-a-stack' })],
    }));
    expect(got.widgets.find(w => w.id === 'c1')?.parentId).toBeUndefined();
  });

  it('strips a self-referencing parentId', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [{ ...stackW, parentId: 's1' }],
    }));
    expect(got.widgets.find(w => w.id === 's1')?.parentId).toBeUndefined();
  });

  it('strips parentId from a stack widget (no nesting)', () => {
    const outer = { ...stackW };
    const inner = { id: 's2', type: 'stack', x: 0, y: 0, w: 0.2, h: 0.2, parentId: 's1' };
    const got = loadPersisted('k', makeLayoutSchema(), stored({ widgets: [outer, inner] }));
    expect(got.widgets.find(w => w.id === 's2')?.parentId).toBeUndefined();
  });

  it('strips a non-string parentId', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [stackW, childW('c1', { parentId: 42 })],
    }));
    expect(got.widgets.find(w => w.id === 'c1')?.parentId).toBeUndefined();
  });

  it('S-19_keeps a parentId that references a tabs container (tabs is a container type)', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [tabsW, childW('c1', { parentId: 't1' })],
    }));
    expect(got.widgets.find(w => w.id === 'c1')?.parentId).toBe('t1');
  });

  it('S-19_strips parentId from a tabs widget (containers never nest)', () => {
    const got = loadPersisted('k', makeLayoutSchema(), stored({
      widgets: [stackW, { ...tabsW, parentId: 's1' }],
    }));
    expect(got.widgets.find(w => w.id === 't1')?.parentId).toBeUndefined();
  });
});

describe('layout store: stack parenting ops ([S-19]/[S-21])', () => {
  let n = 0;
  /** Fresh store per test (unique view id) seeded with a stack + two children. */
  function freshStore(widgets: Widget[]) {
    const id = `vitest-stack-${++n}`;
    const s = getViewLayoutStore(id);
    s._seedWidgets(widgets);
    return { s, dispose: () => destroyViewLayoutStore(id) };
  }

  it('setWidgetParent sets parentId and moves the child to the array end (append order)', () => {
    const { s, dispose } = freshStore([
      childW('c1') as Widget, stackW as Widget, childW('c2') as Widget,
    ]);
    s.setWidgetParent('c1', 's1');
    const w = s.layout.widgets;
    expect(w.find(x => x.id === 'c1')?.parentId).toBe('s1');
    expect(w[w.length - 1].id).toBe('c1');
    dispose();
  });

  it('setWidgetParent refuses: stack-into-stack, self, missing parent, non-stack parent', () => {
    const { s, dispose } = freshStore([
      stackW as Widget,
      { id: 's2', type: 'stack', x: 0, y: 0, w: 0.2, h: 0.2 } as Widget,
      childW('c1') as Widget,
    ]);
    s.setWidgetParent('s2', 's1');        // stack into stack
    s.setWidgetParent('s1', 's1');        // self
    s.setWidgetParent('c1', 'missing');   // missing parent
    s.setWidgetParent('c1', 'c1');        // self / non-stack
    for (const w of s.layout.widgets) expect(w.parentId).toBeUndefined();
    dispose();
  });

  it('S-19_setWidgetParent accepts a tabs container as parent; refuses container nesting', () => {
    const { s, dispose } = freshStore([
      tabsW as Widget, stackW as Widget, childW('c1') as Widget,
    ]);
    s.setWidgetParent('c1', 't1');   // plain child into tabs → allowed
    expect(s.layout.widgets.find(x => x.id === 'c1')?.parentId).toBe('t1');
    s.setWidgetParent('s1', 't1');   // stack into tabs → refused (no nesting)
    expect(s.layout.widgets.find(x => x.id === 's1')?.parentId).toBeUndefined();
    dispose();
  });

  it('S-33_addChildWidget creates the widget already inside the container, at the end', () => {
    const { s, dispose } = freshStore([
      stackW as Widget, childW('c1', { parentId: 's1' }) as Widget,
    ]);
    const id = s.addChildWidget('notes', 's1', { content: 'x' });
    expect(id).toBeTruthy();
    const w = s.layout.widgets;
    const made = w.find(x => x.id === id)!;
    expect(made.parentId).toBe('s1');
    expect(made.config).toEqual({ content: 'x' });
    // Appended last, so it renders as the last child ([S-19] array order).
    expect(w[w.length - 1].id).toBe(id);
    // Inherits the container's rect as the dormant pop-out restore rect.
    expect(made.x).toBeCloseTo(0.1);
    expect(made.y).toBeCloseTo(0.1);
    expect(made.w).toBeCloseTo(0.3);
    expect(made.h).toBeCloseTo(0.5);
    dispose();
  });

  it('S-33_addChildWidget refuses a container type, a missing parent and a non-container parent', () => {
    const { s, dispose } = freshStore([stackW as Widget, childW('c1') as Widget]);
    const before = s.layout.widgets.length;
    expect(s.addChildWidget('stack', 's1')).toBeNull();   // no nesting
    expect(s.addChildWidget('tabs', 's1')).toBeNull();    // no nesting
    expect(s.addChildWidget('notes', 'missing')).toBeNull();
    expect(s.addChildWidget('notes', 'c1')).toBeNull();   // parent isn't a container
    // A refused add leaves NO stray top-level widget behind (one atomic write).
    expect(s.layout.widgets.length).toBe(before);
    dispose();
  });

  it('clearWidgetParent removes parentId (widget returns top-level, rect kept)', () => {
    const { s, dispose } = freshStore([
      stackW as Widget, childW('c1', { parentId: 's1' }) as Widget,
    ]);
    s.clearWidgetParent('c1');
    const c = s.layout.widgets.find(x => x.id === 'c1')!;
    expect(c.parentId).toBeUndefined();
    expect(c.x).toBeCloseTo(0.2);
    dispose();
  });

  it('S-35_moveChildTo drops a child at another sibling\'s position (drag-to-reorder)', () => {
    const { s, dispose } = freshStore([
      stackW as Widget,
      childW('c1', { parentId: 's1' }) as Widget,
      childW('c2', { parentId: 's1' }) as Widget,
      childW('c3', { parentId: 's1' }) as Widget,
    ]);
    const order = () => s.layout.widgets.filter(w => w.parentId === 's1').map(w => w.id);
    // Move first to last: a SWAP would give c3,c2,c1 — a drag must not do that.
    s.moveChildTo('c1', 'c3');
    expect(order()).toEqual(['c2', 'c3', 'c1']);
    // ...and back up to the front.
    s.moveChildTo('c1', 'c2');
    expect(order()).toEqual(['c1', 'c2', 'c3']);
    dispose();
  });

  it('S-35_moveChildTo leaves every non-sibling widget at its own index', () => {
    const { s, dispose } = freshStore([
      childW('top1') as Widget,
      stackW as Widget,
      childW('c1', { parentId: 's1' }) as Widget,
      tabsW as Widget,
      childW('t1kid', { parentId: 't1' }) as Widget,
      childW('c2', { parentId: 's1' }) as Widget,
      childW('top2') as Widget,
    ]);
    s.moveChildTo('c2', 'c1');
    const ids = s.layout.widgets.map(w => w.id);
    // Only the two stack children traded slots; the interleaved widgets stayed.
    expect(ids).toEqual(['top1', 's1', 'c2', 't1', 't1kid', 'c1', 'top2']);
    dispose();
  });

  it('S-35_moveChildTo refuses a non-sibling target, itself, and a top-level widget', () => {
    const { s, dispose } = freshStore([
      stackW as Widget, tabsW as Widget,
      childW('c1', { parentId: 's1' }) as Widget,
      childW('c2', { parentId: 's1' }) as Widget,
      childW('t1kid', { parentId: 't1' }) as Widget,
      childW('top1') as Widget,
    ]);
    const snapshot = s.layout.widgets.map(w => w.id);
    s.moveChildTo('c1', 't1kid');  // other container's child
    s.moveChildTo('c1', 'top1');   // top-level widget
    s.moveChildTo('c1', 'c1');     // itself
    s.moveChildTo('c1', 'gone');   // missing
    s.moveChildTo('top1', 'c1');   // dragging a non-child
    expect(s.layout.widgets.map(w => w.id)).toEqual(snapshot);
    dispose();
  });

  it('reorderChild swaps a child with its sibling neighbour, ignores ends', () => {
    const { s, dispose } = freshStore([
      stackW as Widget,
      childW('c1', { parentId: 's1' }) as Widget,
      childW('c2', { parentId: 's1' }) as Widget,
      childW('c3', { parentId: 's1' }) as Widget,
    ]);
    const order = () => s.layout.widgets.filter(w => w.parentId === 's1').map(w => w.id);
    s.reorderChild('c2', -1);
    expect(order()).toEqual(['c2', 'c1', 'c3']);
    s.reorderChild('c2', -1); // already first → no-op
    expect(order()).toEqual(['c2', 'c1', 'c3']);
    s.reorderChild('c1', 1);
    expect(order()).toEqual(['c2', 'c3', 'c1']);
    dispose();
  });

  it('S-21_removing a stack pops its children back to top-level (never deletes them)', () => {
    const { s, dispose } = freshStore([
      stackW as Widget,
      childW('c1', { parentId: 's1' }) as Widget,
      childW('c2', { parentId: 's1' }) as Widget,
    ]);
    s.removeWidget('s1');
    const ids = s.layout.widgets.map(w => w.id);
    expect(ids).toEqual(['c1', 'c2']);
    for (const w of s.layout.widgets) expect(w.parentId).toBeUndefined();
    dispose();
  });
});

// ── Area groups ([S-36]/[S-37]/[S-38]) ─────────────────────────────────────
//
// The area group is the container whose children stay on the grid. These pin
// the pure geometry rules and the three store ops that fork on it.

// A FACTORY, not a shared constant: seeding hands the object to the store and
// `produce` writes through it, so a shared literal would carry one test's move /
// resize into the next.
const groupW = (extra: Record<string, unknown> = {}) =>
  ({ id: 'g1', type: 'group', x: 0.2, y: 0.2, w: 0.4, h: 0.4, ...extra });
const AREA = { x: 0.2, y: 0.2, w: 0.4, h: 0.4 };
const SCREEN = { w: 1600, h: 1000 };

describe('area group geometry ([S-37]/[S-38])', () => {
  it('S-37_isGroupCollapsed is true only for a group carrying collapsed === true', () => {
    expect(isGroupCollapsed(groupW({ config: { collapsed: true } }))).toBe(true);
    expect(isGroupCollapsed(groupW())).toBe(false);
    expect(isGroupCollapsed(groupW({ config: { collapsed: 'yes' } }))).toBe(false);
    // A stack carrying the same key is not an area group.
    expect(isGroupCollapsed({ type: 'stack', config: { collapsed: true } })).toBe(false);
  });

  it('S-37_collapsed renders as the title bar alone; expanded renders the stored rect', () => {
    expect(groupRenderRect(AREA, false, SCREEN)).toEqual({ x: 0.2, y: 0.2, w: 0.4, h: 0.4 });
    const rolled = groupRenderRect(AREA, true, SCREEN);
    expect(rolled.h).toBeCloseTo(GROUP_HEADER_PX / SCREEN.h, 10);
    // Position + width untouched, so expanding restores the area exactly.
    expect([rolled.x, rolled.y, rolled.w]).toEqual([0.2, 0.2, 0.4]);
  });

  it('S-37_a group already shorter than the title bar never grows when collapsed', () => {
    const tiny = { x: 0, y: 0, w: 0.3, h: 0.01 };
    expect(groupRenderRect(tiny, true, SCREEN).h).toBe(0.01);
  });

  it('S-39_a collapsed group keeps the EDGE its header sits on', () => {
    const th = GROUP_HEADER_PX / SCREEN.h;
    const tw = GROUP_HEADER_PX / SCREEN.w;
    // bottom / right collapse TOWARD that edge: the bar stays put, the area
    // above / left of it is what the freed space uncovers.
    const bottom = groupRenderRect(AREA, true, SCREEN, 'bottom');
    expect(bottom.h).toBeCloseTo(th, 10);
    expect(bottom.y).toBeCloseTo(AREA.y + AREA.h - th, 10);
    const right = groupRenderRect(AREA, true, SCREEN, 'right');
    expect(right.w).toBeCloseTo(tw, 10);
    expect(right.x).toBeCloseTo(AREA.x + AREA.w - tw, 10);
    // left keeps x, shrinks width; top (default) keeps y, shrinks height.
    const left = groupRenderRect(AREA, true, SCREEN, 'left');
    expect([left.x, left.y, left.h]).toEqual([0.2, 0.2, 0.4]);
    expect(left.w).toBeCloseTo(tw, 10);
  });

  it('S-37_the collapsed bar renders at the FULL header height (the gap is added back)', () => {
    // The grid insets every tile by gap/2 per side, so the LOGICAL rect has to
    // carry the gap or the collapsed bar comes out shorter than the same bar
    // does while the group is open.
    for (const gap of [0, 8, 24]) {
      const rolled = groupRenderRect(AREA, true, SCREEN, 'top', gap);
      const renderedPx = rolled.h * SCREEN.h - gap;
      expect(renderedPx).toBeCloseTo(GROUP_HEADER_PX, 6);
    }
  });

  it('S-15_groupBorderMode hides the border only on an explicit `none`', () => {
    expect(groupBorderMode({ config: { borderMode: 'none' } })).toBe('none');
    for (const junk of [undefined, '', 'theme', 'hidden', 0, null, {}]) {
      expect(groupBorderMode({ config: { borderMode: junk } })).toBe('theme');
    }
    expect(groupBorderMode({})).toBe('theme');
  });

  it('S-39_groupHeaderSide accepts the four edges and falls back to top', () => {
    for (const s of ['top', 'bottom', 'left', 'right']) {
      expect(groupHeaderSide({ config: { headerSide: s } })).toBe(s);
    }
    for (const junk of [undefined, '', 'middle', 5, null, {}]) {
      expect(groupHeaderSide({ config: { headerSide: junk } })).toBe('top');
    }
    expect(groupHeaderSide({})).toBe('top');
  });

  it('S-38_membership is decided by the widget rect CENTRE, not by any corner', () => {
    const widgets = [groupW()] as Widget[];
    // Centre inside the area (0.2..0.6): joins, even though the top-left is out.
    expect(areaGroupAtRect(widgets, { x: 0.15, y: 0.15, w: 0.2, h: 0.2 })).toBe('g1');
    // Centre outside: no join, even though the rect overlaps the area.
    expect(areaGroupAtRect(widgets, { x: 0.55, y: 0.55, w: 0.3, h: 0.3 })).toBeNull();
  });

  it('S-38_a collapsed group is not a drop target, and a group never matches itself', () => {
    const inside = { x: 0.3, y: 0.3, w: 0.1, h: 0.1 };
    expect(areaGroupAtRect([groupW({ config: { collapsed: true } })] as Widget[], inside)).toBeNull();
    expect(areaGroupAtRect([groupW()] as Widget[], inside, 'g1')).toBeNull();
  });

  it('S-38_a nested / parented group is not a drop target', () => {
    const parented = groupW({ parentId: 'other' });
    expect(areaGroupAtRect([parented] as Widget[], { x: 0.3, y: 0.3, w: 0.1, h: 0.1 })).toBeNull();
  });

  it('S-36_areaChildRect keeps a new member inside the area and cascades it', () => {
    const area = { x: 0.2, y: 0.2, w: 0.4, h: 0.4 };
    const size = { w: 0.1, h: 0.1 };
    const first = areaChildRect(area, size, 0);
    const second = areaChildRect(area, size, 1);
    for (const r of [first, second]) {
      expect(r.x).toBeGreaterThanOrEqual(area.x);
      expect(r.y).toBeGreaterThanOrEqual(area.y);
      expect(r.x + r.w).toBeLessThanOrEqual(area.x + area.w + 1e-9);
      expect(r.y + r.h).toBeLessThanOrEqual(area.y + area.h + 1e-9);
    }
    expect(second.x).toBeGreaterThan(first.x); // the second doesn't hide the first
  });

  it('S-36_a member larger than the area is shrunk to fit it', () => {
    const area = { x: 0.1, y: 0.1, w: 0.2, h: 0.2 };
    const r = areaChildRect(area, { w: 0.9, h: 0.9 }, 0);
    expect(r.w).toBeLessThanOrEqual(area.w);
    expect(r.h).toBeLessThanOrEqual(area.h);
  });
});

describe('layout store: area group ops ([S-36]/[S-38])', () => {
  let n = 0;
  function freshStore(widgets: Widget[]) {
    const id = `vitest-group-${++n}`;
    const s = getViewLayoutStore(id);
    s._seedWidgets(widgets);
    return { s, dispose: () => destroyViewLayoutStore(id) };
  }
  const at = (s: ReturnType<typeof getViewLayoutStore>, id: string) =>
    s.layout.widgets.find(w => w.id === id)!;

  it('S-36_setWidgetParent applies the drop rect for an AREA group (live geometry)', () => {
    const { s, dispose } = freshStore([groupW() as Widget, childW('c1') as Widget]);
    s.setWidgetParent('c1', 'g1', { x: 0.31, y: 0.32, w: 0.1, h: 0.1 });
    expect(at(s, 'c1').parentId).toBe('g1');
    expect([at(s, 'c1').x, at(s, 'c1').y]).toEqual([0.31, 0.32]);
    dispose();
  });

  it('S-42_joining an area group leaves the widget IN PLACE in the array', () => {
    // Order is what the grid's `<For>` renders from: moving the entry re-inserts
    // the widget's DOM node, which reloads an iframe / restarts a canvas, so a
    // join would visibly "reload" the widget. Area members have no order anyway.
    const { s, dispose } = freshStore([
      groupW() as Widget, childW('c1') as Widget, childW('c2') as Widget,
    ]);
    s.setWidgetParent('c1', 'g1', { x: 0.3, y: 0.3, w: 0.1, h: 0.1 });
    expect(s.layout.widgets.map(w => w.id)).toEqual(['g1', 'c1', 'c2']);
    expect(at(s, 'c1').parentId).toBe('g1');
    dispose();
  });

  it('S-36_setWidgetParent ignores a rect for a stack (its children go dormant)', () => {
    const { s, dispose } = freshStore([stackW as Widget, childW('c1') as Widget]);
    s.setWidgetParent('c1', 's1', { x: 0.9, y: 0.9, w: 0.05, h: 0.05 });
    expect(at(s, 'c1').parentId).toBe('s1');
    expect([at(s, 'c1').x, at(s, 'c1').y]).toEqual([0.2, 0.2]); // restore rect kept
    dispose();
  });

  it('S-38_moving a group carries its members by the same delta', () => {
    const { s, dispose } = freshStore([
      groupW() as Widget,
      childW('c1', { parentId: 'g1', x: 0.25, y: 0.25 }) as Widget,
      childW('c2', { parentId: 'g1', x: 0.45, y: 0.35 }) as Widget,
      childW('top') as Widget,
    ]);
    s.moveWidget('g1', 0.3, 0.25); // +0.1 / +0.05
    expect(at(s, 'c1').x).toBeCloseTo(0.35, 10);
    expect(at(s, 'c1').y).toBeCloseTo(0.30, 10);
    expect(at(s, 'c2').x).toBeCloseTo(0.55, 10);
    expect(at(s, 'c2').y).toBeCloseTo(0.40, 10);
    // A widget that is not a member stays exactly where it was.
    expect([at(s, 'top').x, at(s, 'top').y]).toEqual([0.2, 0.2]);
    dispose();
  });

  it('S-38_resizing a group leaves its members alone (area only)', () => {
    const { s, dispose } = freshStore([
      groupW() as Widget,
      childW('c1', { parentId: 'g1', x: 0.25, y: 0.25 }) as Widget,
    ]);
    s.updateWidget('g1', { x: 0.2, y: 0.2, w: 0.7, h: 0.7 });
    expect([at(s, 'c1').x, at(s, 'c1').y, at(s, 'c1').w]).toEqual([0.25, 0.25, 0.1]);
    dispose();
  });

  it('S-38_a widget placed with its centre inside an area joins that group', () => {
    const { s, dispose } = freshStore([groupW() as Widget]);
    const id = s.addWidget('clock', { x: 0.3, y: 0.3, w: 0.1, h: 0.1 });
    expect(id).toBeTruthy();
    expect(at(s, id!).parentId).toBe('g1');
    dispose();
  });

  it('S-38_a widget placed outside every area stays top-level; a container never joins', () => {
    const { s, dispose } = freshStore([groupW() as Widget]);
    const outside = s.addWidget('clock', { x: 0.8, y: 0.8, w: 0.1, h: 0.1 });
    expect(at(s, outside!).parentId).toBeUndefined();
    const nested = s.addWidget('stack', { x: 0.3, y: 0.3, w: 0.1, h: 0.1 });
    expect(at(s, nested!).parentId).toBeUndefined();
    dispose();
  });

  it('S-36_addChildWidget places a new member INSIDE the area, not on the whole rect', () => {
    const { s, dispose } = freshStore([groupW() as Widget]);
    const id = s.addChildWidget('clock', 'g1');
    const c = at(s, id!);
    expect(c.parentId).toBe('g1');
    expect(c.w).toBeLessThan(AREA.w);
    expect(c.x).toBeGreaterThanOrEqual(AREA.x);
    expect(c.x + c.w).toBeLessThanOrEqual(AREA.x + AREA.w + 1e-9);
    dispose();
  });

  it('S-21_removing a group pops its members to top-level, keeping their places', () => {
    const { s, dispose } = freshStore([
      groupW() as Widget,
      childW('c1', { parentId: 'g1', x: 0.25, y: 0.25 }) as Widget,
    ]);
    s.removeWidget('g1');
    expect(at(s, 'c1').parentId).toBeUndefined();
    expect([at(s, 'c1').x, at(s, 'c1').y]).toEqual([0.25, 0.25]);
    dispose();
  });
});

describe('layout: defaultLayout seed ids are unique per view', () => {
  it('the Home (default) view keeps its canonical stable ids', () => {
    const ids = defaultLayout('default').widgets.map(w => w.id);
    expect(ids).toContain('clock-1');
    expect(ids).toContain('help-1');
    expect(ids).toContain('shortcut-default-1');
    expect(ids).toContain('folder-default-desktop');
  });

  // Regression: two user-created views used to be seeded with IDENTICAL ids
  // (clock-1, help-1, …), so findViewForWidget() mis-routed edits/deletes.
  it('two distinct user views get disjoint widget ids', () => {
    const a = defaultLayout('view-A').widgets.map(w => w.id);
    const b = defaultLayout('view-B').widgets.map(w => w.id);
    expect(a).not.toEqual(b);
    expect(a.some(id => b.includes(id))).toBe(false);
  });

  it('a user view does not collide with the Home view ids', () => {
    const home = new Set(defaultLayout('default').widgets.map(w => w.id));
    const user = defaultLayout('view-A').widgets.map(w => w.id);
    expect(user.some(id => home.has(id))).toBe(false);
  });
});

// Regression (Tray [S-17]): the tray collection went through the SAME seeding
// path as a user-created view, so a profile with no `widget-layout-tray` key got
// clock + help + shortcut as notification-area icons — on every fresh install,
// and on every e2e spec (each one clears localStorage, and the app is killed
// before it can hand the icons back, so a full run stacked up dozens).
describe('layout: the tray collection seeds empty (Tray [S-17])', () => {
  it('defaultLayout("tray") has no widgets', () => {
    expect(defaultLayout(TRAY_COLLECTION_ID).widgets).toEqual([]);
  });

  it('an absent widget-layout-tray key loads as an empty tray, not a seeded view', () => {
    const onCorrupt = vi.fn();
    const got = loadPersisted(
      'widget-layout-tray',
      makeLayoutSchema(TRAY_COLLECTION_ID),
      fakeStorage(),
      { onCorrupt },
    );
    expect(got.widgets).toEqual([]);
    expect(onCorrupt).not.toHaveBeenCalled();
  });

  it('a corrupt blob still yields an empty tray rather than seeded icons', () => {
    const got = makeLayoutSchema(TRAY_COLLECTION_ID).parse('not an object');
    expect(got.widgets).toEqual([]);
  });
});

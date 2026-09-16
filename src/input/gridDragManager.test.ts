// @vitest-environment jsdom
//
// Regression: paste/cut placement over an existing widget. A mousedown on the
// widget under the cursor used to start DRAGGING it (move mode) instead of
// letting the window-level placement click commit the paste. The app ended up
// in a weird half-placing / half-dragging state. startDrag/startResize now no-op
// while `placing()` is active (the placement click commits via onPlace).
import { describe, it, expect, vi } from 'vitest';
import { createGridDragManager, type GridDragManagerOptions } from './gridDragManager.ts';

function fakeGridEl(): HTMLElement {
  const el = document.createElement('div');
  el.getBoundingClientRect = () => ({
    left: 0, top: 0, width: 1000, height: 800, right: 1000, bottom: 800, x: 0, y: 0, toJSON: () => ({}),
  } as DOMRect);
  return el;
}

function makeMgr(placing: boolean) {
  const layoutStore = {
    moveWidget: vi.fn(),
    updateWidget: vi.fn(),
    layout: { widgets: [] },
    storageKey: 'widget-layout',
  } as unknown as GridDragManagerOptions['layoutStore'];
  return createGridDragManager({
    getGridEl: () => fakeGridEl(),
    editMode: () => true,
    placing: () => (placing ? { size: { w: 0.2, h: 0.2 } } : null),
    onPlace: vi.fn(),
    layoutStore,
  });
}

function makeMgrWith(widgets: unknown[]) {
  const layoutStore = {
    moveWidget: vi.fn(),
    updateWidget: vi.fn(),
    setWidgetParent: vi.fn(),
    clearWidgetParent: vi.fn(),
    layout: { widgets },
    storageKey: 'widget-layout',
  } as unknown as GridDragManagerOptions['layoutStore'];
  const mgr = createGridDragManager({
    getGridEl: () => fakeGridEl(),
    editMode: () => true,
    placing: () => null,
    onPlace: vi.fn(),
    layoutStore,
  });
  return { mgr, layoutStore };
}

const fire = (type: string, clientX: number, clientY: number) =>
  window.dispatchEvent(new MouseEvent(type, { button: 0, clientX, clientY }));

// Drop-onto-stack reparenting (src/SPEC.md [S-22]). Grid el is 1000×800 at
// origin, so fractional (0.7, 0.65) = client (700, 520).
describe('gridDragManager: drop onto a stack reparents ([S-22])', () => {
  const stack = { id: 's1', type: 'stack', x: 0.5, y: 0.5, w: 0.4, h: 0.4 };
  const clockIn = { id: 'c1', type: 'clock', x: 0.1, y: 0.1, w: 0.2, h: 0.2 };
  const stack2 = { id: 's2', type: 'stack', x: 0.1, y: 0.1, w: 0.2, h: 0.2 };
  const tabs = { id: 't1', type: 'tabs', x: 0.5, y: 0.5, w: 0.4, h: 0.4 };

  it('S-22_dropping a widget with the cursor inside a stack calls setWidgetParent, not moveWidget', () => {
    const { mgr, layoutStore } = makeMgrWith([stack, clockIn]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockIn);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe('s1');
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).toHaveBeenCalledWith('c1', 's1');
    expect(layoutStore.moveWidget).not.toHaveBeenCalled();
    expect(mgr.dropTarget()).toBe(null);
  });

  it('S-22_dragging a stack over a stack never reparents, plain move', () => {
    const { mgr, layoutStore } = makeMgrWith([stack, stack2]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), stack2);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });

  it('S-22_dropping a widget inside a tabs container reparents too (tabs is a container)', () => {
    const { mgr, layoutStore } = makeMgrWith([tabs, clockIn]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockIn);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe('t1');
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).toHaveBeenCalledWith('c1', 't1');
    expect(layoutStore.moveWidget).not.toHaveBeenCalled();
  });

  it('S-22_dragging a tabs container over a container never reparents, plain move', () => {
    const { mgr, layoutStore } = makeMgrWith([stack, tabs]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), tabs);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });

  it('drop outside any stack moves normally', () => {
    const { mgr, layoutStore } = makeMgrWith([stack, clockIn]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockIn);
    fire('mousemove', 300, 200);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 300, 200);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });
});

// Area groups ([S-38]): joining is decided by the dragged rect's CENTRE, and a
// join keeps the widget where it was dropped instead of swallowing it.
describe('gridDragManager: drop into an area group joins it ([S-38])', () => {
  const group = { id: 'g1', type: 'group', x: 0.5, y: 0.5, w: 0.4, h: 0.4 };
  const collapsed = { ...group, config: { collapsed: true } };
  const clockOut = { id: 'c1', type: 'clock', x: 0.1, y: 0.1, w: 0.2, h: 0.2 };
  const member = { id: 'm1', type: 'clock', x: 0.55, y: 0.55, w: 0.2, h: 0.2, parentId: 'g1' };
  const args = (fn: unknown) => (fn as ReturnType<typeof vi.fn>).mock.calls[0];

  it('S-38_dropping a widget whose centre lands in the area joins it AND keeps the drop rect', () => {
    const { mgr, layoutStore } = makeMgrWith([group, clockOut]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockOut);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe('g1');
    fire('mouseup', 700, 520);
    const [id, parentId, geom] = args(layoutStore.setWidgetParent);
    expect([id, parentId]).toEqual(['c1', 'g1']);
    // The drop rect travels with the join (snapped, hence the tolerance).
    expect(geom.x).toBeCloseTo(0.65, 1);
    expect(geom.y).toBeCloseTo(0.60, 1);
    expect(geom.w).toBeCloseTo(0.2, 1);
    expect(layoutStore.moveWidget).not.toHaveBeenCalled();
  });

  it('S-38_the CURSOR counts too: cursor inside, rect centre outside → still joins', () => {
    // Grabbed near its bottom-right, so the tile trails behind the cursor and
    // its centre never reaches the area. Requiring the centre here rejected a
    // drop the user plainly aimed at the group.
    const { mgr, layoutStore } = makeMgrWith([group, clockOut]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 290, clientY: 290 }), clockOut);
    fire('mousemove', 510, 410);
    expect(mgr.dropTarget()).toBe('g1');
    fire('mouseup', 510, 410);
    expect(args(layoutStore.setWidgetParent).slice(0, 2)).toEqual(['c1', 'g1']);
  });

  it('S-38_neither cursor nor centre in the area → plain move, no join', () => {
    const { mgr, layoutStore } = makeMgrWith([group, clockOut]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockOut);
    fire('mousemove', 300, 200);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 300, 200);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });

  it('S-38_a stack standing inside an area keeps the drop: the enclosing group must not steal it', () => {
    // Regression: the area was matched first, so a container drawn on top of a
    // group could no longer be filled by dragging a widget onto it.
    const stackInArea = { id: 's1', type: 'stack', x: 0.6, y: 0.6, w: 0.2, h: 0.2 };
    const { mgr, layoutStore } = makeMgrWith([group, stackInArea, clockOut]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockOut);
    fire('mousemove', 680, 550); // cursor on the stack, which sits in the group
    expect(mgr.dropTarget()).toBe('s1');
    fire('mouseup', 680, 550);
    expect(layoutStore.setWidgetParent).toHaveBeenCalledWith('c1', 's1');
  });

  it('S-38_dragging a member off its area removes it from the group, keeping the drop rect', () => {
    const { mgr, layoutStore } = makeMgrWith([group, member]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 600, clientY: 480 }), member);
    fire('mousemove', 200, 160);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 200, 160);
    const [id, geom] = args(layoutStore.clearWidgetParent);
    expect(id).toBe('m1');
    // It keeps the rect it was dropped on, out here on the desktop.
    expect(geom.x).toBeCloseTo(0.15, 1);
    expect(geom.y).toBeCloseTo(0.15, 1);
    expect(geom.w).toBeCloseTo(0.2, 1);
    expect(layoutStore.moveWidget).not.toHaveBeenCalled();
  });

  it('S-38_moving a member WITHIN its own area is a plain move, not a re-join', () => {
    const { mgr, layoutStore } = makeMgrWith([group, member]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 600, clientY: 480 }), member);
    fire('mousemove', 700, 560);
    expect(mgr.dropTarget()).toBe('g1');
    fire('mouseup', 700, 560);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.clearWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });

  it('S-38_a collapsed group is not a drop target', () => {
    const { mgr, layoutStore } = makeMgrWith([collapsed, clockOut]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 150, clientY: 120 }), clockOut);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });

  it('S-38_dragging a group over another group never joins (containers do not nest)', () => {
    const other = { id: 'g2', type: 'group', x: 0.05, y: 0.05, w: 0.2, h: 0.2 };
    const { mgr, layoutStore } = makeMgrWith([group, other]);
    mgr.startDrag(new MouseEvent('mousedown', { button: 0, clientX: 100, clientY: 80 }), other);
    fire('mousemove', 700, 520);
    expect(mgr.dropTarget()).toBe(null);
    fire('mouseup', 700, 520);
    expect(layoutStore.setWidgetParent).not.toHaveBeenCalled();
    expect(layoutStore.moveWidget).toHaveBeenCalled();
  });
});

// Side (edge) resize handles: each drags ONLY its own edge, the other axis and
// the opposite edge keep their exact values ([S-31]). Grid el is 1000×800 at
// origin, so a fraction f maps to client f*1000 / f*800.
describe('gridDragManager: side handles resize one edge only ([S-31])', () => {
  const widget = { id: 'w1', x: 0.2, y: 0.2, w: 0.3, h: 0.3 };
  const down = (clientX: number, clientY: number) =>
    new MouseEvent('mousedown', { button: 0, clientX, clientY });

  it('S-31_the right handle widens the widget and leaves x/y/h untouched', () => {
    const { mgr } = makeMgrWith([widget]);
    mgr.startResize(down(500, 280), widget, 'r');
    fire('mousemove', 700, 400);
    const pv = mgr.preview()!;
    expect(pv.x).toBe(0.2);
    expect(pv.y).toBe(0.2);
    expect(pv.h).toBeCloseTo(0.3, 10);
    expect(pv.w).toBeGreaterThan(0.3);
  });

  it('S-31_the left handle moves x and leaves y/h untouched', () => {
    const { mgr } = makeMgrWith([widget]);
    mgr.startResize(down(200, 280), widget, 'l');
    fire('mousemove', 60, 400);
    const pv = mgr.preview()!;
    expect(pv.y).toBe(0.2);
    expect(pv.h).toBeCloseTo(0.3, 10);
    expect(pv.x).toBeLessThan(0.2);
    expect(pv.w).toBeGreaterThan(0.3);
  });

  it('S-31_the bottom handle grows h and leaves x/y/w untouched', () => {
    const { mgr } = makeMgrWith([widget]);
    mgr.startResize(down(350, 400), widget, 'b');
    fire('mousemove', 200, 560);
    const pv = mgr.preview()!;
    expect(pv.x).toBe(0.2);
    expect(pv.y).toBe(0.2);
    expect(pv.w).toBeCloseTo(0.3, 10);
    expect(pv.h).toBeGreaterThan(0.3);
  });

  it('S-31_the top handle moves y and leaves x/w untouched', () => {
    const { mgr } = makeMgrWith([widget]);
    mgr.startResize(down(350, 160), widget, 't');
    fire('mousemove', 900, 60);
    const pv = mgr.preview()!;
    expect(pv.x).toBe(0.2);
    expect(pv.w).toBeCloseTo(0.3, 10);
    expect(pv.y).toBeLessThan(0.2);
    expect(pv.h).toBeGreaterThan(0.3);
  });

  it('S-31_a corner handle still moves both axes', () => {
    const { mgr } = makeMgrWith([widget]);
    mgr.startResize(down(500, 400), widget, 'br');
    fire('mousemove', 700, 560);
    const pv = mgr.preview()!;
    expect(pv.x).toBe(0.2);
    expect(pv.y).toBe(0.2);
    expect(pv.w).toBeGreaterThan(0.3);
    expect(pv.h).toBeGreaterThan(0.3);
  });
});

describe('gridDragManager: placement guards (paste/cut over a widget)', () => {
  const widget = { id: 'w1', x: 0.1, y: 0.1, w: 0.2, h: 0.2 };
  const mousedown = () => new MouseEvent('mousedown', { button: 0, clientX: 100, clientY: 100 });

  it('startDrag is a no-op while placing, the widget under the cursor does NOT enter move mode', () => {
    const mgr = makeMgr(true);
    mgr.startDrag(mousedown(), widget);
    expect(mgr.drag()).toBe(null);
  });

  it('startDrag works normally when not placing', () => {
    const mgr = makeMgr(false);
    mgr.startDrag(mousedown(), widget);
    expect(mgr.drag()?.id).toBe('w1');
  });

  it('startResize is a no-op while placing', () => {
    const mgr = makeMgr(true);
    mgr.startResize(mousedown(), widget, 'br');
    expect(mgr.resize()).toBe(null);
  });
});

// Regression (#98): the window-level placement click must only commit when it
// lands INSIDE the grid. The edit bar / view bar are DOM siblings of the grid
// (floating above it), so their clicks reach the same window listener; before
// the fix, clicking one dropped the widget at the stale ghost position ([S-11]).
describe('gridDragManager: placement click only commits inside the grid ([S-11])', () => {
  function stableGridEl(): HTMLElement {
    const el = document.createElement('div');
    el.getBoundingClientRect = () => ({
      left: 0, top: 0, width: 1000, height: 800, right: 1000, bottom: 800, x: 0, y: 0, toJSON: () => ({}),
    } as DOMRect);
    document.body.appendChild(el);
    return el;
  }

  function placingMgr(gridEl: HTMLElement) {
    const onPlace = vi.fn();
    const layoutStore = {
      moveWidget: vi.fn(),
      updateWidget: vi.fn(),
      layout: { widgets: [] },
      storageKey: 'widget-layout',
    } as unknown as GridDragManagerOptions['layoutStore'];
    createGridDragManager({
      getGridEl: () => gridEl,
      editMode: () => true,
      placing: () => ({ size: { w: 0.2, h: 0.2 } }),
      onPlace,
      layoutStore,
    });
    return onPlace;
  }

  it('a click on a sibling bar (outside the grid) does NOT commit the placement', () => {
    const gridEl = stableGridEl();
    const editBar = document.createElement('div');
    document.body.appendChild(editBar);
    const onPlace = placingMgr(gridEl);
    fire('mousemove', 500, 400); // ghost now tracks the cursor
    editBar.dispatchEvent(new MouseEvent('click', { button: 0, bubbles: true }));
    expect(onPlace).not.toHaveBeenCalled();
    document.body.removeChild(gridEl);
    document.body.removeChild(editBar);
  });

  it('a click inside the grid still commits the placement', () => {
    const gridEl = stableGridEl();
    const child = document.createElement('div');
    gridEl.appendChild(child);
    const onPlace = placingMgr(gridEl);
    fire('mousemove', 500, 400);
    child.dispatchEvent(new MouseEvent('click', { button: 0, bubbles: true }));
    expect(onPlace).toHaveBeenCalledTimes(1);
    document.body.removeChild(gridEl);
  });
});

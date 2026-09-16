/**
 * Widget drag/resize manager (percent layout model).
 *
 * Owns the state machine for dragging a widget around the screen, resizing via
 * corner + edge handles, and hovering a picker ghost during placement. All geometry is
 * in screen FRACTIONS (src/SPEC.md [S-01]); snapping to the optional grid aid is
 * applied only while interacting ([S-06]) and never changes stored values.
 *
 * Also owns the cross-display drag protocol: when the cursor leaves this window
 * during a drag, a `widget-cross-drag-move` message is posted (carrying the
 * widget's PIXEL size so the receiving window can preserve physical size,
 * [S-12]) so another window covering that screen position can render a remote
 * ghost and accept the drop via `widget-cross-drop`.
 */

import { createSignal, createEffect, onCleanup, type Accessor } from 'solid-js';
import {
  addWidget,
  removeWidgetFromKey,
  areaGroupAtRect,
  isAreaGroupType,
  isContainerType,
  type ViewLayoutStore,
} from '../layout.ts';
import {
  snapRect,
  snapFrac,
  clampRect,
  clamp01,
  minFrac,
  gridDims,
  screenSize,
  fracSizeToPx,
  type FracRect,
} from '../gridGeometry.ts';
import { globalDesign } from '../design.ts';
import { postTransient, onTransient } from '../sync.ts';
import { ctxMenu } from '../ctxMenuStore.ts';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type Corner = 'tl' | 'tr' | 'bl' | 'br';
/** Side handles: resize along a single axis, the other axis is untouched. */
export type Edge = 't' | 'r' | 'b' | 'l';
export type ResizeHandle = Corner | Edge;

/** Which edges of the rect each handle moves; `null` = that axis is frozen. */
const HANDLE_MOVES: Record<ResizeHandle, { x: 'left' | 'right' | null; y: 'top' | 'bottom' | null }> = {
  tl: { x: 'left',  y: 'top'    },
  tr: { x: 'right', y: 'top'    },
  bl: { x: 'left',  y: 'bottom' },
  br: { x: 'right', y: 'bottom' },
  t:  { x: null,    y: 'top'    },
  b:  { x: null,    y: 'bottom' },
  l:  { x: 'left',  y: null     },
  r:  { x: 'right', y: null     },
};

export interface DragState {
  id: string;
  w: number;
  h: number;
  /** Grab point offset within the widget, in fractions. */
  offX: number;
  offY: number;
}

export interface ResizeState {
  id: string;
  handle: ResizeHandle;
  /** The widget's fractional edges at resize start. */
  left: number;
  top: number;
  right: number;
  bottom: number;
  /** Cursor fractional position at grab, so the edge moves by the delta (a
   *  click with no movement leaves the widget unchanged). */
  grabX: number;
  grabY: number;
}

export type GhostRect = FracRect;

export interface WidgetRect {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface PlacingSpec {
  /** Fractional size of the widget being placed. */
  size: { w: number; h: number };
}

export interface GridDragManagerOptions {
  /** Accessor returning the grid DOM element (may be undefined until mount). */
  getGridEl: () => HTMLElement | undefined;
  /** Is the grid currently in edit mode? */
  editMode: () => boolean;
  /** Active picker placement (widget being positioned), or null. */
  placing: () => PlacingSpec | null;
  /** Called when a placement click commits the ghost position (fractions). */
  onPlace: (x: number, y: number) => void;
  /** The per-view layout store this grid is bound to. */
  layoutStore: ViewLayoutStore;
}

export interface GridDragManager {
  drag: Accessor<DragState | null>;
  resize: Accessor<ResizeState | null>;
  preview: Accessor<GhostRect | null>;
  remoteGhost: Accessor<GhostRect | null>;
  /** Container widget id the drop would land in: a stack/tabs under the cursor
   *  ([S-22]) or an area group holding the dragged rect's centre ([S-38]).
   *  Null when no eligible container is hovered. */
  dropTarget: Accessor<string | null>;
  startDrag: (e: MouseEvent, widget: WidgetRect) => void;
  startResize: (e: MouseEvent, widget: WidgetRect, handle: ResizeHandle) => void;
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

export function createGridDragManager(opts: GridDragManagerOptions): GridDragManager {
  const [drag, setDrag] = createSignal<DragState | null>(null);
  const [resize, setResize] = createSignal<ResizeState | null>(null);
  const [preview, setPreview] = createSignal<GhostRect | null>(null);
  const [remoteGhost, setRemoteGhost] = createSignal<GhostRect | null>(null);
  const [dropTarget, setDropTarget] = createSignal<string | null>(null);
  const [altHeld, setAltHeld] = createSignal(false);

  /**
   * Top-level container the drop would land in. Null when the dragged widget is
   * itself a container (no nesting).
   *
   * The two container kinds ask different questions, because they mean
   * different things by "in":
   *   - a **stack / tabs** swallows the widget whole, so the CURSOR picks the
   *     target ([S-22]);
   *   - an **area group** only claims the widget where it lies, so its rect
   *     CENTRE counts too ([S-38]).
   *
   * A stack/tabs under the cursor WINS. Nothing stops one from standing inside
   * an area group's rect (a container is never a member — containers don't
   * nest), and the enclosing area must not steal a drop the user aimed at the
   * tile drawn on top of it.
   */
  function containerUnder(fx: number, fy: number, dragId: string, pv: GhostRect | null): string | null {
    const widgets = opts.layoutStore.layout.widgets;
    const dragged = widgets.find(w => w.id === dragId);
    if (!dragged || isContainerType(dragged.type)) return null;
    for (const w of widgets) {
      if (!isContainerType(w.type) || isAreaGroupType(w.type)) continue;
      if (w.parentId !== undefined || w.id === dragId) continue;
      if (fx >= w.x && fx <= w.x + w.w && fy >= w.y && fy <= w.y + w.h) return w.id;
    }
    return pv ? areaGroupAtRect(widgets, pv, dragId, { x: fx, y: fy }) : null;
  }

  const moveWidget = opts.layoutStore.moveWidget;
  const updateWidget = opts.layoutStore.updateWidget;

  /** Snap is active when the global aid is on AND Alt isn't held ([S-06]). */
  const snapOn = () => globalDesign.gridSnap && !altHeld();
  const snap = (r: FracRect) => snapRect(r, snapOn());

  /**
   * Resize rect from the grabbed handle only. The fixed edges stay anchored, so
   * hitting the min size shrinks the widget toward its anchor without shoving
   * its position around ([S-08]). A corner moves one edge per axis; a side
   * handle moves a single edge and leaves the other axis exactly as it was
   * ([S-31]). The moving edge tracks the cursor by its DELTA from the grab
   * point (so a click with no movement is a no-op), then is snapped +
   * screen-clamped and held at least the min size from its fixed edge.
   */
  function resizeRect(r: ResizeState, fx: number, fy: number): FracRect {
    const on = snapOn();
    const { cols, rows } = gridDims();
    const m = minFrac();
    const dx = fx - r.grabX;
    const dy = fy - r.grabY;
    let { left, top, right, bottom } = r;
    const moves = HANDLE_MOVES[r.handle];
    if (moves.x === 'left') {
      let nl = clamp01(r.left + dx);
      if (on) nl = snapFrac(nl, cols);
      left = Math.min(nl, right - m.w);
    } else if (moves.x === 'right') {
      let nr = clamp01(r.right + dx);
      if (on) nr = snapFrac(nr, cols);
      right = Math.max(nr, left + m.w);
    }
    if (moves.y === 'top') {
      let nt = clamp01(r.top + dy);
      if (on) nt = snapFrac(nt, rows);
      top = Math.min(nt, bottom - m.h);
    } else if (moves.y === 'bottom') {
      let nb = clamp01(r.bottom + dy);
      if (on) nb = snapFrac(nb, rows);
      bottom = Math.max(nb, top + m.h);
    }
    return { x: left, y: top, w: right - left, h: bottom - top };
  }

  // Clear the ghost when placement ends.
  createEffect(() => {
    if (!opts.placing()) setPreview(null);
  });

  /** Cursor → fractional point within the grid element. */
  function fracAt(clientX: number, clientY: number): { fx: number; fy: number } {
    const gridEl = opts.getGridEl();
    if (!gridEl) return { fx: 0, fy: 0 };
    const r = gridEl.getBoundingClientRect();
    return {
      fx: (clientX - r.left) / Math.max(1, r.width),
      fy: (clientY - r.top) / Math.max(1, r.height),
    };
  }

  function startDrag(e: MouseEvent, widget: WidgetRect): void {
    if (!opts.editMode() || e.button !== 0) return;
    // During a paste/cut placement, a mousedown on the widget under the cursor
    // must NOT start dragging it; the window-level click commits the placement.
    // Without this guard the underlying widget jumps into move mode on paste.
    if (opts.placing()) return;
    if (ctxMenu.menu) return;
    e.preventDefault();
    const { fx, fy } = fracAt(e.clientX, e.clientY);
    setDrag({
      id: widget.id,
      w: widget.w,
      h: widget.h,
      offX: fx - widget.x,
      offY: fy - widget.y,
    });
    setPreview({ x: widget.x, y: widget.y, w: widget.w, h: widget.h });
  }

  function startResize(e: MouseEvent, widget: WidgetRect, handle: ResizeHandle): void {
    if (e.button !== 0) return;
    // Don't grab a resize handle during a paste/cut placement (it would swallow
    // the placement click + start resizing the underlying widget instead).
    if (opts.placing()) return;
    e.preventDefault();
    e.stopPropagation();
    const { fx, fy } = fracAt(e.clientX, e.clientY);
    setResize({
      id: widget.id,
      handle,
      left: widget.x,
      top: widget.y,
      right: widget.x + widget.w,
      bottom: widget.y + widget.h,
      grabX: fx,
      grabY: fy,
    });
    setPreview({ x: widget.x, y: widget.y, w: widget.w, h: widget.h });
  }

  function onMouseMove(e: MouseEvent): void {
    const placing = opts.placing();
    if (placing) {
      const { fx, fy } = fracAt(e.clientX, e.clientY);
      setPreview(snap({ x: fx, y: fy, w: placing.size.w, h: placing.size.h }));
      return;
    }
    const d = drag();
    const r = resize();
    if (d) {
      const outside = e.clientX < 0 || e.clientY < 0 || e.clientX > window.innerWidth || e.clientY > window.innerHeight;
      if (outside) {
        // Cursor left this window: hide local ghost and broadcast (px size) so
        // other windows can show their own ghost at the appropriate screen pos.
        setPreview(null);
        setDropTarget(null);
        const sz = fracSizeToPx({ w: d.w, h: d.h });
        const scr = screenSize();
        postTransient({
          type: 'widget-cross-drag-move',
          screenX: e.screenX,
          screenY: e.screenY,
          wPx: sz.width,
          hPx: sz.height,
          offsetX: d.offX * scr.w,
          offsetY: d.offY * scr.h,
          dragId: d.id,
        });
        return;
      }
      const { fx, fy } = fracAt(e.clientX, e.clientY);
      const pv = snap({ x: fx - d.offX, y: fy - d.offY, w: d.w, h: d.h });
      setPreview(pv);
      setDropTarget(containerUnder(fx, fy, d.id, pv));
      // Cursor returned to this window: tell other windows to drop their ghost.
      postTransient({ type: 'widget-cross-drag-end', dragId: d.id });
    } else if (r) {
      const { fx, fy } = fracAt(e.clientX, e.clientY);
      setPreview(resizeRect(r, fx, fy));
    }
  }

  function onMouseUp(e: MouseEvent): void {
    if (opts.placing()) return;
    // Only the primary (left) button that started the gesture commits it. A
    // right-click mid-drag (e.g. to open a context menu) must not drop the
    // widget; leave drag/resize state alive for the real left mouseup, matching
    // the `e.button !== 0` guards in startDrag/startResize.
    if (e.button !== 0) return;
    const d = drag();
    const r = resize();
    const pv = preview();

    // Cross-display drop: if the widget drag ended outside this window's
    // bounds, broadcast it so a window covering that screen position can
    // receive and adopt the widget (preserving physical px size, [S-12]).
    if (d && (e.clientX < 0 || e.clientY < 0 || e.clientX > window.innerWidth || e.clientY > window.innerHeight)) {
      const w = opts.layoutStore.layout.widgets.find(ww => ww.id === d.id);
      if (w) {
        const sz = fracSizeToPx({ w: w.w, h: w.h });
        const scr = screenSize();
        postTransient({
          type: 'widget-cross-drop',
          screenX: e.screenX,
          screenY: e.screenY,
          offsetX: d.offX * scr.w,
          offsetY: d.offY * scr.h,
          wPx: sz.width,
          hPx: sz.height,
          widget: {
            type: w.type,
            config: w.config ? JSON.parse(JSON.stringify(w.config)) : undefined,
            label: w.label,
          },
          sourceId: w.id,
          sourceKey: opts.layoutStore.storageKey,
        });
        postTransient({ type: 'widget-cross-drag-end', dragId: d.id });
        setDrag(null);
        setResize(null);
        setPreview(null);
        return;
      }
    }

    if (d) postTransient({ type: 'widget-cross-drag-end', dragId: d.id });

    // Drop with the cursor inside a stack reparents instead of moving ([S-22]).
    // An area group instead JOINS the widget and keeps it where it was dropped
    // ([S-38]) — and a member dropped off its area leaves the group, so the
    // membership a user sees is always the membership stored.
    const target = dropTarget();
    const widgets = opts.layoutStore.layout.widgets;
    const dragged = d ? widgets.find(w => w.id === d.id) : undefined;
    const targetIsArea = !!target && isAreaGroupType(widgets.find(w => w.id === target)?.type ?? '');
    const leavesArea = !!dragged?.parentId
      && dragged.parentId !== target
      && isAreaGroupType(widgets.find(w => w.id === dragged.parentId)?.type ?? '');
    if (d && target && targetIsArea && dragged?.parentId !== target && pv) {
      opts.layoutStore.setWidgetParent(d.id, target, pv);
    } else if (d && target && !targetIsArea) {
      opts.layoutStore.setWidgetParent(d.id, target);
    } else if (d && leavesArea && pv) {
      opts.layoutStore.clearWidgetParent(d.id, pv);
    } else if (d && pv) {
      moveWidget(d.id, pv.x, pv.y);
    }
    if (r && pv) updateWidget(r.id, pv);
    setDrag(null);
    setResize(null);
    setPreview(null);
    setDropTarget(null);
  }

  function onClick(e: MouseEvent): void {
    if (!opts.placing()) return;
    // Only a click that lands INSIDE the grid commits the placement. The edit
    // bar and view bar are siblings of the grid element (floating above it), so
    // a click on them reaches this window-level listener too; without this guard
    // it would drop the widget at the stale ghost position the user never chose.
    const gridEl = opts.getGridEl();
    if (gridEl && e.target instanceof Node && !gridEl.contains(e.target)) return;
    const pv = preview();
    if (pv) {
      opts.onPlace(pv.x, pv.y);
      setPreview(null);
    }
  }

  function onWindowMouseOut(e: MouseEvent): void {
    if (!opts.placing()) return;
    // `toElement` is a legacy, non-standard alias for `relatedTarget` not in
    // the DOM lib types; narrow to read it without `any`.
    const toElement = (e as MouseEvent & { toElement?: EventTarget | null }).toElement;
    if (e.relatedTarget == null && toElement == null) {
      setPreview(null);
    }
  }

  function onWindowBlur(): void {
    if (opts.placing()) setPreview(null);
    setAltHeld(false);
  }

  function onKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Alt') setAltHeld(true);
  }
  function onKeyUp(e: KeyboardEvent): void {
    if (e.key === 'Alt') setAltHeld(false);
  }

  /** Place a remote ghost / compute a drop rect from a cross-window message. */
  function remoteRect(msg: { screenX: number; screenY: number; wPx: number; hPx: number; offsetX: number; offsetY: number }): FracRect | null {
    const gridEl = opts.getGridEl();
    if (!gridEl) return null;
    const cx = msg.screenX - window.screenX;
    const cy = msg.screenY - window.screenY;
    if (cx < 0 || cy < 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
    const r = gridEl.getBoundingClientRect();
    if (cx < r.left || cx > r.right || cy < r.top || cy > r.bottom) return null;
    const fx = (cx - r.left) / Math.max(1, r.width);
    const fy = (cy - r.top) / Math.max(1, r.height);
    // Preserve physical size: source px → fraction of THIS screen ([S-12]).
    const w = msg.wPx / Math.max(1, window.innerWidth);
    const h = msg.hPx / Math.max(1, window.innerHeight);
    const offX = msg.offsetX / Math.max(1, window.innerWidth);
    const offY = msg.offsetY / Math.max(1, window.innerHeight);
    return snap({ x: fx - offX, y: fy - offY, w, h });
  }

  // Cross-display drag listeners: remote ghost, leave, drop.
  const offDragMove = onTransient('widget-cross-drag-move', (msg) => {
    const rect = remoteRect(msg);
    setRemoteGhost(rect);
  });

  const offDragEnd = onTransient('widget-cross-drag-end', () => {
    setRemoteGhost(null);
  });

  const offDrop = onTransient('widget-cross-drop', (msg) => {
    setRemoteGhost(null);
    const rect = remoteRect(msg);
    if (!rect) return;
    const w = msg.widget;
    // Remove source FIRST so addWidget's global limit check sees the
    // post-move count (synchronous in-memory update).
    if (msg.sourceKey && msg.sourceId) {
      removeWidgetFromKey(msg.sourceKey, msg.sourceId);
    }
    addWidget(w.type, clampRect(rect), w.config ?? {});
  });

  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mouseup',   onMouseUp);
  window.addEventListener('click',     onClick);
  window.addEventListener('mouseout',  onWindowMouseOut);
  window.addEventListener('blur',      onWindowBlur);
  window.addEventListener('keydown',   onKeyDown);
  window.addEventListener('keyup',     onKeyUp);

  onCleanup(() => {
    window.removeEventListener('mousemove', onMouseMove);
    window.removeEventListener('mouseup',   onMouseUp);
    window.removeEventListener('click',     onClick);
    window.removeEventListener('mouseout',  onWindowMouseOut);
    window.removeEventListener('blur',      onWindowBlur);
    window.removeEventListener('keydown',   onKeyDown);
    window.removeEventListener('keyup',     onKeyUp);
    offDragMove();
    offDragEnd();
    offDrop();
  });

  return {
    drag,
    resize,
    preview,
    remoteGhost,
    dropTarget,
    startDrag,
    startResize,
  };
}

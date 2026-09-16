/**
 * Pointer drag primitive.
 *
 * Encapsulates the "mousedown → optional threshold → mousemove → mouseup +
 * cleanup" pattern scattered across the codebase (modal drag, folder file
 * drag init, grid widget drag/resize).
 *
 * Usage:
 *   onMouseDown={(e) => beginPointerDrag(e, { onMove: ({dx,dy}) => ... })}
 *
 * Threshold: if set, onStart/onMove fire only after the cursor moves > N px
 * from the mousedown point, useful for "click vs drag" disambiguation.
 *
 * Mouse events are used (not pointer events) to match existing call sites
 * and keep behaviour stable; migrate to pointer events later if needed.
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface PointerDragDelta {
  dx: number;
  dy: number;
}

export interface PointerDragStart {
  x: number;
  y: number;
}

export interface PointerDragOptions {
  /** Fires once when the drag activates (immediately, or after threshold). */
  onStart?: (start: PointerDragStart, e: MouseEvent) => void;
  /** Fires on each mousemove after activation. */
  onMove?: (delta: PointerDragDelta, e: MouseEvent) => void;
  /** Fires on mouseup. `moved` indicates whether the threshold was crossed. */
  onEnd?: (e: MouseEvent, moved: boolean) => void;
  /** Minimum px distance before the drag activates. Default 0. */
  threshold?: number;
  /** Call preventDefault on the mousedown event. Default true. */
  preventDefault?: boolean;
  /** Abort if this predicate returns true for the mousedown target. */
  ignore?: (target: HTMLElement) => boolean;
}

// ---------------------------------------------------------------------------
// Core
// ---------------------------------------------------------------------------

/**
 * Begin a pointer drag from a mousedown event. Installs window-level
 * mousemove/mouseup listeners and cleans them up on mouseup.
 */
export function beginPointerDrag(e: MouseEvent, opts: PointerDragOptions): void {
  if (opts.ignore && e.target instanceof HTMLElement && opts.ignore(e.target)) return;
  if (opts.preventDefault !== false) e.preventDefault();

  const startX = e.clientX;
  const startY = e.clientY;
  const threshold = opts.threshold ?? 0;
  let activated = threshold === 0;

  if (activated) opts.onStart?.({ x: startX, y: startY }, e);

  const onMove = (ev: MouseEvent) => {
    const dx = ev.clientX - startX;
    const dy = ev.clientY - startY;
    if (!activated) {
      if (Math.hypot(dx, dy) < threshold) return;
      activated = true;
      opts.onStart?.({ x: startX, y: startY }, ev);
    }
    opts.onMove?.({ dx, dy }, ev);
  };

  const onUp = (ev: MouseEvent) => {
    window.removeEventListener('mousemove', onMove);
    window.removeEventListener('mouseup', onUp);
    opts.onEnd?.(ev, activated);
  };

  window.addEventListener('mousemove', onMove);
  window.addEventListener('mouseup', onUp);
}

// ---------------------------------------------------------------------------
// Modal drag helper
// ---------------------------------------------------------------------------

/**
 * Build a mousedown handler that drags a positioned modal panel.
 * Clicks on `.close-btn` descendants are ignored so the close affordance
 * keeps working. Movement is divided by the UI scale so dragging feels 1:1
 * at any zoom level.
 */
export function createModalDragHandler(
  getPos: () => { x: number; y: number },
  setPos: (p: { x: number; y: number }) => void,
  getScale: () => number,
): (e: MouseEvent) => void {
  return (e) => {
    let orig = { x: 0, y: 0 };
    beginPointerDrag(e, {
      ignore: (t) => !!t.closest('.close-btn'),
      onStart: () => { orig = getPos(); },
      onMove: ({ dx, dy }) => {
        const s = getScale() || 1;
        setPos({ x: orig.x + dx / s, y: orig.y + dy / s });
      },
    });
  };
}

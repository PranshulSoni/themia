// @vitest-environment jsdom
//
// Characterization test for beginPointerDrag (pins behavior before 2.2 adopts it
// in the view-bar / view-reorder / colour-picker drags). It already backs the
// modal / folder / grid drags, so this also covers them.
import { describe, it, expect, vi } from 'vitest';
import { beginPointerDrag } from './pointerDrag.ts';

const down = (x = 0, y = 0) => new MouseEvent('mousedown', { clientX: x, clientY: y, button: 0 });
const move = (x: number, y: number) => window.dispatchEvent(new MouseEvent('mousemove', { clientX: x, clientY: y }));
const up = (x = 0, y = 0) => window.dispatchEvent(new MouseEvent('mouseup', { clientX: x, clientY: y }));

describe('beginPointerDrag', () => {
  it('threshold 0: activates immediately, reports deltas from the mousedown point', () => {
    const onStart = vi.fn(), onMove = vi.fn(), onEnd = vi.fn();
    beginPointerDrag(down(5, 5), { threshold: 0, onStart, onMove, onEnd, preventDefault: false });
    expect(onStart).toHaveBeenCalledTimes(1);
    move(10, 5);
    expect(onMove).toHaveBeenCalledWith({ dx: 5, dy: 0 }, expect.anything());
    up(12, 5);
    expect(onEnd).toHaveBeenCalledWith(expect.anything(), true);
  });

  it('threshold N: onStart/onMove suppressed until the cursor moves > N px', () => {
    const onStart = vi.fn(), onMove = vi.fn();
    beginPointerDrag(down(0, 0), { threshold: 4, onStart, onMove, preventDefault: false });
    move(2, 2);                       // hypot ~2.83 < 4 → not yet active
    expect(onStart).not.toHaveBeenCalled();
    expect(onMove).not.toHaveBeenCalled();
    move(5, 0);                       // 5 > 4 → activate (onStart then onMove, same event)
    expect(onStart).toHaveBeenCalledTimes(1);
    expect(onMove).toHaveBeenCalledTimes(1);
  });

  it('a drag that never crosses the threshold ends with moved=false', () => {
    const onEnd = vi.fn();
    beginPointerDrag(down(0, 0), { threshold: 4, onEnd, preventDefault: false });
    up(1, 1);
    expect(onEnd).toHaveBeenCalledWith(expect.anything(), false);
  });

  it('removes both listeners on mouseup (no leaked move handler)', () => {
    const onMove = vi.fn();
    beginPointerDrag(down(0, 0), { onMove, preventDefault: false });
    up();
    move(50, 50);                     // after release → ignored
    expect(onMove).not.toHaveBeenCalled();
  });

  it('ignore predicate aborts before any listener is installed', () => {
    const onMove = vi.fn();
    const el = document.createElement('button');
    el.className = 'close-btn';
    const e = new MouseEvent('mousedown', { clientX: 0, clientY: 0 });
    Object.defineProperty(e, 'target', { value: el });
    beginPointerDrag(e, { onMove, ignore: (t) => !!t.closest('.close-btn'), preventDefault: false });
    move(50, 50);
    expect(onMove).not.toHaveBeenCalled();
  });
});

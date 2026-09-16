// Keyboard navigation helpers shared across widgets.

/**
 * Stateful type-ahead buffer. Call `push(char)` on each keypress; it returns
 * the accumulated query (resets after `resetMs` of inactivity). Used by list
 * widgets and menus to jump-to-item by typing the first letters of a label.
 */
export function createTypeAhead(resetMs: number = 500) {
  let buffer = '';
  let lastTs = 0;
  return {
    push(ch: string): string {
      const now = performance.now();
      if (now - lastTs > resetMs) buffer = '';
      lastTs = now;
      buffer += ch.toLowerCase();
      return buffer;
    },
    reset(): void { buffer = ''; lastTs = 0; },
    get value() { return buffer; },
  };
}

/**
 * Returns the index of the first item whose label starts with `query`,
 * starting the search at `startIdx` and wrapping. Returns -1 if none match.
 */
export function findByPrefix<T>(
  items: T[],
  labelOf: (item: T) => string,
  query: string,
  startIdx: number = 0,
): number {
  if (!query || !items.length) return -1;
  const q = query.toLowerCase();
  const n = items.length;
  for (let k = 0; k < n; k++) {
    const i = (startIdx + k) % n;
    if (labelOf(items[i]).toLowerCase().startsWith(q)) return i;
  }
  return -1;
}

/**
 * Is the given keyboard event a single printable character worth using for
 * type-ahead? Filters out modifier-key combos, arrow keys, etc.
 */
export function isTypeAheadKey(e: KeyboardEvent): boolean {
  if (e.ctrlKey || e.metaKey || e.altKey) return false;
  return e.key.length === 1 && /\S/.test(e.key);
}

/**
 * Compute the next index when navigating a linear list with arrow keys.
 * Returns `null` if the key isn't a recognized nav key (caller should ignore).
 * `wrap` controls wrap-around at the ends.
 */
export function nextIndexLinear(
  key: string,
  curr: number,
  len: number,
  wrap: boolean = true,
): number | null {
  if (!len) return null;
  const last = len - 1;
  switch (key) {
    case 'ArrowDown': return curr < 0 ? 0 : curr >= last ? (wrap ? 0 : last) : curr + 1;
    case 'ArrowUp':   return curr < 0 ? last : curr <= 0 ? (wrap ? last : 0) : curr - 1;
    case 'Home':      return 0;
    case 'End':       return last;
    case 'PageDown':  return Math.min(last, (curr < 0 ? 0 : curr) + 10);
    case 'PageUp':    return Math.max(0,    (curr < 0 ? 0 : curr) - 10);
    default: return null;
  }
}

/**
 * Compute the next index for 2D grid navigation (row-major, numCols wide).
 * Returns `null` for unrecognized keys.
 */
export function nextIndexGrid(
  key: string,
  curr: number,
  len: number,
  numCols: number,
): number | null {
  if (!len || numCols < 1) return null;
  const last = len - 1;
  const c = curr < 0 ? 0 : curr;
  switch (key) {
    case 'ArrowRight': return Math.min(last, c + 1);
    case 'ArrowLeft':  return Math.max(0, c - 1);
    case 'ArrowDown':  return Math.min(last, c + numCols);
    case 'ArrowUp':    return Math.max(0, c - numCols);
    case 'Home':       return 0;
    case 'End':        return last;
    case 'PageDown':   return Math.min(last, c + numCols * 5);
    case 'PageUp':     return Math.max(0,    c - numCols * 5);
    default: return null;
  }
}

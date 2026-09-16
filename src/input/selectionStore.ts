/**
 * Selection primitive.
 *
 * Encapsulates the ctrl/shift/plain-click semantics that every list-like
 * widget re-implements (folder, contacts, gallery, todo). One Set-backed
 * signal, one range anchor, and a `click()` method that resolves the
 * modifier combo.
 *
 * Usage:
 *   const sel = createSelection<string>({ orderedIds: () => visibleIds() });
 *   sel.click(id, event);      // ctrl-toggle / shift-range / plain
 *   sel.isSelected(id);        // for rendering
 *   sel.selectAll(); sel.clear();
 *
 * The primitive does NOT own DOM focus, keyboard shortcuts, or visual
 * "last-selected" markers. Widgets wire those on top using `anchor()`
 * and `isSelected()`.
 */

import { createSignal, batch, type Accessor } from 'solid-js';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface SelectionClickEvent {
  ctrlKey?: boolean;
  metaKey?: boolean;
  shiftKey?: boolean;
}

export interface SelectionOptions<T> {
  /**
   * Ordered list of all currently-visible selectable ids. Required for
   * shift-range and selectAll. Read on every call so reactivity works.
   */
  orderedIds: () => T[];
  /**
   * Behaviour when the user plain-clicks an item that is already the
   * sole selection:
   *  - 'deselect' (default): clears the selection (matches folder widget)
   *  - 'keep':     leaves the single-selection as-is
   */
  singleClickToggle?: 'deselect' | 'keep';
}

export interface Selection<T> {
  /** Reactive Set of selected ids. */
  selected: Accessor<Set<T>>;
  isSelected: (id: T) => boolean;
  size: () => number;
  values: () => T[];
  /**
   * The shift-click anchor. Also useful for rendering a "last selected"
   * cursor marker even after the item is deselected.
   */
  anchor: Accessor<T | null>;

  /** Clear selection and anchor. */
  clear: () => void;
  /** Replace selection with the given ids (anchor unchanged). */
  set: (ids: Iterable<T>) => void;
  /** Set the anchor directly (useful for band-select or programmatic nav). */
  setAnchor: (id: T | null) => void;
  /** Add a single id without touching the anchor. */
  add: (id: T) => void;
  /** Remove a single id. */
  remove: (id: T) => void;
  /** Toggle a single id. */
  toggle: (id: T) => void;
  /** Select every id returned by `orderedIds`. */
  selectAll: () => void;
  /**
   * Click with modifier resolution:
   *  - ctrl/meta: toggle membership; on add, anchor := id.
   *  - shift + anchor: select range from anchor to id (additive).
   *  - plain: single-select id (or deselect if already sole selection and
   *           `singleClickToggle === 'deselect'`), anchor := id.
   */
  click: (id: T, event?: SelectionClickEvent) => void;
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

export function createSelection<T>(opts: SelectionOptions<T>): Selection<T> {
  const [selected, setSelected] = createSignal<Set<T>>(new Set());
  const [anchor, setAnchor_] = createSignal<T | null>(null);
  const mode = opts.singleClickToggle ?? 'deselect';

  const isSelected = (id: T) => selected().has(id);
  const size = () => selected().size;
  const values = () => [...selected()];

  function clear(): void {
    batch(() => {
      setSelected(new Set<T>());
      setAnchor_(null);
    });
  }

  function set(ids: Iterable<T>): void {
    setSelected(new Set(ids));
  }

  function setAnchor(id: T | null): void {
    setAnchor_(() => id);
  }

  function add(id: T): void {
    const s = new Set(selected());
    s.add(id);
    setSelected(s);
  }

  function remove(id: T): void {
    const s = new Set(selected());
    s.delete(id);
    setSelected(s);
  }

  function toggle(id: T): void {
    const s = new Set(selected());
    if (s.has(id)) s.delete(id);
    else s.add(id);
    setSelected(s);
  }

  function selectAll(): void {
    setSelected(new Set(opts.orderedIds()));
  }

  function click(id: T, e?: SelectionClickEvent): void {
    if (e?.ctrlKey || e?.metaKey) {
      const s = new Set(selected());
      if (s.has(id)) {
        s.delete(id);
      } else {
        s.add(id);
        setAnchor_(() => id);
      }
      setSelected(s);
      return;
    }

    if (e?.shiftKey && anchor() !== null) {
      const ids = opts.orderedIds();
      const from = ids.indexOf(anchor() as T);
      const to = ids.indexOf(id);
      if (from !== -1 && to !== -1) {
        const s = new Set(selected());
        const [lo, hi] = from < to ? [from, to] : [to, from];
        for (let i = lo; i <= hi; i++) s.add(ids[i]);
        setSelected(s);
      }
      return;
    }

    // Plain click.
    const cur = selected();
    if (mode === 'deselect' && cur.size === 1 && cur.has(id)) {
      setSelected(new Set<T>());
      return;
    }
    batch(() => {
      setSelected(new Set([id]));
      setAnchor_(() => id);
    });
  }

  return {
    selected,
    isSelected,
    size,
    values,
    anchor,
    clear,
    set,
    setAnchor,
    add,
    remove,
    toggle,
    selectAll,
    click,
  };
}

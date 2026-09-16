// Pure tree-ordering logic for the config window's view nodes. Extracted from
// ConfigTree so the nesting + ordering is unit-testable without mounting the
// Solid component (same pattern as `pickerGroups.ts`).

import { isContainerType, type Widget } from '../layout.ts';

export interface TreeWidgetRow {
  widget: Widget;
  /** 1 = a view's own widget, 2 = a widget nested inside a container. */
  depth: number;
  /** Container this row sits inside, for the "one level up" jump. */
  parentId?: string;
}

/**
 * A view's widgets in tree order: top-level widgets in `widgets`-array order,
 * each container immediately followed by ITS children, also in array order
 * (src/SPEC [S-19] -- the same order the container itself renders them in, so
 * the tree and the tile always agree).
 *
 * A child whose `parentId` cannot be honoured (missing / non-container parent)
 * is emitted at depth 1 rather than dropped: `parse` sanitizes such references
 * ([S-20]), but the tree is the only way to reach a widget's config, so a bad
 * reference must never make a widget unreachable.
 */
export function buildWidgetRows(widgets: readonly Widget[]): TreeWidgetRow[] {
  const out: TreeWidgetRow[] = [];
  const emitted = new Set<string>();

  for (const w of widgets) {
    if (w.parentId !== undefined) continue; // rendered under its container
    out.push({ widget: w, depth: 1 });
    emitted.add(w.id);
    if (!isContainerType(w.type)) continue;
    for (const c of widgets) {
      if (c.parentId !== w.id) continue;
      out.push({ widget: c, depth: 2, parentId: w.id });
      emitted.add(c.id);
    }
  }

  // Orphans: keep them visible (top-level), never hidden behind a broken link.
  for (const w of widgets) {
    if (!emitted.has(w.id)) out.push({ widget: w, depth: 1 });
  }
  return out;
}

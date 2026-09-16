// Synthetic children for a container widget rendered OUTSIDE a layout (the
// Add-Widget picker's preview pane). Pure + registry-free so it stays
// node-importable for unit tests, and so it can't join the registry ⇄ container
// module cycle.
//
// A container reads its children from the view's layout store by `_widgetId`.
// The picker mounts widgets with no id at all, so stack/tabs would render their
// (invisible) empty state and the preview pane would sit blank next to every
// other widget's live preview -- which reads as broken rather than as "this
// tile holds other widgets". The demo fixture for the container names the
// widget types to compose instead.

import type { Widget } from '../layout.ts';

/** Deterministic preview child id, so a fixture can pre-collapse / pre-select
 *  one of them (`collapsedIds: ['preview-1']`). */
export function previewChildId(index: number): string {
  return `preview-${index}`;
}

/**
 * Build preview children from `_previewChildren` (an array of widget type
 * names). Anything else -- absent, wrong shape, non-string members -- yields
 * `[]`, and the container falls back to its normal empty state rather than a
 * half-built child list.
 *
 * `configFor` supplies each child's config (registry default + demo override);
 * it is passed in rather than imported so this stays registry-free.
 */
export function previewChildren(
  spec: unknown,
  configFor: (type: string) => Record<string, unknown> | undefined,
): Widget[] {
  if (!Array.isArray(spec)) return [];
  const out: Widget[] = [];
  for (const entry of spec) {
    if (typeof entry !== 'string' || !entry) continue;
    out.push({
      id: previewChildId(out.length),
      type: entry,
      // Dormant: a preview child is never laid out on a grid.
      x: 0, y: 0, w: 0, h: 0,
      config: { ...(configFor(entry) ?? {}) },
    });
  }
  return out;
}

// Pure grouping logic for the Add-Widget picker (library view). Extracted from
// AddWidgetPage so the category/provider bucketing + ordering is unit-testable
// without mounting the Solid component (the test runner only picks up
// `*.test.ts`, not `.tsx` render tests).

import type { WidgetDefinition } from '../widgets/registry.tsx';

export type GroupBy = 'none' | 'category' | 'provider';

export type PickerEntry = [string, WidgetDefinition];
export interface PickerGroup {
  heading: string | null;
  entries: PickerEntry[];
}

// Category heading order in the picker. Buckets not listed here follow,
// sorted alphabetically. `Layout` holds the container widgets (stack, tabs, group).
export const CATEGORY_ORDER = ['DeveloperFavorites', 'Office', 'Online', 'System', 'Themia', 'Layout', 'Others'];
export const NO_PROVIDER_BUCKET = 'General';

// Curated "Developer Favorites" group: these widget types appear in this group
// IN ADDITION TO their own `category` bucket (so they show in both places).
export const DEVELOPER_FAVORITES = ['folder', 'shortcut'];

export const KNOWN_CATEGORIES = new Set(['DeveloperFavorites', 'MostUseful', 'Office', 'Online', 'System', 'Themia', 'Layout', 'Others', 'Other']);

// Group already-filtered/sorted picker entries by the chosen mode. Pure: the
// same inputs always yield the same ordered groups.
export function computePickerGroups(entries: PickerEntry[], mode: GroupBy): PickerGroup[] {
  if (mode === 'none') {
    return entries.length ? [{ heading: null, entries }] : [];
  }

  if (mode === 'category') {
    const buckets = new Map<string, PickerEntry[]>();
    for (const [type, def] of entries) {
      const c = def.category ?? 'Other';
      if (!buckets.has(c)) buckets.set(c, []);
      buckets.get(c)!.push([type, def]);
    }
    // Curated favorites appear additionally in their own group, ordered by the
    // curated list (not alphabetically) and only when present in `entries`.
    const favs = DEVELOPER_FAVORITES
      .map(type => entries.find(([t]) => t === type))
      .filter(Boolean) as PickerEntry[];
    if (favs.length) buckets.set('DeveloperFavorites', favs);
    const known = CATEGORY_ORDER.filter(c => buckets.has(c));
    const rest = [...buckets.keys()].filter(c => !CATEGORY_ORDER.includes(c)).sort();
    return [...known, ...rest].map(c => ({ heading: c, entries: buckets.get(c)! }));
  }

  // mode === 'provider': a widget with N providers appears in N buckets.
  const buckets = new Map<string, PickerEntry[]>();
  for (const [type, def] of entries) {
    const provs: string[] = def.providers ?? [];
    if (provs.length === 0) {
      if (!buckets.has(NO_PROVIDER_BUCKET)) buckets.set(NO_PROVIDER_BUCKET, []);
      buckets.get(NO_PROVIDER_BUCKET)!.push([type, def]);
    } else {
      for (const p of provs) {
        if (!buckets.has(p)) buckets.set(p, []);
        buckets.get(p)!.push([type, def]);
      }
    }
  }
  const provs = [...buckets.keys()].filter(p => p !== NO_PROVIDER_BUCKET).sort();
  const ordered = [...provs, ...(buckets.has(NO_PROVIDER_BUCKET) ? [NO_PROVIDER_BUCKET] : [])];
  return ordered.map(p => ({ heading: p, entries: buckets.get(p)! }));
}

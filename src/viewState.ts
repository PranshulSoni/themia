import { createStore } from 'solid-js/store';
import { loadPersisted, type PersistedSchema } from './persistedStore.ts';

const DEMO_PREFIX = 'demo-';
export function isDemoView(id: string): boolean {
  return id.startsWith(DEMO_PREFIX);
}

// Node-safe storage handle: this module is imported by node unit tests (which
// have no `localStorage`). Falls back to an always-empty store there, so a
// module-init load resolves to defaults instead of throwing a ReferenceError.
const LS: Pick<Storage, 'getItem' | 'setItem'> =
  typeof localStorage !== 'undefined'
    ? localStorage
    : { getItem: () => null, setItem: () => {} };

export interface View {
  id: string;
  name: string;
  icon: string;
}

interface ViewStore {
  list: View[];
  activeId: string;
}

export const VIEWS_KEY = 'widget-screens';
// Fresh installs seed two views so the multi-view feature is discoverable.
// `default` keeps its id (layout keys, hardcoded activeId depend on it).
export const DEFAULT_VIEWS: View[] = [
  { id: 'default', name: 'Home', icon: 'home' },
  { id: 'work',    name: 'Work', icon: 'home' },
];

// Schema'd persistence (1.12). On-disk shape is a bare `View[]` array (written
// here, by importExport, and by demo/dev seeds, so reads stay bare). parse()
// validates each entry (id + name must be strings), drops demo views, folds the
// icon migration (missing icon → 'home'), and falls back to DEFAULT_VIEWS when
// the result is empty or the blob is corrupt. The old path could throw on a
// malformed entry (isDemoView(undefined)); this can't.
const LIST_SCHEMA: PersistedSchema<View[]> = {
  version: 1,
  defaults: () => [...DEFAULT_VIEWS],
  parse: (raw: unknown): View[] => {
    if (!Array.isArray(raw)) return [...DEFAULT_VIEWS];
    const cleaned: View[] = [];
    for (const v of raw) {
      if (!v || typeof v !== 'object') continue;
      const o = v as Record<string, unknown>;
      if (typeof o.id !== 'string' || typeof o.name !== 'string') continue;
      if (isDemoView(o.id)) continue;
      const icon = typeof o.icon === 'string' && o.icon ? o.icon : 'home';
      cleaned.push({ id: o.id, name: o.name, icon });
    }
    return cleaned.length ? cleaned : [...DEFAULT_VIEWS];
  },
};

export function loadViewList(): View[] {
  return loadPersisted(VIEWS_KEY, LIST_SCHEMA, LS, {
    onCorrupt: (m) => console.error(`[views] ${m}`),
  });
}

export const [viewStore, setViewStore] = createStore<ViewStore>({
  list:     loadViewList(),
  activeId: 'default',
});

export function layoutKey(id: string): string { return id === 'default' ? 'widget-layout' : `widget-layout-${id}`; }

export function getActiveViewId(): string { return viewStore.activeId; }

/** Two-letter abbreviation derived from a view name, used as a fallback when
 *  no icon is set. Multi-word names take the first letter of the first two
 *  words ("Home Office" → "HO"); single-word names take the first two chars
 *  ("Work" → "WO"). Names shorter than 2 chars return their single uppercase
 *  char; empty/whitespace-only names return "?". */
export function viewInitials(name: string): string {
  const trimmed = (name ?? '').trim();
  if (!trimmed) return '?';
  const words = trimmed.split(/\s+/).filter(Boolean);
  const raw = words.length >= 2
    ? words[0].charAt(0) + words[1].charAt(0)
    : trimmed.slice(0, 2);
  return raw.toUpperCase();
}

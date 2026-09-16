// Demo mode: auto-generated views with all widgets pre-placed.
// Injected on app init when demoMode is true; cleaned up when turned off.

import { viewStore, setViewStore, layoutKey, isDemoView } from '../views.ts';
import { defaultGridSize } from '../layout.ts';
import registry from '../widgets/registry.tsx';
import { logger } from '../debugLog.ts';
import * as fx from './fixtures.ts';

export { isDemoView };

const VIEWS_KEY = 'widget-screens';

// ── Layout definitions ──────────────────────────────────────────────────────

interface DemoWidget {
  type: string;
  col: number;
  row: number;
  colSpan: number;
  rowSpan: number;
  config?: Record<string, unknown>;
}

interface DemoViewDef {
  id: string;
  name: string;
  widgets: DemoWidget[];
}

const DEMO_VIEWS: DemoViewDef[] = [
  {
    id: 'demo-office',
    name: 'Office',
    widgets: [
      { type: 'folder',   col: 1,  row: 1, colSpan: 3, rowSpan: 4 },
      { type: 'email',    col: 4,  row: 1, colSpan: 3, rowSpan: 4 },
      { type: 'contacts', col: 7,  row: 1, colSpan: 3, rowSpan: 4 },
      { type: 'calendar', col: 10, row: 1, colSpan: 3, rowSpan: 4 },
      { type: 'todo',     col: 13, row: 1, colSpan: 2, rowSpan: 4 },
      { type: 'onedrive', col: 15, row: 1, colSpan: 3, rowSpan: 4 },
    ],
  },
  {
    id: 'demo-online',
    name: 'Online',
    widgets: [
      { type: 'weatherCurrent',  col: 1,  row: 1, colSpan: 2, rowSpan: 2,
        config: { lat: 47.3769, lon: 8.5417, locationName: 'Zurich', unit: 'celsius' } },
      { type: 'github',          col: 3,  row: 1, colSpan: 2, rowSpan: 2,
        config: { repo: 'nicbarker/clay', refreshMinutes: 5 } },
      { type: 'stocks',          col: 5,  row: 1, colSpan: 2, rowSpan: 3,
        config: { symbols: [
          { symbol: 'AAPL', type: 'stock', name: 'Apple' },
          { symbol: 'MSFT', type: 'stock', name: 'Microsoft' },
          { symbol: 'NVDA', type: 'stock', name: 'NVIDIA' },
          { symbol: 'GOOGL', type: 'stock', name: 'Alphabet' },
        ], showSparkline: true, currency: 'USD' } },
      { type: 'weatherForecast', col: 7,  row: 1, colSpan: 3, rowSpan: 3,
        config: { lat: 47.3769, lon: 8.5417, locationName: 'Zurich', unit: 'celsius', days: 7 } },
      { type: 'rss',             col: 10, row: 1, colSpan: 3, rowSpan: 4,
        config: { feedIds: ['rss:demo-verge', 'rss:demo-hn', 'rss:demo-css'], maxItems: 20 } },
    ],
  },
  {
    id: 'demo-system',
    name: 'System',
    widgets: [
      { type: 'cpu',       col: 1,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'ram',       col: 3,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'gpu',       col: 5,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'net',       col: 7,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'disk',      col: 9,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'battery',   col: 11, row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'ping',      col: 13, row: 1, colSpan: 2, rowSpan: 2,
        config: { host: { label: 'Google', address: '8.8.8.8' }, showGraph: true } },
      { type: 'bluetooth', col: 15, row: 1, colSpan: 2, rowSpan: 2 },
    ],
  },
  {
    id: 'demo-utilities',
    name: 'Utilities',
    widgets: [
      { type: 'clock',         col: 1,  row: 1, colSpan: 2, rowSpan: 2 },
      { type: 'music',         col: 3,  row: 1, colSpan: 3, rowSpan: 2 },
      { type: 'apps',          col: 6,  row: 1, colSpan: 3, rowSpan: 4 },
      { type: 'gallery',       col: 9,  row: 1, colSpan: 3, rowSpan: 3 },
      { type: 'notes',         col: 12, row: 1, colSpan: 2, rowSpan: 3,
        config: { content: '# Welcome to Themia\n\nThis is **demo mode** — explore the widgets across these views.\n\n- [x] pick a widget\n- [ ] make it yours', fontSize: 13 } },
      { type: 'calculator',    col: 14, row: 1, colSpan: 2, rowSpan: 3 },
      { type: 'shortcut',      col: 5,  row: 3, colSpan: 2, rowSpan: 2,
        config: {
          items: fx.demoAppLayouts().map(l => ({ id: l.id, path: 'layout:', label: l.name, layout: l.root })),
          viewMode: 'list', clickMode: 'single', hideHeader: false, label: 'App layouts',
        } },
    ],
  },
];

// ── Inject / Remove ─────────────────────────────────────────────────────────

function buildLayout(def: DemoViewDef) {
  const grid = defaultGridSize();
  return {
    cols: grid.cols,
    rows: grid.rows,
    // Only place widgets whose type is actually registered (A1). A demo def may
    // reference a widget that has been disabled/removed (e.g. ping, bookmarks);
    // persisting it would leave an un-renderable blank cell. Filtering here keeps
    // demo mode clean AND self-healing: a re-registered widget reappears with no
    // edit to this file.
    widgets: def.widgets
      .filter(w => registry[w.type])
      .map((w, i) => {
        const reg = registry[w.type];
        return {
          id: `${def.id}-${w.type}-${i}`,
          type: w.type,
          col: w.col,
          row: w.row,
          colSpan: w.colSpan,
          rowSpan: w.rowSpan,
          config: { ...(reg?.defaultConfig ?? {}), ...(w.config ?? {}) },
        };
      }),
  };
}

/** Startup assertion (A1): every widget type referenced by a demo view must
 *  exist in the registry. A miss is dropped from the layout (see buildLayout)
 *  and logged loudly so the drift is visible rather than a silent blank tile. */
function warnUnregisteredDemoTypes(): void {
  const missing = [
    ...new Set(DEMO_VIEWS.flatMap(d => d.widgets.map(w => w.type)).filter(t => !registry[t])),
  ];
  if (missing.length) {
    logger.warn('demo', `demo references unregistered widget type(s), skipping: ${missing.join(', ')}`);
  }
}

/** Add demo views to the store and persist their layouts to localStorage. */
export function injectDemoViews(): void {
  warnUnregisteredDemoTypes();

  // Remove any stale demo views first
  const current = viewStore.list.filter(v => !isDemoView(v.id));

  // Write layout data for each demo view
  for (const def of DEMO_VIEWS) {
    const key = layoutKey(def.id);
    localStorage.setItem(key, JSON.stringify(buildLayout(def)));
  }

  // Append demo views to the list
  const demoEntries = DEMO_VIEWS.map(d => ({ id: d.id, name: d.name, icon: 'home' }));
  const newList = [...current, ...demoEntries];

  setViewStore({ list: newList });
  localStorage.setItem(VIEWS_KEY, JSON.stringify(newList));
}

/** Remove all demo views and their localStorage data. */
export function removeDemoViews(): void {
  // Clean up layout keys
  for (const def of DEMO_VIEWS) {
    localStorage.removeItem(layoutKey(def.id));
  }

  // Remove from view list
  const filtered = viewStore.list.filter(v => !isDemoView(v.id));
  setViewStore({ list: filtered.length ? filtered : [{ id: 'default', name: 'Default', icon: 'home' }] });
  localStorage.setItem(VIEWS_KEY, JSON.stringify(viewStore.list));

  // If active view was a demo view, switch to first real view
  if (isDemoView(viewStore.activeId)) {
    setViewStore({ activeId: viewStore.list[0].id });
  }
}

// Dev-only "Development views".
//
// Generates a set of throwaway views that, together, contain EVERY registered
// widget, split across multiple views because they don't all fit on one. They
// render through the real renderer with real interactions and (under tauri dev)
// live data. Unlike demo views they do NOT touch `demoMode`.
//
// Toggled by a switch in Settings → General (Developer mode). Enabling (and any
// app boot while enabled) REGENERATES them from the registry, so newly added
// widgets always appear. Otherwise they behave like ordinary views (switchable,
// persisted, editable).

import { createSignal } from 'solid-js';
import { viewStore, setViewStore, layoutKey, switchView } from '../views.ts';
import { destroyViewLayoutStore, type Widget } from '../layout.ts';
import registry from '../widgets/registry.tsx';
import { demoWidgetConfigs } from '../demo/fixtures.ts';

const VIEWS_KEY = 'widget-screens';
const FLAG_KEY = 'themia-dev-views';
const DEV_PREFIX = 'dev-';

// Tile grid per view: 4×3 = 12 widgets each.
const COLS = 4;
const ROWS = 3;
const PER_VIEW = COLS * ROWS;

// Local `YYYY-MM-DD` for `offset` days ago, used to seed a habit streak.
function dayKey(offset: number): string {
  const d = new Date();
  d.setDate(d.getDate() - offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

// Config seeds for widgets that render nothing useful without one. Merged over
// the widget's defaultConfig. Points data-driven widgets at public sources so
// they light up live; `demoWidgetConfigs` supplies content-driven ones.
const LAB_CONFIG: Record<string, Record<string, unknown>> = {
  weatherCurrent:  { lat: 47.3769, lon: 8.5417, locationName: 'Zurich', unit: 'celsius' },
  weatherForecast: { lat: 47.3769, lon: 8.5417, locationName: 'Zurich', unit: 'celsius', days: 7 },
  rss:             { feedUrl: 'https://hnrss.org/frontpage', maxItems: 20 },
  webElement:      { url: 'https://example.com', selector: 'body > div > h1', pickedLabel: 'Example Domain', refreshMinutes: 15 },
  github:          { repo: 'nicbarker/clay', refreshMinutes: 5 },
  stocks:          { symbols: [
    { symbol: 'AAPL', type: 'stock', name: 'Apple' },
    { symbol: 'MSFT', type: 'stock', name: 'Microsoft' },
    { symbol: 'NVDA', type: 'stock', name: 'NVIDIA' },
  ], showSparkline: true, currency: 'USD' },

  // Experimental widgets: seeds so they render meaningfully in dev views.
  habitTracker: {
    habits: [
      { id: 'h-read',  name: 'Read 20 min' },
      { id: 'h-water', name: 'Drink water' },
      { id: 'h-walk',  name: 'Walk outside' },
    ],
    log: {
      'h-read':  [dayKey(2), dayKey(1), dayKey(0)],
      'h-water': [dayKey(1), dayKey(0)],
    },
    resetPeriod: 'daily',
  },
  music:   { showSpectrum: true, showAlbumArt: true },
  sunMoon: { lat: 47.37, lon: 8.54, place: 'Zurich' },

  ...demoWidgetConfigs,
};

export function isDevView(id: string): boolean {
  return id.startsWith(DEV_PREFIX);
}

// Reactive enabled-state, backed by the persisted flag, so the Settings toggle
// reflects the current state and updates live.
const [devViewsEnabled, setDevViewsEnabledSignal] = createSignal(
  typeof localStorage !== 'undefined' && localStorage.getItem(FLAG_KEY) === '1',
);
export { devViewsEnabled };

function setFlag(on: boolean): void {
  setDevViewsEnabledSignal(on);
  if (on) localStorage.setItem(FLAG_KEY, '1');
  else localStorage.removeItem(FLAG_KEY);
}

interface BuiltView { id: string; name: string; widgets: Widget[]; }

/** Tile every registered widget across as many views as needed. */
function buildDevViews(): BuiltView[] {
  const types = Object.keys(registry);
  const out: BuiltView[] = [];
  for (let i = 0; i < types.length; i += PER_VIEW) {
    const n = i / PER_VIEW + 1;
    const widgets = types.slice(i, i + PER_VIEW).map((type, j) => {
      const col = j % COLS, row = Math.floor(j / COLS);
      const def = registry[type];
      return {
        id: `${DEV_PREFIX}${n}-${type}`,
        type,
        x: col / COLS, y: row / ROWS, w: 1 / COLS, h: 1 / ROWS,
        config: { ...(def?.defaultConfig ?? {}), ...(LAB_CONFIG[type] ?? {}) },
      } as Widget;
    });
    out.push({ id: `${DEV_PREFIX}${n}`, name: `Dev ${n}`, widgets });
  }
  return out;
}

/** (Re)generate the dev views from the registry. Returns the built views. */
function generateDevViews(): BuiltView[] {
  // Drop existing dev-view layouts + live stores so they reload fresh.
  for (const v of viewStore.list.filter(v => isDevView(v.id))) {
    destroyViewLayoutStore(v.id);
    localStorage.removeItem(layoutKey(v.id));
  }

  const built = buildDevViews();
  for (const v of built) {
    localStorage.setItem(layoutKey(v.id), JSON.stringify({ widgets: v.widgets }));
    destroyViewLayoutStore(v.id);
  }

  const others = viewStore.list.filter(v => !isDevView(v.id));
  const list = [...others, ...built.map(v => ({ id: v.id, name: v.name, icon: 'home' }))];
  setViewStore({ list });
  localStorage.setItem(VIEWS_KEY, JSON.stringify(list));
  setFlag(true);
  return built;
}

/** Toggle-on handler: regenerate the dev views and switch to the first. */
export function injectDevViews(): void {
  const built = generateDevViews();
  if (built.length) switchView(built[0].id);
}

/** Remove all dev views, their layouts, and clear the flag. */
export function removeDevViews(): void {
  for (const v of viewStore.list.filter(v => isDevView(v.id))) {
    destroyViewLayoutStore(v.id);
    localStorage.removeItem(layoutKey(v.id));
  }
  const list = viewStore.list.filter(v => !isDevView(v.id));
  const final = list.length ? list : [{ id: 'default', name: 'Home', icon: 'home' }];
  setViewStore({ list: final });
  localStorage.setItem(VIEWS_KEY, JSON.stringify(final));
  setFlag(false);
  if (isDevView(viewStore.activeId)) switchView(final[0].id);
}

/** Settings toggle handler. */
export function setDevViewsEnabled(on: boolean): void {
  if (on) injectDevViews();
  else removeDevViews();
}

/** Boot hook: regenerate if the flag is set (keeps them current with the
 *  registry). Caller gates on developer mode. */
export function regenerateDevViewsOnBoot(): void {
  if (localStorage.getItem(FLAG_KEY) === '1') generateDevViews();
}

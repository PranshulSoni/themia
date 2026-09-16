import { createStore, produce, reconcile } from 'solid-js/store';
import { createSignal } from 'solid-js';
import { isPro } from './license.ts';
import { onStorageKey, onStorageMatch, sharedStorage } from './sync.ts';
import { viewStore, layoutKey, getActiveViewId } from './viewState.ts';
import { logger } from './debugLog.ts';
import { loadPersisted, savePersistedBare, type PersistedSchema } from './persistedStore.ts';
import { clampRect, cellToFrac, gridDims, type FracRect } from './gridGeometry.ts';

declare global {
  interface Window {
    __themiaResetLayouts?: () => void;
    __themiaSeedWidgets?: (widgets: unknown[], viewId?: string) => void;
  }
}

export const FREE_WIDGET_LIMIT = 10;

/**
 * The tray collection (Tray.SPEC [S-01]): a layout store like a view's, except
 * that nothing renders it on the desktop. Its widgets are reached through their
 * tray icons instead, so their `x/y/w/h` stay at the placeholder `addWidget`
 * assigns and are never read. It lives here, not in `tray.ts`, so the free-tier
 * count can reach it without importing the tray module (which imports this one).
 */
export const TRAY_COLLECTION_ID = 'tray';

// Bump signal so totalWidgetCount() is reactive to cross-window layout writes
// for views that don't have a live store in this window.
const [layoutStorageBump, setLayoutStorageBump] = createSignal(0);
onStorageMatch(
  (key) => key === 'widget-layout' || key.startsWith('widget-layout-'),
  () => setLayoutStorageBump(n => n + 1),
);

/**
 * A widget's geometry is stored as fractions of the active window's screen
 * (src/SPEC.md [S-01]): `x,y` top-left ∈[0,1]; `w,h` size ∈(0,1]. Independent
 * axes per W/H. There is NO stored grid (cols/rows); the snap grid is a live
 * aid computed from the screen, never persisted geometry.
 */
export interface Widget {
  id: string;
  type: string;
  x: number;
  y: number;
  w: number;
  h: number;
  config?: Record<string, unknown>;
  label?: string;
  /** Id of the stack widget containing this one ([S-19]). Absent = top-level.
   *  While parented, x/y/w/h are dormant and act as the pop-out restore rect. */
  parentId?: string;
}

/** Container widget types: they hold other widgets via `parentId` ([S-19]).
 *  Separate widgets with separate presentations (accordion / tab bar / area).
 *  Adding a container type means adding it here so the containment rules
 *  accept it. */
export const STACK_WIDGET_TYPE = 'stack';
export const TABS_WIDGET_TYPE = 'tabs';
/** Area group ([S-36]): the one container that does NOT render its children. */
export const GROUP_WIDGET_TYPE = 'group';

/** Whether a widget type may be a parent ([S-19]). Containers never nest. */
export function isContainerType(type: string): boolean {
  return type === STACK_WIDGET_TYPE || type === TABS_WIDGET_TYPE || type === GROUP_WIDGET_TYPE;
}

/**
 * An **area group** ([S-36]) is a container of a second kind: its children stay
 * on the DESKTOP grid, at their own live `x/y/w/h`, and the group only decides
 * whether they are shown. Stack/tabs are the other kind — they render their
 * children themselves and the children's geometry lies dormant ([S-19]).
 * Everything else about containment (parentId, sanitize, remove-pops-children,
 * no nesting) is shared, so this predicate is the ONLY fork.
 */
export function isAreaGroupType(type: string): boolean {
  return type === GROUP_WIDGET_TYPE;
}

/** Is this widget an area group that is currently rolled up ([S-37])? */
export function isGroupCollapsed(w: Pick<Widget, 'type' | 'config'>): boolean {
  return isAreaGroupType(w.type) && w.config?.collapsed === true;
}

/** Thickness of an area group's own title bar, in app-layer CSS px. Kept in step
 *  with `.group-head` (Group.css) by group SPEC [S-04]. */
export const GROUP_HEADER_PX = 32;

/** Which edge of the area the title bar sits on ([S-39]). */
export type GroupHeaderSide = 'top' | 'bottom' | 'left' | 'right';
const HEADER_SIDES: readonly GroupHeaderSide[] = ['top', 'bottom', 'left', 'right'];

/** The group's configured header edge, defaulting to `top`. Junk in the config
 *  resolves to the default rather than rendering a headerless tile ([S-39]). */
export function groupHeaderSide(w: Pick<Widget, 'config'>): GroupHeaderSide {
  const v = w.config?.headerSide;
  return HEADER_SIDES.includes(v as GroupHeaderSide) ? v as GroupHeaderSide : 'top';
}

/** Whether the group shows the standard widget border ([S-15]). */
export type GroupBorderMode = 'theme' | 'none';

/** The group's configured border, defaulting to the themed one. Junk resolves
 *  to `theme` — an unrecognised value must not silently erase a border. */
export function groupBorderMode(w: Pick<Widget, 'config'>): GroupBorderMode {
  return w.config?.borderMode === 'none' ? 'none' : 'theme';
}

/**
 * The rect an area group RENDERS at: its stored rect while expanded, its title
 * bar alone while collapsed ([S-37]). The bar keeps the edge it is configured
 * on ([S-39]), so a bottom/right header collapses toward THAT edge and the
 * widgets above/left of it are what the freed space uncovers. The stored rect is
 * never touched, so expanding restores the area exactly. Pure.
 */
export function groupRenderRect(
  w: FracRect,
  collapsed: boolean,
  screen: { w: number; h: number },
  side: GroupHeaderSide = 'top',
  gapPx = 0,
): FracRect {
  if (!collapsed) return { x: w.x, y: w.y, w: w.w, h: w.h };
  // The grid insets every tile by widgetGap/2 per side ([S-04]), so a LOGICAL
  // rect of exactly one header renders a header MINUS the gap and the bar comes
  // out shorter than the same bar does when the group is open. Add the gap back
  // here: the rendered band is then `GROUP_HEADER_PX` in both states ([S-37]).
  const th = (GROUP_HEADER_PX + gapPx) / Math.max(1, screen.h);
  const tw = (GROUP_HEADER_PX + gapPx) / Math.max(1, screen.w);
  switch (side) {
    case 'bottom': {
      const h = Math.min(w.h, th);
      return { x: w.x, y: w.y + w.h - h, w: w.w, h };
    }
    case 'left':
      return { x: w.x, y: w.y, w: Math.min(w.w, tw), h: w.h };
    case 'right': {
      const ww = Math.min(w.w, tw);
      return { x: w.x + w.w - ww, y: w.y, w: ww, h: w.h };
    }
    default:
      return { x: w.x, y: w.y, w: w.w, h: Math.min(w.h, th) };
  }
}

/**
 * The top-level area group a widget with this rect belongs in ([S-38]), or null.
 *
 * A drop lands in the area when the widget's rect CENTRE is inside it **or** the
 * cursor is (`point`, when there is one). Either alone is a way to miss a group
 * the user plainly aimed at: the centre alone rejects a big widget grabbed near
 * an edge and released well inside the area, and the cursor alone is not even
 * available for a placement/paste. A COLLAPSED group is never a target — its
 * area is bare desktop. Last match wins, so the most recently added group takes
 * an overlap.
 */
export function areaGroupAtRect(
  widgets: readonly Widget[],
  rect: FracRect,
  exceptId?: string,
  point?: { x: number; y: number },
): string | null {
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const inside = (g: Widget, x: number, y: number) =>
    x >= g.x && x <= g.x + g.w && y >= g.y && y <= g.y + g.h;
  let hit: string | null = null;
  for (const g of widgets) {
    if (!isAreaGroupType(g.type) || g.parentId !== undefined || g.id === exceptId) continue;
    if (isGroupCollapsed(g)) continue;
    if (inside(g, cx, cy) || (point && inside(g, point.x, point.y))) hit = g.id;
  }
  return hit;
}

/**
 * Where a widget created INSIDE an area group lands ([S-36]/[S-33]). An area
 * group's children carry live geometry, so — unlike a stack child, which
 * inherits the container's whole rect as a dormant restore rect — a new child
 * needs a real position: its default size, inset from the area's top-left and
 * cascaded by the member count so the second child doesn't hide the first, held
 * inside the area on both axes. Pure.
 */
export function areaChildRect(area: FracRect, size: { w: number; h: number }, index: number): FracRect {
  const padX = area.w * 0.03;
  const padY = area.h * 0.03;
  const w = Math.max(0, Math.min(size.w, area.w - 2 * padX));
  const h = Math.max(0, Math.min(size.h, area.h - 2 * padY));
  const k = index % 5; // restart the cascade rather than march off the area
  const x = Math.min(area.x + padX + area.w * 0.06 * k, area.x + area.w - padX - w);
  const y = Math.min(area.y + padY + area.h * 0.06 * k, area.y + area.h - padY - h);
  return clampRect({ x, y, w, h });
}

/** Fractional geometry passed to addWidget / used by drag-resize. */
export type WidgetGeom = FracRect;

export interface LayoutState {
  widgets: Widget[];
}

/** Snap-grid granularity for the current screen. Re-exported from geometry so
 *  demo/lab seeders + callers keep one import site. */
export { gridDims as defaultGridSize };

// Seed geometry is authored directly as screen fractions ([S-01]), no longer
// derived from a cell grid, so it looks the same proportionally on every screen.
export function defaultLayout(viewId?: string): LayoutState {
  // [S-17] The tray collection starts EMPTY. It is not a view: its widgets are
  // notification-area icons, so seeding it the way a fresh desktop is seeded put
  // three icons (clock, help, shortcut) in the shell that nobody asked for — on
  // every fresh profile, and on every e2e spec, whose `localStorage.clear()`
  // makes the profile fresh again.
  if (viewId === TRAY_COLLECTION_ID) return { widgets: [] };

  // The built-in "Work" view gets a distinct showcase layout so the two
  // default views look meaningfully different: calendar + todo + notes +
  // clock instead of the folder-centric Home seed.
  if (viewId === 'work') {
    return {
      widgets: [
        { id: 'clock-work',    type: 'clock',    x: 0.78, y: 0.04, w: 0.20, h: 0.16 },
        { id: 'calendar-work', type: 'calendar', x: 0.02, y: 0.04, w: 0.50, h: 0.72 },
        { id: 'todo-work',     type: 'todo',     x: 0.54, y: 0.04, w: 0.20, h: 0.58 },
        { id: 'notes-work',    type: 'notes',    x: 0.54, y: 0.64, w: 0.20, h: 0.28 },
      ],
    };
  }

  // The built-in Home ('default') view keeps its canonical, stable widget ids —
  // persistence identity and migrations depend on them. Every OTHER view is
  // user-created and must get ids unique to it; otherwise two seeded views share
  // ids and findViewForWidget() can't tell them apart, mis-routing edits/deletes.
  const uid = (base: string) => (viewId && viewId !== 'default') ? `${base}-${viewId}` : base;
  const widgets: Widget[] = [
    { id: uid('clock-1'), type: 'clock', x: 0.78, y: 0.04, w: 0.20, h: 0.16 },
    { id: uid('help-1'),  type: 'help',  x: 0.02, y: 0.04, w: 0.24, h: 0.34 },
    { id: uid('shortcut-default-1'), type: 'shortcut',
      x: 0.80, y: 0.80, w: 0.18, h: 0.16,
      config: {
        items: [
          { id: 'seed-recycle',   path: 'shell:RecycleBinFolder', label: 'Recycle Bin' },
          { id: 'seed-thispc',    path: 'shell:MyComputerFolder', label: 'This PC' },
          { id: 'seed-downloads', path: 'shell:Downloads',        label: 'Downloads' },
          { id: 'seed-documents', path: 'shell:Personal',         label: 'Documents' },
          { id: 'seed-pictures',  path: 'shell:My Pictures',      label: 'Pictures' },
          { id: 'seed-home',      path: 'shell:UsersFilesFolder', label: 'Home' },
        ],
        viewMode: 'grid',
        itemSize: 'medium',
        clickMode: 'double',
        headerMode: 'none',
        label: '',
      },
    },
  ];
  // Default-view-only: seed a Folder widget that opens the user's Desktop. The
  // path is left empty; FolderView resolves desktopDir() at render and fills it
  // in, with no App-level timer or sentinel (1.26: no wall-clock in correctness paths).
  if (viewId === 'default') {
    widgets.push({
      id: 'folder-default-desktop',
      type: 'folder',
      x: 0.02, y: 0.42, w: 0.24, h: 0.50,
      config: {
        path: '',
        viewMode: 'list',
        itemSize: 'medium',
        navigationMode: 'explorer',
        clickMode: 'double',
        showHidden: false,
        showFileExtension: false,
        filter: '',
        gridStretchHeight: false,
        gridItemSize: 'small',
        detailsColumns: { size: true, modified: true, type: true, cloudSync: false },
      },
    });
  }
  return { widgets };
}

/** Structural guard for a persisted widget. The grid render + drag math
 *  dereference every geometry field, so an entry missing any of them must be
 *  dropped rather than trusted (it would otherwise place at NaN / crash).
 *  Runs AFTER `migrateGeom`, so it validates the percent shape ([S-13]). */
function isWidget(v: unknown): v is Widget {
  if (!v || typeof v !== 'object') return false;
  const w = v as Record<string, unknown>;
  return (
    typeof w.id === 'string' &&
    typeof w.type === 'string' &&
    typeof w.x === 'number' && Number.isFinite(w.x) &&
    typeof w.y === 'number' && Number.isFinite(w.y) &&
    typeof w.w === 'number' && Number.isFinite(w.w) &&
    typeof w.h === 'number' && Number.isFinite(w.h)
  );
}

/**
 * Migrate one raw persisted widget to the percent shape ([S-07]). Idempotent:
 *   - already-percent (x/y/w/h present) → returned untouched;
 *   - legacy cell shape (col/row/colSpan/rowSpan) → converted using the blob's
 *     top-level cols/rows, then clamped on-screen;
 *   - anything else → returned as-is for isWidget to reject.
 */
function migrateGeom(raw: unknown, cols: number, rows: number): unknown {
  if (!raw || typeof raw !== 'object') return raw;
  const w = raw as Record<string, unknown>;
  if (typeof w.x === 'number' && typeof w.y === 'number' &&
      typeof w.w === 'number' && typeof w.h === 'number') {
    return raw;
  }
  if (typeof w.col === 'number' && typeof w.row === 'number' &&
      typeof w.colSpan === 'number' && typeof w.rowSpan === 'number') {
    const { col, row, colSpan, rowSpan, ...rest } = w;
    const rect = cellToFrac(col as number, row as number, colSpan as number, rowSpan as number, cols, rows);
    return { ...rest, ...rect };
  }
  return raw;
}

// Schema'd persistence (1.12). Validates the LayoutState shape: cols/rows/gap
// must be numbers, widgets an array of structurally-valid Widgets.
// Built per-view because the default layout depends on viewId + window size.
// Persisted BARE (no envelope) because importExport / totalWidgetCount /
// removeWidgetFromKey read widget-layout* keys directly as plain blobs.
// Exported for characterization tests (the parse/migrate path is the data-
// integrity core: drops structurally-invalid widgets, migrates legacy shapes).
export function makeLayoutSchema(viewId?: string): PersistedSchema<LayoutState> {
  return {
    version: 1,
    defaults: () => defaultLayout(viewId),
    parse: (raw: unknown): LayoutState => {
      const def = defaultLayout(viewId);
      if (!raw || typeof raw !== 'object') return def;
      const d = raw as Record<string, unknown>;
      // Legacy blobs carry a top-level cols/rows used only to convert their
      // cell geometry to fractions; absent → use the current screen grid.
      const live = gridDims();
      const cols = typeof d.cols === 'number' ? d.cols : live.cols;
      const rows = typeof d.rows === 'number' ? d.rows : live.rows;
      const widgets = Array.isArray(d.widgets)
        ? sanitizeParents((d.widgets as unknown[]).map(w => migrateGeom(w, cols, rows)).filter(isWidget))
        : def.widgets;
      return { widgets };
    },
  };
}

/**
 * Drop parentId references that cannot be honoured ([S-20]): non-string, self,
 * missing target, target not a container, or carried by a container itself (no
 * nesting). The WIDGET always survives; only the bad reference is removed, so
 * an orphaned child reappears top-level instead of vanishing.
 */
function sanitizeParents(widgets: Widget[]): Widget[] {
  const byId = new Map(widgets.map(w => [w.id, w]));
  return widgets.map(w => {
    const pid = (w as unknown as Record<string, unknown>).parentId;
    if (pid === undefined) return w;
    const parent = typeof pid === 'string' ? byId.get(pid) : undefined;
    const valid =
      typeof pid === 'string' &&
      pid !== w.id &&
      !isContainerType(w.type) &&
      parent !== undefined &&
      isContainerType(parent.type);
    if (valid) return w;
    const { parentId: _dropped, ...rest } = w;
    return rest as Widget;
  });
}

function loadFor(storageKey: string, viewId?: string): LayoutState {
  return loadPersisted(storageKey, makeLayoutSchema(viewId), localStorage, {
    onCorrupt: (m) => logger.error('layout', m),
  });
}

// ---------------------------------------------------------------------------
// Per-view layout store factory
// ---------------------------------------------------------------------------

export interface ViewLayoutStore {
  viewId: string;
  storageKey: string;
  layout: LayoutState;
  addWidget: (type: string, geom?: WidgetGeom, defaultConfig?: Record<string, unknown>) => string | null;
  /** Create a widget directly INSIDE a container, no placement step ([S-33]).
   *  Returns the new id, or null when the parent is missing / not a container,
   *  the type is itself a container (no nesting), or the free-tier limit is
   *  reached. The child inherits the container's rect as its dormant pop-out
   *  restore rect, so popping it out later lands it where the container is —
   *  except in an AREA group, where the child's geometry is live and it lands
   *  on a real rect inside the area instead ([S-36]). Pass `geom` to place an
   *  area member exactly (a paste restoring its captured spot, [S-41]). */
  addChildWidget: (type: string, parentId: string, defaultConfig?: Record<string, unknown>, geom?: WidgetGeom) => string | null;
  moveWidget: (id: string, x: number, y: number) => void;
  updateWidget: (id: string, geom: WidgetGeom) => void;
  updateWidgetConfig: (id: string, config: Record<string, unknown>, immediate?: boolean) => void;
  updateWidgetLabel: (id: string, label: string) => void;
  removeWidget: (id: string) => void;
  /** Remove a widget AND, for an area group, the members inside it ([S-41]).
   *  For a TRANSPORT only (cut, cross-window move), where the destination has
   *  already re-created them. Plain `removeWidget` keeps them ([S-21]): deleting
   *  a group must never delete the user's widgets. */
  removeWidgetTree: (id: string) => void;
  /** Put a widget inside a container ([S-19]). No-op when invalid (container-
   *  into-container, self, missing/non-container target). Child moves to the
   *  array end so newly added children render last in the container.
   *  `geom` is honoured only for an AREA group, whose children keep live grid
   *  geometry ([S-36]): it is where the widget was dropped. */
  setWidgetParent: (id: string, parentId: string, geom?: WidgetGeom) => void;
  /** Pop a widget back to top-level. Its stored rect (clamped) is reused;
   *  pass `geom` to land it somewhere else. */
  clearWidgetParent: (id: string, geom?: WidgetGeom) => void;
  /** Move a widget to a sibling's position (drag-to-reorder). Siblings are
   *  widgets sharing a `parentId` — the children of one container ([S-35]), or
   *  the top level, which is where the tray's icon order lives (Tray.SPEC
   *  [S-01]). No-op across two different parents: that is a containment change,
   *  which `setWidgetParent` owns, never something a reorder means. */
  moveChildTo: (id: string, targetId: string) => void;
  /** Step a widget one place toward its previous (-1) / next (+1) sibling.
   *  Expressed on `moveChildTo`, so there is one reorder path. */
  reorderChild: (id: string, dir: -1 | 1) => void;
  destroy: () => void;
  /** Test-only: drop widgets to [] + cancel any pending debounced save. */
  _resetToEmpty: () => void;
  /** Test-only: overwrite widgets list in-place + persist immediately. */
  _seedWidgets: (widgets: Widget[]) => void;
}

// Widget ids are minted from the clock, so anything that creates SEVERAL in one
// tick (pasting a group with its members, [S-41]) would hand them the same id
// and the store would treat them as one widget. A process-wide counter breaks
// the tie; it never has to survive a restart, since the timestamp already
// separates runs.
let idSeq = 0;

function createViewLayoutStore(viewId: string): ViewLayoutStore {
  const storageKey = layoutKey(viewId);
  const [layout, setLayout] = createStore<LayoutState>(loadFor(storageKey, viewId));

  let _saveTimer: number | undefined;

  function persistNow(): void {
    savePersistedBare(
      storageKey,
      {
        widgets: layout.widgets,
      },
      sharedStorage,
      { onError: (m) => logger.error('layout', m) },
    );
  }

  function save(): void {
    if (_saveTimer !== undefined) clearTimeout(_saveTimer);
    _saveTimer = window.setTimeout(() => {
      _saveTimer = undefined;
      persistNow();
    }, 50);
  }

  function flushSave(): void {
    if (_saveTimer !== undefined) {
      clearTimeout(_saveTimer);
      _saveTimer = undefined;
      persistNow();
    }
  }

  // Test-only: drop the in-memory layout to an empty state and cancel any
  // pending debounced save so the next `beforeunload` flushSave does not
  // re-persist stale default widgets after the test has explicitly wiped
  // localStorage. No-op outside webdriver, gated by the global hook.
  function resetToEmpty(): void {
    if (_saveTimer !== undefined) {
      clearTimeout(_saveTimer);
      _saveTimer = undefined;
    }
    setLayout(reconcile(
      { widgets: [] },
      { key: 'id', merge: false },
    ));
  }

  // Test-only: overwrite this view's widget list in-place, bypassing the
  // picker + click-to-place flow that costs ~2–3 s per call. Tests use this
  // when they only need a widget to render; exercising the placement code
  // path is the job of the dedicated picker/placement specs.
  function seedWidgets(widgets: Widget[]): void {
    if (_saveTimer !== undefined) {
      clearTimeout(_saveTimer);
      _saveTimer = undefined;
    }
    setLayout(reconcile(
      { widgets },
      { key: 'id', merge: false },
    ));
    persistNow();
  }

  // Cross-window sync: listen for THIS view's storage key
  const offSync = onStorageKey(storageKey, (newValue) => {
    if (!newValue) return;
    let parsed: unknown;
    try { parsed = JSON.parse(newValue); } catch { return; }
    // Validate + migrate through the same schema the initial load uses.
    const next = makeLayoutSchema(viewId).parse(parsed);
    setLayout(reconcile(next, { key: 'id', merge: false }));
  });

  const onUnload = () => flushSave();
  window.addEventListener('beforeunload', onUnload);

  function destroy(): void {
    flushSave();
    offSync();
    window.removeEventListener('beforeunload', onUnload);
  }

  function addWidget(type: string, geom: WidgetGeom = { x: 0, y: 0, w: 0.2, h: 0.2 }, defaultConfig: Record<string, unknown> = {}): string | null {
    if (atFreeWidgetLimit()) return null;
    const id = `${type}-${Date.now()}-${idSeq++}`;
    const r = clampRect(geom);
    // Landing inside an area group joins it ([S-38]). Click-to-place, paste and
    // a cross-window drop all create widgets through here, so the rule holds
    // for every way a widget can arrive on the area — one path, not three.
    const parentId = isContainerType(type) ? null : areaGroupAtRect(layout.widgets, r);
    setLayout(produce((state: LayoutState) => {
      state.widgets.push({
        id, type, x: r.x, y: r.y, w: r.w, h: r.h,
        ...(parentId ? { parentId } : {}),
        config: defaultConfig,
      });
    }));
    // Persist synchronously: handlePlace opens the config window immediately
    // after this returns, and the freshly-spawned window reads the new widget
    // from localStorage; a debounced save would race that read.
    persistNow();
    return id;
  }

  // Create a widget already parented to a container ([S-33]). One atomic write
  // (not addWidget + setWidgetParent): a rejected reparent must never leave a
  // stray top-level widget the user did not ask for. The container's own rect
  // becomes the child's dormant restore rect, so a later pop-out lands the
  // widget on top of the container instead of at (0,0).
  function addChildWidget(
    type: string,
    parentId: string,
    defaultConfig: Record<string, unknown> = {},
    geom?: WidgetGeom,
  ): string | null {
    if (isContainerType(type)) return null; // containers never nest ([S-19])
    const parent = layout.widgets.find((w: Widget) => w.id === parentId);
    if (!parent || !isContainerType(parent.type)) return null;
    if (atFreeWidgetLimit()) return null;
    const id = `${type}-${Date.now()}-${idSeq++}`;
    // An area group's children are laid out on the grid, so the child gets a
    // real rect inside the area ([S-36]); every other container's child gets
    // the container's rect as a dormant restore rect ([S-33]). An explicit
    // `geom` is a restored position (paste, [S-41]); otherwise the size is a
    // FRACTION OF THE AREA, not the registry's px default, because the caller
    // is the config window, whose own viewport says nothing about the monitor
    // the widget will land on.
    const r = !isAreaGroupType(parent.type)
      ? clampRect(parent)
      : geom
        ? clampRect(geom)
        : areaChildRect(
          parent,
          { w: parent.w * 0.4, h: parent.h * 0.4 },
          layout.widgets.filter((w: Widget) => w.parentId === parentId).length,
        );
    setLayout(produce((state: LayoutState) => {
      state.widgets.push({ id, type, x: r.x, y: r.y, w: r.w, h: r.h, parentId, config: defaultConfig });
    }));
    // Persist synchronously, like addWidget: the config window navigates to the
    // new widget's page immediately after this returns, and the widget windows
    // read it back from storage.
    persistNow();
    return id;
  }

  // Moving an AREA group carries its members ([S-38]): the group and everything
  // in it travel as one unit, by the same delta. That is the whole point of
  // grouping widgets by area — the arrangement inside the area is what the user
  // built, and it has to survive the move. Members are clamped individually, so
  // a group shoved against an edge keeps every member on screen ([S-09]).
  // Resizing a group is deliberately NOT propagated: it only changes the area.
  function moveWidget(id: string, x: number, y: number): void {
    const self = layout.widgets.find((w: Widget) => w.id === id);
    if (self && isAreaGroupType(self.type)) {
      const dx = x - self.x;
      const dy = y - self.y;
      setLayout(produce((state: LayoutState) => {
        const g = state.widgets.find((w: Widget) => w.id === id);
        if (!g) return;
        g.x = x; g.y = y;
        if (dx === 0 && dy === 0) return;
        for (const w of state.widgets) {
          if (w.parentId !== id) continue;
          const r = clampRect({ x: w.x + dx, y: w.y + dy, w: w.w, h: w.h });
          w.x = r.x; w.y = r.y; w.w = r.w; w.h = r.h;
        }
      }));
      save();
      return;
    }
    setLayout('widgets', (w: Widget) => w.id === id, { x, y });
    save();
  }

  function updateWidget(id: string, geom: WidgetGeom): void {
    const r = clampRect(geom);
    setLayout('widgets', (w: Widget) => w.id === id, { x: r.x, y: r.y, w: r.w, h: r.h });
    save();
  }

  function updateWidgetConfig(id: string, config: Record<string, unknown>, immediate = false): void {
    setLayout('widgets', (w: Widget) => w.id === id, { config });
    // `immediate` skips the 50ms save debounce so other windows' webviews pick
    // up the change at once: used for cross-widget drag moves, where a lagged
    // persist makes the dropped item appear late in another monitor's window.
    if (immediate) {
      if (_saveTimer !== undefined) { clearTimeout(_saveTimer); _saveTimer = undefined; }
      persistNow();
    } else {
      save();
    }
  }

  function updateWidgetLabel(id: string, label: string): void {
    setLayout(produce((state: LayoutState) => {
      const w = state.widgets.find((w: Widget) => w.id === id);
      if (w) {
        if (label && label.trim()) w.label = label.trim();
        else delete w.label;
      }
    }));
    save();
  }

  function removeWidget(id: string): void {
    setLayout(produce((state: LayoutState) => {
      const idx = state.widgets.findIndex((w: Widget) => w.id === id);
      if (idx !== -1) state.widgets.splice(idx, 1);
      // Removing a stack pops its children back to top-level ([S-21]); user
      // widgets are never deleted as a side effect. Their dormant rects
      // (clamped) become live again.
      for (const w of state.widgets) {
        if (w.parentId === id) {
          delete w.parentId;
          const r = clampRect(w);
          w.x = r.x; w.y = r.y; w.w = r.w; w.h = r.h;
        }
      }
    }));
    save();
  }

  function removeWidgetTree(id: string): void {
    const self = layout.widgets.find((w: Widget) => w.id === id);
    if (self && isAreaGroupType(self.type)) {
      setLayout(produce((state: LayoutState) => {
        state.widgets = state.widgets.filter((w: Widget) => w.id !== id && w.parentId !== id);
      }));
      save();
      return;
    }
    removeWidget(id);
  }

  function setWidgetParent(id: string, parentId: string, geom?: WidgetGeom): void {
    setLayout(produce((state: LayoutState) => {
      const idx = state.widgets.findIndex((w: Widget) => w.id === id);
      const parent = state.widgets.find((w: Widget) => w.id === parentId);
      if (idx === -1 || !parent) return;
      const w = state.widgets[idx];
      if (id === parentId || isContainerType(w.type) || !isContainerType(parent.type)) return;
      w.parentId = parentId;
      if (isAreaGroupType(parent.type)) {
        // An area group's child keeps LIVE geometry ([S-36]): where it was
        // dropped is where it stays.
        if (geom) {
          const r = clampRect(geom);
          w.x = r.x; w.y = r.y; w.w = r.w; w.h = r.h;
        }
        // …and it KEEPS ITS ARRAY POSITION ([S-42]). Area members have no order
        // (group [S-02]), and the grid renders them itself, so moving the entry
        // would only shuffle the `<For>` row — which re-inserts the widget's DOM
        // node, and a re-inserted node reloads its iframe / restarts its canvas.
        // Joining a group must cost nothing visually.
        return;
      }
      // A stack/tabs child's rect goes dormant (untouched, it is the pop-out
      // restore rect) and it appends, because there child order IS render order.
      state.widgets.splice(idx, 1);
      state.widgets.push(w);
    }));
    save();
  }

  function clearWidgetParent(id: string, geom?: WidgetGeom): void {
    setLayout(produce((state: LayoutState) => {
      const w = state.widgets.find((x: Widget) => x.id === id);
      if (!w || w.parentId === undefined) return;
      delete w.parentId;
      const r = clampRect(geom ?? w);
      w.x = r.x; w.y = r.y; w.w = r.w; w.h = r.h;
    }));
    save();
  }

  // Move a child to a sibling's position ([S-35]). Child order is the order of
  // appearance in the flat `widgets` array ([S-19]), so the siblings' array
  // SLOTS are held fixed and only their occupants are re-dealt: everything else
  // in the array (other containers' children, top-level widgets) keeps its
  // index, and the container's own render order follows.
  function moveChildTo(id: string, targetId: string): void {
    setLayout(produce((state: LayoutState) => {
      const w = state.widgets.find((x: Widget) => x.id === id);
      if (!w || id === targetId) return;
      const slots: number[] = [];
      const sibs: Widget[] = [];
      state.widgets.forEach((x: Widget, i: number) => {
        if (x.parentId === w.parentId) { slots.push(i); sibs.push(x); }
      });
      const from = sibs.findIndex(x => x.id === id);
      // A target in another container (or top-level) is not a sibling: no-op
      // rather than a cross-container move, which drag-to-reorder never means.
      const to = sibs.findIndex(x => x.id === targetId);
      if (from === -1 || to === -1) return;
      const [moved] = sibs.splice(from, 1);
      sibs.splice(to, 0, moved);
      slots.forEach((slot, k) => { state.widgets[slot] = sibs[k]; });
    }));
    save();
  }

  function reorderChild(id: string, dir: -1 | 1): void {
    const w = layout.widgets.find((x: Widget) => x.id === id);
    if (!w) return;
    const sibs = layout.widgets.filter((x: Widget) => x.parentId === w.parentId);
    const other = sibs[sibs.findIndex(x => x.id === id) + dir];
    if (!other) return; // already at an end
    moveChildTo(id, other.id);
  }

  return {
    viewId,
    storageKey,
    get layout() { return layout; },
    addWidget,
    addChildWidget,
    moveWidget,
    updateWidget,
    updateWidgetConfig,
    updateWidgetLabel,
    removeWidget,
    removeWidgetTree,
    setWidgetParent,
    clearWidgetParent,
    moveChildTo,
    reorderChild,
    destroy,
    _resetToEmpty: resetToEmpty,
    _seedWidgets: seedWidgets,
  } as ViewLayoutStore;
}

// Test-only hook: e2e helpers call this from `resetAppState` to nuke every
// live store's pending save before the page reload runs `beforeunload`.
// Without it, a debounced save from the default Folder widget (which resolves +
// persists the Desktop path on mount) lands AFTER the test's `localStorage.clear()`
// + setItem-empty, leaving the default widgets in storage when the new page loads.
if (typeof window !== 'undefined' && navigator.webdriver) {
  window.__themiaResetLayouts = () => {
    for (const s of stores.values()) s._resetToEmpty();
  };
  // Fast-seed a widget list into the active (or named) view, bypassing the
  // picker + place flow. Cuts ~2–3 s per widget vs `pickWidget`+
  // `clickGridCell` in specs that only care about the rendered result.
  // Accepts BOTH the percent shape (x/y/w/h) and the legacy cell shape
  // (col/row/colSpan/rowSpan); the latter is migrated through the same
  // geometry path as a persisted load ([S-07]) so existing specs keep working.
  window.__themiaSeedWidgets = (
    widgets: unknown[],
    viewId?: string,
  ) => {
    const id = viewId ?? getActiveViewId();
    const store = getViewLayoutStore(id);
    // Legacy cell-shaped seeds (col/row/colSpan/rowSpan) were written against the
    // classic coarse 12×8 grid, NOT the live fine snap grid (gridDims() ≈
    // screen/10px). Migrate them against that nominal grid so e.g. `colSpan:4`
    // becomes ~1/3 width (as the spec intends) instead of 4/192 ≈ 2%, a
    // microscopic, content-less widget that breaks the spec. Percent-shaped seeds
    // (x/y/w/h) pass straight through migrateGeom untouched.
    const migrated = (widgets ?? [])
      .map(w => migrateGeom(w, 12, 8))
      .filter(isWidget);
    store._seedWidgets(migrated);
  };
}

// ---------------------------------------------------------------------------
// Registry: one store per viewId, lazily created
// ---------------------------------------------------------------------------

const stores = new Map<string, ViewLayoutStore>();

export function getViewLayoutStore(id: string): ViewLayoutStore {
  let s = stores.get(id);
  if (!s) {
    s = createViewLayoutStore(id);
    stores.set(id, s);
  }
  return s;
}

export function destroyViewLayoutStore(id: string): void {
  const s = stores.get(id);
  if (!s) return;
  s.destroy();
  stores.delete(id);
}

export function activeLayoutStore(): ViewLayoutStore {
  return getViewLayoutStore(getActiveViewId());
}

/** Find the viewId whose store currently contains the given widget id. */
export function findViewForWidget(widgetId: string): string | null {
  for (const [id, s] of stores) {
    if (s.layout.widgets.some(w => w.id === widgetId)) return id;
  }
  return null;
}

/**
 * Total widget count across ALL views in this browsing context.
 * Reads live stores when available, falls back to localStorage for views the
 * current window has never instantiated. Reactive via the storage bump signal.
 *
 * The tray collection (Tray.SPEC [S-01]) is counted alongside the views: it
 * holds real widget instances, so leaving it out would hand the free tier an
 * uncapped side door — and `addWidget` already refuses at the ceiling whichever
 * collection it is adding to.
 */
export function totalWidgetCount(): number {
  layoutStorageBump();
  let total = 0;
  const seenKey = new Set<string>();
  for (const id of [...viewStore.list.map(v => v.id), TRAY_COLLECTION_ID]) {
    const s = stores.get(id);
    if (s) {
      total += s.layout.widgets.length;
      seenKey.add(s.storageKey);
      continue;
    }
    const key = layoutKey(id);
    if (seenKey.has(key)) continue;
    seenKey.add(key);
    try {
      const raw = localStorage.getItem(key);
      if (!raw) continue;
      const data = JSON.parse(raw);
      if (Array.isArray(data.widgets)) total += data.widgets.length;
    } catch {
      // ignore: a corrupt/absent per-view layout key contributes 0 to the count.
    }
  }
  return total;
}

/** Would adding `add` more widgets cross the free-tier ceiling? Parameterised
 *  because a paste can bring several at once (an area group + its members,
 *  [S-41]) and a HALF-pasted group is worse than a paywall. */
export function wouldExceedFreeLimit(add = 1): boolean {
  return !isPro() && totalWidgetCount() + add > FREE_WIDGET_LIMIT;
}

export function atFreeWidgetLimit(): boolean {
  return wouldExceedFreeLimit(1);
}

// ---------------------------------------------------------------------------
// Global mutator wrappers: back-compat for module callers (operationLog,
// widgetClipboard, gridDragManager, widget files). Each routes to the right
// per-view store. Active-view ops use the active store; widget-id ops
// look up which view owns the widget.
// ---------------------------------------------------------------------------

export function addWidget(type: string, geom?: WidgetGeom, defaultConfig?: Record<string, unknown>): string | null {
  return activeLayoutStore().addWidget(type, geom, defaultConfig);
}

export function moveWidget(id: string, x: number, y: number): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).moveWidget(id, x, y);
}

export function updateWidget(id: string, geom: WidgetGeom): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).updateWidget(id, geom);
}

export function updateWidgetConfig(id: string, config: Record<string, unknown>): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).updateWidgetConfig(id, config);
}

export function updateWidgetLabel(id: string, label: string): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).updateWidgetLabel(id, label);
}

export function removeWidget(id: string): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).removeWidget(id);
}

/** Remove a widget as part of a MOVE: an area group takes its members with it
 *  ([S-41]). See `ViewLayoutStore.removeWidgetTree`. */
export function removeWidgetTree(id: string): void {
  const sid = findViewForWidget(id);
  if (sid) getViewLayoutStore(sid).removeWidgetTree(id);
}


/**
 * Remove a widget from a specific view by storage key. Used for cross-window
 * cut/drop where the source view may not be active in this window.
 * If the view has a live store, route through it; otherwise edit localStorage.
 *
 * `alsoMembers` takes an area group's members with it ([S-41]) — pass it only
 * when the destination re-created them (a CUT does; a cross-window DRAG does
 * not, and its members pop to top-level per [S-22]).
 */
export function removeWidgetFromKey(storageKey: string, id: string, alsoMembers = false): void {
  // Find live store with matching storageKey
  for (const s of stores.values()) {
    if (s.storageKey === storageKey) {
      if (alsoMembers) s.removeWidgetTree(id);
      else s.removeWidget(id);
      return;
    }
  }
  // No live store: edit localStorage directly; storage event will sync
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return;
    const data = JSON.parse(raw);
    if (!Array.isArray(data.widgets)) return;
    const idx = data.widgets.findIndex((w: Widget) => w.id === id);
    if (idx === -1) return;
    const gone = data.widgets[idx];
    data.widgets.splice(idx, 1);
    if (alsoMembers && isAreaGroupType(gone?.type)) {
      data.widgets = data.widgets.filter((w: Widget) => w.parentId !== id);
    } else {
      // A removed container's children must never vanish with it ([S-21]); the
      // live-store path pops them, so the raw path has to as well.
      for (const w of data.widgets as Widget[]) if (w.parentId === id) delete w.parentId;
    }
    localStorage.setItem(storageKey, JSON.stringify(data));
  } catch {
    // ignore: best-effort cross-window removal; a parse/write failure leaves the
    // source widget in place rather than corrupting the stored layout.
  }
}

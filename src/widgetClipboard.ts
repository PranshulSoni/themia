import { createSignal } from 'solid-js';
import type { Widget } from './layout.ts';
import {
  addWidget, activeLayoutStore, findViewForWidget, getViewLayoutStore,
  isAreaGroupType, wouldExceedFreeLimit,
} from './layout.ts';
import { fracSizeToPx, pxSizeToFrac, screenSize } from './gridGeometry.ts';
import { onStorageKey, sharedStorage } from './sync.ts';
import { logger } from './debugLog.ts';
import {
  loadPersisted,
  savePersistedBare,
  isString,
  isNumber,
  type PersistedSchema,
} from './persistedStore.ts';

const CLIPBOARD_KEY = 'widget-clipboard';

// Size is stored in PIXELS so a paste onto a different-size monitor preserves
// physical size ([S-12]); paste converts to a fraction of the target screen.
/**
 * A member of a copied AREA GROUP ([S-41]). Position is stored as a PIXEL
 * offset from the group's top-left (not an absolute rect and not a fraction of
 * the source screen), so the arrangement inside the area survives a paste
 * anywhere, on any monitor: the group lands where the user drops it, keeps its
 * physical size ([S-12]), and every member keeps its physical size AND its
 * physical place within it.
 */
export interface ClippedMember {
  type: string;
  config?: Record<string, unknown>;
  label?: string;
  dxPx: number;
  dyPx: number;
  wPx: number;
  hPx: number;
}

export interface ClippedWidget {
  type: string;
  config?: Record<string, unknown>;
  wPx: number;
  hPx: number;
  label?: string;
  /** Present only for an area group: the widgets inside it ([S-41]). */
  members?: ClippedMember[];
}

export interface ClipboardState {
  widget: ClippedWidget | null;
  mode: 'copy' | 'cut';
  sourceWidgetId?: string; // for 'cut': the widget id to remove on paste
  sourceLayoutKey?: string; // for 'cut': the storage key of the view the source lives on
}

// Schema'd persistence (1.12). The clipboard is consumed by pasteWidget(), which
// dereferences widget.wPx/hPx, so a corrupt persisted widget must become `null`
// (paste no-ops) rather than crash. So validate the nested shape: a widget
// missing its mandatory type/wPx/hPx is dropped to null.
function parseClippedMember(raw: unknown): ClippedMember | null {
  if (!raw || typeof raw !== 'object') return null;
  const m = raw as Record<string, unknown>;
  if (!isString(m.type)) return null;
  for (const k of ['dxPx', 'dyPx', 'wPx', 'hPx']) if (!isNumber(m[k])) return null;
  const out: ClippedMember = {
    type: m.type,
    dxPx: m.dxPx as number, dyPx: m.dyPx as number,
    wPx: m.wPx as number, hPx: m.hPx as number,
  };
  if (m.config && typeof m.config === 'object') out.config = m.config as Record<string, unknown>;
  if (isString(m.label)) out.label = m.label;
  return out;
}

function parseClippedWidget(raw: unknown): ClippedWidget | null {
  if (!raw || typeof raw !== 'object') return null;
  const w = raw as Record<string, unknown>;
  if (!isString(w.type) || !isNumber(w.wPx) || !isNumber(w.hPx)) return null;
  const out: ClippedWidget = { type: w.type, wPx: w.wPx, hPx: w.hPx };
  if (w.config && typeof w.config === 'object') out.config = w.config as Record<string, unknown>;
  if (isString(w.label)) out.label = w.label;
  // A structurally-broken member is DROPPED, never half-built: the paste then
  // rebuilds a group with one widget missing instead of throwing on a NaN rect.
  if (Array.isArray(w.members)) {
    const members = w.members.map(parseClippedMember).filter((m): m is ClippedMember => m !== null);
    if (members.length) out.members = members;
  }
  return out;
}

const SCHEMA: PersistedSchema<ClipboardState> = {
  version: 1,
  defaults: () => ({ widget: null, mode: 'copy' }),
  parse: (raw: unknown): ClipboardState => {
    if (!raw || typeof raw !== 'object') return { widget: null, mode: 'copy' };
    const r = raw as Record<string, unknown>;
    const out: ClipboardState = {
      widget: parseClippedWidget(r.widget),
      mode: r.mode === 'cut' ? 'cut' : 'copy',
    };
    if (isString(r.sourceWidgetId)) out.sourceWidgetId = r.sourceWidgetId;
    if (isString(r.sourceLayoutKey)) out.sourceLayoutKey = r.sourceLayoutKey;
    return out;
  },
};

const [state, setState] = createSignal<ClipboardState>(loadFromStorage());
export const widgetClipboard = state;

function loadFromStorage(): ClipboardState {
  return loadPersisted(CLIPBOARD_KEY, SCHEMA, localStorage, {
    onCorrupt: (m) => logger.error('widgetClipboard', m),
  });
}

function saveToStorage(s: ClipboardState): void {
  // Bare format: the clipboard key is synced cross-window and read raw.
  savePersistedBare(
    CLIPBOARD_KEY,
    { widget: s.widget, mode: s.mode, sourceWidgetId: s.sourceWidgetId, sourceLayoutKey: s.sourceLayoutKey },
    sharedStorage,
    { onError: (m) => logger.error('widgetClipboard', m) },
  );
}

/** The widgets inside an area group, as px offsets from its top-left ([S-41]).
 *  Empty for every other widget type — only an area group owns placed members. */
function clipMembers(w: Widget): ClippedMember[] {
  if (!isAreaGroupType(w.type)) return [];
  const viewId = findViewForWidget(w.id);
  const store = viewId ? getViewLayoutStore(viewId) : activeLayoutStore();
  const scr = screenSize();
  return store.layout.widgets
    .filter(m => m.parentId === w.id)
    .map(m => ({
      type: m.type,
      config: m.config ? JSON.parse(JSON.stringify(m.config)) : undefined,
      label: m.label,
      dxPx: (m.x - w.x) * scr.w,
      dyPx: (m.y - w.y) * scr.h,
      wPx: m.w * scr.w,
      hPx: m.h * scr.h,
    }));
}

function clipFrom(w: Widget): ClippedWidget {
  const px = fracSizeToPx({ w: w.w, h: w.h });
  const members = clipMembers(w);
  return {
    type: w.type,
    config: w.config ? JSON.parse(JSON.stringify(w.config)) : undefined,
    wPx: px.width,
    hPx: px.height,
    label: w.label,
    ...(members.length ? { members } : {}),
  };
}

export function copyWidget(w: Widget): void {
  const clip: ClipboardState = { widget: clipFrom(w), mode: 'copy' };
  setState(clip);
  saveToStorage(clip);
}

export function cutWidget(w: Widget): void {
  const clip: ClipboardState = {
    widget: clipFrom(w),
    mode: 'cut',
    sourceWidgetId: w.id,
    sourceLayoutKey: activeLayoutStore().storageKey,
  };
  setState(clip);
  saveToStorage(clip);
}

/** How many widgets a paste of the current clipboard would create: an area
 *  group brings its members ([S-41]), so the free-tier check has to count them
 *  BEFORE anything lands — a half-pasted group is worse than none. */
export function clipboardWidgetCount(): number {
  const w = state().widget;
  return w ? 1 + (w.members?.length ?? 0) : 0;
}

/** Paste the clipboard content into the current view with its top-left at the
 *  fractional point (x, y). Size is restored to physical px on this screen
 *  ([S-12]). An area group brings the widgets that were inside it, at the same
 *  physical places within the area ([S-41]). Returns true on success. */
export function pasteWidget(x = 0, y = 0): boolean {
  const s = state();
  if (!s.widget) return false;
  // All-or-nothing under the free tier: the caller routes to the license page.
  if (wouldExceedFreeLimit(clipboardWidgetCount())) return false;

  const size = pxSizeToFrac({ width: s.widget.wPx, height: s.widget.hPx });
  const ok = addWidget(
    s.widget.type,
    { x, y, w: size.w, h: size.h },
    s.widget.config ?? {},
  );
  if (!ok) return false;

  // Members are re-created INSIDE the pasted group, each at its captured offset
  // converted to a fraction of THIS screen. `addChildWidget` is one atomic write
  // per member with the parent named explicitly ([S-33]) — never addWidget +
  // reparent, and never a reliance on the drop-geometry rule ([S-38]), which
  // would drop a member that overhangs the area's edge.
  const members = s.widget.members ?? [];
  if (members.length) {
    const scr = screenSize();
    const store = activeLayoutStore();
    for (const m of members) {
      const rect = {
        x: x + m.dxPx / Math.max(1, scr.w),
        y: y + m.dyPx / Math.max(1, scr.h),
        w: m.wPx / Math.max(1, scr.w),
        h: m.hPx / Math.max(1, scr.h),
      };
      const id = store.addChildWidget(m.type, ok, m.config ?? {}, rect);
      if (id && m.label) store.updateWidgetLabel(id, m.label);
    }
  }

  // For 'cut', we want the source widget removed. The source-removal is
  // triggered by the source window itself when it sees its own clipboard,
  // but cross-window cut needs explicit handling, so we just clear clipboard
  // here and let the user delete the original.
  if (s.mode === 'cut') {
    const cleared: ClipboardState = { widget: null, mode: 'copy' };
    setState(cleared);
    saveToStorage(cleared);
  }

  return true;
}

export function hasClipboard(): boolean {
  return state().widget !== null;
}

// Sync clipboard from other windows via storage events
onStorageKey(CLIPBOARD_KEY, () => {
  setState(loadFromStorage());
});

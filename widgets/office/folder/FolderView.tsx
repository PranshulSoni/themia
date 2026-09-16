import { createSignal, createMemo, createEffect, onMount, onCleanup, batch, untrack } from 'solid-js';
import { appActive } from '../../../src/appActive';
import { debugLog, logger } from '../../../src/debugLog';
import type { JSX, Component } from 'solid-js';
import { createStore, reconcile } from 'solid-js/store';
import { For, Show, Switch, Match } from 'solid-js';
import { watch } from '@tauri-apps/plugin-fs';
import { invoke } from '../../../src/demo/invoke.ts';
import { isDemo } from '../../../src/demo/invoke.ts';
import type { Commands } from '../../../src/ipc.ts';
import { demoFolderEntries } from '../../../src/demo/fixtures.ts';
import { emit, listen } from '@tauri-apps/api/event';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { desktopDir } from '@tauri-apps/api/path';
import { startDrag as startNativeDrag, registerDropZone, type NativeDragEvent } from '../../../src/nativeDragDrop.ts';
import { pastableSources, samePath } from './dropTarget.ts';
import { parseFilterKeywords, matchesFilter } from './fileFilter.ts';
import {
  freshGestureState, beginPress, pollStep, resetGestureCandidates,
} from './gesture.ts';
import { gridTemplate } from './gridLayout.ts';
import { createWatchHandle } from './watchLifecycle.ts';
import { createSelection } from '../../../src/input/selectionStore.ts';
import { onAppBlur } from '../../../src/input/appBlur.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './FolderView.css';
import { resolveColumns, getColumnDef } from './columns.ts';
import { t } from '../../../src/i18n.ts';
// Local signal to track cut-pending files for visual feedback (dimmed items).
// The actual clipboard data lives in the Windows system clipboard.
import { selectedFolder, selectFolder, clearSelectedFolder } from '../../../src/folderSelection.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import { showCtxMenu as _showCtxMenu, CtxMenuItem } from '../../../src/ctxMenuStore.ts';
import { beginOp } from '../../../src/operationLog.ts';
import {
  Cloud, CircleCheck, GitBranch,
  Folder, Copy, Scissors, ClipboardPaste, Trash2,
  ExternalLink, AppWindow, CheckSquare, RefreshCw,
  ArrowUp, ArrowDown, ChevronLeft, ChevronRight, ChevronDown, Home,
  Image, Film, Music, BookOpen, FileText, FileSpreadsheet,
  Presentation, Package, Cog, Code, File,
  FolderPlus, Plus, Search, Pencil, Link, Info,
  List, Table, LayoutGrid, Grid3x3, Eye, EyeOff,
} from 'lucide-solid';

// ── Type definitions ──────────────────────────────────────────────────────
interface DirEntry {
  name: string;
  isDirectory: boolean;
  isFile: boolean;
  isSymlink: boolean;
  isHidden: boolean;
}

type ItemSize = 'xs' | 'small' | 'medium' | 'large';
interface ItemSizePreset {
  padV: number; padH: number; icon: number; font: number; gapH: number; badge: number;
}
const ITEM_SIZE_PRESETS: Record<ItemSize, ItemSizePreset> = {
  xs:     { padV: 1,  padH: 8,  icon: 11, font: 10, gapH: 5,  badge: 10 },
  small:  { padV: 3,  padH: 10, icon: 13, font: 11, gapH: 7,  badge: 12 },
  medium: { padV: 6,  padH: 12, icon: 16, font: 12, gapH: 9,  badge: 14 },
  large:  { padV: 9,  padH: 14, icon: 20, font: 13, gapH: 11, badge: 16 },
};

type GridItemSize = 'small' | 'medium' | 'large';
interface GridSizePreset {
  minColWidth: number; rowHeight: number; iconSize: number; thumbnailSize: number;
  fontSize: number; fontIconSize: number; padTop: number; padSide: number; padBottom: number;
  innerGap: number; lineClamp: number; thumbFetch: number;
}
const GRID_SIZE_PRESETS: Record<GridItemSize, GridSizePreset> = {
  small:  { minColWidth: 80,  rowHeight: 90,  iconSize: 32, thumbnailSize: 40, fontSize: 10, fontIconSize: 26, padTop: 12, padSide: 10, padBottom: 8,  innerGap: 5, lineClamp: 2, thumbFetch: 64  },
  medium: { minColWidth: 110, rowHeight: 120, iconSize: 48, thumbnailSize: 56, fontSize: 11, fontIconSize: 38, padTop: 14, padSide: 12, padBottom: 10, innerGap: 6, lineClamp: 2, thumbFetch: 96  },
  large:  { minColWidth: 150, rowHeight: 160, iconSize: 64, thumbnailSize: 80, fontSize: 12, fontIconSize: 50, padTop: 16, padSide: 14, padBottom: 12, innerGap: 7, lineClamp: 3, thumbFetch: 128 },
};

// The `grid` (labelled tiles) and `icons` (icon-only) view modes share all grid
// layout / keyboard / icon-sizing behaviour; `icons` just hides the filename and
// shrinks tiles to the icon. This is the single predicate for "is a grid layout".
type ViewMode = 'list' | 'details' | 'grid' | 'icons';
function isGridViewMode(vm: string | undefined): boolean {
  return vm === 'grid' || vm === 'icons';
}

interface FolderViewProps {
  path?: string;
  viewMode?: ViewMode;
  navigationMode?: 'explorer' | 'navigate';
  clickMode?: 'single' | 'double';
  filter?: string;
  showHidden?: boolean;
  showFileExtension?: boolean;
  headerMode?: HeaderMode;
  itemSize?: ItemSize;
  gridStretchHeight?: boolean;
  gridItemSize?: GridItemSize;
  detailsColumns?: string[] | Record<string, boolean>;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
}

interface BandRect {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

interface CursorState {
  x: number;
  y: number;
  left_down: boolean;
  ctrl_down: boolean;
}

interface CloudBadgeProps {
  fp: string;
  inline: boolean;
}

interface GitStatusResult {
  is_git: boolean;
  branch: string;
  files: Record<string, string>;
  staged: number;
  modified: number;
  untracked: number;
  deleted: number;
  conflicted: number;
}

interface GitRepoInfo {
  branch: string;
  dirty: boolean;
}

interface FileIconProps {
  fp: string;
  entry: DirEntry;
  class?: string;
  imgClass?: string;
}

// ── ShellNew & shell menu caches ──────────────────────────────────────────

interface ShellNewEntry {
  extension: string;
  label: string;
  icon_base64: string | null;
  template_data: number[] | null;
}

interface ShellMenuEntry {
  id: string;
  label: string;
  icon_path: string | null;
  icon_base64: string | null;
  command: string;
  extended: boolean;
}

let shellNewCache: ShellNewEntry[] | null = null;
let shellFileMenuCache: ShellMenuEntry[] | null = null;
let shellDirMenuCache: ShellMenuEntry[] | null = null;
let shellBgMenuCache: ShellMenuEntry[] | null = null;

// Wraps a shell icon (base64 PNG) as a Solid Component so ContextMenu can
// render it via <Dynamic>: building <img> JSX inside an event handler leaks
// computations (no enclosing reactive root to own/dispose them).
function shellIconComponent(base64: string): Component<{ size?: number }> {
  return (iprops) => {
    const s = iprops.size ?? 14;
    return <img src={`data:image/png;base64,${base64}`} width={s} height={s} style="display:block" />;
  };
}

async function ensureShellCaches(): Promise<void> {
  const promises: Promise<void>[] = [];
  if (!shellNewCache) {
    promises.push(invoke('get_shell_new_entries').then(r => { shellNewCache = r; }));
  }
  if (!shellFileMenuCache) {
    promises.push(invoke('get_shell_menu_entries', { isDirectory: false, isBackground: false }).then(r => { shellFileMenuCache = r; }));
  }
  if (!shellDirMenuCache) {
    promises.push(invoke('get_shell_menu_entries', { isDirectory: true, isBackground: false }).then(r => { shellDirMenuCache = r; }));
  }
  if (!shellBgMenuCache) {
    promises.push(invoke('get_shell_menu_entries', { isDirectory: false, isBackground: true }).then(r => { shellBgMenuCache = r; }));
  }
  await Promise.all(promises);
}

// ── Sort field type ───────────────────────────────────────────────────────
type SortField = 'name' | 'size' | 'modified' | 'type';

// ── Module-level keyboard router ───────────────────────────────────────────
// WS_EX_NOACTIVATE means clicking a widget never grants it Win32 keyboard
// focus.  A WH_KEYBOARD_LL hook in the Rust backend intercepts our shortcut
// keys and emits a "folder-key" Tauri event to whichever widget last called
// activate_widget_keyboard().  This listener routes that event to the correct
// component instance's handleKeyDown without activating any window.
interface SyntheticKeyEvent {
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  key: string;
  preventDefault: () => void;
}

// Keys the Rust WH_KEYBOARD_LL hook emits without Ctrl. Anything else (c/v/x/a)
// comes from a Ctrl+<letter> combo. Shift is prefixed as `shift+<action>`.
const NON_CTRL_KEYS: ReadonlySet<string> = new Set([
  'delete', 'escape', 'f2',
  'up', 'down', 'left', 'right',
  'enter', 'backspace',
  'home', 'end', 'pageup', 'pagedown',
]);

const KEY_ALIAS: Record<string, string> = {
  up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight',
  enter: 'Enter', backspace: 'Backspace',
  home: 'Home', end: 'End', pageup: 'PageUp', pagedown: 'PageDown',
  delete: 'Delete', escape: 'Escape', f2: 'F2',
};

let _activeKeyHandler: ((e: SyntheticKeyEvent) => void) | null = null;
getCurrentWindow().listen<string>('folder-key', (event) => {
  let raw = event.payload;
  let shift = false;
  if (raw.startsWith('shift+')) { shift = true; raw = raw.slice(6); }
  const isCtrlLetter = !NON_CTRL_KEYS.has(raw);
  _activeKeyHandler?.({
    ctrlKey:       isCtrlLetter,
    metaKey:       false,
    shiftKey:      shift,
    key:           KEY_ALIAS[raw] ?? raw,
    preventDefault: () => {},
  });
}).catch(() => {});

// ── Module-level cut-pending state ────────────────────────────────────────
// Tracks which files have been cut (Ctrl+X) for visual dimming.
// Updated when the user cuts files; cleared on paste or new copy.
const [cutPendingPaths, setCutPendingPaths] = createSignal<string[]>([]);

// ── Webdriver-only debug bridge ──────────────────────────────────────────
// Under webdriver (e2e) expose a counter that tests can poll to check the
// watcher → load path without waiting on rendered DOM. No-op in prod.
interface FolderDebug {
  watchRegistered: number;
  watchRejected: number;
  watchError: string;
  watchEvents:    number;
  loadFires:      Array<{ src: string; ts: number }>;
  pollFires:      number;
  trailingFires:  number;
  lastPath:       string;
}
let _folderDebug: FolderDebug | null = null;
if (typeof navigator !== 'undefined' && navigator.webdriver) {
  _folderDebug = {
    watchRegistered: 0, watchRejected: 0, watchError: '', watchEvents: 0,
    loadFires: [], pollFires: 0, trailingFires: 0, lastPath: '',
  };
  (window as Window & { __folderDebug?: FolderDebug }).__folderDebug = _folderDebug;
}

// ── Module-level drag target ──────────────────────────────────────────────
// Shared across all FolderView instances in the same window so the
// destination widget can show drag-over indicators during same-window drag.


// ── Constants ──────────────────────────────────────────────────────────────
// Windows file attribute flags used by all cloud-sync providers
// (OneDrive, Google Drive, Dropbox, Synology Drive, Box, iCloud, …)
const FILE_ATTR_REPARSE_POINT: number         = 0x400;    // managed by a sync engine
const FILE_ATTR_OFFLINE: number               = 0x1000;   // data not present locally
const FILE_ATTR_RECALL_ON_OPEN: number        = 0x40000;  // cloud placeholder (alt flag)
const FILE_ATTR_PINNED: number                = 0x80000;  // always keep on this device
const FILE_ATTR_UNPINNED: number              = 0x100000; // free up space eligible
const FILE_ATTR_RECALL_ON_DATA_ACCESS: number = 0x400000; // cloud-only placeholder

const EXT_ICON_MAP: Record<string, typeof File> = {
  jpg: Image, jpeg: Image, png: Image, gif: Image, webp: Image, svg: Image, bmp: Image,
  mp4: Film, mov: Film, avi: Film, mkv: Film, wmv: Film,
  mp3: Music, wav: Music, flac: Music, aac: Music, ogg: Music,
  pdf: BookOpen,
  doc: FileText, docx: FileText, odt: FileText,
  xls: FileSpreadsheet, xlsx: FileSpreadsheet, csv: FileSpreadsheet,
  ppt: Presentation, pptx: Presentation,
  zip: Package, rar: Package, '7z': Package, tar: Package, gz: Package,
  exe: Cog, msi: Cog, bat: Cog,
  js: Code, ts: Code, py: Code, rs: Code, html: Code, css: Code,
  txt: FileText, md: FileText, log: FileText,
};

// ── Pure helpers (no reactivity needed) ───────────────────────────────────
function pathJoin(dir: string, name: string): string {
  return dir.replace(/\//g, '\\').replace(/\\$/, '') + '\\' + name;
}

function entryPath(dir: string, entry: DirEntry): string {
  return pathJoin(dir, entry.name);
}

function folderName(p: string): string {
  return p.replace(/\\/g, '/').split('/').filter(Boolean).pop() ?? p;
}

function fmtSize(bytes: number | null | undefined): string {
  if (bytes == null) return '—';
  if (bytes < 1024)             return `${bytes} B`;
  if (bytes < 1024 * 1024)     return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 ** 3)       return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function fmtDate(mtime: Date | number | null | undefined): string {
  if (!mtime) return '—';
  const d = mtime instanceof Date ? mtime
    : new Date(typeof mtime === 'number' ? mtime : mtime * 1000);
  if (isNaN(d.getTime())) return '—';
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

// ── Component ──────────────────────────────────────────────────────────────
export default function FolderView(props: FolderViewProps) {
  const layoutStore = useLayout();
  // Props are accessed via props.xxx; never destructure, it breaks reactivity.
  const p: FolderViewProps = props; // shorthand alias; still reactive
  // ── Sort ─────────────────────────────────────────────────────────────────
  const [sortField, setSortField] = createSignal<SortField>('name');

  // ── Live search ──────────────────────────────────────────────────────────
  const [showSearch,     setShowSearch]     = createSignal<boolean>(false);
  const [runtimeFilter,  setRuntimeFilter]  = createSignal<string>('');
  let searchInputEl: HTMLInputElement | undefined;

  // ── Inline rename ────────────────────────────────────────────────────────
  const [renamingPath, setRenamingPath] = createSignal<string | null>(null);
  const [renameValue,  setRenameValue]  = createSignal<string>('');
  const [pendingRenameName, setPendingRenameName] = createSignal<string | null>(null);
  let renameInputEl: HTMLInputElement | undefined;

  // ── Scroll position memory ────────────────────────────────────────────────
  const scrollPositions = new Map<string, number>();
  const [pendingScrollPath, setPendingScrollPath] = createSignal<string | null>(null);

  // ── Folder item count + thumbnail caches ─────────────────────────────────
  const [folderCountCache, setFolderCountCache] = createStore<Record<string, number | undefined>>({});

  // ── Navigation history ───────────────────────────────────────────────────
  const [history,      setHistory]      = createSignal<string[]>([]);
  const [historyIndex, setHistoryIndex] = createSignal<number>(-1);
  const [currentPath,  setCurrentPath]  = createSignal<string>(p.path ?? '');

  const canGoBack    = createMemo(() => historyIndex() > 0);
  const canGoForward = createMemo(() => historyIndex() < history().length - 1);
  const isAtHome     = createMemo(() => currentPath() === (p.path ?? ''));

  // Sync when `path` prop changes
  createEffect(() => {
    const root = p.path ?? '';
    batch(() => {
      setHistory(root ? [root] : []);
      setHistoryIndex(root ? 0 : -1);
      setCurrentPath(root);
    });
  });

  // First-run / unconfigured folder: resolve the user's Desktop as the root,
  // lazily here at widget render, no App-level timer or `__seedDesktop` sentinel
  // (1.26: no wall-clock in correctness paths). One shot, only when this widget
  // has no path yet. Persist from the STORED config (not props) so the resolved
  // path is concrete; the path effect above then re-navigates to it.
  onMount(() => {
    const wid = p._widgetId;
    if (!wid || (p.path ?? '')) return;
    void desktopDir()
      .then((path) => {
        if (!path) return;
        const w = layoutStore.layout.widgets.find((x) => x.id === wid);
        const cfg = (w?.config ?? {}) as Record<string, unknown>;
        if (cfg.path) return; // already filled, guard against a double-resolve
        layoutStore.updateWidgetConfig(wid, { ...cfg, path });
      })
      .catch(() => { /* desktopDir unavailable, leave empty, no retry (rare) */ });
  });

  function saveScrollForPath(path: string): void {
    if (!scrollWrapEl || !path) return;
    const el = scrollWrapEl.querySelector('.list, .grid-container, .details-container') as HTMLElement | null;
    if (el) scrollPositions.set(path, el.scrollTop);
  }

  function navigateTo(newPath: string): void {
    if (!newPath || newPath === currentPath()) return;
    debugLog(DBG, `nav → ${newPath}`);
    saveScrollForPath(currentPath());
    const base = history().slice(0, historyIndex() + 1);
    batch(() => {
      setHistory([...base, newPath]);
      setHistoryIndex(base.length);
      setCurrentPath(newPath);
    });
    clearSelection();
    setPendingScrollPath(newPath);
    // Keep paste target in sync when navigating within the selected widget.
    if (p._widgetId && selectedFolder()?.widgetId === p._widgetId) {
      selectFolder(p._widgetId, newPath);
    }
  }

  function goBack(): void {
    if (!canGoBack()) return;
    debugLog(DBG, `nav ← back`);
    saveScrollForPath(currentPath());
    const idx = historyIndex() - 1;
    const target = history()[idx];
    batch(() => { setHistoryIndex(idx); setCurrentPath(target); });
    clearSelection();
    setPendingScrollPath(target);
  }

  function goForward(): void {
    if (!canGoForward()) return;
    debugLog(DBG, `nav → forward`);
    saveScrollForPath(currentPath());
    const idx = historyIndex() + 1;
    const target = history()[idx];
    batch(() => { setHistoryIndex(idx); setCurrentPath(target); });
    clearSelection();
    setPendingScrollPath(target);
  }

  function goHome(): void { navigateTo(p.path ?? ''); }

  function handleMouseDown(e: MouseEvent): void {
    // Any click anywhere in the widget activates the keyboard router.
    if (e.button === 0) {
      _activeKeyHandler = handleKeyDown;
      invoke('activate_widget_keyboard', { label: getCurrentWindow().label }).catch(() => {});
    }
    if ((p.navigationMode ?? 'explorer') !== 'navigate') return;
    if (e.button === 3) { e.preventDefault(); goBack(); }
    else if (e.button === 4) { e.preventDefault(); goForward(); }
  }

  function handleItemDblClick(entry: DirEntry): void {
    const fp = entryPath(currentPath(), entry);
    const lower = entry.name.toLowerCase();
    if (lower.endsWith('.url')) {
      invoke('open_file', { path: fp });
      return;
    }
    if (lower.endsWith('.lnk')) {
      invoke('resolve_shortcut', { path: fp })
        .then(({ target_path, is_directory }) => {
          if ((p.navigationMode ?? 'explorer') === 'navigate' && is_directory) {
            navigateTo(target_path);
          } else {
            invoke('open_file', { path: target_path });
          }
        })
        .catch(() => invoke('open_file', { path: fp }));
      return;
    }
    if ((p.navigationMode ?? 'explorer') === 'navigate' && entry.isDirectory) {
      navigateTo(fp);
    } else {
      invoke('open_file', { path: fp });
    }
  }

  // ── Selection ────────────────────────────────────────────────────────────
  // Backed by the shared selection primitive (ctrl/shift/plain click semantics,
  // anchor tracking, selectAll). `selectItem` / `clearSelection` remain thin
  // wrappers so call-sites don't change, and `selectItem` still focuses the
  // scroll wrapper so keyboard shortcuts keep flowing into this widget.
  const selection = createSelection<string>({
    orderedIds: () => filteredEntries().map(e => entryPath(currentPath(), e)),
    singleClickToggle: 'deselect',
  });
  const selectedPaths = selection.selected;
  const lastSelectedPath = selection.anchor;

  function clearSelection(): void {
    selection.clear();
  }

  // Escape clears both the visual selection and any pending cut state,
  // mirroring File Explorer behavior where Esc cancels a Ctrl+X mid-cut.
  function clearAfterEscape(): void {
    selection.set(new Set<string>());
    if (cutPendingPaths().length > 0) {
      setCutPendingPaths([]);
      invoke('clipboard_set_files', { paths: [], cut: false })
        .catch(e => logger.error(DBG, `clipboard_set_files clear failed: ${e}`));
    }
  }

  function selectItem(fp: string, event?: { ctrlKey?: boolean; metaKey?: boolean; shiftKey?: boolean }): void {
    scrollWrapEl?.focus({ preventScroll: true });
    selection.click(fp, event);
  }

  // ── Keyboard & clipboard ops ─────────────────────────────────────────────
  function handleKeyDown(e: KeyboardEvent | SyntheticKeyEvent): void {
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    const shift = (e as KeyboardEvent).shiftKey ?? false;
    if (e.ctrlKey || e.metaKey) {
      if      (e.key === 'c') { e.preventDefault(); debugLog(DBG, `shortcut Ctrl+C copy n=${selectedPaths().size}`); copySelected('copy'); }
      else if (e.key === 'x') { e.preventDefault(); debugLog(DBG, `shortcut Ctrl+X cut n=${selectedPaths().size}`); copySelected('cut'); }
      else if (e.key === 'v') { e.preventDefault(); debugLog(DBG, `shortcut Ctrl+V paste`); paste(); }
      else if (e.key === 'a') { e.preventDefault(); debugLog(DBG, `shortcut Ctrl+A selectAll`); selectAll(); }
    } else if (e.key === 'Delete' && selectedPaths().size > 0) {
      e.preventDefault(); debugLog(DBG, `shortcut Delete n=${selectedPaths().size}`); deleteSelected();
    } else if (e.key === 'Escape') {
      debugLog(DBG, `shortcut Escape deselect`);
      clearAfterEscape();
    } else if (e.key === 'F2' && selectedPaths().size === 1) {
      e.preventDefault();
      const fp = [...selectedPaths()][0];
      const entry = filteredEntries().find(e => entryPath(currentPath(), e) === fp);
      if (entry) { debugLog(DBG, `shortcut F2 rename ${fp}`); startRename(fp, entry); }
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const sel = [...selectedPaths()];
      if (sel.length === 1) {
        const entry = filteredEntries().find(en => entryPath(currentPath(), en) === sel[0]);
        if (entry) { debugLog(DBG, `shortcut Enter open ${sel[0]}`); handleItemDblClick(entry); }
      }
    } else if (e.key === 'Backspace') {
      if (canGoBack()) { e.preventDefault(); debugLog(DBG, `shortcut Backspace back`); goBack(); }
    } else if (['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Home','End','PageUp','PageDown'].includes(e.key)) {
      e.preventDefault();
      moveSelectionByKey(e.key, shift);
    }
  }

  function gridNumCols(): number {
    if (!isGridViewMode(p.viewMode)) return 1;
    const w = wrapWidth();
    if (!w) return 1;
    return Math.max(1, Math.floor(w / gridColWidth()));
  }

  function moveSelectionByKey(key: string, shift: boolean): void {
    const ents = filteredEntries();
    if (!ents.length) return;
    const cp = currentPath();
    const ids = ents.map(en => entryPath(cp, en));
    const anchorId = lastSelectedPath();
    const currIdx = anchorId ? ids.indexOf(anchorId) : -1;
    const cols = gridNumCols();
    const last = ids.length - 1;
    let next: number;
    const grid = isGridViewMode(p.viewMode);
    switch (key) {
      case 'ArrowDown':  next = currIdx < 0 ? 0    : Math.min(last, currIdx + (grid ? cols : 1)); break;
      case 'ArrowUp':    next = currIdx < 0 ? last : Math.max(0,    currIdx - (grid ? cols : 1)); break;
      case 'ArrowRight': if (grid) next = Math.min(last, (currIdx < 0 ? 0 : currIdx) + 1); else return; break;
      case 'ArrowLeft':  if (grid) next = Math.max(0,    (currIdx < 0 ? 0 : currIdx) - 1); else return; break;
      case 'Home':       next = 0; break;
      case 'End':        next = last; break;
      case 'PageDown':   next = Math.min(last, (currIdx < 0 ? 0 : currIdx) + (grid ? cols * 5 : 10)); break;
      case 'PageUp':     next = Math.max(0,    (currIdx < 0 ? 0 : currIdx) - (grid ? cols * 5 : 10)); break;
      default: return;
    }
    const targetId = ids[next];
    selection.click(targetId, { shiftKey: shift });
    // Scroll into view.
    queueMicrotask(() => {
      scrollWrapEl?.querySelector<HTMLElement>(`[data-fp="${cssEscape(targetId)}"]`)
        ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  }

  function cssEscape(s: string): string {
    return globalThis.CSS?.escape ? globalThis.CSS.escape(s) : s.replace(/["\\]/g, '\\$&');
  }

  function startRename(fp: string, entry: DirEntry): void {
    setRenamingPath(fp);
    setRenameValue(entry.name);
    requestAnimationFrame(() => {
      if (!renameInputEl) return;
      renameInputEl.focus();
      const dot = entry.name.lastIndexOf('.');
      if (!entry.isDirectory && dot > 0) {
        renameInputEl.setSelectionRange(0, dot);
      } else {
        renameInputEl.select();
      }
    });
  }

  async function commitRename(): Promise<void> {
    const fp = renamingPath();
    if (!fp) return;
    const newName = renameValue().trim();
    setRenamingPath(null);
    if (!newName) return;
    const entry = filteredEntries().find(e => entryPath(currentPath(), e) === fp);
    if (!entry || newName === entry.name) return;
    const sep = fp.includes('\\') ? '\\' : '/';
    const lastSep = Math.max(fp.lastIndexOf('\\'), fp.lastIndexOf('/'));
    const parent = fp.slice(0, lastSep);
    const newPath = parent + sep + newName;
    // Push a pending entry before invoking so a quick Ctrl+Z blocks on this
    // rename rather than popping an older entry and racing its reverse cmd.
    const op = beginOp({
      kind: 'rename',
      dir: parent,
      fromPath: newPath,
      originalPath: fp,
    });
    try {
      debugLog(DBG, `rename ${fp} → ${newName}`);
      await invoke('fs_rename', { path: fp, newName });
      op.resolve();
      refresh();
    } catch (err) {
      op.reject(err);
      logger.error(DBG, `Rename failed: ${err}`);
    }
  }

  function cancelRename(): void { setRenamingPath(null); }

  // Auto-start rename when a newly created file/folder appears in entries
  createEffect(() => {
    const name = pendingRenameName();
    if (!name) return;
    const cp = currentPath();
    if (!cp) return;
    const entry = filteredEntries().find(e => e.name === name);
    if (!entry) return;
    setPendingRenameName(null);
    const fp = entryPath(cp, entry);
    selectItem(fp, {});
    startRename(fp, entry);
  });

  function copySelected(mode: 'copy' | 'cut'): void {
    if (selectedPaths().size === 0) return;
    const paths = [...selectedPaths()];
    const cut = mode === 'cut';
    invoke('clipboard_set_files', { paths, cut }).catch(e => logger.error(DBG, `clipboard_set_files failed: ${e}`));
    setCutPendingPaths(cut ? paths : []);
  }

  function beginPasteOp(srcPaths: string[], destDir: string, cut: boolean) {
    const sep = destDir.includes('\\') ? '\\' : '/';
    const basenames = srcPaths.map(p => {
      const i = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'));
      return p.slice(i + 1);
    });
    const createdPaths = basenames.map(n => destDir + sep + n);
    if (cut) {
      const srcDir = srcPaths[0]?.replace(/[/\\][^/\\]+$/, '') ?? '';
      return beginOp({
        kind: 'paste-cut',
        srcDir,
        destDir,
        movedPaths: createdPaths,
        originalPaths: srcPaths,
      });
    }
    return beginOp({
      kind: 'paste-copy',
      destDir,
      createdPaths,
    });
  }

  async function paste(): Promise<void> {
    const destDir = selectedFolder()?.path ?? currentPath();
    if (!destDir) return;
    debugLog(DBG, `paste BEGIN dest=${destDir}`);
    const t0 = performance.now();
    try {
      const cb = await invoke('clipboard_get_files');
      if (cb.paths.length) {
        debugLog(DBG, `paste fs_paste n=${cb.paths.length} cut=${cb.cut}`);
        const srcDir = cb.paths[0]?.replace(/[/\\][^/\\]+$/, '') ?? '';
        // Push the op-log entry BEFORE fs_paste: a big cut/copy can take
        // many seconds, and undoLast() waits on this handle rather than
        // popping an older, unrelated entry mid-flight.
        const op = beginPasteOp(cb.paths, destDir, cb.cut);
        try {
          await invoke('fs_paste', { paths: cb.paths, destDir, cut: cb.cut });
        } catch (err) {
          op.reject(err);
          throw err;
        }
        op.resolve();
        if (cb.cut) {
          setCutPendingPaths([]);
          invoke('clipboard_set_files', { paths: [], cut: false }).catch(e => logger.error(DBG, `clipboard_set_files clear failed: ${e}`));
        }
        loadSource = 'paste';
        load(currentPath());
        emit('folder-changed', destDir);
        if (cb.cut && srcDir && srcDir !== destDir) emit('folder-changed', srcDir);
        debugLog(DBG, `paste END elapsed=${(performance.now() - t0) | 0}ms`);
        return;
      }
      // Try virtual file streams (e.g. files copied from Outlook)
      const names = await invoke('clipboard_extract_virtual_files');
      if (names.length) {
        debugLog(DBG, `paste virtual n=${names.length}`);
        await invoke('save_virtual_files', { destDir });
        loadSource = 'paste';
        load(currentPath());
        emit('folder-changed', destDir);
      }
      debugLog(DBG, `paste END elapsed=${(performance.now() - t0) | 0}ms`);
    } catch (err) {
      logger.error(DBG, `paste FAILED: ${err}`);
    }
  }

  async function deleteSelected(): Promise<void> {
    if (selectedPaths().size === 0) return;
    const paths = [...selectedPaths()];
    const dir = currentPath();
    debugLog(DBG, `delete BEGIN n=${paths.length}`);
    // Push the op-log entry up front: fs_delete enumerates the Recycle Bin
    // (slow on large bins) to capture stable TrashItem ids. undoLast waits
    // on this handle so Ctrl+Z mid-delete blocks rather than misfiring on
    // an older entry.
    const op = beginOp({ kind: 'delete', dir, paths, tokens: [] });
    selection.set(new Set<string>());
    try {
      const tokens = await invoke('fs_delete', { paths });
      loadSource = 'delete';
      load(dir);
      emit('folder-changed', dir);
      if (tokens && tokens.length) {
        op.resolve({ tokens });
      } else {
        // Rust returned an empty list: nothing was trashed, drop the entry.
        op.reject(new Error('no tokens returned'));
      }
      debugLog(DBG, `delete END`);
    } catch (err) {
      op.reject(err);
      logger.error(DBG, `delete FAILED: ${err}`);
    }
  }

  function selectAll(): void {
    selection.selectAll();
  }

  // ── Drag / rubber-band ───────────────────────────────────────────────────
  // Mirror WidgetGrid exactly: permanent window listeners at component level,
  // handlers read reactive state. This is the only pattern proven to work on
  // the WS_EX_NOACTIVATE + HWND_BOTTOM Tauri window.

  const [dragOverTarget, setDragOverTarget] = createSignal<string | null>(null);
  const [band, setBand] = createSignal<BandRect | null>(null);
  // Single source of truth for the press → click | band | drag gesture. All
  // reset/decision logic lives in ./gesture.ts so it stays unit-testable and
  // can NEVER strand stale state that misclassifies a plain click (#58, [S-65]).
  const g = freshGestureState();

  // Full reset used on every abnormal / teardown exit path: clears the pure
  // gesture candidate AND the two render/DOM mirrors it drives.
  function resetGesture(): void {
    resetGestureCandidates(g);
    setBand(null);
    setDragOverTarget(null);
  }

  // Physical-pixel origin of this Tauri window (for GetCursorPos → clientXY conversion).
  // GetCursorPos returns virtual-screen physical coords; clientX/Y are relative to
  // the window viewport in logical CSS pixels.
  let winOriginPx: { x: number; y: number } = { x: 0, y: 0 };
  getCurrentWindow().innerPosition()
    .then(p => { winOriginPx = { x: p.x, y: p.y }; })
    .catch(() => {});

  // Convert physical screen coords (from GetCursorPos) to CSS client coords.
  function physToClient(px: number, py: number): [number, number] {
    const dpr = window.devicePixelRatio || 1;
    return [(px - winOriginPx.x) / dpr, (py - winOriginPx.y) / dpr];
  }

  // Returns full destination path, or null if nowhere valid to drop.
  // Works for subfolders in this widget AND for other folder widgets.
  function _fileDragDropTarget(x: number, y: number, dragPaths: string[]): string | null {
    const el = document.elementFromPoint(x, y);
    if (!el) return null;

    // 1. Cursor over a directory item in ANY widget
    const itemEl = el.closest('[data-fp][data-is-dir]') as HTMLElement | null;
    const itemFp = itemEl?.dataset.fp;
    if (itemFp && !dragPaths.includes(itemFp)) {
      return itemFp;
    }

    // 2. Cursor over another folder widget's background
    const folderWrap = el.closest('[data-folder-root]') as HTMLElement | null;
    if (folderWrap?.dataset.folderRoot) {
      const dest = folderWrap.dataset.folderRoot;
      // Avoid dropping into the exact folder the files already live in
      const srcDir = dragPaths[0]?.replace(/[/\\][^/\\]+$/, '') ?? '';
      if (dest !== srcDir) return dest;
    }

    return null;
  }

  // ── Polling-based mouse tracking ────────────────────────────────────────────
  // On a WS_EX_NOACTIVATE + HWND_BOTTOM window Chromium suppresses mousemove
  // events while a button is held on an unfocused window.  We bypass the browser
  // entirely: poll Win32 GetCursorPos + GetAsyncKeyState via a Tauri command.
  //
  // Once the drag threshold (5px) is reached, we hand off to native OLE
  // DoDragDrop which handles all cross-window routing via the OS.
  // Rubber-band selection still uses polling for the full duration.

  // The loop owns exactly one generation of the gesture. It stops the instant a
  // fresh press (or a reset) bumps `g.generation`, so a fast re-press can never
  // leave two loops racing the same state, and any abnormal exit (a transient
  // cursor-read failure) fully resets rather than stranding a stale candidate.
  async function runMousePoll(gen: number): Promise<void> {
    while (g.polling && g.generation === gen) {
      let st: CursorState;
      try {
        st = await invoke('get_cursor_state');
      } catch {
        resetGesture();
        return;
      }
      if (!g.polling || g.generation !== gen) return;

      const [cx, cy] = physToClient(st.x, st.y);
      const action = pollStep(g, { cx, cy, leftDown: st.left_down });

      if (action.kind === 'release') {
        setBand(null);
        setDragOverTarget(null);
        return;
      }

      if (action.kind === 'startDrag') {
        // Threshold reached: hand off to native OLE drag/drop (blocks until the
        // user drops or cancels). pollStep already tore the candidate down.
        setBand(null);
        setDragOverTarget(null);
        const paths = [...action.paths];
        const srcDir = paths[0]?.replace(/[/\\][^/\\]+$/, '') ?? '';
        debugLog(DBG, `drag-start n=${paths.length}`);
        startNativeDrag({
          type: 'files',
          filePaths: paths,
          effectAllowed: 'copyMove',
        }).then((effect) => {
          debugLog(DBG, `drag-end effect=${effect}`);
          if (effect === 'move') {
            selection.set(new Set<string>());
            loadSource = 'drag-move';
            load(currentPath());
            if (srcDir) emit('folder-changed', srcDir);
          }
        }).catch(e => logger.error(DBG, `drag-end failed: ${e}`));
        return;
      }

      if (action.kind === 'band') {
        const b = action.rect;
        setBand(b);
        if (scrollWrapEl) {
          const rect = { left: Math.min(b.x1,b.x2), top: Math.min(b.y1,b.y2),
                         right: Math.max(b.x1,b.x2), bottom: Math.max(b.y1,b.y2) };
          const sel = new Set<string>();
          for (const item of scrollWrapEl.querySelectorAll<HTMLElement>('[data-fp]')) {
            const r = item.getBoundingClientRect();
            if (r.right >= rect.left && r.left <= rect.right &&
                r.bottom >= rect.top  && r.top  <= rect.bottom) {
              const fp = item.dataset.fp;
              if (fp) sel.add(fp);
            }
          }
          selection.set(sel);
        }
      }

      await new Promise(r => setTimeout(r, 16)); // ~60 fps
    }
  }

  function startMousePoll(): void {
    if (g.polling) return; // idempotent: never spin up a second loop
    g.polling = true;
    const gen = g.generation;
    runMousePoll(gen).catch(() => { resetGesture(); });
  }

  onCleanup(() => {
    resetGesture();
    if (_activeKeyHandler === handleKeyDown) _activeKeyHandler = null;
  });

  // mousedown on container: record initial state then start the poll loop
  function handleContainerMouseDown(e: MouseEvent): void {
    if (e.button !== 0) return;
    // Mark this folder widget as the selected paste target.
    if (p._widgetId && currentPath()) selectFolder(p._widgetId, currentPath());
    // Claim the keyboard router for this widget instance.
    // activate_widget_keyboard() tells the Rust WH_KEYBOARD_LL hook which
    // window should receive "folder-key" events, no SetForegroundWindow,
    // so no app is brought to the front.
    _activeKeyHandler = handleKeyDown;
    scrollWrapEl?.focus({ preventScroll: true });
    invoke('activate_widget_keyboard', { label: getCurrentWindow().label }).catch(() => {});
    const itemEl = (e.target as HTMLElement | null)?.closest('[data-fp]') as HTMLElement | null;
    const fp = itemEl?.dataset.fp;
    // beginPress fully wipes any leftover gesture state BEFORE arming, so a
    // press can never inherit a stale drag origin (#58).
    if (itemEl && fp) {
      const paths = selectedPaths().has(fp) && selectedPaths().size > 0
        ? [...selectedPaths()] : [fp];
      beginPress(g, { x: e.clientX, y: e.clientY, onItem: true, paths });
    } else {
      e.preventDefault();
      beginPress(g, { x: e.clientX, y: e.clientY, onItem: false });
    }
    startMousePoll();
  }

  const rubberBandStyle = createMemo((): string | null => {
    const b = band();
    if (!b) return null;
    const r = scrollWrapEl?.getBoundingClientRect();
    if (!r) return null;
    return `left:${Math.min(b.x1,b.x2)-r.left}px;top:${Math.min(b.y1,b.y2)-r.top}px;width:${Math.abs(b.x2-b.x1)}px;height:${Math.abs(b.y2-b.y1)}px`;
  });

  // ── Cloud sync ─────────────��────────────────────────────────────��────────
  const [attrCache, setAttrCache] = createStore<Record<string, number | undefined>>({});

  // ── Git status ───────────��──────────────────────────────────────────────
  const [gitStatus, setGitStatus] = createSignal<GitStatusResult | null>(null);

  onMount(() => {
    ensureShellCaches().catch(() => {});

    let unlistenFolderChanged: (() => void) | undefined;
    let unlistenClearSelection: (() => void) | undefined;

    // When any folder widget successfully moves/copies files it broadcasts
    // 'folder-changed' with the affected path.  Reload immediately if it
    // matches what this widget is currently showing, no polling delay.
    listen('folder-changed', (e) => {
      if (e.payload === currentPath()) {
        loadSource = 'folder-changed-event';
        debugLog(DBG, `event→load payload=${e.payload}`);
        load(currentPath());
      }
    }).then(fn => { unlistenFolderChanged = fn; }).catch(() => {});

    // Escape pressed anywhere while over the desktop/widgets → clear this
    // instance's selection + cut state, regardless of whether it's the
    // currently-active keyboard target. Skip when an input is focused so
    // Escape during inline rename/search cancels the input instead.
    listen('folder-clear-selection', () => {
      const tag = document.activeElement?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (selectedPaths().size > 0 || cutPendingPaths().length > 0) {
        debugLog(DBG, `broadcast clear-selection`);
      }
      clearAfterEscape();
    }).then(fn => { unlistenClearSelection = fn; }).catch(() => {});

    // ── Native OLE drop zone ────────────────────────────────────────────────
    // All drops (from Explorer, from other folder widgets, cross-window)
    // arrive via the Rust IDropTarget → "native-drag" Tauri event.
    const unlistenDropZone = registerDropZone(
      `folder-${p._widgetId}`,
      () => scrollWrapEl,
      {
        onEnter(ev) {
          if (p._editMode) return;
          handleNativeDragOver(ev);
        },
        onOver(ev) {
          if (p._editMode) return;
          handleNativeDragOver(ev);
        },
        onLeave() {
          setDragOverTarget(null);
        },
        onDrop(ev) {
          if (p._editMode) return false;
          handleNativeDrop(ev);
          return true;
        },
      },
    );

    function handleNativeDragOver(ev: NativeDragEvent) {
      const cp = currentPath();
      if (!scrollWrapEl || !cp) return;
      if (!ev.dataTypes.includes('files') && !ev.dataTypes.includes('virtual_files')) return;

      const el = document.elementFromPoint(ev.clientX, ev.clientY);
      const itemEl = el?.closest('[data-fp][data-is-dir]') as HTMLElement | null;
      if (itemEl) {
        setDragOverTarget(itemEl.dataset.fp ?? cp);
      } else {
        setDragOverTarget(cp);
      }
    }

    function handleNativeDrop(ev: NativeDragEvent) {
      setDragOverTarget(null);
      const cp = currentPath();
      if (!cp) return;
      debugLog(DBG, `native-drop files=${ev.filePaths?.length ?? 0} virtual=${ev.virtualFileNames?.length ?? 0}`);

      const el = document.elementFromPoint(ev.clientX, ev.clientY);
      const itemEl = el?.closest('[data-fp][data-is-dir]');
      const destDir = (itemEl as HTMLElement)?.dataset.fp ?? cp;

      // Virtual files: always copy
      if (ev.virtualFileNames && ev.virtualFileNames.length > 0) {
        invoke('save_virtual_files', { destDir })
          .then(() => {
            load(currentPath());
            emit('folder-changed', destDir);
          })
          .catch(e => logger.error(DBG, `save_virtual_files failed: ${e}`));
        return;
      }

      if (ev.filePaths && ev.filePaths.length > 0) {
        const cut = !ev.ctrlHeld;
        // Drop same-folder / drop-onto-self sources: forwarding them to the
        // shell move engine would delete the dragged items. [S-54]
        const paths = pastableSources(ev.filePaths, destDir);
        if (paths.length === 0) return;
        const srcDir = paths[0]?.replace(/[/\\][^/\\]+$/, '') ?? '';

        const op = beginPasteOp(paths, destDir, cut);
        invoke('fs_paste', { paths, destDir, cut })
          .then(() => {
            op.resolve();
            load(currentPath());
            emit('folder-changed', destDir);
            if (cut && srcDir && !samePath(srcDir, destDir)) emit('folder-changed', srcDir);
          })
          .catch(e => {
            op.reject(e);
            logger.error(DBG, `fs_paste failed: ${e}`);
          });
      }
    }

    // Click outside any folder widget → deselect the paste target.
    // Click outside this widget instance → clear file selection.
    // Exception: ignore clicks inside a context menu, because mousedown
    // fires before click and would clear the selection before the menu
    // action (copy/cut/delete/paste) can read it.
    function handleGlobalMouseDown(e: MouseEvent): void {
      const target = e.target as HTMLElement | null;
      if (target?.closest('.ctx-menu, .ctx-submenu, .ctx-backdrop')) return;
      if (!target?.closest('[data-folder-root]')) clearSelectedFolder();
      if (scrollWrapEl && !scrollWrapEl.contains(target)) clearSelection();
    }
    document.addEventListener('mousedown', handleGlobalMouseDown);

    // Clicking outside the Themia app (another app, the desktop) never lands a
    // DOM mousedown in the webview, so handleGlobalMouseDown can't catch it.
    // Clear the file selection on app blur too, but leave the paste target
    // (selectedFolder) intact so the copy-here → switch app → copy a file →
    // come back → paste workflow keeps working.
    const cleanupBlur = onAppBlur(() => clearSelection());

    onCleanup(() => {
      unlistenFolderChanged?.();
      unlistenClearSelection?.();
      cleanupBlur();
      unlistenDropZone();
      document.removeEventListener('mousedown', handleGlobalMouseDown);
    });
  });

  // Return the cloud sync status for a file path, matching File Explorer's
  // overlay icons. Works for OneDrive, Synology Drive, and any provider
  // that uses the Windows Cloud Files API (cfapi).
  //   "cloud-only": not downloaded, online placeholder
  //   "pinned":     always keep on this device (solid green checkmark)
  //   "synced":     locally available, can be freed (green checkmark)
  //   null:         not cloud-managed
  function cloudStatus(fp: string): 'cloud-only' | 'pinned' | 'synced' | null {
    const attrs = attrCache[fp] ?? 0;
    if (!attrs) return null;
    // Cloud-only: data not present locally
    if (attrs & (FILE_ATTR_OFFLINE | FILE_ATTR_RECALL_ON_DATA_ACCESS | FILE_ATTR_RECALL_ON_OPEN))
      return 'cloud-only';
    // Pinned: always keep on device
    if (attrs & FILE_ATTR_PINNED)
      return 'pinned';
    // Synced: reparse point = managed by sync engine & downloaded locally
    if (attrs & FILE_ATTR_REPARSE_POINT)
      return 'synced';
    // Unpinned without reparse = cloud placeholder (online-only)
    if (attrs & FILE_ATTR_UNPINNED)
      return 'cloud-only';
    return null;
  }

  // ── Directory state ──────────────────────────────────────────────────────
  const [entries,   setEntries]   = createSignal<DirEntry[]>([]);
  const [loading,   setLoading]   = createSignal<boolean>(false);
  const [error,     setError]     = createSignal<string | null>(null);
  const [sortOrder, setSortOrder] = createSignal<'asc' | 'desc'>('asc');

  // ── Scroll indicator ─────────────────────────────────────────────────────
  const [canScrollDown, setCanScrollDown] = createSignal<boolean>(false);

  function checkScroll(e: Event | HTMLElement): void {
    const el = ((e as Event)?.target ?? e) as HTMLElement | null;
    if (!el) return;
    setCanScrollDown(el.scrollHeight - el.scrollTop - el.clientHeight > 4);
  }

  // ── Scroll wrap & grid stretch ───────────────────────────────────────────
  let scrollWrapEl: HTMLDivElement | undefined;
  const [wrapWidth,  setWrapWidth]  = createSignal<number>(0);
  const [wrapHeight, setWrapHeight] = createSignal<number>(0);

  function attachScrollWrap(el: HTMLDivElement): void {
    scrollWrapEl = el;
    if (!el) return;
    setWrapWidth(el.clientWidth);
    setWrapHeight(el.clientHeight);
    const ro = new ResizeObserver(([e]) => {
      setWrapWidth(e.contentRect.width);
      setWrapHeight(e.contentRect.height);
    });
    ro.observe(el);
    onCleanup(() => ro.disconnect());
  }

  const gridPreset = createMemo(() => GRID_SIZE_PRESETS[p.gridItemSize ?? 'small']);
  const isIconGrid = (): boolean => (p.viewMode ?? 'list') === 'icons';
  // Effective column width / row height drive layout math (column count, stretch
  // fit) AND the template, so the two stay consistent everywhere. The icon-grid
  // view drops the filename gutter: the tile only needs to hold the icon plus its
  // side/top/bottom padding, giving compact square cells.
  const gridColWidth = createMemo(() => {
    const gp = gridPreset();
    return isIconGrid() ? gp.iconSize + gp.padSide * 2 : gp.minColWidth;
  });
  const gridRowHeight = createMemo(() => {
    const gp = gridPreset();
    return isIconGrid() ? gp.iconSize + gp.padTop + gp.padBottom : gp.rowHeight;
  });
  const itemSizePreset = createMemo(() => ITEM_SIZE_PRESETS[p.itemSize ?? 'medium']);
  const itemSizeVars = createMemo(() => {
    const s = itemSizePreset();
    return `--item-pad-v:${s.padV}px;--item-pad-h:${s.padH}px;--item-icon:${s.icon}px;--item-font:${s.font}px;--item-gap-h:${s.gapH}px;--badge-size:${s.badge}px;`;
  });

  const effectiveStretchHeight = createMemo((): boolean => {
    if (!(p.gridStretchHeight ?? false) || wrapWidth() === 0 || wrapHeight() === 0) return false;
    const gap     = 1;
    const colW    = gridColWidth();
    const rowH    = gridRowHeight();
    const numCols = Math.max(1, Math.floor(wrapWidth() / colW));
    const numRows = Math.ceil(filteredEntries().length / numCols);
    return numRows * rowH + Math.max(0, numRows - 1) * gap <= wrapHeight();
  });

  // ── Icon / stat caches ───────────────────────────────────────────────────
  // iconCache is keyed by `${size}:${path}` so multiple sizes coexist without
  // wiping on grid-size toggles.
  const [iconCache, setIconCache] = createStore<Record<string, string>>({});
  const [statCache, setStatCache] = createStore<Record<string, { size?: number; mtime?: Date | number } | null>>({});
  const iconKey = (fp: string, size: number) => `${size}:${fp}`;
  const currentIconSize = (): number =>
    isGridViewMode(p.viewMode) ? gridPreset().iconSize : 32;

  let iconsInFlight: Promise<void> | null = null; // de-dupe concurrent calls
  async function loadIcons(ents: DirEntry[], dir: string): Promise<void> {
    // Serialize: if a run is in flight, await it before proceeding. [S-60]
    // After the await we re-check against the now-populated cache: if
    // nothing is missing we no-op, otherwise we kick off a fresh batch.
    // Returning early here used to drop the second effect-firing on
    // navigation (stale-entries call still in flight when the fresh-entries
    // call arrived), leaving the new folder's rows on fallback icons.
    while (iconsInFlight) await iconsInFlight;

    const size = currentIconSize();
    const missing = ents
      .map(e => entryPath(dir, e))
      .filter(fp => !(iconKey(fp, size) in iconCache));
    if (missing.length === 0) return;

    iconsInFlight = (async () => {
      const CHUNK = 50;
      try {
        for (let i = 0; i < missing.length; i += CHUNK) {
          const chunk = missing.slice(i, i + CHUNK);
          try {
            const result = await invoke('get_file_icons_batch', { paths: chunk, size });
            for (const [fp, dataUrl] of Object.entries(result)) setIconCache(iconKey(fp, size), dataUrl);
          } catch { /* non-fatal */ }
        }
      } finally {
        iconsInFlight = null;
      }
    })();
    await iconsInFlight;
  }

  // ── Shared batch-loader helpers ──────────────────────────────────────────
  // All per-entry loaders (stats/attrs/counts/gitRepo) share the same shape:
  // filter to paths not yet cached, invoke a Rust command with { paths }, write
  // results into the cache. createBatchLoader factors that out; .reset() drops
  // the inflight flag (used on navigation to a new folder).
  // Closed set of `{ paths: string[] } -> Record<string, Raw>` batch commands.
  // Each member's args is exactly `{ paths: string[] }` (verified against the
  // `Commands` map below), so a single typed `invoke` binding handles them all
  // without a loose `string` command.
  type BatchCommand =
    | 'get_file_stats_batch'
    | 'get_file_attributes_batch'
    | 'get_folder_item_counts'
    | 'get_git_repo_info_batch';
  // Compile-time proof that every batch command takes `{ paths: string[] }`. If a
  // future command in the set diverges, this assignment fails to type-check.
  type _AssertBatchArgs = Commands[BatchCommand]['args'] extends { paths: string[] } ? true : never;
  const _assertBatchArgs: _AssertBatchArgs = true; void _assertBatchArgs;
  function createBatchLoader<K extends BatchCommand, Raw extends Commands[K]['result'][string], V>(opts: {
    cache: Record<string, V | undefined>;
    setCache: (fp: string, val: V) => void;
    select: (ents: DirEntry[], dir: string) => string[];
    command: K;
    transform?: (raw: Raw) => V;
  }): { load: (ents: DirEntry[], dir: string) => Promise<void>; reset: () => void } {
    let inflight = false;
    return {
      async load(ents, dir) {
        if (inflight) return;
        const missing = opts.select(ents, dir).filter(fp => !(fp in opts.cache));
        if (missing.length === 0) return;
        inflight = true;
        try {
          // `command` is one of BatchCommand, all of which take `{ paths }` (proven
          // above); invoke through a binding narrowed to that uniform arg shape.
          const callBatch = invoke as (cmd: K, args: { paths: string[] }) => Promise<Record<string, Raw>>;
          const result = await callBatch(opts.command, { paths: missing });
          for (const [fp, raw] of Object.entries(result)) {
            opts.setCache(fp, opts.transform ? opts.transform(raw) : (raw as unknown as V));
          }
        } catch { /* non-fatal */ }
        finally { inflight = false; }
      },
      reset() { inflight = false; },
    };
  }

  // Path selectors for batch loaders.
  const allEntryPaths = (ents: DirEntry[], dir: string) => ents.map(e => entryPath(dir, e));
  const dirEntryPaths = (ents: DirEntry[], dir: string) =>
    ents.filter(e => e.isDirectory).map(e => entryPath(dir, e));

  // Wires a loader to currentPath + filteredEntries, with optional guard.
  function useEntriesEffect(
    fn: (ents: DirEntry[], dir: string) => void,
    guard?: () => boolean,
  ): void {
    createEffect(() => {
      if (guard && !guard()) return;
      const cp = currentPath();
      if (!cp) return;
      fn(filteredEntries(), cp);
    });
  }

  const statsLoader = createBatchLoader<'get_file_stats_batch', [number, number], { size?: number; mtime?: Date | number } | null>({
    cache: statCache,
    setCache: setStatCache,
    select: allEntryPaths,
    command: 'get_file_stats_batch',
    transform: ([size, mtime]) => ({ size, mtime: new Date(mtime) }),
  });
  useEntriesEffect(statsLoader.load, () => (p.viewMode ?? 'list') === 'details');

  // Fetch icons whenever entries change or the icon size (via viewMode/gridItemSize) changes.
  // Single source of truth: load() no longer calls loadIcons directly.
  // loadIcons is a no-op when every entry path is already cached at the current size.
  createEffect(() => {
    const cp = currentPath();
    const ents = entries();
    if (!cp || ents.length === 0) return;
    // Track size so a grid toggle re-fires the effect.
    currentIconSize();
    loadIcons(ents, cp);
  });

  // ── Close search on outside click ──────────────────────────────────────
  createEffect(() => {
    if (!showSearch()) return;
    const handler = (e: MouseEvent) => {
      const root = scrollWrapEl?.closest('.folder-view');
      if (root && !root.contains(e.target as Node)) {
        setShowSearch(false);
        setRuntimeFilter('');
      }
    };
    document.addEventListener('mousedown', handler);
    onCleanup(() => document.removeEventListener('mousedown', handler));
  });

  // ── Scroll position restore ─────────────────────────────────────────────
  // Waits until entries have loaded for the target path, then restores scroll.
  createEffect(() => {
    const ents = entries();      // track: fires after load() populates entries
    const target = pendingScrollPath();
    if (!target || target !== currentPath() || loading()) return;
    if (ents.length === 0) return; // empty folder, nothing to restore
    const saved = scrollPositions.get(target);
    setPendingScrollPath(null);
    if (saved == null) return;
    requestAnimationFrame(() => {
      const el = scrollWrapEl?.querySelector('.list, .grid-container, .details-container') as HTMLElement | null;
      if (el) el.scrollTop = saved;
    });
  });

  // ── Sorted + filtered entries ────────────────────────────────────────────
  const sortedEntries = createMemo((): DirEntry[] => {
    const field = sortField();
    const dir   = sortOrder() === 'asc' ? 1 : -1;
    const cp    = currentPath();
    return [...entries()].sort((a: DirEntry, b: DirEntry) => {
      if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
      if (field === 'size') {
        const as_ = a.isDirectory ? -1 : (statCache[entryPath(cp, a)]?.size ?? -1);
        const bs_ = b.isDirectory ? -1 : (statCache[entryPath(cp, b)]?.size ?? -1);
        const sizeCmp = (as_ as number) - (bs_ as number);
        return sizeCmp !== 0 ? sizeCmp * dir : a.name.localeCompare(b.name);
      }
      if (field === 'modified') {
        const am = statCache[entryPath(cp, a)]?.mtime;
        const bm = statCache[entryPath(cp, b)]?.mtime;
        const at = am instanceof Date ? am.getTime() : (typeof am === 'number' ? am * 1000 : 0);
        const bt = bm instanceof Date ? bm.getTime() : (typeof bm === 'number' ? bm * 1000 : 0);
        const modCmp = (at as number) - (bt as number);
        return modCmp !== 0 ? modCmp * dir : a.name.localeCompare(b.name);
      }
      if (field === 'type') {
        const ae = a.isDirectory ? '' : (a.name.split('.').pop() ?? '').toLowerCase();
        const be = b.isDirectory ? '' : (b.name.split('.').pop() ?? '').toLowerCase();
        const typeCmp = ae.localeCompare(be);
        return typeCmp !== 0 ? typeCmp * dir : a.name.localeCompare(b.name);
      }
      return a.name.localeCompare(b.name) * dir;
    });
  });

  const filteredEntries = createMemo((): DirEntry[] => {
    // Comma-separated keywords, OR-matched, case-insensitive. [S-21]
    const cf = parseFilterKeywords(p.filter);
    const rf = parseFilterKeywords(runtimeFilter());
    let result = sortedEntries();
    // Hidden-file filter runs here (reactively) rather than inside load() so
    // toggling showHidden updates the list without needing a reload. [S-22]
    if (!(p.showHidden ?? false)) result = result.filter(e => !e.isHidden);
    if (cf.length) result = result.filter(e => matchesFilter(e.name, cf));
    if (rf.length) result = result.filter(e => matchesFilter(e.name, rf));
    return result;
  });

  // File attributes (GetFileAttributesW). Cheap per-file; batch eliminates IPC roundtrips.
  const attrsLoader = createBatchLoader<'get_file_attributes_batch', number, number>({
    cache: attrCache,
    setCache: setAttrCache,
    select: allEntryPaths,
    command: 'get_file_attributes_batch',
  });
  useEntriesEffect(attrsLoader.load);

  // Subdirectory item counts (only when the details column is enabled).
  const countsLoader = createBatchLoader<'get_folder_item_counts', number, number>({
    cache: folderCountCache,
    setCache: setFolderCountCache,
    select: dirEntryPaths,
    command: 'get_folder_item_counts',
  });
  useEntriesEffect(countsLoader.load, () => resolveColumns(p.detailsColumns).visible.includes('itemCount'));

  // ── Image detection (kept for ctx-menu "open as image" etc.) ─────────────
  const THUMBNAIL_EXTS = new Set(['jpg', 'jpeg', 'png', 'gif', 'bmp']);

  function isImageEntry(entry: DirEntry): boolean {
    if (entry.isDirectory) return false;
    const ext = entry.name.split('.').pop()?.toLowerCase() ?? '';
    return THUMBNAIL_EXTS.has(ext);
  }

  // Git status for the current directory (single path, not batched).
  let gitStatusInFlight = false;
  async function loadGitStatus(dir: string): Promise<void> {
    if (gitStatusInFlight) return;
    gitStatusInFlight = true;
    try {
      setGitStatus(await invoke('get_git_status', { path: dir }));
    } catch { setGitStatus(null); }
    finally { gitStatusInFlight = false; }
  }
  createEffect(() => {
    const cp = currentPath();
    if (!cp || !resolveColumns(p.detailsColumns).visible.includes('gitStatus')) { setGitStatus(null); return; }
    filteredEntries(); // track so we reload after fs changes
    loadGitStatus(cp);
  });

  // Per-subdirectory git repo info (branch + dirty flag shown on folder items).
  const [gitRepoCache, setGitRepoCache] = createStore<Record<string, GitRepoInfo | undefined>>({});
  const gitRepoLoader = createBatchLoader<'get_git_repo_info_batch', GitRepoInfo, GitRepoInfo>({
    cache: gitRepoCache,
    setCache: setGitRepoCache,
    select: dirEntryPaths,
    command: 'get_git_repo_info_batch',
  });
  useEntriesEffect(gitRepoLoader.load, () => resolveColumns(p.detailsColumns).visible.includes('gitStatus'));

  // Drop per-path caches on navigation so memory doesn't grow unbounded.
  createEffect(() => {
    currentPath(); // track
    setStatCache(reconcile({}));
    setAttrCache(reconcile({}));
    setFolderCountCache(reconcile({}));
    setGitRepoCache(reconcile({}));
    statsLoader.reset();
    attrsLoader.reset();
    countsLoader.reset();
    gitRepoLoader.reset();
    gitStatusInFlight = false;
  });

  function gitFileStatus(fp: string): 'staged' | 'modified' | 'untracked' | 'deleted' | 'conflicted' | null {
    const gs = gitStatus();
    if (!gs?.is_git) return null;
    const code = gs.files[fp];
    if (!code) return null;
    const x = code[0], y = code[1];
    if (code === '??') return 'untracked';
    if (x === 'U' || y === 'U' || (x === 'A' && y === 'A') || (x === 'D' && y === 'D')) return 'conflicted';
    if (y === 'D') return 'deleted';
    if (y === 'M') return 'modified';
    if ('MADRC'.includes(x) && y === ' ') return 'staged';
    if ('MADRC'.includes(x)) return 'modified';
    return null;
  }

  // ── Load directory ───────────────────────────────────────────────────────
  // Debug tag kept for error logs + remaining user-action logs.
  const DBG = `folder[${(p._widgetId ?? 'anon').toString().slice(-4)}]`;
  let loadInFlight = false;
  let loadQueued = false;       // true → re-run load() once after the current run ends
  // Tag for which trigger caused the most recent load. Used by event handlers
  // that want to annotate the reason (e.g. paste/delete/drag-move); currently
  // read only by the load path but preserved as an intent marker for future
  // telemetry and breadcrumbs on errors.
  let loadSource = 'initial'; // eslint-disable-line @typescript-eslint/no-unused-vars
  async function load(dir: string): Promise<void> {
    if (loadInFlight) {
      // Don't drop the update: queue a follow-up that re-reads the dir once
      // the current load completes. Prevents watcher events being lost when
      // load() is slow (large dirs, cold cloud-sync placeholders, etc.).
      loadQueued = true;
      return;
    }
    loadInFlight = true;
    try {
      do {
        loadQueued = false;
        setLoading(true); setError(null);
        try {
          if (isDemo()) {
            setEntries(demoFolderEntries());
          } else {
            // Custom command (not plugin-fs readDir) so we get `isHidden`:
            // Win32 FILE_ATTRIBUTE_HIDDEN isn't surfaced by plugin-fs, which
            // meant hidden files always rendered. The showHidden filter is
            // applied reactively in filteredEntries() so toggling the flag
            // re-filters without a reload.
            const result = await invoke('folder_read_dir', { path: dir });
            setEntries(result.filter(e => !!e.name));
            // Icons fetched by the dedicated createEffect that tracks entries() + iconSize.
          }
        } catch (e) { setError(String(e)); }
        finally { setLoading(false); }
      } while (loadQueued);
    } finally {
      loadInFlight = false;
    }
  }

  createEffect(() => {
    const cp = currentPath();
    if (!cp && isDemo()) { loadSource = 'initial-demo'; untrack(() => load('demo://folder')); return; }
    if (!cp) { setEntries([]); return; }

    loadSource = 'initial';
    untrack(() => load(cp));

    let cancelled = false;
    // [S-63] Owns the unwatch fn: fires on beforeunload too (page reload skips
    // onCleanup, orphaning the Rust watcher), exactly once across all paths.
    const watchHandle = createWatchHandle();
    let watcherActive = false;      // flipped true once watch() resolves
    let pollTimer: ReturnType<typeof setTimeout> | null = null;
    let lastSnap = '';
    let lastLoadAt = 0;
    let trailingTimer: ReturnType<typeof setTimeout> | null = null;
    // Rate cap for watcher-triggered reloads. First event in a quiet period fires
    // immediately (leading); events inside MIN_GAP get coalesced into one trailing fire.
    const MIN_GAP_MS = 2000;

    function fireLoad(src: string) {
      if (cancelled || !appActive()) return;
      lastLoadAt = Date.now();
      loadSource = src;
      if (_folderDebug) _folderDebug.loadFires.push({ src, ts: Date.now() });
      load(cp);
    }

    // ── Fast path: OS watcher, leading+trailing throttle ────────────────────
    // Cloud-sync dirs (OneDrive/Dropbox) fire thousands of events/min; without
    // a hard rate cap, shell-API icon work pins the CPU.
    watch(cp, () => {
      if (cancelled) return;
      if (_folderDebug) _folderDebug.watchEvents += 1;
      const elapsed = Date.now() - lastLoadAt;
      if (elapsed >= MIN_GAP_MS && !trailingTimer) {
        fireLoad('watch-leading');
        return;
      }
      if (trailingTimer) return;
      trailingTimer = setTimeout(() => {
        trailingTimer = null;
        if (_folderDebug) _folderDebug.trailingFires += 1;
        fireLoad('watch-trailing');
      }, Math.max(0, MIN_GAP_MS - elapsed));
    }, { recursive: false, delayMs: 500 })
      .then(fn => {
        watchHandle.adopt(fn);              // unwatches immediately if released
        if (watchHandle.released()) return;
        watcherActive = true;
        if (_folderDebug) { _folderDebug.watchRegistered += 1; _folderDebug.lastPath = cp; }
      })
      .catch(err => {
        // Watcher unavailable, fall back to polling.
        if (_folderDebug) { _folderDebug.watchRejected += 1; _folderDebug.watchError = String(err?.message ?? err); _folderDebug.lastPath = cp; }
        if (!cancelled) startPolling();
      });

    // ── Slow fallback: only runs if watch() rejected. Polls every 5 s. ───────
    const startPolling = () => {
      const poll = async () => {
        if (cancelled || watcherActive) return;
        if (appActive()) {
          try {
            const result = await invoke('folder_read_dir', { path: cp });
            const snap = result.map(e => e.name + (e.isDirectory ? 'd' : 'f')).sort().join('|');
            if (lastSnap !== '' && snap !== lastSnap) { if (_folderDebug) _folderDebug.pollFires += 1; fireLoad('poll'); }
            lastSnap = snap;
          } catch { /* dir removed or access denied */ }
        }
        if (!cancelled && !watcherActive) pollTimer = setTimeout(poll, 5000);
      };
      pollTimer = setTimeout(poll, 5000);
    };

    onCleanup(() => {
      cancelled = true;
      watchHandle.release();
      if (trailingTimer) clearTimeout(trailingTimer);
      if (pollTimer) clearTimeout(pollTimer);
    });
  });

  // ── Details column template ──────────────────────────────────────────────
  const resolvedCols = createMemo(() => resolveColumns(p.detailsColumns));
  const visibleCols  = () => resolvedCols().visible;
  const isColVis = (k: string) => visibleCols().includes(k);
  const detailColTemplate = createMemo((): string =>
    ['1fr', ...visibleCols().map(k => getColumnDef(k)?.width ?? '0px')].join(' ')
  );

  // ── Context menu ─────────────────────────────────────────────────────────
  function showCtxMenu(e: MouseEvent, items: CtxMenuItem[]): void {
    _showCtxMenu(e, items, 175);
  }

  function refresh(): void {
    const cp = currentPath();
    // Clear EVERY per-path cache + loader dedup state so Size/Modified/Items/git
    // reload fresh, mirroring the on-navigation reset above. Nulling only
    // attrCache left statCache/folderCountCache/gitRepoCache (and the loaders'
    // internal "already fetched" sets) intact, freezing those columns. [S-35]
    setStatCache(reconcile({}));
    setAttrCache(reconcile({}));
    setFolderCountCache(reconcile({}));
    setGitRepoCache(reconcile({}));
    statsLoader.reset();
    attrsLoader.reset();
    countsLoader.reset();
    gitRepoLoader.reset();
    gitStatusInFlight = false;
    if (!cp) return;
    load(cp);
  }

  function handleItemRightClick(e: MouseEvent, entry: DirEntry): void {
    const fp = entryPath(currentPath(), entry);
    if (!selectedPaths().has(fp)) selectItem(fp, {});
    const n = selectedPaths().size > 1 ? ` (${selectedPaths().size})` : '';
    const sel = [...selectedPaths()];

    // Shell menu entries for this item type
    const shellEntries = (entry.isDirectory ? shellDirMenuCache : shellFileMenuCache) ?? [];
    const shellItems: CtxMenuItem[] = shellEntries
      .filter(e => !e.extended)
      .map(e => ({
        icon: e.icon_base64 ? shellIconComponent(e.icon_base64) : undefined,
        label: e.label,
        action: () => invoke('execute_shell_command', { verbId: e.id, targetPath: fp, isDirectory: entry.isDirectory, isBackground: false }),
      }));

    const wid = p._widgetId;
    function setWidgetCfg(partial: Partial<FolderViewProps>): void {
      if (!wid) return;
      // updateWidgetConfig REPLACES the stored config (layout.ts), so spread the
      // full existing config rather than a key whitelist — otherwise clickMode/
      // gridItemSize/label are dropped. Mirrors the onMount writer above.
      const w = layoutStore.layout.widgets.find((x) => x.id === wid);
      const cfg = (w?.config ?? {}) as Record<string, unknown>;
      layoutStore.updateWidgetConfig(wid, { ...cfg, ...partial });
    }
    const widgetItems: CtxMenuItem[] = isImageEntry(entry) && wid ? [
      { sep: true },
      { icon: ExternalLink, label: t('folder.openInExplorer'), disabled: !p.path, action: () => p.path && invoke('open_in_explorer', { path: p.path }) },
      { sep: true },
      { header: true, label: t('ctxMenu.view') },
      { icon: List,         label: t('ctxMenu.list'),     active: (p.viewMode ?? 'list') === 'list', action: () => setWidgetCfg({ viewMode: 'list' }) },
      { icon: Table,        label: t('ctxMenu.details'),  active: p.viewMode === 'details', action: () => setWidgetCfg({ viewMode: 'details' }) },
      { icon: LayoutGrid,   label: t('ctxMenu.grid'),     active: p.viewMode === 'grid',    action: () => setWidgetCfg({ viewMode: 'grid' }) },
      { icon: Grid3x3,      label: t('ctxMenu.iconGrid'), active: p.viewMode === 'icons',   action: () => setWidgetCfg({ viewMode: 'icons' }) },
      { sep: true },
      {
        icon: p.showHidden ? Eye : EyeOff,
        label: p.showHidden ? t('folder.hideHidden') : t('folder.showHidden_'),
        action: () => setWidgetCfg({ showHidden: !p.showHidden }),
      },
    ] : [];

    const items: CtxMenuItem[] = [
      { icon: ExternalLink, label: t('common.open'),   action: () => handleItemDblClick(entry) },
      // "Open with…" is the OS app-chooser (the `openas` verb): a shell COM
      // cascade that the enumerated registry verbs never include. Files only.
      ...(!entry.isDirectory
        ? [{ icon: AppWindow, label: t('folder.openWith'), action: () => invoke('open_with', { path: fp }) } as CtxMenuItem]
        : []),
      { icon: Pencil,       label: t('common.rename'), action: () => startRename(fp, entry), disabled: sel.length !== 1 },
      { sep: true },
      { icon: Copy,           label: `${t('common.copy')}${n}`,    action: () => copySelected('copy') },
      { icon: Scissors,       label: `${t('common.cut')}${n}`,     action: () => copySelected('cut') },
      { icon: ClipboardPaste, label: t('common.paste'),       action: paste, disabled: !currentPath() },
      { icon: Link,           label: t('folder.copyPath'),   action: () => navigator.clipboard.writeText(fp) },
      ...(shellItems.length ? [{ sep: true } as CtxMenuItem, ...shellItems] : []),
      { sep: true },
      { icon: Info,           label: t('folder.properties'),  action: () => invoke('show_file_properties', { path: fp }) },
      { icon: Trash2,         label: t('common.delete'),      action: deleteSelected, danger: true },
      ...widgetItems,
    ];
    showCtxMenu(e, items);
  }

  async function createNewFolder(dir: string): Promise<void> {
    debugLog(DBG, `create-folder dir=${dir}`);
    const sep = dir.includes('\\') ? '\\' : '/';
    // Placeholder path in case fs_create_folder collides and renames.
    // Patched on resolve with the real path.
    const op = beginOp({ kind: 'create', dir, createdPath: dir + sep + 'New Folder' });
    try {
      const created = await invoke('fs_create_folder', { destDir: dir, name: 'New Folder' });
      const createdPath = created || (dir + sep + 'New Folder');
      op.resolve({ createdPath });
      setPendingRenameName('New Folder');
      refresh();
    } catch (err) {
      op.reject(err);
      logger.error(DBG, `create-folder FAILED: ${err}`);
    }
  }

  async function createNewFile(dir: string, entry: ShellNewEntry): Promise<void> {
    debugLog(DBG, `create-file dir=${dir} ext=${entry.extension}`);
    const name = `New ${entry.label}${entry.extension}`;
    const sep = dir.includes('\\') ? '\\' : '/';
    const op = beginOp({ kind: 'create', dir, createdPath: dir + sep + name });
    try {
      const data = entry.template_data ?? [];
      await invoke('fs_write_file', { destDir: dir, fileName: name, data });
      op.resolve();
      setPendingRenameName(name);
      refresh();
    } catch (err) {
      op.reject(err);
      logger.error(DBG, `create-file FAILED: ${err}`);
    }
  }

  function handleBgRightClick(e: MouseEvent): void {
    if (e.target !== e.currentTarget &&
        !(e.target as HTMLElement).classList.contains('list') &&
        !(e.target as HTMLElement).classList.contains('grid-container') &&
        !(e.target as HTMLElement).classList.contains('details-container') &&
        !(e.target as HTMLElement).classList.contains('details-table') &&
        !(e.target as HTMLElement).classList.contains('state-msg')) return;

    const cp = currentPath();

    // Build "New" submenu from ShellNew entries
    const shellNewItems: CtxMenuItem[] = [
      { icon: FolderPlus, label: t('ctxMenu.folder'), action: () => { if (cp) createNewFolder(cp); } },
      ...(shellNewCache?.length ? [{ sep: true } as CtxMenuItem] : []),
      ...(shellNewCache ?? []).map(entry => ({
        icon: entry.icon_base64 ? shellIconComponent(entry.icon_base64) : undefined,
        label: entry.label,
        action: () => { if (cp) createNewFile(cp, entry); },
      } as CtxMenuItem)),
    ];

    // Shell background menu entries
    const shellBgEntries = (shellBgMenuCache ?? []).filter(e => !e.extended);
    const shellBgItems: CtxMenuItem[] = shellBgEntries.map(e => ({
      icon: e.icon_base64 ? shellIconComponent(e.icon_base64) : undefined,
      label: e.label,
      action: () => { if (cp) invoke('execute_shell_command', { verbId: e.id, targetPath: cp, isDirectory: false, isBackground: true }); },
    }));

    showCtxMenu(e, [
      { icon: ClipboardPaste, label: t('common.paste'),      action: paste,     disabled: !cp },
      { icon: CheckSquare,    label: t('folder.selectAll'), action: selectAll, disabled: !filteredEntries().length },
      { icon: Link,           label: t('folder.copyPath'),  action: () => cp && navigator.clipboard.writeText(cp), disabled: !cp },
      { sep: true },
      { icon: Plus, label: t('common.new'), children: shellNewItems },
      ...(shellBgItems.length ? [{ sep: true } as CtxMenuItem, ...shellBgItems] : []),
      { sep: true },
      { icon: RefreshCw,      label: t('common.refresh'),    action: refresh },
    ]);
  }

  // ── Display helpers ──────────────────────────────────────────────────────
  function fallbackIcon(entry: DirEntry, size: number = 16): JSX.Element {
    if (entry.isDirectory) return <Folder size={size} strokeWidth={1.5} />;
    const ext = entry.name?.split('.').pop()?.toLowerCase() ?? '';
    const Icon = EXT_ICON_MAP[ext] ?? File;
    return <Icon size={size} strokeWidth={1.5} />;
  }

  function extLabel(entry: DirEntry): string {
    if (entry.isDirectory) return t('ctxMenu.folder');
    const ext = entry.name?.split('.').pop()?.toLowerCase() ?? '';
    return ext ? `.${ext}` : '—';
  }

  function displayName(entry: DirEntry): string {
    if (entry.isDirectory || (p.showFileExtension ?? false)) return entry.name;
    const dot = entry.name.lastIndexOf('.');
    return dot > 0 ? entry.name.slice(0, dot) : entry.name;
  }

  // ── Shared item event props ──────────────────────────────────────────────
  function itemProps(entry: DirEntry): Record<string, unknown> {
    const fp = entryPath(currentPath(), entry);
    return {
      'data-fp':     fp,
      ...(entry.isDirectory ? { 'data-is-dir': 'true' } : {}),
      onClick:       (e: MouseEvent) => {
        e.stopPropagation();
        selectItem(fp, e);
        const isModified = e.ctrlKey || e.metaKey || e.shiftKey;
        if ((p.clickMode ?? 'double') === 'single' && !isModified) {
          handleItemDblClick(entry);
        }
      },
      onDblClick:    ()  => handleItemDblClick(entry),
      onContextMenu: (e: MouseEvent) => { e.stopPropagation(); handleItemRightClick(e, entry); },
    };
  }

  // ── Container interaction (rubber band + click-to-deselect) ─────────────
  function handleContainerClick(): void {
    // A real band / drag this gesture suppresses the trailing background click
    // (which would otherwise clear the selection). Consume the flag once.
    if (g.bandWasActive)     { g.bandWasActive = false;     scrollWrapEl?.focus({ preventScroll: true }); return; }
    if (g.fileDragWasActive) { g.fileDragWasActive = false; scrollWrapEl?.focus({ preventScroll: true }); return; }
    selection.set(new Set<string>());
    scrollWrapEl?.focus({ preventScroll: true });
  }

  // ── Shared bg event props ────────────────────────────────────────────────
  const bgProps = {
    onContextMenu: handleBgRightClick,
    onMouseDown:   handleContainerMouseDown,
  };

  // ── Cloud badge ──────────────────────────────────────────────────────────
  // Lucide SVG icons for consistent sizing/alignment:
  //   cloud-only → Cloud (blue)   synced → CircleCheck (green)   pinned → CircleCheck (solid green)
  function CloudBadge(bprops: CloudBadgeProps): JSX.Element {
    const status = () => cloudStatus(bprops.fp);
    const bs = () => itemSizePreset().badge;
    return (
      <Show when={status()}>
        <span
          class="cloud-badge-icon"
          classList={{ inline: bprops.inline, [String(status())]: true }}
          title={status() === 'cloud-only' ? t('folder.cloudOnly') : status() === 'pinned' ? t('folder.cloudPinned') : t('folder.cloudSynced')}
        >
          <Switch>
            <Match when={status() === 'cloud-only'}>
              <Cloud size={bs()} strokeWidth={2} />
            </Match>
            <Match when={status() === 'pinned'}>
              <CircleCheck size={bs()} strokeWidth={2} />
            </Match>
            <Match when={status() === 'synced'}>
              <CircleCheck size={bs()} strokeWidth={2} />
            </Match>
          </Switch>
        </span>
      </Show>
    );
  }

  // ── Git badge ────────────────────────────────────────────────────────────
  const GIT_STATUS_LABEL: Record<string, string> = {
    staged: 'S', modified: 'M', untracked: 'U', deleted: 'D', conflicted: 'C',
  };
  const GIT_STATUS_TITLE: Record<string, string> = {
    staged: t('folder.gitStaged'), modified: t('folder.gitModified'), untracked: t('folder.gitUntracked'), deleted: t('folder.gitDeleted'), conflicted: t('folder.gitConflicted'),
  };

  function GitBadge(gprops: { fp: string }): JSX.Element {
    const status = () => gitFileStatus(gprops.fp);
    return (
      <Show when={status() && vm() !== 'grid'}>
        <span class="git-badge" classList={{ [status()!]: true }} title={GIT_STATUS_TITLE[status()!]}>
          {GIT_STATUS_LABEL[status()!]}
        </span>
      </Show>
    );
  }

  // ── Icon cell ────────────────────────────────────────────────────────────
  function FileIcon(iprops: FileIconProps): JSX.Element {
    const src = () => iconCache[iconKey(iprops.fp, currentIconSize())];
    // In grid view, size the lucide fallback to the grid preset so it matches
    // the thumbnail it stands in for. Otherwise it renders at the tiny 16px
    // list default, which is what persists in demo/preview mode (no OS icons
    // are fetched there), leaving folder grid icons far smaller than expected.
    const fallbackSize = () => isGridViewMode(p.viewMode) ? gridPreset().iconSize : 16;
    return (
      <Show when={src()}
        fallback={<span class={iprops.class ?? 'item-icon'}>{fallbackIcon(iprops.entry, fallbackSize())}</span>}>
        <img class={iprops.imgClass ?? 'item-icon-img'} src={src()} alt="" draggable={false} />
      </Show>
    );
  }

  // ── View mode ────────────────────────────────────────────────────────────
  const vm = (): string => p.viewMode ?? 'list';

  // ────────────────────────────────────────────────────────────────────────
  return (
    <div
      class="folder-view"
      classList={{
        'click-mode-single': (p.clickMode ?? 'double') === 'single',
        'click-mode-double': (p.clickMode ?? 'double') === 'double',
      }}
      onMouseDown={handleMouseDown}
    >
      {/* ── Header ── */}
      <WidgetHeader
        mode={p.headerMode ?? 'normal'}
        icon={Folder}
        title={p.label || (currentPath() ? folderName(currentPath()) : (isDemo() ? t('folder.documents') : t('ctxMenu.folder')))}
        onDblClick={() => { const fp = currentPath(); if (fp) invoke('open_in_explorer', { path: fp }); }}
        titleOverride={
          <>
            <Show when={showSearch()} fallback={
              <>
                <span class="title-text">
                  {p.label || (currentPath() ? folderName(currentPath()) : (isDemo() ? t('folder.documents') : t('ctxMenu.folder')))}
                </span>
                <Show when={gitStatus()?.is_git && !isGridViewMode(vm())}>
                  <span class="git-header-info">
                    <span class="git-branch-badge" title={t('folder.branchTitle', { branch: gitStatus()!.branch })} classList={{
                      clean: gitStatus()!.staged + gitStatus()!.modified + gitStatus()!.untracked + gitStatus()!.deleted + gitStatus()!.conflicted === 0,
                      conflicted: gitStatus()!.conflicted > 0,
                      modified: gitStatus()!.conflicted === 0 && (gitStatus()!.modified > 0 || gitStatus()!.deleted > 0),
                      staged: gitStatus()!.conflicted === 0 && gitStatus()!.modified === 0 && gitStatus()!.deleted === 0 && gitStatus()!.staged > 0,
                      untracked: gitStatus()!.conflicted === 0 && gitStatus()!.modified === 0 && gitStatus()!.deleted === 0 && gitStatus()!.staged === 0 && gitStatus()!.untracked > 0,
                    }}>
                      <span class="git-branch-icon"><GitBranch size={12} strokeWidth={1.5} /></span>
                      <span class="git-branch-name">{gitStatus()!.branch}</span>
                    </span>
                    <Show when={gitStatus()!.staged + gitStatus()!.modified + gitStatus()!.untracked + gitStatus()!.deleted + gitStatus()!.conflicted > 0}>
                      <span class="git-stats">
                        <Show when={gitStatus()!.staged > 0}>
                          <span class="git-stat staged" title={t('folder.countStaged', { n: gitStatus()!.staged })}>{gitStatus()!.staged}S</span>
                        </Show>
                        <Show when={gitStatus()!.modified > 0}>
                          <span class="git-stat modified" title={t('folder.countModified', { n: gitStatus()!.modified })}>{gitStatus()!.modified}M</span>
                        </Show>
                        <Show when={gitStatus()!.untracked > 0}>
                          <span class="git-stat untracked" title={t('folder.countUntracked', { n: gitStatus()!.untracked })}>{gitStatus()!.untracked}U</span>
                        </Show>
                        <Show when={gitStatus()!.deleted > 0}>
                          <span class="git-stat deleted" title={t('folder.countDeleted', { n: gitStatus()!.deleted })}>{gitStatus()!.deleted}D</span>
                        </Show>
                        <Show when={gitStatus()!.conflicted > 0}>
                          <span class="git-stat conflicted" title={t('folder.countConflicted', { n: gitStatus()!.conflicted })}>{gitStatus()!.conflicted}C</span>
                        </Show>
                      </span>
                    </Show>
                  </span>
                </Show>
              </>
            }>
              <input
                ref={(el) => { searchInputEl = el; }}
                class="title-search-input"
                type="text"
                placeholder={t('folder.searchPlaceholder')}
                value={runtimeFilter()}
                onInput={(e) => setRuntimeFilter(e.currentTarget.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Escape') { e.preventDefault(); setShowSearch(false); setRuntimeFilter(''); }
                }}
              />
            </Show>
          </>
        }
        actions={
          <Show when={currentPath() && !error()}>
            <>
              <button
                class="action-btn"
                classList={{ active: showSearch() }}
                title={t('common.search')}
                onClick={() => {
                  const next = !showSearch();
                  setShowSearch(next);
                  if (!next) { setRuntimeFilter(''); }
                  else requestAnimationFrame(() => searchInputEl?.focus());
                }}
              >
                <Search size={13} strokeWidth={1.5} />
              </button>
              <Show when={vm() !== 'details'}>
                <button
                  class="action-btn"
                  title={sortOrder() === 'asc' ? t('folder.sortAsc') : t('folder.sortDesc')}
                  onClick={() => setSortOrder(o => o === 'asc' ? 'desc' : 'asc')}
                >
                  {sortOrder() === 'asc' ? <ArrowUp size={13} strokeWidth={1.5} /> : <ArrowDown size={13} strokeWidth={1.5} />}
                </button>
              </Show>
              <Show when={(p.navigationMode ?? 'explorer') === 'navigate'}>
                <div class="nav-divider" />
                <button class="action-btn nav-btn" classList={{ disabled: !canGoBack() }}
                  title={t('common.back')} onClick={goBack}><ChevronLeft size={14} strokeWidth={1.5} /></button>
                <button class="action-btn nav-btn" classList={{ disabled: !canGoForward() }}
                  title={t('folder.forward')} onClick={goForward}><ChevronRight size={14} strokeWidth={1.5} /></button>
                <button class="action-btn nav-btn" classList={{ disabled: isAtHome() }}
                  title={t('folder.goHome')} onClick={goHome}><Home size={13} strokeWidth={1.5} /></button>
              </Show>
            </>
          </Show>
        }
      />

      {/* ── Body ── */}
      <div class="widget-body">
      <Switch>
        <Match when={!currentPath() && !isDemo()}>
          <div class="state-msg" innerHTML={t('folder.selectFolder')} />
        </Match>
        <Match when={loading() && entries().length === 0}>
          <div class="state-msg">{t('common.loading')}</div>
        </Match>
        <Match when={error()}>
          <div class="state-msg error">{error()}</div>
        </Match>
        <Match when={true}>
          <div
            class="scroll-wrap"
            ref={attachScrollWrap}
            data-folder-root={currentPath()}
            tabindex="0"
            onKeyDown={handleKeyDown}
            style="outline:none"
          >
            <Switch>

              {/* ── Grid (labelled tiles) + Icon grid (icons only) ── */}
              <Match when={isGridViewMode(vm())}>
                <div
                  class="grid-container"
                  classList={{ 'drag-over-bg': dragOverTarget() === currentPath(), 'icon-grid': isIconGrid() }}
                  style={(() => { const gp = gridPreset(); const tpl = gridTemplate({ minColWidth: gridColWidth(), rowHeight: gridRowHeight(), stretchHeight: effectiveStretchHeight() }); return `gap:var(--item-gap,1px);grid-template-columns:${tpl.columns};grid-auto-rows:${tpl.autoRows};overflow-y:${effectiveStretchHeight() ? 'hidden' : 'auto'};--grid-icon-size:${gp.iconSize}px;--grid-thumb-size:${gp.thumbnailSize}px;--grid-font-size:${gp.fontSize}px;--grid-font-icon-size:${gp.fontIconSize}px;--grid-pad-top:${gp.padTop}px;--grid-pad-side:${gp.padSide}px;--grid-pad-bottom:${gp.padBottom}px;--grid-item-gap:${gp.innerGap}px;--grid-line-clamp:${gp.lineClamp};`; })()}
                  onScroll={checkScroll}
                  onClick={handleContainerClick}
                  {...bgProps}
                >
                  <For each={filteredEntries()}>
                    {(entry) => {
                      const fp = entryPath(currentPath(), entry);
                      return (
                        <div
                          class="grid-item"
                          classList={{
                            selected:      selectedPaths().has(fp),
                            'last-selected': lastSelectedPath() === fp && !selectedPaths().has(fp),
                            'cut-pending': cutPendingPaths().includes(fp),
                            'drag-over':   entry.isDirectory && (dragOverTarget() === fp),
                          }}
                          // Icon grid hides the label, so surface the name on hover.
                          title={isIconGrid() ? displayName(entry) : undefined}
                          {...itemProps(entry)}
                        >
                          <span class="grid-icon-wrap">
                            <FileIcon fp={fp} entry={entry} class="grid-icon" imgClass="grid-icon-img" />
                            <CloudBadge fp={fp} inline={false} />
                          </span>
                          {/* Icon grid drops the filename, but a rename still needs
                              its inline input, so only the static label is hidden. */}
                          <Show when={renamingPath() === fp} fallback={
                            <Show when={!isIconGrid()}>
                              <span class="grid-name">{displayName(entry)}</span>
                            </Show>
                          }>
                            <input
                              ref={(el) => { renameInputEl = el; }}
                              class="rename-input"
                              value={renameValue()}
                              onInput={(e) => setRenameValue(e.currentTarget.value)}
                              onKeyDown={(e) => {
                                if (e.key === 'Enter') { e.preventDefault(); commitRename(); }
                                else if (e.key === 'Escape') { e.preventDefault(); cancelRename(); }
                              }}
                              onBlur={commitRename}
                              onClick={(e) => e.stopPropagation()}
                            />
                          </Show>
                        </div>
                      );
                    }}
                  </For>
                </div>
              </Match>

              {/* ── Details ── */}
              <Match when={vm() === 'details'}>
                <div
                  class="details-container"
                  classList={{ 'drag-over-bg': dragOverTarget() === currentPath() }}
                  onScroll={checkScroll}
                  onClick={handleContainerClick}
                  {...bgProps}
                >
                  <div class="details-table" style={`${itemSizeVars()}--det-cols:${detailColTemplate()}`}>
                    <div class="details-header">
                      <div class="det-name det-sort-header" classList={{ 'sort-active': sortField() === 'name' }}
                        onClick={(e) => { e.stopPropagation(); sortField() === 'name' ? setSortOrder(o => o === 'asc' ? 'desc' : 'asc') : (setSortField('name'), setSortOrder('asc')); }}>
                        {t('folder.colName')} <Show when={sortField() === 'name'}>{sortOrder() === 'asc' ? <ArrowUp size={9} strokeWidth={2} /> : <ArrowDown size={9} strokeWidth={2} />}</Show>
                      </div>
                      <For each={visibleCols()}>
                        {(key) => (
                          <Switch>
                            <Match when={key === 'size'}>
                              <div class="det-size det-sort-header" classList={{ 'sort-active': sortField() === 'size' }}
                                onClick={(e) => { e.stopPropagation(); sortField() === 'size' ? setSortOrder(o => o === 'asc' ? 'desc' : 'asc') : (setSortField('size'), setSortOrder('asc')); }}>
                                {t('folder.colSize')} <Show when={sortField() === 'size'}>{sortOrder() === 'asc' ? <ArrowUp size={9} strokeWidth={2} /> : <ArrowDown size={9} strokeWidth={2} />}</Show>
                              </div>
                            </Match>
                            <Match when={key === 'modified'}>
                              <div class="det-modified det-sort-header" classList={{ 'sort-active': sortField() === 'modified' }}
                                onClick={(e) => { e.stopPropagation(); sortField() === 'modified' ? setSortOrder(o => o === 'asc' ? 'desc' : 'asc') : (setSortField('modified'), setSortOrder('asc')); }}>
                                {t('folder.colModified')} <Show when={sortField() === 'modified'}>{sortOrder() === 'asc' ? <ArrowUp size={9} strokeWidth={2} /> : <ArrowDown size={9} strokeWidth={2} />}</Show>
                              </div>
                            </Match>
                            <Match when={key === 'type'}>
                              <div class="det-type det-sort-header" classList={{ 'sort-active': sortField() === 'type' }}
                                onClick={(e) => { e.stopPropagation(); sortField() === 'type' ? setSortOrder(o => o === 'asc' ? 'desc' : 'asc') : (setSortField('type'), setSortOrder('asc')); }}>
                                {t('folder.colType')} <Show when={sortField() === 'type'}>{sortOrder() === 'asc' ? <ArrowUp size={9} strokeWidth={2} /> : <ArrowDown size={9} strokeWidth={2} />}</Show>
                              </div>
                            </Match>
                            <Match when={key === 'cloudSync'}><div class="det-cloud" title={t('folder.cloudSyncTitle')}><Cloud size={13} strokeWidth={1.5} /></div></Match>
                            <Match when={key === 'gitStatus'}><div class="det-git" title={t('folder.gitStatusTitle')}><GitBranch size={13} strokeWidth={1.5} /></div></Match>
                            <Match when={key === 'itemCount'}><div class="det-type" title={t('folder.itemCountTitle')}>{t('folder.itemsHeader')}</div></Match>
                          </Switch>
                        )}
                      </For>
                    </div>
                    <For each={filteredEntries()}>
                      {(entry) => {
                        const fp = entryPath(currentPath(), entry);
                        return (
                          <div
                            class="details-row"
                            classList={{
                              selected:        selectedPaths().has(fp),
                              'last-selected': lastSelectedPath() === fp && !selectedPaths().has(fp),
                              'cut-pending':   cutPendingPaths().includes(fp),
                              'drag-over':     entry.isDirectory && (dragOverTarget() === entryPath(currentPath(), entry)),
                            }}
                            {...itemProps(entry)}
                          >
                            <div class="det-name">
                              <FileIcon fp={fp} entry={entry} />
                              <Show when={renamingPath() === fp} fallback={
                                <span class="item-name">{displayName(entry)}</span>
                              }>
                                <input
                                  ref={(el) => { renameInputEl = el; }}
                                  class="rename-input"
                                  value={renameValue()}
                                  onInput={(e) => setRenameValue(e.currentTarget.value)}
                                  onKeyDown={(e) => {
                                    if (e.key === 'Enter') { e.preventDefault(); commitRename(); }
                                    else if (e.key === 'Escape') { e.preventDefault(); cancelRename(); }
                                  }}
                                  onBlur={commitRename}
                                  onClick={(e) => e.stopPropagation()}
                                />
                              </Show>
                              <Show when={!isColVis('gitStatus')}>
                                <GitBadge fp={fp} />
                              </Show>
                            </div>
                            <For each={visibleCols()}>
                              {(key) => (
                                <Switch>
                                  <Match when={key === 'size'}>
                                    <div class="det-size">{entry.isDirectory ? '—' : fmtSize(statCache[fp]?.size)}</div>
                                  </Match>
                                  <Match when={key === 'modified'}>
                                    <div class="det-modified">{fmtDate(statCache[fp]?.mtime)}</div>
                                  </Match>
                                  <Match when={key === 'type'}>
                                    <div class="det-type">{extLabel(entry)}</div>
                                  </Match>
                                  <Match when={key === 'cloudSync'}>
                                    <div class="det-cloud">
                                      <Switch>
                                        <Match when={cloudStatus(fp) === 'cloud-only'}><span class="det-cloud-icon cloud-only" title={t('folder.cloudOnly')}><Cloud size={14} strokeWidth={2} /></span></Match>
                                        <Match when={cloudStatus(fp) === 'pinned'}><span class="det-cloud-icon pinned" title={t('folder.cloudPinned')}><CircleCheck size={14} strokeWidth={2} /></span></Match>
                                        <Match when={cloudStatus(fp) === 'synced'}><span class="det-cloud-icon synced" title={t('folder.cloudSynced')}><CircleCheck size={14} strokeWidth={2} /></span></Match>
                                        <Match when={true}>—</Match>
                                      </Switch>
                                    </div>
                                  </Match>
                                  <Match when={key === 'gitStatus'}>
                                    <div class="det-git">
                                      {(() => {
                                        const repo = entry.isDirectory ? gitRepoCache[fp] : undefined;
                                        if (repo) return <span class={`det-git-repo ${repo.dirty ? 'dirty' : 'clean'}`} title={repo.dirty ? t('folder.repoDirty', { branch: repo.branch }) : t('folder.repoClean', { branch: repo.branch })}><span class="det-git-repo-icon"><GitBranch size={11} strokeWidth={1.5} /></span><span class="det-git-branch">{repo.branch}</span></span>;
                                        const s = gitFileStatus(fp);
                                        return s
                                          ? <span class={`det-git-icon ${s}`} title={GIT_STATUS_TITLE[s]}>{GIT_STATUS_LABEL[s]}</span>
                                          : '—';
                                      })()}
                                    </div>
                                  </Match>
                                  <Match when={key === 'itemCount'}>
                                    <div class="det-type">
                                      {entry.isDirectory ? (fp in folderCountCache ? String(folderCountCache[fp]) : '—') : '—'}
                                    </div>
                                  </Match>
                                </Switch>
                              )}
                            </For>
                          </div>
                        );
                      }}
                    </For>
                  </div>
                </div>
              </Match>

              {/* ── List (default) ── */}
              <Match when={true}>
                <div
                  class="list"
                  classList={{ 'drag-over-bg': dragOverTarget() === currentPath() }}
                  style={`${itemSizeVars()}gap:var(--item-gap,1px);`}
                  onScroll={checkScroll}
                  onClick={handleContainerClick}
                  {...bgProps}
                >
                  <For each={filteredEntries()}>
                    {(entry) => {
                      const fp = entryPath(currentPath(), entry);
                      return (
                        <div
                          class="item"
                          classList={{
                            selected:        selectedPaths().has(fp),
                            'last-selected': lastSelectedPath() === fp && !selectedPaths().has(fp),
                            'cut-pending':   cutPendingPaths().includes(fp),
                            'drag-over':     entry.isDirectory && (dragOverTarget() === entryPath(currentPath(), entry)),
                          }}
                          {...itemProps(entry)}
                        >
                          <FileIcon fp={fp} entry={entry} />
                          <Show when={renamingPath() === fp} fallback={
                            <span class="item-name">{displayName(entry)}</span>
                          }>
                            <input
                              ref={(el) => { renameInputEl = el; }}
                              class="rename-input"
                              value={renameValue()}
                              onInput={(e) => setRenameValue(e.currentTarget.value)}
                              onKeyDown={(e) => {
                                if (e.key === 'Enter') { e.preventDefault(); commitRename(); }
                                else if (e.key === 'Escape') { e.preventDefault(); cancelRename(); }
                              }}
                              onBlur={commitRename}
                              onClick={(e) => e.stopPropagation()}
                            />
                          </Show>
                          <GitBadge fp={fp} />
                          <CloudBadge fp={fp} inline={true} />
                        </div>
                      );
                    }}
                  </For>
                </div>
              </Match>

            </Switch>

            <Show when={rubberBandStyle()}>
              <div class="rubber-band" style={rubberBandStyle()!} aria-hidden="true" />
            </Show>
            <Show when={canScrollDown() && !effectiveStretchHeight()}>
              <div class="scroll-indicator" aria-hidden="true"><ChevronDown size={14} strokeWidth={2} /></div>
            </Show>
          </div>
        </Match>
      </Switch>
      </div>

    </div>
  );
}

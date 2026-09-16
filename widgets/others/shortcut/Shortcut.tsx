import { createSignal, createEffect, createMemo, onMount, onCleanup, For, Show } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { invoke } from '../../../src/ipc.ts';
import { readTextFile } from '@tauri-apps/plugin-fs';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { onAppBlur } from '../../../src/input/appBlur.ts';
import {
  Link, Settings, Globe, Folder as FolderIcon, LayoutTemplate,
  ExternalLink, Copy, Pencil, Trash2, Search, X as XIcon,
  FolderOpen, FolderSearch, Rocket, FileCode,
} from 'lucide-solid';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { useLayout } from '../../../src/viewContext.tsx';
import { getViewLayoutStore } from '../../../src/layout.ts';
import { registerDropZone, startDrag } from '../../../src/nativeDragDrop.ts';
import { recordOp } from '../../../src/operationLog.ts';
import { logger } from '../../../src/debugLog.ts';
import { createPolling } from '../../../src/createPolling.ts';
import { t } from '../../../src/i18n.ts';
import { findQuickSet } from './quickSets.ts';
import {
  isRecycleBinPath, recycleBinStatus, recycleBinHasItems, binBadgeLabel,
  refreshRecycleBinStatus, fmtBinSize, RECYCLE_POLL_MS,
} from './recycleBin.ts';
import {
  itemKind, newId, displayName as itemDisplayName, parseUrlFileBody,
  parseUrlFileIcon, fileStem,
  type ShortcutItem,
} from './items.ts';
import { rootToEntries, collectAssignments, type Assignment, type LaunchEntry } from './layout.ts';
import {
  freshGestureState, beginPress, pollStep, resetGestureCandidates,
} from '../../office/folder/gesture.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './Shortcut.css';

// Webdriver test hooks mirrored onto the window so e2e specs can assert on the
// IPC we would have sent (Tauri 2's `__TAURI_INTERNALS__.invoke` is non-writable
// and can't be stubbed reliably). Only ever read/written when navigator.webdriver.
interface ShortcutTestWindow extends Window {
  __shortcutLastOpen?: { kind: string; path: string; ts: number };
  __shortcutLastLaunch?: { entries: LaunchEntry[]; ts: number };
}

// ── Module-level keyboard router (mirrors folder widget's) ──────────────────
// WS_EX_NOACTIVATE means the widget window never gains Win32 keyboard focus.
// The Rust WH_KEYBOARD_LL hook emits a `folder-key` Tauri event to whichever
// widget last called `activate_widget_keyboard`. Each widget type owns its own
// active-handler slot and ignores events destined for others.
interface SyntheticKeyEvent {
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  key: string;
  preventDefault: () => void;
}

const NON_CTRL_KEYS: ReadonlySet<string> = new Set([
  'delete', 'escape', 'up', 'down', 'left', 'right',
  'enter', 'home', 'end', 'pageup', 'pagedown',
]);
const KEY_ALIAS: Record<string, string> = {
  up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight',
  enter: 'Enter', home: 'Home', end: 'End',
  pageup: 'PageUp', pagedown: 'PageDown',
  delete: 'Delete', escape: 'Escape',
};

let _shortcutKeyHandler: ((e: SyntheticKeyEvent) => void) | null = null;
try {
  getCurrentWindow().listen<string>('folder-key', (ev) => {
    let raw = ev.payload;
    let shift = false;
    if (raw.startsWith('shift+')) { shift = true; raw = raw.slice(6); }
    const isCtrlLetter = !NON_CTRL_KEYS.has(raw);
    _shortcutKeyHandler?.({
      ctrlKey: isCtrlLetter,
      metaKey: false,
      shiftKey: shift,
      key: KEY_ALIAS[raw] ?? raw,
      preventDefault: () => {},
    });
  }).catch(() => {});
} catch {
  // Non-Tauri context (vite preview, unit tests): skip.
}

export interface ShortcutConfigData {
  items?: ShortcutItem[];
  viewMode?: string;
  itemSize?: string;
  gridItemSize?: string;
  gridStretchHeight?: boolean;
  gridIconOnly?: boolean;
  clickMode?: string;
  headerMode?: HeaderMode;
  showFileExtension?: boolean;
  label?: string;
}

export interface ShortcutProps extends ShortcutConfigData {
  _widgetId?: string;
  _editMode?: boolean;
}

const DBG = 'shortcut';

export default function Shortcut(props: ShortcutProps) {
  const layoutStore = useLayout();
  const items = () => (props.items ?? []) as ShortcutItem[];
  const viewMode = () => props.viewMode ?? 'grid';
  const itemSize = () => props.itemSize ?? 'medium';
  const gridItemSize = () => props.gridItemSize ?? 'medium';
  const gridStretchHeight = () => props.gridStretchHeight ?? false;
  const gridIconOnly = () => props.gridIconOnly ?? false;
  const clickMode = () => props.clickMode ?? 'double';
  const headerMode = (): HeaderMode => props.headerMode ?? 'normal';
  const showFileExtension = () => props.showFileExtension ?? false;

  function currentConfigSnapshot(): ShortcutConfigData {
    return {
      items: (props.items ?? []).map(i => ({ ...i })),
      viewMode: viewMode(),
      itemSize: itemSize(),
      gridItemSize: gridItemSize(),
      gridStretchHeight: gridStretchHeight(),
      gridIconOnly: gridIconOnly(),
      clickMode: clickMode(),
      headerMode: headerMode(),
      showFileExtension: showFileExtension(),
      label: props.label ?? '',
    };
  }

  /**
   * Persist a new items array and push a `widget-config` op-log entry so
   * Ctrl+Z can revert to the snapshot captured here. Pass `undoLabel = null`
   * to skip recording (e.g. for undo-driven writes, programmatic sync, or
   * mutations that aren't user-initiated).
   */
  function writeItems(next: ShortcutItem[], undoLabel: string | null = null, immediate = false) {
    if (!props._widgetId) return;
    const prev = currentConfigSnapshot();
    const nextCfg = { ...prev, items: next };
    layoutStore.updateWidgetConfig(props._widgetId, nextCfg, immediate);
    if (undoLabel) {
      recordOp({
        kind: 'widget-config',
        widgetId: props._widgetId,
        label: undoLabel,
        previousConfig: prev as Record<string, unknown>,
      });
    }
  }

  // -- Selection state -------------------------------------------------------
  const [selected, setSelected] = createSignal<Set<string>>(new Set());
  const [anchorId, setAnchorId] = createSignal<string | null>(null);

  function clearSelection() { setSelected(new Set<string>()); setAnchorId(null); }
  function isSelected(id: string): boolean { return selected().has(id); }
  function selectOnly(id: string) { setSelected(new Set([id])); setAnchorId(id); }
  function toggleSelection(id: string) {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
    setAnchorId(id);
  }
  function rangeSelect(id: string) {
    const all = visibleItems();
    const anchor = anchorId() ?? id;
    const i1 = all.findIndex(x => x.id === anchor);
    const i2 = all.findIndex(x => x.id === id);
    if (i1 < 0 || i2 < 0) { selectOnly(id); return; }
    const [lo, hi] = i1 <= i2 ? [i1, i2] : [i2, i1];
    const next = new Set<string>();
    for (let i = lo; i <= hi; i++) next.add(all[i].id);
    setSelected(next);
  }

  // Drop any selection entries that no longer exist in items.
  createEffect(() => {
    const ids = new Set(items().map(i => i.id));
    const cur = selected();
    let dirty = false;
    const next = new Set<string>();
    for (const id of cur) { if (ids.has(id)) next.add(id); else dirty = true; }
    if (dirty) setSelected(next);
  });

  // -- Search ----------------------------------------------------------------
  const [searchOpen, setSearchOpen] = createSignal(false);
  const [searchQ, setSearchQ] = createSignal('');
  let searchInputEl: HTMLInputElement | undefined;

  function openSearch() {
    setSearchOpen(true);
    queueMicrotask(() => searchInputEl?.focus());
  }
  function closeSearch() {
    setSearchOpen(false);
    setSearchQ('');
  }

  const visibleItems = createMemo<ShortcutItem[]>(() => {
    const q = searchQ().trim().toLowerCase();
    if (!q) return items();
    return items().filter(it => {
      const name = itemDisplayName(it).toLowerCase();
      const path = (it.path ?? '').toLowerCase();
      return name.includes(q) || path.includes(q);
    });
  });

  // -- Broken-target detection (file items only) ----------------------------
  const [brokenSet, setBrokenSet] = createSignal<Set<string>>(new Set());
  const existsInFlight = new Set<string>();
  createEffect(() => {
    for (const it of items()) {
      if (itemKind(it.path) !== 'file') continue;
      if (existsInFlight.has(it.path)) continue;
      existsInFlight.add(it.path);
      invoke('fs_exists', { path: it.path })
        .then(ok => {
          setBrokenSet(prev => {
            const next = new Set(prev);
            if (ok) next.delete(it.path); else next.add(it.path);
            return next;
          });
        })
        .catch(e => logger.error(DBG, `fs_exists(${it.path}) failed: ${e}`))
        .finally(() => existsInFlight.delete(it.path));
    }
  });

  // -- Recycle Bin fill state ------------------------------------------------
  // A `shell:RecycleBinFolder` item badges itself while the bin holds anything
  // ([S-75]). Only widgets that actually pin the bin poll; the status itself is
  // shared process-wide, so several such widgets still cost one IPC per tick.
  const hasRecycleItem = createMemo(() => items().some(it => isRecycleBinPath(it.path)));
  createEffect(() => { if (hasRecycleItem()) void refreshRecycleBinStatus(); });
  createPolling(
    () => { if (hasRecycleItem()) void refreshRecycleBinStatus(); },
    () => RECYCLE_POLL_MS,
  );

  // -- Icon cache for file + shell items -------------------------------------
  const [iconCache, setIconCache] = createSignal<Record<string, string>>({});
  const inFlight = new Set<string>();
  /** Load `path`'s Windows shell icon into the cache. `key` defaults to the
   *  path; pass a different one when the same path can render two different
   *  icons (the Recycle Bin's full / empty variants). */
  function fetchFileIcon(path: string, key: string = path) {
    if (!path) return;
    if (iconCache()[key] != null) return;
    if (inFlight.has(key)) return;
    inFlight.add(key);
    invoke('get_file_icon', { path, size: 48 })
      .then(src => setIconCache(prev => ({ ...prev, [key]: src })))
      .catch(e => {
        setIconCache(prev => ({ ...prev, [key]: '' }));
        logger.error(DBG, `get_file_icon failed: ${e}`);
      })
      .finally(() => inFlight.delete(key));
  }
  createEffect(() => {
    for (const it of items()) {
      // Items carrying an explicit icon source (e.g. a dropped .url's IconFile)
      // load it regardless of kind.
      if (it.iconPath) fetchFileIcon(it.iconPath);
      const kind = itemKind(it.path);
      if (kind === 'layout') {
        // Layout items preview a stack of the apps they launch, so fetch a shell
        // icon per assigned non-URL target (URL targets use their favicon).
        for (const a of (it.layout ? collectAssignments(it.layout) : [])) {
          if (a.kind !== 'url') fetchFileIcon(a.target);
        }
        continue;
      }
      // [S-17] Shell namespace items (Recycle Bin, This PC, …) use the real
      // Windows icon, keyed per bin state so the full / empty variants don't
      // share one cache slot.
      if (kind === 'shell') { fetchFileIcon(it.path, shellIconKey(it.path)); continue; }
      if (kind !== 'file') continue;
      fetchFileIcon(it.path);
    }
  });

  /** Cache key for a shell item's icon: the Recycle Bin renders a different
   *  glyph when it holds something, so its state is part of the key. */
  function shellIconKey(path: string): string {
    return isRecycleBinPath(path)
      ? `${path}#${recycleBinHasItems() ? 'full' : 'empty'}`
      : path;
  }

  // -- Favicon failure tracking ---------------------------------------------
  const [faviconFailed, setFaviconFailed] = createSignal<Record<string, boolean>>({});
  function markFaviconFailed(url: string) {
    setFaviconFailed(prev => ({ ...prev, [url]: true }));
  }

  function faviconUrl(url: string): string | null {
    try {
      // Resolve the favicon for the full origin (scheme + host), so subdomains
      // get their own icon: keep.google.com → Keep's favicon, not Google's.
      // faviconV2 (what Chrome's bookmark UI uses) honours the full URL; the
      // older s2 `?domain=` endpoint collapses subdomains to the root domain.
      const origin = new URL(url).origin;
      return `https://t3.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON`
        + `&fallback_opts=TYPE,SIZE,URL&url=${encodeURIComponent(origin)}&size=64`;
    } catch { return null; }
  }

  // -- Open target -----------------------------------------------------------
  // - URL items   → default browser (open_url)
  // - shell items → ShellExecuteW for `shell:` URIs (open_in_explorer routes them)
  // - file items  → default associated app via open_file (ShellExecuteW "open"
  //                 verb, with a context-menu fallback). Directories naturally
  //                 land in Explorer; files launch their default handler
  //                 (image viewer, .lnk target, .url browser, etc.).
  function open(item: ShortcutItem) {
    const k = itemKind(item.path);
    // Webdriver test hook. `__TAURI_INTERNALS__.invoke` is non-writable in
    // Tauri 2 so e2e specs can't reliably stub the IPC. Mirror what we'd be
    // sending to Rust on a window-scoped global so tests can assert on it.
    const isWd = typeof window !== 'undefined' && navigator.webdriver;
    if (isWd) {
      (window as ShortcutTestWindow).__shortcutLastOpen = { kind: k, path: item.path, ts: Date.now() };
    }
    if (k === 'layout') {
      const entries = rootToEntries(item.layout);
      if (!entries.length) return;
      if (isWd) (window as ShortcutTestWindow).__shortcutLastLaunch = { entries, ts: Date.now() };
      invoke('launch_app_layout', { entries })
        .catch(e => logger.error(DBG, `launch_app_layout failed: ${e}`));
      return;
    }
    if (k === 'url')   { invoke('open_url',         { url:  item.path }); return; }
    if (k === 'shell') { invoke('open_in_explorer', { path: item.path }); return; }
    invoke('open_file', { path: item.path });
  }

  function handleItemClick(e: MouseEvent, item: ShortcutItem) {
    if (props._editMode) return;
    // Interacting with an item claims the keyboard router for this widget so
    // arrow / Delete / Enter act on the selection the user just made. A real
    // mouse click also fires mousedown on the root (which claims it), but
    // synthetic clicks in tests don't, so claim here too.
    activateKeyboard();
    // Modifier-clicks always act as selection commands regardless of clickMode.
    if (e.ctrlKey || e.metaKey) { toggleSelection(item.id); return; }
    if (e.shiftKey)             { rangeSelect(item.id); return; }

    // Plain click: select-only. Open only when clickMode is single.
    selectOnly(item.id);
    if (clickMode() === 'single') open(item);
  }

  function handleItemDblClick(_e: MouseEvent, item: ShortcutItem) {
    if (props._editMode) return;
    if (clickMode() === 'double') open(item);
  }

  // -- Bulk actions keyed off selection --------------------------------------
  function openSelected() {
    const ids = selected();
    const targets = items().filter(i => ids.has(i.id));
    if (targets.length === 0) return;
    for (const it of targets) open(it);
  }
  function removeSelected() {
    const ids = selected();
    if (ids.size === 0) return;
    const next = items().filter(i => !ids.has(i.id));
    const label = ids.size === 1 ? 'Remove shortcut' : `Remove ${ids.size} shortcuts`;
    writeItems(next, label);
    clearSelection();
  }
  function selectAll() {
    setSelected(new Set(visibleItems().map(i => i.id)));
    setAnchorId(visibleItems()[0]?.id ?? null);
  }

  // -- Keyboard handler (routed via the `folder-key` Tauri event) ------------
  function handleKey(e: SyntheticKeyEvent) {
    if (props._editMode) return;
    // While rename/search input is focused, don't steal keys.
    const active = document.activeElement as HTMLElement | null;
    if (active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA')) return;

    if (e.ctrlKey && e.key.toLowerCase() === 'a') {
      e.preventDefault(); selectAll(); return;
    }
    if (e.key === 'Escape') {
      e.preventDefault();
      if (searchOpen()) { closeSearch(); return; }
      clearSelection();
      return;
    }
    if (e.key === 'Enter') { e.preventDefault(); openSelected(); return; }
    if (e.key === 'Delete') { e.preventDefault(); removeSelected(); return; }

    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' ||
        e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      const all = visibleItems();
      if (all.length === 0) return;
      const anchor = anchorId();
      const curIdx = anchor ? all.findIndex(x => x.id === anchor) : -1;
      const delta = (e.key === 'ArrowDown' || e.key === 'ArrowRight') ? 1 : -1;
      const nextIdx = curIdx < 0 ? (delta > 0 ? 0 : all.length - 1) : Math.max(0, Math.min(all.length - 1, curIdx + delta));
      const target = all[nextIdx];
      if (!target) return;
      if (e.shiftKey) rangeSelect(target.id);
      else            selectOnly(target.id);
    }
  }

  // Activate this widget's keyboard router on any mousedown inside it.
  function activateKeyboard() {
    _shortcutKeyHandler = handleKey;
    try {
      invoke('activate_widget_keyboard', { label: getCurrentWindow().label }).catch(() => {});
    } catch {
      // ignore: non-Tauri context (dev browser/tests) has no keyboard router
    }
  }

  // Also install a plain window-level keydown listener as a fallback for the
  // dev-browser / non-Tauri context (where the Rust hook isn't present) and
  // for unit tests. In Tauri this is redundant with the hook routing.
  //
  // NOTE: do NOT claim the keyboard slot on mount. The Rust WH_KEYBOARD_LL
  // hook is process-global; claiming it before the user has touched the
  // widget makes it swallow stray global keystrokes (an Arrow selects the
  // first item, Enter then opens it) and leaves an item permanently
  // "preselected". We claim only on real interaction (root mousedown or an
  // item click), mirroring the folder widget.
  onMount(() => {
    function onWin(e: KeyboardEvent) {
      if (_shortcutKeyHandler !== handleKey) return;
      handleKey({
        ctrlKey: e.ctrlKey, metaKey: e.metaKey, shiftKey: e.shiftKey,
        key: e.key, preventDefault: () => e.preventDefault(),
      });
    }
    document.addEventListener('keydown', onWin);

    // Keep the selection only when the click lands on one of THIS instance's
    // own item rows. Anything else clears it: the empty list background, this
    // widget's header/chrome, another shortcut widget, or the desktop, the
    // same as the folder widget, where a background click deselects. Ignore
    // clicks inside a context menu: mousedown fires before click and would wipe
    // the selection before a menu action (Open/Remove/…) can read it.
    function onGlobalMouseDown(e: MouseEvent) {
      const target = e.target as HTMLElement | null;
      if (target?.closest('.ctx-menu, .ctx-submenu, .ctx-backdrop')) return;
      const onOwnItem = !!(listEl && target && listEl.contains(target)
        && target.closest('.shortcut-item'));
      if (onOwnItem) return;
      clearSelection();
    }
    document.addEventListener('mousedown', onGlobalMouseDown);

    // Clicking outside the Themia app entirely (another app, the desktop) never
    // lands a DOM mousedown in the webview, so the global handler above can't
    // catch it, so clear the selection on app blur too.
    const cleanupBlur = onAppBlur(() => clearSelection());

    onCleanup(() => {
      document.removeEventListener('keydown', onWin);
      document.removeEventListener('mousedown', onGlobalMouseDown);
      cleanupBlur();
      if (_shortcutKeyHandler === handleKey) _shortcutKeyHandler = null;
    });
  });

  // -- Rename ---------------------------------------------------------------
  const [editingId, setEditingId] = createSignal<string | null>(null);
  function commitRename(item: ShortcutItem, raw: string) {
    const label = raw.trim();
    const cur = items().find(x => x.id === item.id);
    const wasLabel = cur?.label ?? '';
    if (label === wasLabel) { setEditingId(null); return; }
    const next = items().map(x =>
      x.id === item.id ? { ...x, label: label || undefined } : x,
    );
    writeItems(next, 'Rename shortcut');
    setEditingId(null);
  }

  // -- Per-item actions ------------------------------------------------------
  function copyToClipboard(text: string) {
    navigator.clipboard.writeText(text).catch(() => {});
  }
  // Reveal a file/folder in Explorer (selected). `open_in_explorer` runs the
  // `/select,"<path>"` verb, so the parent folder opens with the item focused.
  function openItemLocation(item: ShortcutItem) {
    invoke('open_in_explorer', { path: item.path }).catch((err) =>
      logger.error(DBG, `open_in_explorer failed: ${err}`));
  }
  // Re-pick the target file for a file item (keeps id + label).
  async function changeItemPath(item: ShortcutItem) {
    const selected = await openDialog({ directory: false, multiple: false });
    if (typeof selected !== 'string') return;
    writeItems(items().map(x => x.id === item.id ? { ...x, path: selected } : x), 'Change shortcut');
    invoke('push_windows_to_desktop').catch(() => {});
  }
  // Open the visual layout editor window for a layout item.
  function editLayout(item: ShortcutItem) {
    if (!props._widgetId) return;
    invoke('open_layout_window', {
      parentLabel: getCurrentWindow().label,
      widgetId: props._widgetId,
      viewId: layoutStore.viewId,
      itemId: item.id,
    }).catch((err) => logger.error(DBG, `open_layout_window failed: ${err}`));
  }

  // -- Per-item context menu (type-specific) ---------------------------------
  function handleItemCtx(e: MouseEvent, item: ShortcutItem) {
    e.preventDefault(); e.stopPropagation();

    // If right-clicked item isn't part of the selection, reset the selection
    // to just that item so bulk actions operate on the user's visible target.
    const sel = selected();
    const isMulti = sel.size > 1 && sel.has(item.id);
    if (!sel.has(item.id)) selectOnly(item.id);

    const k = itemKind(item.path);
    const count = isMulti ? sel.size : 1;

    type CtxItem = Parameters<typeof showCtxMenu>[1][number];
    const menu: CtxItem[] = [];

    // Open / Launch: always first, supports multi-open.
    menu.push({
      icon: ExternalLink,
      label: isMulti ? `Open ${count}` : (k === 'layout' ? 'Launch layout' : 'Open'),
      action: () => isMulti ? openSelected() : open(item),
    });

    // Type-specific actions (single selection only).
    if (!isMulti) {
      if (k === 'url') {
        menu.push({ icon: Copy, label: t('shortcut.copyUrl'), action: () => copyToClipboard(item.path) });
      } else if (k === 'file') {
        menu.push({ icon: FolderOpen, label: t('shortcut.openFileLocation'), action: () => openItemLocation(item) });
        menu.push({ icon: Copy, label: t('shortcut.copyPath'), action: () => copyToClipboard(item.path) });
        menu.push({ icon: FolderSearch, label: t('shortcut.changeFile'), action: () => changeItemPath(item) });
      } else if (k === 'shell') {
        menu.push({ icon: FolderOpen, label: t('shortcut.openLocation'), action: () => openItemLocation(item) });
        menu.push({ icon: Copy, label: t('shortcut.copyPath'), action: () => copyToClipboard(item.path) });
      } else if (k === 'layout') {
        menu.push({ icon: LayoutTemplate, label: t('shortcut.editLayoutMenu'), action: () => editLayout(item) });
      }
    }

    menu.push({ icon: Pencil, label: t('common.rename'), disabled: isMulti, action: () => setEditingId(item.id) });
    menu.push({ sep: true });
    menu.push({
      icon: Trash2,
      label: isMulti ? `Remove ${count}` : 'Remove',
      danger: true,
      action: () => isMulti
        ? removeSelected()
        : writeItems(items().filter(x => x.id !== item.id), 'Remove shortcut'),
    });

    showCtxMenu(e, menu);
  }

  // -- Reorder (HTML5 DnD within widget) ------------------------------------
  const [dragId, setDragId] = createSignal<string | null>(null);
  const [dragOverId, setDragOverId] = createSignal<string | null>(null);

  function onDragStartItem(e: DragEvent, item: ShortcutItem) {
    if (props._editMode) { e.preventDefault(); return; }
    setDragId(item.id);
    if (e.dataTransfer) {
      e.dataTransfer.setData('application/x-shortcut-item', item.id);
      e.dataTransfer.effectAllowed = 'move';
    }
  }
  function onDragOverItem(e: DragEvent, item: ShortcutItem) {
    if (!dragId()) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
    setDragOverId(item.id);
  }
  function onDropItem(e: DragEvent, item: ShortcutItem) {
    e.preventDefault();
    const src = dragId();
    setDragId(null); setDragOverId(null);
    if (!src || src === item.id) return;
    const list = items().slice();
    const srcIdx = list.findIndex(x => x.id === src);
    const tgtIdx = list.findIndex(x => x.id === item.id);
    if (srcIdx < 0 || tgtIdx < 0) return;
    const [moved] = list.splice(srcIdx, 1);
    list.splice(tgtIdx, 0, moved);
    writeItems(list, 'Reorder shortcuts');
  }
  function onDragEndItem() { setDragId(null); setDragOverId(null); }

  // -- Native drops (files, URLs, cross-widget items) ------------------------
  let rootEl: HTMLDivElement | undefined;
  let listEl: HTMLDivElement | undefined;
  const [nativeOver, setNativeOver] = createSignal(false);

  // Live intra-widget reorder preview. The OLE drag blocks the JS poll loop,
  // but the IDropTarget keeps emitting DragOver → `native-drag` 'over' events
  // while it runs, so we reorder a DISPLAY-only copy of the visible items as
  // the cursor moves (committed to config on drop). `oleDragId` is the id of
  // the item this widget instance is currently dragging (set in startOleDrag).
  const [dragPreview, setDragPreview] = createSignal<ShortcutItem[] | null>(null);
  let oleDragId: string | null = null;

  // What the list actually renders: the live preview while dragging, else the
  // search-filtered items.
  const displayItems = createMemo<ShortcutItem[]>(() => dragPreview() ?? visibleItems());

  /** Reorder the visible items so `draggedId` lands in the slot the cursor
   *  currently resolves to, the SAME hit-test the drop commit uses, so the
   *  preview is WYSIWYG. Returns null when the dragged id isn't visible. */
  function previewOrderFor(x: number, y: number, draggedId: string): ShortcutItem[] | null {
    const base = visibleItems();
    const srcIdx = base.findIndex(i => i.id === draggedId);
    if (srcIdx < 0) return null;
    const target = dropTarget(x, y, draggedId);
    // Cursor over the dragged row itself → keep the order we already show
    // (snapping back to the original here would flicker, since the dragged row
    // tracks the cursor).
    if (target === 'self') return dragPreview() ?? base.slice();
    const without = base.filter(i => i.id !== draggedId);
    let insertAt = target == null ? without.length : without.findIndex(i => i.id === target);
    if (insertAt < 0) insertAt = without.length;
    without.splice(insertAt, 0, base[srcIdx]);
    return without;
  }

  /** Persist a finished live-preview reorder. Maps the reordered visible subset
   *  back onto the full items() list (filter-safe) and skips the write when the
   *  order is unchanged (no undo entry for a no-op). */
  function commitPreview(preview: ShortcutItem[]): void {
    let next: ShortcutItem[];
    if (!searchQ().trim()) {
      next = preview;
    } else {
      const visIds = new Set(visibleItems().map(i => i.id));
      let p = 0;
      next = items().map(it => (visIds.has(it.id) ? preview[p++] : it));
    }
    const cur = items();
    if (next.length === cur.length && next.every((x, i) => x.id === cur[i].id)) return;
    writeItems(next, 'Reorder shortcuts');
  }

  function acceptNative(ev: { dataTypes: string[]; widgetData?: string }) {
    if (ev.dataTypes.includes('files')) return true;
    if (ev.dataTypes.includes('url')) return true;
    if (ev.widgetData) {
      try {
        const d = JSON.parse(ev.widgetData);
        if (d?.kind === 'shortcut-item') return true;
      } catch {
        // ignore: malformed drag payload, not a shortcut item
      }
    }
    return false;
  }

  // Recompute the live reorder preview from a native-drag event, but only while
  // THIS widget is the OLE drag source; cross-widget drags append on drop and
  // get no in-place preview. The source id normally comes from `oleDragId` (set
  // in startOleDrag); real `over` events carry no payload. As a fallback we also
  // accept a self-sourced shortcut-item in the event's `widgetData` (the native
  // event bus / tests can attach it to `over` events).
  function updateLivePreview(ev: { clientX: number; clientY: number; widgetData?: string }) {
    let draggedId = oleDragId;
    if (!draggedId && ev.widgetData) {
      try {
        const d = JSON.parse(ev.widgetData);
        if (d?.kind === 'shortcut-item' && d.sourceWidgetId === props._widgetId) {
          draggedId = d.item?.id ?? null;
        }
      } catch {
        // ignore: malformed drag payload, no live preview for it
      }
    }
    if (!draggedId) return;
    const order = previewOrderFor(ev.clientX, ev.clientY, draggedId);
    if (order) setDragPreview(order);
  }

  onMount(() => {
    if (!props._widgetId) return;
    const cleanup = registerDropZone(
      `shortcut-${props._widgetId}`,
      () => rootEl,
      {
        onEnter(ev) {
          if (props._editMode) return;
          if (acceptNative(ev)) setNativeOver(true);
          updateLivePreview(ev);
        },
        onOver(ev) {
          if (props._editMode) return;
          if (acceptNative(ev)) setNativeOver(true);
          updateLivePreview(ev);
        },
        onLeave() {
          setNativeOver(false);
          // Dragging back out of the widget reverts to the committed order.
          if (oleDragId) setDragPreview(null);
        },
        onDrop(ev) {
          setNativeOver(false);
          if (props._editMode) return false;

          // Cross-widget / reorder via widget data (JSON blob).
          if (ev.widgetData) {
            try {
              const d = JSON.parse(ev.widgetData);
              if (d?.kind === 'shortcut-item' && d.item?.path) {
                handleShortcutItemDrop(d, ev);
                return true;
              }
            } catch (e) {
              logger.error(DBG, `parse widget-data failed: ${e}`);
            }
          }

          // URL drop from browser (Rust extracts via CFSTR_INETURLW).
          if (ev.urls && ev.urls.length > 0) {
            const appended = ev.urls.map(u => ({ id: newId(), path: u } as ShortcutItem));
            writeItems([...items(), ...appended],
              appended.length === 1 ? 'Add shortcut' : `Add ${appended.length} shortcuts`);
            return true;
          }

          if (ev.filePaths && ev.filePaths.length > 0) {
            void handleNativeFiles(ev.filePaths);
            return true;
          }
          return false;
        },
      },
    );
    onCleanup(cleanup);
  });

  async function handleNativeFiles(paths: string[]): Promise<void> {
    const appended: ShortcutItem[] = [];
    for (const p of paths) {
      const lower = p.toLowerCase();
      if (lower.endsWith('.url')) {
        try {
          const body = await readTextFile(p);
          const url = parseUrlFileBody(body);
          if (url) {
            const isHttp = /^https?:\/\//i.test(url);
            const iconFile = parseUrlFileIcon(body);
            appended.push({
              id: newId(),
              path: url,
              // The .url filename is the shortcut's real name, so keep it.
              label: fileStem(p) || undefined,
              // Custom-protocol shortcuts (steam://…) have no favicon; use the
              // .url's own IconFile so the real app/game icon shows. http(s)
              // shortcuts keep using their favicon.
              iconPath: !isHttp && iconFile ? iconFile : undefined,
            });
            continue;
          }
        } catch (e) {
          logger.error(DBG, `readTextFile(${p}) failed: ${e}`);
        }
      } else if (lower.endsWith('.lnk')) {
        try {
          const tgt = await invoke(
            'resolve_shortcut', { path: p },
          );
          if (tgt?.target_path) {
            appended.push({ id: newId(), path: tgt.target_path });
            continue;
          }
        } catch (e) {
          logger.error(DBG, `resolve_shortcut(${p}) failed: ${e}`);
        }
      }
      appended.push({ id: newId(), path: p });
    }
    if (appended.length > 0) {
      writeItems([...items(), ...appended],
        appended.length === 1 ? 'Add shortcut' : `Add ${appended.length} shortcuts`);
    }
  }

  /**
   * Handle an OLE-dragged shortcut-item that was dropped on this widget.
   * Payload shape:
   *   { kind: 'shortcut-item', sourceWidgetId, dragId, item: ShortcutItem }
   *
   * Three cases:
   *  - source == this widget → intra-widget reorder: move the item in-place
   *    using elementFromPoint to find the drop target row.
   *  - different source → receive: append the item here. The sender removes
   *    itself when startNativeDrag resolves with effect='move'.
   *  - source is this widget via a pointer-free "virtual" reorder: same as
   *    case 1 in practice.
   */
  /** Remove the moved item from the widget it came from. `sourceViewId` is
   *  carried in the drag payload because the source widget may live in another
   *  monitor window, whose view store this window may never have instantiated
   *  (so `findViewForWidget` alone would come up empty and silently duplicate
   *  the item instead of moving it). */
  function removeFromSource(widgetId: string | undefined, viewId: string | undefined, itemId: string): void {
    if (!widgetId || !viewId) return;
    const store = getViewLayoutStore(viewId);
    const w = store.layout.widgets.find(x => x.id === widgetId);
    if (!w) return;
    const cur = (w.config?.items ?? []) as ShortcutItem[];
    if (!cur.some(x => x.id === itemId)) return;
    store.updateWidgetConfig(widgetId, {
      ...(w.config ?? {}),
      items: cur.filter(x => x.id !== itemId),
    }, true);
  }

  function handleShortcutItemDrop(
    d: { sourceWidgetId?: string; sourceViewId?: string; dragId?: string; item: ShortcutItem },
    ev: { clientX: number; clientY: number },
  ) {
    const incomingItem = d.item;
    const isSelfDrop = d.sourceWidgetId === props._widgetId;

    if (isSelfDrop) {
      // If a live preview is showing, commit exactly what the user sees. The
      // preview already tracked the cursor through the whole drag, so this is
      // WYSIWYG and needs no re-hit-test.
      const preview = dragPreview();
      setDragPreview(null);
      if (preview) { commitPreview(preview); return; }
      // Fallback (no preview, e.g. a drag too fast to emit DragOver, or a
      // programmatic drop in tests): reorder in place. `dropTarget` resolves the
      // cursor to the row to insert BEFORE (a row id), to the end (null →
      // dropped on empty space), or to a no-op ('self' → dropped on the dragged
      // row). Working by id keeps this correct even when a search filter is
      // active (DOM order = visibleItems, a subset of items()).
      const cur = items();
      const srcIdx = cur.findIndex(x => x.id === incomingItem.id);
      if (srcIdx < 0) return;
      const target = dropTarget(ev.clientX, ev.clientY, incomingItem.id);
      if (target === 'self') return;
      const moved = cur[srcIdx];
      const without = cur.filter(x => x.id !== incomingItem.id);
      let insertAt = target == null ? without.length : without.findIndex(x => x.id === target);
      if (insertAt < 0) insertAt = without.length;
      without.splice(insertAt, 0, moved);
      // Skip the write (and its undo entry) when the order didn't change.
      if (without.length === cur.length && without.every((x, i) => x.id === cur[i].id)) return;
      writeItems(without, 'Reorder shortcuts');
    } else {
      // Cross-widget receive: add the item here (with a fresh id so it can't
      // collide with something already in our list). Persist immediately so the
      // sender's window reflects the move without the debounce lag.
      const copy: ShortcutItem = { ...incomingItem, id: newId() };
      writeItems([...items(), copy], 'Move shortcut', true);
      // The RECEIVER also removes the original ([S-35]). The sender cannot: its
      // `start_native_drag` promise and this drop event are two independent
      // messages into the webview, so acting on the promise raced the drop and
      // deleted items that were never re-added (#94 follow-up: a shortcut
      // dropped back in its own list vanished). Routing through the source's own
      // view store keeps this correct across monitor windows too.
      removeFromSource(d.sourceWidgetId, d.sourceViewId, incomingItem.id);
    }
  }

  /**
   * Insertion hit-test for intra-widget reorder, resolving the cursor position
   * to one of three outcomes:
   *   - `'self'`  → cursor is over the dragged row itself: no-op.
   *   - `string`  → id of the row the cursor is over: insert the item BEFORE it.
   *   - `null`    → cursor is over empty space (e.g. past the last item):
   *                 append at the end.
   * Works for the vertical (list / details) and 2D (grid) layouts alike, since
   * the bounding-rect test is the same. Dropping on empty space appends rather
   * than vanishing, the regression the previous no-match→no-op path caused.
   */
  function dropTarget(x: number, y: number, srcId: string): 'self' | string | null {
    if (!rootEl) return 'self';
    const nodes = Array.from(rootEl.querySelectorAll<HTMLElement>('.shortcut-item'));
    for (const n of nodes) {
      const r = n.getBoundingClientRect();
      if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) {
        return n.dataset.id === srcId ? 'self' : (n.dataset.id ?? null);
      }
    }
    return null;
  }

  // -- OLE drag (cross-widget / drag-out) -----------------------------------
  // WS_EX_NOACTIVATE + HWND_BOTTOM windows have unreliable mousemove delivery
  // while a button is held; Chromium suppresses motion events on unfocused
  // windows. Mirror the folder widget: poll GetCursorPos via the
  // `get_cursor_state` Tauri command, and once the cursor moves past the 5px
  // threshold, hand off to OLE `DoDragDrop` which handles cross-window
  // routing. HTML5 `draggable` is intentionally NOT set on items; it would
  // race with OLE and trap the drag inside this window.

  interface CursorState { x: number; y: number; left_down: boolean; ctrl_down: boolean; }

  // Physical-pixel origin of this Tauri window (for GetCursorPos → clientXY).
  let winOriginPx: { x: number; y: number } = { x: 0, y: 0 };
  try {
    getCurrentWindow().innerPosition()
      .then(p => { winOriginPx = { x: p.x, y: p.y }; })
      .catch(() => {});
  } catch {
    // ignore: non-Tauri context has no window position; origin stays at 0,0
  }

  function physToClient(px: number, py: number): [number, number] {
    const dpr = window.devicePixelRatio || 1;
    return [(px - winOriginPx.x) / dpr, (py - winOriginPx.y) / dpr];
  }

  // Single source of truth for the press → click | rubber-band | OLE-drag
  // gesture. All reset/decision logic lives in the SHARED folder gesture state
  // machine (../../office/folder/gesture.ts) so it stays unit-testable and can
  // NEVER strand stale state that misclassifies the next plain click (#58,
  // [S-74]). The band signal below is the render mirror the machine drives.
  const g = freshGestureState();
  const [band, setBand] = createSignal<{ x1: number; y1: number; x2: number; y2: number } | null>(null);

  // Full reset for every abnormal / teardown exit path: clears the pure gesture
  // candidate AND the band render mirror, and bumps the generation so any
  // still-suspended poll loop tears itself down instead of racing a fresh one.
  function resetGesture(): void {
    resetGestureCandidates(g);
    setBand(null);
  }

  // The loop owns exactly one generation of the gesture. It stops the instant a
  // fresh press (or a reset) bumps `g.generation`, so a fast re-press can never
  // leave two loops racing the same state, and any abnormal exit (a transient
  // cursor-read failure, or an external item-row mouseup) fully resets rather
  // than stranding a stale drag candidate that phantom-drags the next press (#58).
  async function runMousePoll(gen: number): Promise<void> {
    while (g.polling && g.generation === gen) {
      let st: CursorState;
      try { st = await invoke('get_cursor_state'); }
      catch { resetGesture(); return; }
      if (!g.polling || g.generation !== gen) return;

      const [cx, cy] = physToClient(st.x, st.y);
      const action = pollStep(g, { cx, cy, leftDown: st.left_down });

      if (action.kind === 'release') {
        // Mouse released: tear the band visual down. A plain background press is
        // already deselected at mousedown by the global handler ([S-62]).
        setBand(null);
        return;
      }

      if (action.kind === 'startDrag') {
        // Threshold reached on an item press: hand off to the native OLE drag
        // (blocks until drop/cancel). pollStep already tore the candidate down.
        // The item id rode in `paths`; resolve it back to the live item.
        setBand(null);
        const id = action.paths[0];
        const item = id ? items().find(x => x.id === id) : undefined;
        if (item) startOleDrag(item);
        return;
      }

      if (action.kind === 'band') {
        const b = action.rect;
        setBand(b);
        if (listEl) {
          const rect = { left: Math.min(b.x1, b.x2), top: Math.min(b.y1, b.y2),
                         right: Math.max(b.x1, b.x2), bottom: Math.max(b.y1, b.y2) };
          const sel = new Set<string>();
          for (const el of listEl.querySelectorAll<HTMLElement>('.shortcut-item')) {
            const r = el.getBoundingClientRect();
            if (r.right >= rect.left && r.left <= rect.right &&
                r.bottom >= rect.top && r.top <= rect.bottom) {
              const id = el.dataset.id;
              if (id) sel.add(id);
            }
          }
          setSelected(sel);
        }
      }

      await new Promise(r => setTimeout(r, 16));
    }
  }

  function startMousePoll(): void {
    if (g.polling) return; // idempotent: never spin up a second loop
    g.polling = true;
    const gen = g.generation;
    runMousePoll(gen).catch(() => { resetGesture(); });
  }

  const rubberBandStyle = createMemo((): string | null => {
    const b = band();
    if (!b || !listEl) return null;
    const r = listEl.getBoundingClientRect();
    return `left:${Math.min(b.x1, b.x2) - r.left}px;top:${Math.min(b.y1, b.y2) - r.top}px;`
         + `width:${Math.abs(b.x2 - b.x1)}px;height:${Math.abs(b.y2 - b.y1)}px`;
  });

  // mousedown on the list background (not an item): begin a rubber band.
  function onListMouseDown(e: MouseEvent) {
    if (props._editMode) return;
    if (e.button !== 0) return;
    const target = e.target as HTMLElement | null;
    // Item presses are handled by onItemMouseDown (selection + OLE drag).
    if (target && target.closest('.shortcut-item')) return;
    e.preventDefault();
    // beginPress fully wipes any leftover gesture state BEFORE arming a fresh
    // background (rubber-band) candidate, so a press can never inherit a stale
    // drag origin from an aborted gesture (#58).
    beginPress(g, { x: e.clientX, y: e.clientY, onItem: false });
    startMousePoll();
  }

  function startOleDrag(item: ShortcutItem) {
    const sessionId = `sd-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    // Arm the live-reorder preview and dim the dragged row for the duration of
    // the OLE drag.
    oleDragId = item.id;
    setDragId(item.id);
    setDragPreview(null);
    const payload = {
      kind: 'shortcut-item',
      sourceWidgetId: props._widgetId,
      sourceViewId: layoutStore.viewId,
      dragId: sessionId,
      item,
    };
    startDrag({
      type: 'widget',
      widgetJson: JSON.stringify(payload),
      effectAllowed: 'copyMove',
    }).then((effect) => {
      // The sender NEVER removes the item here. The drop event and this promise
      // are independent messages into the webview, so a removal keyed off
      // `effect === 'move'` could (and did) run before the drop handler had
      // re-added or reordered anything, making the item vanish on a plain
      // drop-in-place. The receiver owns both halves of a move now.
      //
      // What IS safe here: committing a reorder the user already saw. A live
      // preview only exists while the cursor is over THIS list ([S-65]), so if
      // one is still pending after a real drop (effect != 'none'), the drop
      // event was lost or arrived late — commit exactly what was on screen.
      // commitPreview skips a write when the order didn't change, so the drop
      // handler doing it first makes this a no-op rather than a double edit.
      const pending = dragPreview();
      if (effect !== 'none' && pending) commitPreview(pending);
    }).catch(e => {
      logger.error(DBG, `startDrag failed: ${e}`);
    }).finally(() => {
      // Drag finished (drop, cross-widget move, or cancel): drop the visual
      // drag state. The committed order (if any) was already written by the
      // drop handler / onLeave revert.
      oleDragId = null;
      setDragId(null);
      setDragPreview(null);
    });
  }

  function onItemMouseDown(e: MouseEvent, item: ShortcutItem) {
    if (props._editMode) return;
    if (e.button !== 0) return;
    // beginPress wipes prior state before arming an item drag candidate; the
    // item id rides in `paths` so the poll loop can resolve it back on handoff.
    beginPress(g, { x: e.clientX, y: e.clientY, onItem: true, paths: [item.id] });
    startMousePoll();
  }

  // Kept as no-ops for callers; cross-widget drag is OLE-driven via
  // mousedown polling and doesn't need DOM mouseup tracking.
  function onItemMouseMove(_e: MouseEvent, _item: ShortcutItem) { /* unused; kept for compatibility */ }
  // A DOM mouseup over an item row can beat the poll loop's own release
  // detection ([S-63]); reset the whole gesture here too so a marquee released
  // over an item doesn't leave the lasso stuck, AND so the stopped loop can't
  // strand a candidate for the next press to phantom-drag (#58). resetGesture
  // bumps the generation, which also tears down any loop still mid-await.
  function onItemMouseUp() { resetGesture(); }

  onCleanup(() => {
    resetGesture();
    oleDragId = null; setDragPreview(null);
  });

  // -- Per-item icon rendering ----------------------------------------------
  // One icon for a single layout-pane assignment (favicon for URLs, shell icon
  // for apps/files/folders, lucide fallback while/until it loads).
  function renderAssignmentIcon(a: Assignment, size: number) {
    if (a.kind === 'url') {
      const fav = faviconUrl(a.target);
      if (fav && !faviconFailed()[a.target]) {
        return <img class="shortcut-stack-img" style={{ width: `${size}px`, height: `${size}px` }}
                    src={fav} alt="" draggable={false} onError={() => markFaviconFailed(a.target)} />;
      }
      return <Globe size={size} strokeWidth={1.5} />;
    }
    const src = iconCache()[a.target];
    if (src) return <img class="shortcut-stack-img" style={{ width: `${size}px`, height: `${size}px` }}
                         src={src} alt="" draggable={false} />;
    if (a.kind === 'explorer') return <FolderOpen size={size} strokeWidth={1.5} />;
    if (a.kind === 'path')     return <FileCode size={size} strokeWidth={1.5} />;
    return <Rocket size={size} strokeWidth={1.5} />;
  }

  function renderIcon(item: ShortcutItem) {
    const k = itemKind(item.path);
    if (k === 'layout') {
      // Show a stack of the apps this layout opens; fall back to the generic
      // layout glyph when nothing is assigned yet.
      const assigns = item.layout ? collectAssignments(item.layout) : [];
      if (assigns.length === 0) {
        return <span class="shortcut-icon-lucide"><LayoutTemplate size={iconSize()} strokeWidth={1.5} /></span>;
      }
      const stackSize = Math.max(12, Math.round(iconSize() * 0.72));
      const overlap = Math.round(stackSize * 0.42);
      const shown = assigns.slice(0, 4);
      return (
        <span class="shortcut-layout-stack">
          {shown.map((a, i) => (
            <span class="shortcut-layout-stack-item"
                  style={i > 0 ? { 'margin-left': `-${overlap}px` } : undefined}>
              {renderAssignmentIcon(a, stackSize)}
            </span>
          ))}
        </span>
      );
    }
    if (k === 'url') {
      // Custom-protocol shortcuts with an IconFile (e.g. Steam games) show that
      // real icon; http(s) URLs (or before/if the icon fails) use the favicon.
      if (item.iconPath) {
        const src = iconCache()[item.iconPath];
        if (src) return <img class="shortcut-icon-img" src={src} alt="" draggable={false} />;
      }
      const fav = faviconUrl(item.path);
      if (fav && !faviconFailed()[item.path]) {
        return <img class="shortcut-icon-img" src={fav} alt="" draggable={false}
                    onError={() => markFaviconFailed(item.path)} />;
      }
      return <span class="shortcut-icon-lucide"><Globe size={iconSize()} strokeWidth={1.5} /></span>;
    }
    if (k === 'shell') {
      // [S-17] Real Windows namespace icon; the lucide quick-set glyph is only
      // the placeholder shown while it loads (or if the shell refuses it).
      const src = iconCache()[shellIconKey(item.path)];
      if (src) return <img class="shortcut-icon-img" src={src} alt="" draggable={false} />;
      const qs = findQuickSet(item.path);
      if (qs) {
        return <span class="shortcut-icon-lucide">
          <Dynamic component={qs.icon} size={iconSize()} strokeWidth={1.5} />
        </span>;
      }
      return <span class="shortcut-icon-lucide"><FolderIcon size={iconSize()} strokeWidth={1.5} /></span>;
    }
    const src = iconCache()[item.path];
    if (src) return <img class="shortcut-icon-img" src={src} alt="" draggable={false} />;
    return <span class="shortcut-icon-lucide"><FolderIcon size={iconSize()} strokeWidth={1.5} /></span>;
  }

  // Icon dimensions mirror the folder widget's ITEM_SIZE_PRESETS / GRID_SIZE_PRESETS
  // exactly so the two widgets are visually identical at every size.
  const iconSize = createMemo(() => {
    if (viewMode() === 'grid') {
      const gs = gridItemSize();
      return gs === 'small' ? 32 : gs === 'large' ? 64 : 48;
    }
    return itemSize() === 'small' ? 13 : itemSize() === 'large' ? 20 : 16;
  });

  function fileItemDisplay(it: ShortcutItem): string {
    if (it.label) return it.label;
    if (!showFileExtension()) return itemDisplayName(it);
    const p = it.path ?? '';
    const base = p.replace(/\\/g, '/').split('/').filter(Boolean).pop() ?? p;
    return base;
  }
  function renderName(it: ShortcutItem): string {
    return itemKind(it.path) === 'file' ? fileItemDisplay(it) : itemDisplayName(it);
  }

  /** Hover text. The Recycle Bin reports what it holds ([S-75]); file items keep
   *  the broken-target annotation ([S-44]); everything else shows its path. */
  function itemTitle(it: ShortcutItem): string {
    if (isRecycleBinPath(it.path)) {
      const s = recycleBinStatus();
      if (!s) return renderName(it);
      if (s.items === 0) return `${renderName(it)} — ${t('shortcut.binEmpty')}`;
      const count = s.items === 1
        ? t('shortcut.binCountOne',   { n: s.items })
        : t('shortcut.binCountOther', { n: s.items });
      return `${renderName(it)} — ${count} (${fmtBinSize(s.bytes)})`;
    }
    if (itemKind(it.path) === 'file' && brokenSet().has(it.path)) {
      return `${it.path} (target missing)`;
    }
    return it.path;
  }

  /** Count-badge geometry, scaled off the icon so it reads at every item size
   *  without swallowing a 13px list-row glyph. */
  const binBadgeHeight = createMemo(() => Math.min(20, Math.max(12, Math.round(iconSize() * 0.42))));
  const binBadgeFont   = createMemo(() => Math.max(9, Math.round(binBadgeHeight() * 0.66)));

  return (
    <div
      class="shortcut-widget"
      classList={{
        [`view-${viewMode()}`]: true,
        [`size-${itemSize()}`]: true,
        [`grid-size-${gridItemSize()}`]: viewMode() === 'grid',
        'grid-stretch': viewMode() === 'grid' && gridStretchHeight(),
        'grid-icon-only': viewMode() === 'grid' && gridIconOnly(),
        [`click-${clickMode()}`]: true,
        'drag-over': nativeOver(),
      }}
      ref={el => { rootEl = el; }}
      onMouseDown={activateKeyboard}
    >
      <WidgetHeader
        mode={headerMode()}
        icon={Link}
        title={props.label || 'Shortcuts'}
        titleOverride={
          <Show
            when={searchOpen()}
            fallback={<span class="title-text">{props.label || 'Shortcuts'}</span>}
          >
            <input
              class="search-input"
              type="text"
              placeholder={t('shortcut.searchPlaceholder')}
              value={searchQ()}
              ref={el => { searchInputEl = el; }}
              onInput={(e) => setSearchQ(e.currentTarget.value)}
              onKeyDown={(e) => {
                if (e.key === 'Escape') { e.preventDefault(); closeSearch(); }
              }}
            />
          </Show>
        }
        actions={
          <button
            class="search-toggle-btn"
            title={searchOpen() ? 'Close search' : 'Search'}
            onClick={() => searchOpen() ? closeSearch() : openSearch()}
          >
            <Show
              when={searchOpen()}
              fallback={<Search size={13} strokeWidth={1.5} />}
            >
              <XIcon size={13} strokeWidth={1.5} />
            </Show>
          </button>
        }
      />

      <div class="widget-body">
      <Show
        when={items().length > 0}
        fallback={
          <div class="shortcut-empty">
            <span>
              Drop files or URLs here,<br />
              or click <Settings size={13} strokeWidth={1.5} style="display:inline-block;vertical-align:-2px" /> to add.
            </span>
          </div>
        }
      >
        <Show
          when={visibleItems().length > 0}
          fallback={<div class="shortcut-empty">{t('shortcut.noMatches')}</div>}
        >
        <div
          class="shortcut-list"
          ref={el => { listEl = el; }}
          onMouseDown={onListMouseDown}
        >
          <For each={displayItems()}>
            {(item) => (
              <div
                class="shortcut-item"
                classList={{
                  'drag-src':  dragId() === item.id,
                  'drag-over': dragOverId() === item.id,
                  'selected':  isSelected(item.id),
                  'broken':    itemKind(item.path) === 'file' && brokenSet().has(item.path),
                }}
                data-id={item.id}
                title={itemTitle(item)}
                onClick={(e) => handleItemClick(e, item)}
                onDblClick={(e) => handleItemDblClick(e, item)}
                onContextMenu={(e) => handleItemCtx(e, item)}
                onMouseDown={(e) => onItemMouseDown(e, item)}
                onMouseMove={(e) => onItemMouseMove(e, item)}
                onMouseUp={onItemMouseUp}
                onDragStart={(e) => onDragStartItem(e, item)}
                onDragOver={(e) => onDragOverItem(e, item)}
                onDrop={(e) => onDropItem(e, item)}
                onDragEnd={onDragEndItem}
              >
                <div class="shortcut-icon">
                  {renderIcon(item)}
                  {/* Recycle Bin fill indicator: how many files are in it ([S-75]). */}
                  <Show when={isRecycleBinPath(item.path) && recycleBinHasItems()}>
                    <span
                      class="shortcut-bin-badge"
                      style={{
                        height: `${binBadgeHeight()}px`,
                        'min-width': `${binBadgeHeight()}px`,
                        'font-size': `${binBadgeFont()}px`,
                      }}
                    >
                      {binBadgeLabel(recycleBinStatus()?.items ?? 0)}
                    </span>
                  </Show>
                </div>
                <Show
                  when={editingId() === item.id}
                  fallback={<span class="shortcut-name">{renderName(item)}</span>}
                >
                  <input
                    class="shortcut-rename"
                    autofocus
                    value={item.label ?? itemDisplayName(item)}
                    onBlur={(e) => commitRename(item, e.currentTarget.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') { e.preventDefault(); (e.currentTarget as HTMLInputElement).blur(); }
                      else if (e.key === 'Escape') { e.preventDefault(); setEditingId(null); }
                    }}
                    onClick={(e) => e.stopPropagation()}
                    onDblClick={(e) => e.stopPropagation()}
                  />
                </Show>
                <Show when={viewMode() === 'details' && itemKind(item.path) !== 'shell' && itemKind(item.path) !== 'layout'}>
                  <span class="shortcut-path">{item.path}</span>
                </Show>
              </div>
            )}
          </For>
          <Show when={rubberBandStyle()}>
            <div class="rubber-band" style={rubberBandStyle()!} aria-hidden="true" />
          </Show>
        </div>
        </Show>
      </Show>
      </div>
    </div>
  );
}

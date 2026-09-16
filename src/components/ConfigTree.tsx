import { For, Show, createEffect, createMemo, createSignal, onMount, onCleanup, type Accessor } from 'solid-js';
import type { MonitorDescriptor } from '../ipc.ts';
import { logger } from '../debugLog.ts';
import { ChevronRight, ChevronDown, ChevronUp, Plus, Monitor, Pencil, Copy, Trash2 } from 'lucide-solid';
import type { LucideIcon } from 'lucide-solid';
import { ask } from '@tauri-apps/plugin-dialog';
import { viewStore, addView, atFreeViewLimit, nextViewName, switchView, monitorLabel, fetchActiveMonitors, duplicateView, deleteView, isDemoView } from '../views.ts';
import { showCtxMenu, type CtxMenuItem } from '../ctxMenuStore.ts';
import { viewInitials } from '../viewState.ts';
import { getViewLayoutStore, isContainerType, TRAY_COLLECTION_ID } from '../layout.ts';
import { buildWidgetRows } from './configTreeRows.ts';
import registry from '../widgets/registry.tsx';
import { navItems } from './SettingsModal.tsx';
import { VIEW_ICON_MAP } from './iconMap.tsx';
import { trayIconKeyOf, trayWidgetTitle } from '../tray.ts';
import { createTypeAhead, findByPrefix, isTypeAheadKey, nextIndexLinear } from '../input/kbdNav.ts';
import type { ConfigTarget } from '../config/target.ts';
import { onStorageKey, postTransient, onTransient } from '../sync.ts';
import { t } from '../i18n.ts';

interface ConfigTreeProps {
  selection: Accessor<ConfigTarget>;
  expanded: Accessor<string | null>;
  onSelect: (t: ConfigTarget) => void;
  onToggleExpand: (viewId: string) => void;
  /** Parent supplies a callback to receive imperative focus handles. */
  registerHandle?: (h: { focusActive(): void; focusFirst(): void }) => void;
}

function targetEq(a: ConfigTarget, b: ConfigTarget): boolean {
  if (a.kind !== b.kind) return false;
  if (a.kind === 'app' && b.kind === 'app') return a.page === b.page;
  if (a.kind === 'widget' && b.kind === 'widget') return a.widgetId === b.widgetId;
  if (a.kind === 'view' && b.kind === 'view') return a.viewId === b.viewId;
  if (a.kind === 'display' && b.kind === 'display') return a.monitorId === b.monitorId;
  if (a.kind === 'add' && b.kind === 'add') return true;
  return false;
}

// One flat record per visible tree row, emitted in DOM render order so a flat
// index can drive roving tabindex + Arrow-key navigation across the otherwise
// nested structure.
type Row =
  | { kind: 'app';      label: string; target: ConfigTarget; icon: LucideIcon }
  | { kind: 'display';  label: string; target: ConfigTarget }
  | { kind: 'view';     label: string; target: ConfigTarget; viewId: string }
  | {
      kind: 'widget'; label: string; target: ConfigTarget; viewId: string;
      /** 1 = the view's own widget, 2 = nested inside a container. */
      depth: number;
      /** Container this row sits in, for the ArrowLeft "one level up" jump. */
      parentId?: string;
      /** True when this row is itself a container, so ArrowRight can descend. */
      isContainer: boolean;
    }
  | { kind: 'addView';  label: string }
  /** Closes the tray group (Tray.SPEC [S-08]), the way `addView` closes the
   *  views group. The tray's own widgets are ordinary `widget` rows carrying
   *  `viewId: TRAY_COLLECTION_ID`: a tray widget's config page is the same page
   *  a view's widget gets. */
  | { kind: 'addTray';  label: string };

// Left sidebar of the unified config window. Tree: pinned App Settings group,
// then one node per view: selecting a view opens its settings; the
// chevron expands it to one node per widget. Driven by reactive stores so it
// updates live as views and widgets are added or removed.
export default function ConfigTree(props: ConfigTreeProps) {
  const isActive = (t: ConfigTarget) => targetEq(props.selection(), t);

  // Mirror App.tsx's edit-mode signal via the shared localStorage key. Writing
  // here propagates to widget windows through the `storage` event; the
  // onStorageKey subscription catches changes the other direction (e.g. Ctrl+E
  // pressed on a widget window) so this entry's active state stays in sync.
  const EDIT_MODE_KEY = 'themia-edit-mode';
  const [editMode, setEditMode_] = createSignal(localStorage.getItem(EDIT_MODE_KEY) === 'true');
  const toggleEditMode = () => {
    const next = !editMode();
    setEditMode_(next);
    localStorage.setItem(EDIT_MODE_KEY, String(next));
    // Broadcast over the reliable cross-window channel (see App.tsx) so every
    // widget window enters/leaves edit mode, not just whichever ones happen to
    // receive the `storage` event.
    postTransient({ type: 'edit-mode-set', value: next });
  };
  const offEditModeSync = onStorageKey(EDIT_MODE_KEY, (v) => setEditMode_(v === 'true'));
  onCleanup(offEditModeSync);

  // Adopt edit-mode changes broadcast by widget windows (toggled via the pill,
  // Ctrl+E, etc.) so the slider tracks them live.
  const offEditModeSet = onTransient('edit-mode-set', (m) => setEditMode_(!!m.value));
  onCleanup(offEditModeSet);

  // The localStorage snapshot read above can be stale: it survives an app
  // restart, so the key may still say "true" from a previous session while the
  // freshly-launched widget windows actually booted with edit mode off. That
  // left the slider showing the wrong state until the next toggle. Ask the live
  // widget windows for the real edit-mode state on mount and adopt their reply.
  const offEditModeState = onTransient('edit-mode-state', (m) => setEditMode_(!!m.value));
  onCleanup(offEditModeState);
  onMount(() => postTransient({ type: 'edit-mode-query' }));

  // Currently-connected monitors, one tree node each. Refreshed on the
  // `themia:display-changed` event so plug/unplug updates the tree live. Each
  // node is keyed on the STABLE id ([MI-01]) and labelled off the live
  // `deviceName`, never off the key (#70).
  const [activeMonitors, setActiveMonitors] = createSignal<MonitorDescriptor[]>([]);
  const refreshMonitors = () => {
    fetchActiveMonitors()
      .then(setActiveMonitors)
      .catch(e => {
        // Surface it: an empty list silently drops every display from the tree.
        logger.error('displays', `get_active_monitors failed: ${e}`);
        setActiveMonitors([]);
      });
  };
  onMount(() => {
    refreshMonitors();
    window.addEventListener('themia:display-changed', refreshMonitors);
    onCleanup(() => window.removeEventListener('themia:display-changed', refreshMonitors));
  });

  // Returns true only when a view was actually added (so a rows() rebuild is
  // guaranteed). Callers that arm restoreFocusOnRebuild must gate on this: the
  // early-return paths below (license redirect, addView failure) add no row and
  // trigger no rebuild, so arming the flag on them would leave it stuck true and
  // let the next unrelated rebuild steal focus back into the tree (#94).
  function handleAddView(): boolean {
    if (atFreeViewLimit()) {
      props.onSelect({ kind: 'app', page: 'license' });
      return false;
    }
    const id = addView(nextViewName());
    if (!id) return false;
    switchView(id);
    props.onSelect({ kind: 'view', viewId: id });
    return true;
  }

  // Right-click menu on a view row: the view actions that used to live on the
  // screen-switcher chips (settings / duplicate / add / delete).
  function handleViewCtx(e: MouseEvent, viewId: string) {
    const view = viewStore.list.find(v => v.id === viewId);
    const demo = isDemoView(viewId);
    const items: CtxMenuItem[] = [];
    if (!demo) {
      items.push({
        icon: Copy, label: t('app.duplicateView'),
        action: () => { const id = duplicateView(viewId); if (id) { switchView(id); props.onSelect({ kind: 'view', viewId: id }); } },
      });
    }
    items.push({ icon: Plus, label: t('app.addViewTree'), action: handleAddView });
    if (!demo && viewStore.list.length > 1) {
      items.push({ sep: true });
      items.push({
        icon: Trash2, label: t('view.deleteView.action'), danger: true,
        action: async () => {
          if (await ask(t('view.deleteView.ask', { name: view?.name ?? '' }), { title: t('view.deleteView.title'), kind: 'warning' })) {
            // If the panel is showing the view we're deleting (or a widget in it),
            // move the selection to a surviving view so the tree highlight and the
            // panel don't strand on a now-gone target.
            const sel = props.selection();
            const affected =
              (sel.kind === 'view' && sel.viewId === viewId) ||
              (sel.kind === 'widget' && getViewLayoutStore(viewId).layout.widgets.some(w => w.id === sel.widgetId));
            deleteView(viewId);
            if (affected) props.onSelect({ kind: 'view', viewId: viewStore.activeId });
          }
        },
      });
    }
    showCtxMenu(e, items);
  }

  // Right-click menu on a widget row: removal, mirroring the widget chrome's
  // remove action but reachable from the config tree. A CONTAINER row also
  // offers the creation path, same as its grid menu ([S-34]) -- the tree is
  // where its children are listed, so it is where you reach for another one.
  function handleWidgetCtx(e: MouseEvent, viewId: string, widgetId: string, label: string, isContainer: boolean) {
    const isTray = viewId === TRAY_COLLECTION_ID;
    showCtxMenu(e, [
      ...(isContainer ? [
        {
          icon: Plus, label: t('ctxMenu.addToContainer'),
          action: () => props.onSelect({ kind: 'add', into: widgetId }),
        },
        { sep: true },
      ] : []),
      // A tray row's position IS the icon's position in the notification area
      // (Tray.SPEC [S-08]), and the tree is where that order is visible, so it
      // is where it is changed.
      ...(isTray ? [
        {
          icon: ChevronUp, label: t('tray.moveUp'),
          action: () => getViewLayoutStore(viewId).reorderChild(widgetId, -1),
        },
        {
          icon: ChevronDown, label: t('tray.moveDown'),
          action: () => getViewLayoutStore(viewId).reorderChild(widgetId, 1),
        },
        { sep: true },
      ] : []),
      {
        icon: Trash2,
        label: isTray ? t('tray.removeAction') : t('ctxMenu.deleteWidget'),
        danger: true,
        action: async () => {
          const ok = isTray
            ? await ask(t('tray.removeAsk', { name: label }), { title: t('tray.removeTitle'), kind: 'warning' })
            : await ask(t('ctxMenu.deleteWidgetAsk', { name: label }), { title: t('ctxMenu.deleteWidgetTitle'), kind: 'warning' });
          if (ok) {
            // If the panel is showing the widget we're deleting, move the
            // selection off it so the panel doesn't strand on a now-gone target
            // ("Widget not found."). Mirrors handleViewCtx. A tray widget has no
            // view to fall back to, so it lands on the default node.
            const sel = props.selection();
            const affected = sel.kind === 'widget' && sel.widgetId === widgetId;
            getViewLayoutStore(viewId).removeWidget(widgetId);
            if (affected) {
              props.onSelect(isTray ? { kind: 'app', page: 'general' } : { kind: 'view', viewId });
            }
          }
        },
      },
    ]);
  }

  // Flat row list in render order, keeps tree roving in sync with what the
  // DOM actually shows, including expanded view widgets.
  const rows = createMemo<Row[]>(() => {
    const out: Row[] = [];
    for (const item of navItems()) {
      out.push({ kind: 'app', label: item.label, target: { kind: 'app', page: item.page }, icon: item.icon });
    }
    for (const m of activeMonitors()) {
      out.push({
        kind: 'display',
        label: monitorLabel(m.id, activeMonitors()),
        target: { kind: 'display', monitorId: m.id },
      });
    }
    for (const v of viewStore.list) {
      out.push({ kind: 'view', label: v.name, target: { kind: 'view', viewId: v.id }, viewId: v.id });
      if (props.expanded() === v.id) {
        const store = getViewLayoutStore(v.id);
        // Container children are nested UNDER their container, not listed as
        // its siblings (stack SPEC [S-14] / tabs SPEC [S-12]).
        for (const { widget: w, depth, parentId } of buildWidgetRows(store.layout.widgets)) {
          const def = registry[w.type];
          out.push({
            kind: 'widget',
            label: w.label || def?.label || w.type,
            target: { kind: 'widget', widgetId: w.id },
            viewId: v.id,
            depth,
            parentId,
            isContainer: isContainerType(w.type),
          });
        }
      }
    }
    out.push({ kind: 'addView', label: t('app.addViewTree') });
    // The tray collection closes the tree, as a flat group like Displays rather
    // than an expandable node (Tray.SPEC [S-08]): it holds one short list that
    // is the whole point of the section, so there is nothing worth hiding
    // behind a chevron.
    for (const w of getViewLayoutStore(TRAY_COLLECTION_ID).layout.widgets) {
      out.push({
        kind: 'widget',
        // [S-14] The name the TRAY shows, so the row and the tooltip agree.
        label: trayWidgetTitle(w),
        target: { kind: 'widget', widgetId: w.id },
        viewId: TRAY_COLLECTION_ID,
        depth: 1,
        // A tray widget is never a container ([S-01]), so it never descends.
        isContainer: false,
      });
    }
    out.push({ kind: 'addTray', label: t('tray.add') });
    return out;
  });

  // Index of the row that currently owns `tabIndex=0`. Defaults to the active
  // selection's row when one is found; falls back to row 0 otherwise.
  const [focusIdx, setFocusIdx] = createSignal(0);
  const refs: HTMLButtonElement[] = [];
  const typeAhead = createTypeAhead();
  // Set when a key/click event triggered a structural change (expand/collapse).
  // The effect that runs after the row list rebuilds reads this and re-applies
  // DOM focus to the now-current row. `<For>` recreates buttons on every
  // rebuild, so the browser loses focus during the transition otherwise.
  let restoreFocusOnRebuild = false;

  const activeRowIdx = createMemo(() => {
    const list = rows();
    return list.findIndex(r => 'target' in r && targetEq(r.target, props.selection()));
  });

  // Keep the roving index in sync with selection so Tab into the tree lands
  // on the active item, not on a stale one from a prior navigation.
  function syncFocusToActive() {
    const i = activeRowIdx();
    if (i >= 0) setFocusIdx(i);
  }

  function focusRow(i: number) {
    setFocusIdx(i);
    queueMicrotask(() => refs[i]?.focus());
  }

  function activate(row: Row) {
    // Only adding a view rebuilds the row list (a new view row appears), which
    // unmounts the focused button and drops focus to <body>; flag that so the
    // effect re-applies focus after the render. Selecting an existing view just
    // changes props.selection() — NOT a rows() dependency — so no rebuild fires
    // and the effect never runs to clear the flag. Setting it here left the flag
    // stuck true, so the next unrelated rebuild stole focus back into the tree.
    if (row.kind === 'addView') { if (handleAddView()) restoreFocusOnRebuild = true; return; }
    // The tray picker is a full-page swap like any other `add` target, so unlike
    // addView it adds no row here and needs no focus restore.
    if (row.kind === 'addTray') { props.onSelect({ kind: 'add', toTray: true }); return; }
    props.onSelect(row.target!);
  }

  function handleKeyDown(e: KeyboardEvent) {
    // Roving navigation is bare-key only. Let modified chords (e.g. Alt+←/→ for
    // window back/forward history) bubble untouched; otherwise Alt+ArrowLeft on
    // an expanded view row would both collapse it here AND go back at the window
    // level.
    if (e.altKey || e.ctrlKey || e.metaKey) return;
    const list = rows();
    if (!list.length) return;
    const curr = Math.min(focusIdx(), list.length - 1);
    const row = list[curr];
    if (!row) return;

    if (e.key === 'Enter') {
      e.preventDefault();
      activate(row);
      return;
    }
    if (e.key === ' ') {
      // Space on an expandable row toggles expansion; on others it activates.
      e.preventDefault();
      if (row?.kind === 'view') { restoreFocusOnRebuild = true; props.onToggleExpand(row.viewId); }
      else activate(row);
      return;
    }
    if (e.key === 'ArrowRight') {
      if (row?.kind === 'view') {
        if (props.expanded() !== row.viewId) {
          e.preventDefault();
          restoreFocusOnRebuild = true;
          props.onToggleExpand(row.viewId);
          return;
        }
        // Already expanded, move to first child widget if any.
        const next = list.findIndex((r, i) => i > curr && r.kind === 'widget' && r.viewId === row.viewId);
        if (next >= 0) { e.preventDefault(); focusRow(next); return; }
      }
      // On a container row, descend into its first nested child.
      if (row?.kind === 'widget' && row.isContainer) {
        const widgetId = row.target.kind === 'widget' ? row.target.widgetId : '';
        const next = list.findIndex(r => r.kind === 'widget' && r.parentId === widgetId);
        if (next >= 0) { e.preventDefault(); focusRow(next); return; }
      }
      return;
    }
    if (e.key === 'ArrowLeft') {
      if (row?.kind === 'view' && props.expanded() === row.viewId) {
        e.preventDefault();
        restoreFocusOnRebuild = true;
        props.onToggleExpand(row.viewId);
        return;
      }
      if (row?.kind === 'widget') {
        // One level up: a nested child goes to its container, a view's widget to
        // its view row. A TRAY widget has no parent row at all — its group is
        // flat (Tray.SPEC [S-08]) — so there is nowhere to go and the key falls
        // through to the plain roving move.
        const parent = row.parentId
          ? list.findIndex(r => r.kind === 'widget' && r.target.kind === 'widget' && r.target.widgetId === row.parentId)
          : row.viewId === TRAY_COLLECTION_ID
            ? -1
            : list.findIndex(r => r.kind === 'view' && r.viewId === row.viewId);
        if (parent >= 0) { e.preventDefault(); focusRow(parent); return; }
      }
      return;
    }

    const next = nextIndexLinear(e.key, curr, list.length);
    if (next !== null) {
      e.preventDefault();
      focusRow(next);
      return;
    }

    if (isTypeAheadKey(e)) {
      const q = typeAhead.push(e.key);
      const hit = findByPrefix(
        list,
        (r) => r.label,
        q,
        Math.max(0, curr + (q.length === 1 ? 1 : 0)),
      );
      if (hit >= 0) { e.preventDefault(); focusRow(hit); }
    }
  }

  onMount(() => {
    syncFocusToActive();
    props.registerHandle?.({
      focusActive() {
        const i = activeRowIdx();
        const target = i >= 0 ? i : 0;
        focusRow(target);
      },
      focusFirst() { focusRow(0); },
    });
  });

  // <For> recreates every button when the row list rebuilds, so the browser
  // drops DOM focus during the transition. After any keyboard-triggered
  // expand/collapse we re-apply focus on the next microtask; by then Solid
  // has finished its render pass and the ref callbacks have populated `refs`.
  createEffect(() => {
    rows(); // track
    if (!restoreFocusOnRebuild) return;
    restoreFocusOnRebuild = false;
    queueMicrotask(() => {
      const i = Math.min(focusIdx(), refs.length - 1);
      refs[i]?.focus();
    });
  });

  // Keep the page the panel is showing visible in the tree. The window can open
  // straight onto a deep target (a widget in the last view, a display), another
  // window can navigate us via `config-navigate`, and Alt+←/→ can jump anywhere
  // in the history -- in all of those the sidebar stayed scrolled wherever it
  // happened to be, so the active row could sit off-screen.
  //
  // Deliberately tracks the SELECTION and the active row's index, not the whole
  // row list: expanding a view above the active row shifts it (possibly past the
  // fold) so we follow it, but expanding one below leaves it where it is and
  // must not yank the list away from what the user just clicked.
  // `block: 'nearest'` scrolls the minimum needed, so a row already fully on
  // screen never moves at all.
  let revealQueued = false;
  createEffect(() => {
    props.selection();
    activeRowIdx();
    if (revealQueued) return;
    revealQueued = true;
    // Resolve the row inside the microtask rather than here. A single navigation
    // lands as two separate store writes (expand the owning view, then set the
    // selection), so this effect runs once on the intermediate state where
    // `activeRowIdx` still points at the row we are leaving -- acting on it
    // scrolled to the wrong row first and only then to the right one. Deferring
    // also lets `<For>` finish repopulating `refs`; a stale entry left over from
    // a longer previous list is no longer in the document, so skip if detached.
    queueMicrotask(() => {
      revealQueued = false;
      const el = refs[activeRowIdx()];
      if (el?.isConnected) el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
  });

  return (
    <div class="config-tree">
      <nav class="config-tree-list cfg-scrollbar" role="tree" aria-label={t('app.settingsNavAria')} onKeyDown={handleKeyDown}>
      <For each={rows()}>
        {(row, i) => {
          const tabIdx = () => focusIdx() === i() ? 0 : -1;
          const activeClass = () => 'target' in row && isActive(row.target);
          if (row.kind === 'app') {
            const Icon = row.icon;
            const isFirstApp = rows().findIndex(r => r.kind === 'app') === i();
            return (
              <>
                <Show when={isFirstApp}><div class="config-tree-group-label">{t('app.appSettings')}</div></Show>
                <button
                  ref={(el) => { refs[i()] = el; }}
                  class="settings-nav-item config-tree-item config-tree-child kbd-focusable"
                  classList={{ active: activeClass() }}
                  role="treeitem"
                  tabIndex={tabIdx()}
                  onClick={() => { setFocusIdx(i()); props.onSelect(row.target!); }}
                >
                  <Icon size={15} strokeWidth={1.5} />
                  <span class="config-tree-label">{row.label}</span>
                </button>
              </>
            );
          }
          if (row.kind === 'display') {
            // Group header before the first display row.
            const isFirstDisplay = rows().findIndex(r => r.kind === 'display') === i();
            return (
              <>
                <Show when={isFirstDisplay}><div class="config-tree-group-label">{t('app.displays')}</div></Show>
                <button
                  ref={(el) => { refs[i()] = el; }}
                  class="settings-nav-item config-tree-item config-tree-child kbd-focusable"
                  classList={{ active: activeClass() }}
                  role="treeitem"
                  tabIndex={tabIdx()}
                  onClick={() => { setFocusIdx(i()); props.onSelect(row.target!); }}
                >
                  <Monitor size={15} strokeWidth={1.5} />
                  <span class="config-tree-label">{row.label}</span>
                </button>
              </>
            );
          }
          if (row.kind === 'view') {
            const view = viewStore.list.find(v => v.id === row.viewId)!;
            const isFirstView = rows().findIndex(r => r.kind === 'view') === i();
            return (
              <>
                <Show when={isFirstView}><div class="config-tree-group-label">{t('app.viewsGroup')}</div></Show>
                <button
                  ref={(el) => { refs[i()] = el; }}
                  class="settings-nav-item config-tree-item config-tree-view kbd-focusable"
                  classList={{ active: activeClass() }}
                  role="treeitem"
                  aria-level={1}
                  tabIndex={tabIdx()}
                  aria-expanded={props.expanded() === row.viewId}
                  onClick={() => { setFocusIdx(i()); activate(row); }}
                  onContextMenu={(e) => handleViewCtx(e, row.viewId)}
                >
                  <span
                    class="config-tree-chevron"
                    onClick={(e) => { e.stopPropagation(); setFocusIdx(i()); restoreFocusOnRebuild = true; props.onToggleExpand(row.viewId); }}
                  >
                    <Show when={props.expanded() === row.viewId} fallback={<ChevronRight size={14} strokeWidth={1.5} />}>
                      <ChevronDown size={14} strokeWidth={1.5} />
                    </Show>
                  </span>
                  {view?.icon && VIEW_ICON_MAP[view.icon]
                    ? VIEW_ICON_MAP[view.icon](15)
                    : <span class="config-tree-view-letters">{viewInitials(view?.name ?? row.label)}</span>}
                  <span class="config-tree-label">{row.label}</span>
                </button>
              </>
            );
          }
          if (row.kind === 'widget') {
            const store = getViewLayoutStore(row.viewId);
            const widgetId = row.target.kind === 'widget' ? row.target.widgetId : undefined;
            const widget = store.layout.widgets.find(w => widgetId === w.id);
            const def = widget ? registry[widget.type] : null;
            // A TRAY widget sits directly in its group, not under an expandable
            // parent (Tray.SPEC [S-08]), so it takes the flat child indent that
            // the app/display rows use — not the nested one.
            const isTray = row.viewId === TRAY_COLLECTION_ID;
            const isFirstTray = isTray
              && rows().findIndex(r => r.kind === 'widget' && r.viewId === TRAY_COLLECTION_ID) === i();
            // [S-14] Show the icon the tray actually shows, not the widget
            // type's, so the row and the notification area agree.
            const trayIcon = () => (isTray && widget ? VIEW_ICON_MAP[trayIconKeyOf(widget)] : undefined);
            return (
              <>
                <Show when={isFirstTray}><div class="config-tree-group-label">{t('tray.group')}</div></Show>
                <button
                  ref={(el) => { refs[i()] = el; }}
                  class="settings-nav-item config-tree-item config-tree-child config-tree-widget kbd-focusable"
                  classList={{
                    active: activeClass(),
                    'config-tree-nested': !isTray,
                    'config-tree-nested-child': row.depth > 1,
                  }}
                  role="treeitem"
                  aria-level={row.depth + 1}
                  tabIndex={tabIdx()}
                  onClick={() => { setFocusIdx(i()); props.onSelect(row.target!); }}
                  onContextMenu={(e) => widgetId && handleWidgetCtx(e, row.viewId, widgetId, row.label, isContainerType(widget?.type ?? ''))}
                >
                  <span class="config-tree-widget-icon">
                    {trayIcon() ? trayIcon()!(15) : def?.icon ? def.icon() : null}
                  </span>
                  <span class="config-tree-label">{row.label}</span>
                </button>
              </>
            );
          }
          if (row.kind === 'addTray') {
            // Closes the tray group. When the tray is empty this row is the
            // whole group, so it carries the header itself (Tray.SPEC [S-08]).
            const trayIsEmpty = !rows().some(r => r.kind === 'widget' && r.viewId === TRAY_COLLECTION_ID);
            return (
              <>
                <Show when={trayIsEmpty}><div class="config-tree-group-label">{t('tray.group')}</div></Show>
                <button
                  ref={(el) => { refs[i()] = el; }}
                  class="settings-nav-item config-tree-item config-tree-child config-tree-add-view kbd-focusable"
                  role="treeitem"
                  tabIndex={tabIdx()}
                  onClick={() => { setFocusIdx(i()); activate(row); }}
                >
                  <Plus size={15} strokeWidth={1.5} />
                  <span class="config-tree-label">{row.label}</span>
                </button>
              </>
            );
          }
          // addView
          return (
            <button
              ref={(el) => { refs[i()] = el; }}
              class="settings-nav-item config-tree-item config-tree-view config-tree-add-view kbd-focusable"
              role="treeitem"
              tabIndex={tabIdx()}
              onClick={() => { setFocusIdx(i()); activate(row); }}
            >
              <Plus size={15} strokeWidth={1.5} />
              <span class="config-tree-label">{row.label}</span>
            </button>
          );
        }}
      </For>
      </nav>
      {/* Add Widget + edit-mode toggle pinned to the sidebar bottom, always
          reachable, never scroll away with the tree. They live outside the roving
          <nav> so the tree's arrow-key handler doesn't steal focus; each is a
          plain Tab stop. */}
      <div class="config-tree-footer">
        <button
          class="settings-nav-item config-tree-item config-tree-add-widget kbd-focusable"
          onClick={() => props.onSelect({ kind: 'add' })}
        >
          <Plus size={15} strokeWidth={1.5} />
          <span class="config-tree-label">{t('app.addWidget')}</span>
        </button>
        <button
          class="settings-nav-item config-tree-item config-tree-edit-mode kbd-focusable"
          role="switch"
          aria-checked={editMode()}
          onClick={toggleEditMode}
        >
          <Pencil size={15} strokeWidth={1.5} />
          <span class="config-tree-label">{t('app.editWidgets')}</span>
          <span class={`design-switch${editMode() ? ' on' : ''}`} aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}

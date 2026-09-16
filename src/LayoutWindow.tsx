// ---------------------------------------------------------------------------
// App-layout editor window (mounted from main.tsx on `?layout=1`)
//
// Pops the visual pane-tree editor for a Shortcut widget's `layout` item out
// into its own OS window. Reads + writes the item's `layout` tree through the
// shared per-view layout store (localStorage + storage events keep the widget
// window and config window in sync). Assignment of an app to a pane is handed
// off to a separate picker window, which broadcasts the choice back via the
// `layout-assign` transient message.
// ---------------------------------------------------------------------------

import { createSignal, createMemo, onMount, onCleanup, Show, type JSX } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { listen } from '@tauri-apps/api/event';
import { X } from 'lucide-solid';

import { getViewLayoutStore, updateWidgetConfig } from './layout.ts';
import { globalDesign } from './design.ts';
import LayoutEditor from '../widgets/others/shortcut/LayoutEditor.tsx';
import {
  newLeaf, validateNode,
  type LayoutNode,
} from '../widgets/others/shortcut/layout.ts';
import './App.css';
import './styles/config-base.css';
import './LayoutWindow.css';
import { t } from './i18n.ts';

interface ShortcutItem { id: string; path: string; label?: string; layout?: LayoutNode }

function readParams() {
  const p = new URLSearchParams(window.location.search);
  return {
    widgetId: p.get('widget') ?? '',
    viewId: p.get('view') ?? '',
    itemId: p.get('item') ?? '',
  };
}

function closeWindow(): void {
  getCurrentWindow().close().catch(() => {});
}

export default function LayoutWindow(): JSX.Element {
  // Touch globalDesign so design.ts applies the theme palette to :root.
  void globalDesign.theme;

  const init = readParams();
  const [widgetId, setWidgetId] = createSignal(init.widgetId);
  const [viewId, setViewId] = createSignal(init.viewId);
  const [itemId, setItemId] = createSignal(init.itemId);

  // The store must exist in this window so updateWidgetConfig can route writes
  // to it and our reads stay reactive to cross-window storage events.
  const store = createMemo(() => getViewLayoutStore(viewId()));

  const widget = createMemo(() => store().layout.widgets.find(w => w.id === widgetId()));
  const items = createMemo<ShortcutItem[]>(() => (widget()?.config?.items ?? []) as ShortcutItem[]);
  const item = createMemo<ShortcutItem | undefined>(() => items().find(i => i.id === itemId()));
  const node = createMemo<LayoutNode>(() => validateNode(item()?.layout ?? newLeaf()));

  function writeItems(next: ShortcutItem[]) {
    const w = widget();
    if (!w) return;
    updateWidgetConfig(w.id, { ...(w.config ?? {}), items: next });
  }

  function setNode(nextNode: LayoutNode) {
    writeItems(items().map(i => i.id === itemId() ? { ...i, layout: nextNode } : i));
  }

  function setName(name: string) {
    const label = name.trim();
    writeItems(items().map(i => i.id === itemId() ? { ...i, label: label || undefined } : i));
  }

  onMount(() => {
    // Re-invoking open_layout_window focuses this window and emits navigate
    // with a (possibly different) target item.
    const unlistenNav = listen<{ widget: string; view: string; item: string }>('layout-navigate', (e) => {
      const p = e.payload;
      if (!p) return;
      setViewId(p.view);
      setWidgetId(p.widget);
      setItemId(p.item);
    });

    onCleanup(() => {
      unlistenNav.then(fn => fn()).catch(() => {});
    });
  });

  return (
    <div class="layout-window">
      <header class="layout-head">
        <input
          class="layout-name-input text-input"
          type="text"
          placeholder={t('view.layoutNamePlaceholder')}
          value={item()?.label ?? ''}
          onInput={(e) => setName(e.currentTarget.value)}
        />
        <button class="layout-close" title={t('common.close')} onClick={closeWindow}>
          <X size={15} strokeWidth={1.5} />
        </button>
      </header>

      <div class="layout-body cfg">
        <Show
          when={item()}
          fallback={<div class="layout-empty">{t('view.layoutUnavailable')}</div>}
        >
          <p class="layout-lede">
            Hover a pane to split or remove it. Drag dividers to resize. Pick a source inside each
            pane. It launches on whichever display you open the shortcut from.
          </p>
          <LayoutEditor node={node()} onChange={setNode} />
        </Show>
      </div>
    </div>
  );
}

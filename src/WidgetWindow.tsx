// ---------------------------------------------------------------------------
// Widget pop-out window (mounted from main.tsx on `?widgetwindow=1`)
//
// Pops a single widget out into its own decorated OS window. Generic: renders
// `registry[type].component` for the target widget, bound to that widget's view
// layout store, so its config reads/writes route to the same store as the grid
// tile. localStorage + storage events keep the window and the grid widget in
// sync. Singleton: re-invoking `open_widget_window` focuses this window and
// emits `widget-window-navigate` so it retargets a (possibly different) widget
// instead of spawning a duplicate.
//
// Opt-in per widget type via the registry `popOut` flag; the "Open in window"
// action lives in the widget chrome context menu (see WidgetGrid).
// ---------------------------------------------------------------------------

import { createSignal, createMemo, onMount, onCleanup, ErrorBoundary, Show, type JSX } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { listen } from '@tauri-apps/api/event';
import { TriangleAlert } from 'lucide-solid';

import { getViewLayoutStore } from './layout.ts';
import { LayoutContext } from './viewContext.tsx';
import { globalDesign } from './design.ts';
import { invoke } from './ipc.ts';
import { logger } from './debugLog.ts';
import { t } from './i18n.ts';
import registry from './widgets/registry.tsx';
import ContextMenu from './ContextMenu.tsx';
import './App.css';
import './WidgetWindow.css';

function readParams() {
  const p = new URLSearchParams(window.location.search);
  return {
    widgetId: p.get('widget') ?? '',
    viewId: p.get('view') ?? '',
  };
}

export default function WidgetWindow(): JSX.Element {
  // Touch globalDesign so design.ts applies the theme palette to :root.
  void globalDesign.theme;

  const init = readParams();
  const [widgetId, setWidgetId] = createSignal(init.widgetId);
  const [viewId, setViewId] = createSignal(init.viewId);

  // The store must exist in this window so the widget's writes route to it and
  // our reads stay reactive to cross-window storage events. getViewLayoutStore
  // is memoized per viewId, so store() is stable while the view is unchanged.
  const store = createMemo(() => getViewLayoutStore(viewId()));
  const widget = createMemo(() => store().layout.widgets.find(w => w.id === widgetId()));

  // Open the config window for this widget (empty-state "Configure" buttons in
  // networked widgets call _onConfigure). Routes back through the same command
  // the grid uses.
  function configure() {
    invoke('open_config_window', {
      parentLabel: getCurrentWindow().label,
      target: `widget:${widgetId()}`,
    }).catch(e => logger.error('widget-window', `open_config_window failed: ${e}`));
  }

  onMount(() => {
    // Re-invoking open_widget_window focuses this window and emits navigate with
    // a (possibly different) target widget.
    const unlistenNav = listen<{ widget: string; view: string }>('widget-window-navigate', (e) => {
      const p = e.payload;
      if (!p) return;
      setViewId(p.view);
      setWidgetId(p.widget);
    });
    onCleanup(() => { unlistenNav.then(fn => fn()).catch(() => {}); });
  });

  return (
    <div class="widget-window">
      {/* Keyed on the store so a navigate to a different VIEW rebuilds the
          context provider with the right store; config edits (same store) never
          remount the widget, so an editor keeps focus + caret. */}
      <Show when={store()} keyed>
        {(s) => (
          <LayoutContext.Provider value={s}>
            <Show
              when={widget()}
              fallback={<div class="widget-window-empty">{t('app.widgetWindowUnavailable')}</div>}
              keyed
            >
              {(w) => {
                const def = registry[w.type];
                if (!def) return <div class="widget-window-empty">{t('app.widgetWindowUnknownType')}</div>;
                return (
                  <div class={`widget-window-body${def.noChrome ? ' no-chrome' : ''}`}>
                    <ErrorBoundary
                      fallback={(err) => (
                        <div class="widget-window-empty">
                          <TriangleAlert size={16} strokeWidth={1.5} />
                          <span>{String(err)}</span>
                        </div>
                      )}
                    >
                      <Dynamic
                        component={def.component}
                        {...(w.config ?? {})}
                        // The OS title bar of this window already carries the
                        // widget's icon and label (open_widget_window sets it),
                        // so rendering the widget header too would show the
                        // same title twice. The window IS the header here.
                        headerMode="none"
                        label={w.label ?? ''}
                        _widgetId={w.id}
                        _editMode={false}
                        _onConfigure={configure}
                        _onConfigureWidget={configure}
                      />
                    </ErrorBoundary>
                  </div>
                );
              }}
            </Show>
          </LayoutContext.Provider>
        )}
      </Show>
      {/* Item-level context menus (showCtxMenu) need a renderer in THIS window,
          otherwise right-click in a popped-out widget updates the store but
          paints nothing. Mirrors App.tsx / ConfigWindow.tsx. */}
      <ContextMenu />
    </div>
  );
}

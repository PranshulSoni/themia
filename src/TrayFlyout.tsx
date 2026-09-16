// ---------------------------------------------------------------------------
// Tray flyout window (mounted from main.tsx on `?trayflyout=1`)
//
// One borderless panel hosting one tray widget ([S-05]). Singleton: Rust reuses
// this window for every tray icon, so N icons cost one WebView.
//
// The target comes from the URL and ONLY from the URL — Rust retargets the
// window by reloading it with another `widget=`. There is deliberately no
// "switch target in place" event: that gave the panel a second source of truth
// that could disagree with the tray, and did (a hidden webview's throttled
// renderer kept showing the previous widget).
//
// The widget is bound to the tray layout store, exactly as WidgetWindow binds a
// popped-out widget to its view's store, so edits made here and edits made in
// Settings are the same edits.
// ---------------------------------------------------------------------------

import { createMemo, onMount, onCleanup, ErrorBoundary, Show, type JSX } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { TriangleAlert } from 'lucide-solid';

import { trayStore } from './tray.ts';
import { LayoutContext } from './viewContext.tsx';
import { globalDesign } from './design.ts';
import { invoke } from './ipc.ts';
import { logger } from './debugLog.ts';
import { t } from './i18n.ts';
import registry, { resolveHeaderMode } from './widgets/registry.tsx';
import ContextMenu from './ContextMenu.tsx';
import './App.css';
import './TrayFlyout.css';

function targetWidgetId(): string {
  return new URLSearchParams(window.location.search).get('widget') ?? '';
}

export default function TrayFlyout(): JSX.Element {
  // Touch globalDesign so design.ts applies the theme palette to :root.
  void globalDesign.theme;

  // Not a signal: the target is fixed for the lifetime of this document, and
  // Rust reloads the document to change it.
  const widgetId = targetWidgetId();

  const store = trayStore();
  const widget = createMemo(() => store.layout.widgets.find(w => w.id === widgetId));

  function close() {
    invoke('tray_flyout_hide').catch(e =>
      logger.error('tray', `tray_flyout_hide failed: ${String(e)}`)
    );
  }

  // Open this widget's page in Settings. The flyout is dismissed on the way, so
  // the settings window is not left underneath a panel that is about to blur
  // itself away anyway.
  function configure() {
    invoke('open_config_window', {
      parentLabel: getCurrentWindow().label,
      target: `widget:${widgetId}`,
    }).catch(e => logger.error('tray', `open_config_window failed: ${String(e)}`));
    close();
  }

  onMount(() => {
    // [S-12] Tell Rust this document is up and which widget it mounted. The
    // window is held HIDDEN until this lands, so switching icons never shows the
    // previous widget while the new page loads. It must be sent even when the
    // widget id resolves to nothing — the panel still has to appear, carrying
    // the "no longer available" fallback, rather than never opening at all.
    invoke('tray_flyout_ready', { widget: widgetId }).catch(e =>
      logger.error('tray', `tray_flyout_ready failed: ${String(e)}`)
    );

    // Esc closes ([S-06]). Deliberately a plain window listener rather than the
    // shared escape stack: this window hosts exactly one widget and has no
    // layered surfaces to unwind, and a widget's own editor may still stop the
    // event before it reaches us when it has something of its own to cancel.
    function onKeyDown(e: KeyboardEvent) {
      if (e.key !== 'Escape' || e.defaultPrevented) return;
      close();
    }
    window.addEventListener('keydown', onKeyDown);
    onCleanup(() => window.removeEventListener('keydown', onKeyDown));
  });

  return (
    <div class="tray-flyout">
      <LayoutContext.Provider value={store}>
        <Show
          when={widget()}
          fallback={<div class="tray-flyout-empty">{t('tray.widgetUnavailable')}</div>}
          keyed
        >
          {(w) => {
            const def = registry[w.type];
            if (!def) return <div class="tray-flyout-empty">{t('app.widgetWindowUnknownType')}</div>;
            return (
              <div class={`tray-flyout-body${def.noChrome ? ' no-chrome' : ''}`}>
                <ErrorBoundary
                  fallback={(err) => (
                    <div class="tray-flyout-empty">
                      <TriangleAlert size={16} strokeWidth={1.5} />
                      <span>{String(err)}</span>
                    </div>
                  )}
                >
                  <Dynamic
                    component={def.component}
                    {...(w.config ?? {})}
                    // Unlike the pop-out window, a flyout has no OS title bar,
                    // so the widget's own header is the only thing naming it —
                    // it keeps whatever mode the instance is configured for.
                    headerMode={resolveHeaderMode(w.config, def)}
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
      {/* Item-level context menus need a renderer in THIS window, or a
          right-click inside the flyout updates the store and paints nothing.
          Mirrors App.tsx / WidgetWindow.tsx. */}
      <ContextMenu />
    </div>
  );
}

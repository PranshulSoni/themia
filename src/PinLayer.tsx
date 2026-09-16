/**
 * Root of a monitor's always-on-top PIN overlay ([S-45]), the `monitor-{i}-pin`
 * window.
 *
 * It is the same full-work-area transparent overlay as `monitor-{i}` and shows
 * the same view at the same geometry — it just sits in the `WS_EX_TOPMOST`
 * band, and renders ONLY the widgets whose config says `alwaysOnTop`. The base
 * overlay renders everything else, so exactly one window paints each widget
 * (`layerShowsWidget`).
 *
 * Deliberately NOT App.tsx with a flag: this window has no view bar, no edit
 * chrome, no add-widget flow, no update check, no tray sync, no analytics and
 * no startup animation — running App's onMount here would double every one of
 * those per monitor. What the two windows genuinely share (the hit-mask push
 * and the Rust bridges) lives in overlayBridge.ts and is imported by both.
 *
 * It stands down completely while edit mode is on: a topmost window that
 * cleared its region for edit mode ([S-17]) would be a full-screen input trap
 * over every other application, so the pinned widgets go back to the base
 * overlay and are edited there.
 */

import { createEffect, createMemo, onCleanup, onMount, Show, type JSX } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';

import ContextMenu from './ContextMenu.tsx';
import Toast from './components/Toast.tsx';
import ViewRoot from './ViewRoot.tsx';
import WidgetGrid from './WidgetGrid.tsx';
import { editMode, setEditMode } from './editMode.ts';
import { encodeTarget, type ConfigTarget } from './config/target.ts';
import { gameModeSuspended, initGameMode } from './gameMode.ts';
import { globalDesign } from './design.ts';
import { initEscapeStack, onEscapeStackActive } from './input/escapeStack.ts';
import { initFocusStore, inputFocused, hoverFocusable } from './input/focusStore.ts';
import { initNativeDragListener } from './nativeDragDrop.ts';
import { installOverlayBridge } from './overlayBridge.ts';
import { invoke } from './ipc.ts';
import { logger } from './debugLog.ts';
import { markBootSuccess } from './safeMode.ts';
import { useInteractive } from './input/interactivityStore.ts';
import { getMonitorDisabled, initForMonitor, viewStore } from './views.ts';
import './App.css';

export default function PinLayer(): JSX.Element {
  const winLabel = getCurrentWindow().label;

  // Touch globalDesign so design.ts applies the theme palette to :root here
  // too; the pin overlay paints the same tiles as the base one.
  void globalDesign.theme;

  initFocusStore();
  initEscapeStack();
  initGameMode();

  // Same interactivity reasons the base overlay publishes, minus the ones this
  // window cannot enter (edit mode, placing). Without them a text field in a
  // pinned widget would never get real Win32 focus, because the window keeps
  // WS_EX_NOACTIVATE until `set_interactive` strips it.
  useInteractive('input',       inputFocused);
  useInteractive('hover-input', hoverFocusable);

  installOverlayBridge({ extraDeps: () => { void editMode(); } });

  // Active view, re-keyed on demo flips exactly like the base overlay, so both
  // windows show the same view and a demo toggle remounts both subtrees.
  const activeViewKeyed = createMemo(() => {
    void globalDesign.demoMode;
    const v = viewStore.list.find(s => s.id === viewStore.activeId);
    return v ? { ...v } : undefined;
  });

  function openConfigWindow(target: ConfigTarget) {
    invoke('open_config_window', {
      parentLabel: winLabel,
      target: encodeTarget(target),
    }).catch(e => logger.error('tauri', `open_config_window failed: ${e}`));
  }

  onMount(() => {
    try { markBootSuccess(winLabel); } catch { markBootSuccess('widget'); }

    // Tell the native WH_KEYBOARD_LL hook to stop consuming Escape while a DOM
    // escape handler is active (an open context menu here), so the keystroke
    // reaches this webview and the escape stack can run.
    onEscapeStackActive((active) => {
      invoke('set_escape_wanted', { label: winLabel, wanted: active })
        .catch(e => logger.error('tauri', `set_escape_wanted failed: ${e}`));
    });

    // [MI-01] Resolve THIS window's monitor so the per-monitor view assignment
    // and widget scale match the base overlay covering the same display. Rust
    // maps a pin overlay to the same stable monitor id as its base window.
    const initMonitor = () => {
      invoke('get_monitor_id', { label: winLabel })
        .then(monitorId => { if (monitorId) return initForMonitor(monitorId); })
        .catch(e => logger.error('tauri', `get_monitor_id failed: ${e}`));
    };
    initMonitor();
    window.addEventListener('themia:display-changed', initMonitor);
    onCleanup(() => window.removeEventListener('themia:display-changed', initMonitor));

    // Native (Explorer → widget) drops land on whichever window owns the
    // pixels, and above other apps that is this one.
    initNativeDragListener();
  });

  // A pinned widget hosted here must never keep painting while the base
  // overlay is suspended for a fullscreen app / game — it would be the ONE
  // thing left on top of the game. `gameModeSuspended` gates both windows.
  createEffect(() => {
    void gameModeSuspended();
  });

  return (
    <Show when={!getMonitorDisabled() && !editMode() && !gameModeSuspended()}>
      <>
        <Show when={activeViewKeyed()} keyed>
          {(view) => (
            <ViewRoot viewId={view.id} active={true}>
              <WidgetGrid
                pinLayer
                editMode={false}
                placing={null}
                onConfigure={(widget) => openConfigWindow({ kind: 'widget', widgetId: widget.id })}
                // Editing happens on the base overlay: flipping the flag empties
                // this window and hands the widget back ([S-45]).
                onEnterEditMode={() => setEditMode(true)}
                onPlace={() => {}}
                onStartClipboardPlacing={() => {}}
                onAddToContainer={(containerId) => openConfigWindow({ kind: 'add', into: containerId })}
              />
            </ViewRoot>
          )}
        </Show>
        <ContextMenu />
        <Toast />
      </>
    </Show>
  );
}

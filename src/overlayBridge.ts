/**
 * The Rust bridge every overlay window installs: per-monitor scale, the
 * interactivity + text-input flags, and the desktop pass-through hit-mask
 * push ([S-17]).
 *
 * Lifted out of App.tsx unchanged so the always-on-top pin overlay ([S-45])
 * runs the SAME code instead of a second, drifting copy of it — the mask is a
 * paint clip, so a pin overlay with its own half of this would show clipped
 * widgets in ways the base overlay never does. One path, two callers.
 *
 * Callers differ only in which extra reactive sources move their tracked
 * elements, which they pass as `extraDeps`.
 */

import { createEffect, onCleanup } from 'solid-js';
import { getCurrentWindow } from '@tauri-apps/api/window';

import { anyGroupCollapsing } from './groupCollapse.ts';
import { bootOverlayActive } from './bootOverlay.ts';
import { ctxMenu } from './ctxMenuStore.ts';
import { globalDesign } from './design.ts';
import { gameModeSuspended } from './gameMode.ts';
import { getViewLayoutStore } from './layout.ts';
import { hiddenWidgetIds } from './widgetVisibility.ts';
import { inputFocused } from './input/focusStore.ts';
import { invoke } from './ipc.ts';
import { isInteractive } from './input/interactivityStore.ts';
import { logger } from './debugLog.ts';
import { maskBand, maskRect } from './hitMask.ts';
import { toast } from './toastStore.ts';
import {
  getMonitorDisabled, getMonitorScale, getViewBarHidden, getViewBarIconOnly,
  getViewBarPosition, getViewBarSize, viewStore,
} from './views.ts';

export interface OverlayBridgeOptions {
  /** Reactive sources owned by the caller that move / show / hide tracked
   *  elements (edit mode, placing ghost, the update badge, …). Touched inside
   *  the rect-push effect. */
  extraDeps?: () => void;
}

/** Install the per-window Rust bridges. Call once from an overlay root's
 *  component body (it registers effects and a resize listener). */
export function installOverlayBridge(opts: OverlayBridgeOptions = {}): void {
  const winLabel = getCurrentWindow().label;

  // Drive the Rust `set_interactive` bridge ONLY on real false↔true
  // transitions. `isInteractive()` is `count() > 0` over a multi-reason
  // store; adding a second reason (e.g. opening a context menu while the
  // cursor is already over a focusable widget element) bumps count 1→2
  // without changing the boolean. A naive effect would re-run on every
  // count change and fire set_interactive(false) then (true), which
  // re-clips and unclips the window region → a visible flash. Deduping on
  // the boolean keeps the bridge quiet unless interactivity actually flips.
  let lastInteractive: boolean | null = null;
  createEffect(() => {
    const now = isInteractive();
    if (now === lastInteractive) return;
    lastInteractive = now;
    invoke('set_interactive', { label: winLabel, interactive: now })
      .catch(e => logger.error('tauri', `set_interactive failed: ${e}`));
  });

  // While a text input is focused, the Rust WH_KEYBOARD_LL hook must stand
  // down: the window holds real Win32 focus, so the input owns every key
  // natively. Without this the hook ate Backspace/Enter/Escape/arrows in
  // inline inputs and swallowed the Backspaces Vietnamese IME tools inject
  // to replace composed characters ("Chào" → "Chaào"). Deduped on the
  // boolean like set_interactive above (issue #35).
  let lastTextInput: boolean | null = null;
  createEffect(() => {
    const now = inputFocused();
    if (now === lastTextInput) return;
    lastTextInput = now;
    invoke('set_text_input_focused', { label: winLabel, focused: now })
      .catch(e => logger.error('tauri', `set_text_input_focused failed: ${e}`));
  });

  // ── Per-monitor widget scale ──
  // This window covers exactly one monitor; `getMonitorScale()` resolves that
  // monitor's scale factor. Writing `--monitor-scale` to :root drives the
  // `#app.app-layer` transform (see App.css) so widgets + chrome scale
  // independently of the general uiScale.
  createEffect(() => {
    document.documentElement.style.setProperty('--monitor-scale', String(getMonitorScale()));
  });

  // ── Desktop pass-through: push interactive rects to Rust ──
  // Every surface that should keep capturing input (active-view widgets +
  // any [data-themia-interactive] chrome) reports its bounding box to the
  // Rust WM_NCHITTEST handler. Everything outside those rects falls through
  // to the Windows desktop so folder icons stay clickable and the native
  // Explorer "View / Sort / New" menu appears on empty area.
  let pendingRectsRaf: number | null = null;
  let lastSentRects = ''; // E2: last payload pushed, to skip redundant IPC
  const pushRects = () => {
    if (pendingRectsRaf != null) return;
    pendingRectsRaf = requestAnimationFrame(() => {
      pendingRectsRaf = null;
      // A group is CLOSING ([S-43]). Its tile already carries the collapsed
      // rect and its members are fading, so the shape measured now is smaller
      // than what is still being painted — and the region is a PAINT clip
      // ([S-17]), so pushing it would cut the animation off at the title bar
      // instead of playing it. Hold the shape that is on screen; the phase
      // ending re-runs the effect below, which pushes the settled one.
      if (anyGroupCollapsing()) return;
      const dpr = window.devicePixelRatio || 1;
      // `.is-hidden` widgets are excluded: a widget that paints nothing
      // (Now Playing with nothing playing, music [S-20]) must not leave an
      // invisible rectangle in the hit-mask, or it would swallow clicks on
      // desktop icons over a patch of screen the user sees as empty.
      const els = document.querySelectorAll<HTMLElement>(
        '.view-root.active .widget:not(.is-hidden), [data-themia-interactive]'
      );
      // [x, y, w, h, radius] in physical px. Radius comes from the
      // element's computed border-top-left-radius; widgets pick up the
      // global --radius (8 px), chrome elements (mode pill, view bar)
      // bring their own. Rust builds an HRGN from these via
      // CreateRoundRectRgn and OR's them together into the window's
      // SetWindowRgn shape, so empty pixels are literally not part of
      // the window.
      // Widgets carry a transform during the startup intro animation
      // (scale + translate, [S-15]). getBoundingClientRect folds that
      // transform into the measured box, so building the SetWindowRgn mask
      // from it would clip widgets to a half-animated rect (cropped headers,
      // wrong size). A widget's TRUE geometry is the inline left/top/width/
      // height rectStyle wrote (app-layer CSS px) mapped to viewport px by the
      // monitor scale (app-layer is `scale()` at origin 0,0), independent of
      // any transform, so the mask is never influenced by the animation.
      // Chrome (`[data-themia-interactive]`) has no such inline rect → gBCR.
      const scale = getMonitorScale() || 1;
      const rects: [number, number, number, number, number][] = [];
      els.forEach(el => {
        let left: number, top: number, w: number, h: number;
        // A tile may publish a SMALLER band than its own box when that is all
        // it paints ([S-13a]): a group with a see-through area contributes
        // its title bar only, so the desktop under the area keeps its clicks.
        // Same app-layer CSS px as the inline rect (and same reason: measuring
        // would fold in the intro transform), four numbers, ignored if junk.
        const band = maskBand(el.dataset.maskRect);
        if (el.classList.contains('widget') && band) {
          [left, top, w, h] = [band[0] * scale, band[1] * scale, band[2] * scale, band[3] * scale];
        } else if (el.classList.contains('widget') && el.style.width) {
          left = parseFloat(el.style.left) * scale;
          top  = parseFloat(el.style.top) * scale;
          w    = parseFloat(el.style.width) * scale;
          h    = parseFloat(el.style.height) * scale;
        } else {
          const r = el.getBoundingClientRect();
          left = r.left; top = r.top; w = r.width; h = r.height;
        }
        if (w <= 0 || h <= 0) return;
        const radiusPx = parseFloat(
          getComputedStyle(el).borderTopLeftRadius || '0',
        ) || 0;
        // Snap OUTWARD PER EDGE, never origin-floor + size-ceil: the region
        // is a paint clip as well as a hit-test shape, so an under-covering
        // rect deletes the tile's bottom/right border row (hitMask.ts).
        rects.push(maskRect(left, top, w, h, radiusPx, dpr));
      });
      // E2: skip the IPC hop (and the Rust SetWindowRgn re-apply) when the shape
      // + interactivity are byte-identical to the last push. Many effect re-runs
      // (an interactivity flip that moves nothing, a toast/ctx-menu show) produce
      // identical rects; re-shipping them thrashes SetWindowRgn for no change.
      const interactive = isInteractive();
      const payload = JSON.stringify({ rects, interactive });
      if (payload === lastSentRects) return;
      lastSentRects = payload;
      invoke('update_widget_rects', { rects, interactive })
        .catch(e => logger.error('tauri', `update_widget_rects failed: ${e}`));
    });
  };
  createEffect(() => {
    // Touch reactive sources that move/show/hide tracked elements.
    const sid = viewStore.activeId;
    void viewStore.list.length;
    void gameModeSuspended();
    // Caller-owned sources: edit mode, the placing ghost, the update badge…
    opts.extraDeps?.();
    // Interactivity (edit mode / modal / focused input / hover / placing)
    // decides whether Rust clips the region or clears it for the whole
    // window. The `interactive` flag travels with each push, so re-push
    // whenever it flips; otherwise a focus/hover transition that doesn't
    // move any widget would leave Rust's clip decision stale.
    void isInteractive();
    // When themia is disabled on this monitor the whole UI unmounts, so
    // re-push so the interactive region clears and the display goes
    // fully pass-through.
    void getMonitorDisabled();
    // Per-monitor scale resizes every widget + chrome element; their
    // bounding boxes change, so re-push the interactive region.
    void getMonitorScale();
    // Context menu: when open, its full-window backdrop (tagged
    // data-themia-interactive) joins the rect set, so the region covers
    // the whole window and the menu is fully visible. Tracking it here
    // makes the effect re-push on open AND close.
    void ctxMenu.menu;
    // Toast: when shown, its data-themia-interactive box joins the rect
    // set so the region covers it. Tracking the signal re-pushes on show
    // AND hide; otherwise the region stays stale and the toast is
    // clipped away by SetWindowRgn.
    void toast();
    // Boot overlay ([S-16]/[S-23]): while mounted its full-screen
    // data-themia-interactive root joins the rect set, so the region
    // covers the whole screen and the animation shows edge-to-edge.
    // Tracking the signal re-pushes on END too; without it the region
    // would stay full-screen after the overlay unmounts.
    void bootOverlayActive();
    // View bar position / size / icon-only / hidden all change the
    // bar's bounding rect (or mount/unmount it). Re-push so the
    // interactive region follows the bar.
    void getViewBarPosition();
    void getViewBarSize();
    void getViewBarIconOnly();
    void getViewBarHidden();
    void globalDesign.widgetGap;
    // A widget hiding or re-appearing (music [S-20]) adds/removes its rect
    // from the mask without moving anything, so the push must track the
    // hidden set too — otherwise the mask keeps the stale invisible rect.
    void hiddenWidgetIds();
    // [S-43] An area group's expand/collapse changes WHICH tiles exist (its
    // members) and how tall its own tile is, without moving a single x/y/w/h:
    // the geometry below would never notice, and the mask would keep the shape
    // from before the toggle — an expanded group's members clipped away by the
    // region, invisible until something else (a hover, an edit-mode flip)
    // happened to re-push. Track the flag, and the animation phase with it, so
    // the push lands when the toggle happens AND again when it settles.
    void anyGroupCollapsing();
    const ls = getViewLayoutStore(sid);
    for (const w of ls.layout.widgets) {
      void w.x; void w.y; void w.w; void w.h;
      // The config keys that change the MASK — deliberately not the whole
      // config object, which would re-run this on every keystroke in a note.
      // `collapsed` decides which tiles exist ([S-43]); the surface keys
      // decide whether a group masks its whole rect or only its title bar
      // ([S-13a]), and `headerSide` which edge that bar sits on.
      void w.config?.collapsed;
      void w.config?.bgMode; void w.config?.bgAlpha; void w.config?.transparentBg;
      void w.config?.headerSide;
      // [S-45] Pinning moves a widget between the base and the pin overlay, so
      // BOTH windows must re-push: one loses the tile from its mask, the other
      // gains it. Without this the base overlay keeps an invisible rect that
      // eats clicks meant for the desktop.
      void w.config?.alwaysOnTop;
    }
    pushRects();
  });
  const onResize = () => pushRects();
  window.addEventListener('resize', onResize);
  onCleanup(() => {
    window.removeEventListener('resize', onResize);
    if (pendingRectsRaf != null) cancelAnimationFrame(pendingRectsRaf);
  });
}

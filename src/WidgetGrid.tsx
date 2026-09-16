import { createEffect, createMemo, createSignal, ErrorBoundary, onCleanup, onMount } from 'solid-js';
import { For, Show } from 'solid-js';
import {
  GROUP_HEADER_PX, groupBorderMode, groupHeaderSide, groupRenderRect, isAreaGroupType,
  isContainerType, isGroupCollapsed, type GroupHeaderSide, type Widget,
} from './layout.ts';
import {
  GROUP_COLLAPSE_MS, groupCollapsePhase, resetGroupCollapse, syncGroupCollapse,
} from './groupCollapse.ts';
import { devicePxPerCssPx, gridDims, widgetBox, type FracRect } from './gridGeometry.ts';
import { groupAreaPaints } from '../widgets/others/group/groupStyle.ts';
import { globalDesign, introAnimFor, introMsFor } from './design.ts';
import type { PlacingState } from './placing.ts';
import { logger } from './debugLog.ts';
import { TriangleAlert, RotateCw } from 'lucide-solid';

interface WidgetGridProps {
  editMode: boolean;
  /** [S-45] True when this grid is the monitor's always-on-top pin overlay, so
   *  it renders the pinned widgets and the base overlay renders the rest.
   *  Absent = the base overlay. */
  pinLayer?: boolean;
  placing: PlacingState | null;
  onConfigure: (widget: Widget) => void;
  onEnterEditMode: () => void;
  /** Commit placement at fractional (x, y): the ghost top-left. */
  onPlace: (x: number, y: number) => void;
  onStartClipboardPlacing: (mode: 'copy' | 'cut', sourceId?: string) => void;
  /** Open the Add Widget page bound to a container widget ([S-34]). */
  onAddToContainer: (containerId: string) => void;
}

// Absolute-position style for a fractional rect. All the rounding lives in
// `widgetBox` ([S-04]/[S-04a]): a shared integer boundary between neighbours,
// then every rendered edge snapped onto a whole DEVICE pixel so the tile's 1px
// border can never be split across two rows and read as clipped.
function rectStyle(r: FracRect, gap: number, W: number, H: number, q: number): string {
  const b = widgetBox(r, gap, W, H, q);
  return (
    `position: absolute;` +
    `left: ${b.left}px;` +
    `top: ${b.top}px;` +
    `width: ${b.width}px;` +
    `height: ${b.height}px;`
  );
}

import { showCtxMenu, updateCtxMenuItems } from './ctxMenuStore.ts';
import { hyperspaceFromVars, hyperspaceShakeStyle } from './hyperspace.ts';
import { sheetAxisFor, sheetFromVars, sheetOffsets } from './introSheets.ts';
import HackerTile from './components/HackerTile.tsx';
import { bootOverlayActive, bootOverlayStyle, introBaseDelayMs, introStaggerMs, introAnimMode, isOverlayStyle, consumeIntroPlay, introReplayNonce } from './bootOverlay.ts';
import { getMonitorScale } from './views.ts';
import { useLayout } from './viewContext.tsx';
import registry, { resolveHeaderMode, viewActions } from './widgets/registry.tsx';
import { WidgetChromeContext, type WidgetChrome } from './widgetChrome.ts';
import { invoke } from './ipc.ts';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { Settings, X, Copy, Scissors, Pencil, ExternalLink, Plus } from 'lucide-solid';
import { copyWidget, cutWidget } from './widgetClipboard.ts';
import { createGridDragManager, type Corner, type Edge } from './input/gridDragManager.ts';
import { isWidgetHidden } from './widgetVisibility.ts';
import { effectivePinned, layerShowsWidget } from './pinLayer.ts';
import { t } from './i18n.ts';
import './WidgetGrid.css';

const corners: { key: Corner; cursor: string; rotate: string }[] = [
  { key: 'tl', cursor: 'nw-resize', rotate: '180deg' },
  { key: 'tr', cursor: 'ne-resize', rotate: '270deg' },
  { key: 'bl', cursor: 'sw-resize', rotate: '90deg'  },
  { key: 'br', cursor: 'se-resize', rotate: '0deg'   },
];

// Side handles ([S-31]): each drags its own edge only, so the opposite edge and
// the other axis stay put. `rotate` turns the grip line to lie along the edge.
const edges: { key: Edge; cursor: string; rotate: string }[] = [
  { key: 't', cursor: 'ns-resize', rotate: '0deg'  },
  { key: 'b', cursor: 'ns-resize', rotate: '0deg'  },
  { key: 'l', cursor: 'ew-resize', rotate: '90deg' },
  { key: 'r', cursor: 'ew-resize', rotate: '90deg' },
];

// Fallback shown when a widget throws during render (1.17). Keeps the rest of
// the grid alive: one bad widget degrades to a single dead tile instead of
// taking down every widget on the view. Logs once (onMount) so the crash is
// visible in Settings > Logs, and offers a retry for transient failures.
function WidgetErrorFallback(props: { label: string; widgetId: string; error: unknown; onRetry: () => void }) {
  onMount(() => {
    const msg = props.error instanceof Error ? (props.error.stack || props.error.message) : String(props.error);
    logger.error('widget', `"${props.label}" (${props.widgetId}) crashed during render: ${msg}`);
  });
  return (
    <div class="widget-error" role="alert">
      <TriangleAlert size={18} strokeWidth={1.5} class="widget-error-icon" />
      <div class="widget-error-text">
        <span class="widget-error-title">{props.label}</span>
        <span class="widget-error-sub">{t('common.failedToRender')}</span>
      </div>
      <button
        class="widget-error-retry"
        title={t('common.retry')}
        onMouseDown={(e) => e.stopPropagation()}
        onClick={(e) => { e.stopPropagation(); props.onRetry(); }}
      ><RotateCw size={13} strokeWidth={1.5} /></button>
    </div>
  );
}

export default function WidgetGrid(props: WidgetGridProps) {
  let gridEl: HTMLDivElement | undefined;
  const layoutStore = useLayout();
  const layout = layoutStore.layout;

  // Intro plays are armed one-shots ([S-27]): boot, a real view transition and
  // the boot-time monitor-init switch arm one; this mount consumes it. A grid
  // that mounts with nothing armed (view-list edit, demo flip, game-mode
  // resume…) renders with NO intro animations. Rows created AFTER the initial
  // render (placing/pasting/sync-added widgets) never animate either;
  // `gridSettled` flips once the mount-time rows exist.
  const playIntroOnMount = consumeIntroPlay();
  let gridSettled = false;
  onMount(() => { gridSettled = true; });

  const mgr = createGridDragManager({
    getGridEl: () => gridEl,
    editMode:  () => props.editMode,
    placing:   () => (props.placing ? { size: props.placing.size } : null),
    onPlace:   (x, y) => props.onPlace(x, y),
    layoutStore,
  });
  const { drag, resize, preview, remoteGhost, dropTarget, startDrag, startResize } = mgr;

  // Start the expand / collapse animation for any group whose `collapsed` flag
  // just flipped ([S-43]). The flag itself is persisted config and syncs across
  // windows; the PHASE is this window's own transient, so each monitor animates
  // its own copy. Cleared on unmount so a view switch mid-flight can't leave a
  // phase (and with it the held hit-mask) standing.
  createEffect(() => {
    syncGroupCollapse(
      layout.widgets
        .filter(w => isAreaGroupType(w.type))
        .map(w => ({ id: w.id, collapsed: isGroupCollapsed(w) })),
    );
  });
  onCleanup(resetGroupCollapse);

  // What the GRID lays out. Stack/tabs children are rendered by their container,
  // never here ([S-19]); an AREA group's members are rendered here, at their own
  // desktop geometry, and the group only decides whether they appear at all
  // ([S-36]/[S-37]) — collapsed, they are not in the DOM, so they also drop out
  // of the window hit-mask ([S-17]) and clicks land on the desktop again.
  //
  // A group that is still CLOSING keeps them ([S-43]): they fade out with the
  // area over `GROUP_COLLAPSE_MS` and unmount when the phase ends, instead of
  // blinking away a frame before the area they stand on.
  //
  // Area groups are emitted FIRST so their panel paints behind the widgets
  // standing on it, whatever order the widgets array holds them in.
  const topLevelWidgets = createMemo(() => {
    const byId = new Map(layout.widgets.map(w => [w.id, w]));
    const groups: Widget[] = [];
    const rest: Widget[] = [];
    // [S-45] Split by layer first: a pinned widget is painted by the monitor's
    // topmost pin overlay and by nothing else, so the two windows never draw
    // the same tile. A group's members follow the group (`effectivePinned`),
    // otherwise the panel and the widgets standing on it would land on
    // different windows.
    const onThisLayer = (w: Widget) =>
      layerShowsWidget(effectivePinned(w, byId), props.pinLayer === true, props.editMode);
    for (const w of layout.widgets) {
      if (!onThisLayer(w)) continue;
      if (w.parentId === undefined) {
        (isAreaGroupType(w.type) ? groups : rest).push(w);
        continue;
      }
      const parent = byId.get(w.parentId);
      if (!parent || !isAreaGroupType(parent.type)) continue;
      if (!isGroupCollapsed(parent) || groupCollapsePhase(parent.id) === 'out') rest.push(w);
    }
    return [...groups, ...rest];
  });

  // Snap-grid granularity for the cell overlay, recomputed on window resize so
  // the lines track the live screen ([S-06]).
  const [screenTick, setScreenTick] = createSignal(0);
  onMount(() => {
    const onResize = () => setScreenTick(n => n + 1);
    window.addEventListener('resize', onResize);
    // A dpr change (the display's scaling setting edited under a running app)
    // moves the device-pixel grid every rect is snapped to ([S-04a]) without
    // necessarily resizing the window, so watch it directly rather than hoping
    // a resize rides along. The query matches only the CURRENT dpr, so it fires
    // exactly when dpr leaves that value; re-arm against the new one.
    let mq: MediaQueryList | null = null;
    const onDpr = () => { setScreenTick(n => n + 1); arm(); };
    const arm = () => {
      mq?.removeEventListener('change', onDpr);
      mq = window.matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`);
      mq.addEventListener('change', onDpr);
    };
    arm();
    onCleanup(() => {
      window.removeEventListener('resize', onResize);
      mq?.removeEventListener('change', onDpr);
    });
  });
  const cells = createMemo(() => { screenTick(); return gridDims(); });
  // Container px size for pixel-perfect rect rounding; tracks window resize.
  //
  // Widgets render INSIDE `#app.app-layer`, which CSS sizes as `100vw/scale`
  // and then `transform: scale(scale)` (App.css). So the rects must be laid out
  // in that container's own coordinate space (full viewport ÷ monitor scale),
  // not the raw viewport. This keeps each widget's on-screen position and
  // footprint stable across scale changes (`frac · innerWidth/scale · scale =
  // frac · innerWidth`), while the transform magnifies only the content inside
  // (text, icons, padding). Using the raw viewport here double-counts the scale
  // and drifts widgets toward the bottom-right as they grow.
  const screen = createMemo(() => {
    screenTick();
    const s = getMonitorScale() || 1;
    return { w: window.innerWidth / s, h: window.innerHeight / s };
  });
  // Device pixels per app-layer CSS px ([S-04a]): what every rendered edge is
  // snapped to, so a tile's 1px border is always a whole device band.
  const deviceQ = createMemo(() => {
    screenTick();
    return devicePxPerCssPx(getMonitorScale() || 1);
  });
  // The edit-mode cell overlay is shown only when the snap aid is on ([S-06a]).
  const showGridLines = () => props.editMode && globalDesign.gridSnap;

  // The active sheet intro's axis + its two offsets ([S-28]); null for every
  // other style. Derived from the WHOLE top-level set, not from one widget,
  // because that is what makes each sheet rigid: one shared distance keeps the
  // group's internal spacing fixed for the entire slide. Cheap (a pass over the
  // rects), and it re-derives with the layout and the screen so a resize
  // retimes nothing but the travel.
  const sheet = createMemo(() => {
    const axis = sheetAxisFor(introAnimFor(introAnimMode()));
    if (!axis) return null;
    const extent = axis === 'x' ? screen().w : screen().h;
    return { axis, offsets: sheetOffsets(topLevelWidgets(), axis, extent) };
  });

  // The header edge of the group the CURRENT ghost belongs to, or null when the
  // ghost is not a group's ([S-40]). Covers all three ghost sources: dragging an
  // existing group, resizing one, and placing a new one from the picker.
  const ghostHeaderSide = createMemo<GroupHeaderSide | null>(() => {
    const p = props.placing;
    if (p && !p.fromClipboard) {
      return isAreaGroupType(p.type)
        ? groupHeaderSide({ config: p.defaultConfig })
        : null;
    }
    const id = drag()?.id ?? resize()?.id;
    if (!id) return null;
    const w = layout.widgets.find(x => x.id === id);
    return w && isAreaGroupType(w.type) ? groupHeaderSide(w) : null;
  });

  // The split sits one header-thickness in from its edge, plus the half-gap the
  // real tile is inset by, so the line lands exactly on the rendered divider.
  function ghostSplitStyle(side: GroupHeaderSide): string {
    const off = `${GROUP_HEADER_PX + globalDesign.widgetGap / 2}px`;
    switch (side) {
      case 'bottom': return `bottom: ${off};`;
      case 'left':   return `left: ${off};`;
      case 'right':  return `right: ${off};`;
      default:       return `top: ${off};`;
    }
  }

  // While a MEMBER is being moved or resized (or a widget is being dragged into
  // an area), that area draws the same bound + header split the group's own
  // ghost shows ([S-40]). Aligning a widget inside a group means aligning it to
  // the group's body edge, so the guide has to be on screen while you do it.
  const guideGroup = createMemo(() => {
    const id = drag()?.id ?? resize()?.id;
    if (!id) return null;
    const w = layout.widgets.find(x => x.id === id);
    if (!w || isAreaGroupType(w.type)) return null; // the group draws its own
    const target = dropTarget();
    const groupId = (target && isAreaGroupType(
      layout.widgets.find(x => x.id === target)?.type ?? '',
    )) ? target : w.parentId;
    if (!groupId) return null;
    const g = layout.widgets.find(x => x.id === groupId);
    return g && isAreaGroupType(g.type) && !isGroupCollapsed(g)
      ? { rect: { x: g.x, y: g.y, w: g.w, h: g.h }, side: groupHeaderSide(g) }
      : null;
  });

  // Pop the widget out into its own OS window (registry `popOut` opt-in). Routes
  // through the shared layout store so the window and grid tile stay in sync.
  function popOutWidget(widget: Widget, def: typeof registry[string]) {
    invoke('open_widget_window', {
      parentLabel: getCurrentWindow().label,
      widgetType: widget.type,
      widgetId: widget.id,
      viewId: layoutStore.viewId,
      title: widget.label || def.label || 'Themia',
    }).catch(err => logger.error('widget-window', `open_widget_window failed: ${err}`));
  }

  function handleWidgetCtx(e: MouseEvent, widget: Widget) {
    const def = registry[widget.type];
    if (!def) return;

    function buildItems(cfg: Record<string, unknown>) {
      const setConfig = (partial: Record<string, unknown>) => {
        const newCfg = { ...cfg, ...partial };
        layoutStore.updateWidgetConfig(widget.id, newCfg);
        updateCtxMenuItems(buildItems(newCfg));
      };
      const widgetItems = def.contextActions ? def.contextActions(cfg, setConfig, viewActions) : [];
      return [
        ...(!props.editMode ? [{ icon: Pencil, label: t('ctxMenu.editMove'), action: () => props.onEnterEditMode?.() }] : []),
        // Containers hold widgets, so "add one in here" is their primary
        // action and leads the menu ([S-34]).
        ...(isContainerType(widget.type)
          ? [{ icon: Plus, label: t('ctxMenu.addToContainer'), action: () => props.onAddToContainer(widget.id) }]
          : []),
        { icon: Settings, label: t('ctxMenu.configure'), action: () => props.onConfigure(widget) },
        ...(def.popOut ? [{ icon: ExternalLink, label: t('ctxMenu.openInWindow'), action: () => popOutWidget(widget, def) }] : []),
        ...(widgetItems.length ? [{ sep: true }, ...widgetItems] : []),
        ...(props.editMode ? [
          { sep: true },
          { icon: Copy,     label: t('ctxMenu.copyWidget'),  action: () => { copyWidget(widget); props.onStartClipboardPlacing?.('copy'); }, shortcut: '⌃C' },
          { icon: Scissors, label: t('ctxMenu.cutWidget'),   action: () => { cutWidget(widget); props.onStartClipboardPlacing?.('cut', widget.id); }, shortcut: '⌃X' },
        ] : []),
        { sep: true },
        { icon: X, label: t('ctxMenu.removeWidget'), danger: true, action: async () => { if (window.confirm(t('ctxMenu.removeWidgetConfirm'))) layoutStore.removeWidget(widget.id); } },
      ];
    }

    showCtxMenu(e, buildItems(widget.config ?? {}));
  }

  return (
    <div
      class={`grid${props.editMode ? ' edit' : ''}${props.placing ? ' placing' : ''}`}
      // --widget-intro-duration is mode-picked ([S-24]): the startup duration
      // at boot (and during settings replays, which flip the mode back), the
      // transition duration after a view switch. Owned here, not :root, since only
      // this grid knows the live mode.
      // --group-collapse-ms is the SAME constant the phase timer runs on
      // ([S-43]), published once here so CSS and the JS clock cannot drift.
      // Screen shake while a hyperspace boot overlay plays ([S-16]), purely
      // visual; the hit-mask reads logical geometry, never this transform.
      // (hacker is per-widget now and shakes nothing grid-wide.)
      style={`--widget-intro-duration: ${introMsFor(introAnimMode())}ms;--group-collapse-ms: ${GROUP_COLLAPSE_MS}ms;${(() => {
          const os = bootOverlayStyle();
          return os ? hyperspaceShakeStyle(os) : '';
        })()}`}
      ref={el => { gridEl = el; }}
    >
      <Show when={showGridLines()}>
        {/* CSS-gradient grid: one element, no per-cell DOM, so a fine (~10px)
            grid stays cheap. Cell size = 100%/cols × 100%/rows. */}
        <div
          class="grid-lines"
          style={`background-size: calc(100% / ${cells().cols}) calc(100% / ${cells().rows});`}
          aria-hidden="true"
        />
      </Show>

      <For each={topLevelWidgets()}>
        {(widget, i) => {
          const def = registry[widget.type];
          if (!def) return null;
          // A collapsed area group renders as its title bar alone ([S-37]); its
          // stored rect is never touched, so expanding restores the area
          // exactly. Every other widget renders at its own rect.
          const collapsed = () => isGroupCollapsed(widget);
          const rect = () => groupRenderRect(
            widget, collapsed(), screen(), groupHeaderSide(widget), globalDesign.widgetGap,
          );
          // Expand / collapse animation ([S-43]). The tile's inline rect flips
          // to its target at once and the transition class carries the BOX
          // there, so the border, the bar and the area all travel together;
          // a member of a group mid-flight fades with it.
          const collapsePhase = () => groupCollapsePhase(widget.id);
          const memberPhase = () => (widget.parentId ? groupCollapsePhase(widget.parentId) : undefined);
          const collapseClass = () => {
            if (isAreaGroupType(widget.type)) return collapsePhase() ? ' group-anim' : '';
            const p = memberPhase();
            return p === 'out' ? ' group-leaving' : p === 'in' ? ' group-entering' : '';
          };
          // The hit-mask follows PAINT, not layout ([S-17]/[S-13a]). A group
          // whose area is see-through paints only its title bar, so that band
          // is all it may contribute: masking the whole tile would leave an
          // invisible window over the desktop the user can see through it —
          // wallpaper visible, icons under it dead. Published in app-layer CSS
          // px, the same space the inline rect uses, so App reads it without
          // measuring (a `getBoundingClientRect` here would fold in the intro
          // animation's transform, which is what [S-17] snaps around).
          // Empty areas keep the full rect: the "add a widget" placeholder is
          // real paint, and clipping it away would hide the way to fill it.
          const maskRectAttr = () => {
            if (!isAreaGroupType(widget.type) || collapsed()) return undefined;
            const cfg = widget.config ?? {};
            if (groupAreaPaints(cfg)) return undefined;
            if (!layout.widgets.some(w => w.parentId === widget.id)) return undefined;
            const bar = groupRenderRect(
              widget, true, screen(), groupHeaderSide(widget), globalDesign.widgetGap,
            );
            const b = widgetBox(bar, globalDesign.widgetGap, screen().w, screen().h, deviceQ());
            return `${b.left},${b.top},${b.width},${b.height}`;
          };
          // Staggered intro: each widget's animation starts a little after the
          // previous one, cascading the view in. Capped so a dense view doesn't
          // leave the last widget waiting. The style is mode-picked ([S-24]):
          // the boot mount (and settings replays) play the startup style;
          // view switches play the transition style. 'none' skips the
          // animation entirely ([S-15]).
          const anim = () => introAnimFor(introAnimMode());
          // The mode-picked per-widget intro duration ([S-24]/[S-16]).
          const introMs = () => introMsFor(introAnimMode());
          // Hold a widget's CSS intro until the boot overlay's reveal point
          // ([S-16] hyperspace jump). hacker has NO widget CSS intro: its tile
          // canvas (a child, rendered below) covers the widget and fades to
          // reveal it ([S-23]), so this only applies to overlay styles.
          const introBase = bootOverlayActive() ? introBaseDelayMs(anim()) : 0;
          // The [S-15] cascade, from the shared rule — the sheet styles ([S-28])
          // resolve to 0 there, because they fly each side in as ONE RIGID SHEET
          // and a cascade would peel it apart into a queue. Shared with the
          // replay poke (bootOverlay) so a preview can't re-stagger what this
          // renders flat.
          const introDelay = () => `animation-delay: ${introBase + introStaggerMs(anim(), i())}ms;`;
          // Whether THIS row animates ([S-27]): only when the grid mount
          // consumed an armed play AND the row was part of that mount-time
          // render; a row created later (a placed/pasted widget) stays still.
          // A settings replay bumps the nonce so even silent rows re-acquire
          // their intro class for the preview (captured per row, so widgets
          // placed AFTER a replay stay still too).
          const introEligible = playIntroOnMount && !gridSettled;
          const rowNonce = introReplayNonce();
          const showIntro = () => introEligible || introReplayNonce() > rowNonce;
          const hasIntro = () => anim() !== 'none' && anim() !== 'hacker' && showIntro();
          // Per-widget intro origin, for the styles whose start point depends on
          // WHERE the widget sits: the warp core for hyperspace ([S-16], offset
          // toward the bottom so the tile flies in from the background instead
          // of scaling down from the front), and its sheet's shared offset for
          // sides / topbottom ([S-28]).
          const introVars = () => {
            const a = anim();
            if (!showIntro()) return '';
            if (isOverlayStyle(a)) return hyperspaceFromVars(widget, screen().w, screen().h, a);
            const s = sheet();
            return s ? sheetFromVars(widget, s.axis, s.offsets) : '';
          };
          return (
            <>
              <div
                // `is-hidden` is REPORTED by the widget itself
                // (widgetVisibility, music [S-20]); the grid only reacts. The
                // widget suspends its own hiding in edit mode, so no edit-mode
                // guard is needed here — there is one source of truth.
                class={`widget${hasIntro() ? ` intro intro-${anim()}` : ''}${drag()?.id === widget.id || resize()?.id === widget.id ? ' is-dragging' : ''}${props.editMode ? ' edit' : ''}${def.noChrome ? ' no-chrome' : ''}${def.canTransparent && widget.config?.transparentBg === true ? ' bare' : ''}${isAreaGroupType(widget.type) && groupBorderMode(widget) === 'none' ? ' no-widget-border' : ''}${dropTarget() === widget.id ? ' stack-drop-hover' : ''}${isWidgetHidden(widget.id) ? ' is-hidden' : ''}${collapseClass()}`}
                style={`${rectStyle(rect(), globalDesign.widgetGap, screen().w, screen().h, deviceQ())}${hasIntro() ? introDelay() : ''}${introVars()}${!props.editMode && widget.config?.layer === 'behind' ? ' z-index: -1;' : !props.editMode && widget.config?.layer === 'front' ? ' z-index: 3;' : ''}`}
                // The band this tile contributes to the hit-mask when it is
                // smaller than the tile ([S-13a]); absent = the whole tile.
                data-mask-rect={maskRectAttr()}
                // Drag the RENDERED rect, so a collapsed group's ghost is the
                // title bar the user grabbed and not the area it is hiding. The
                // drop writes x/y only, so the stored area survives the move.
                onMouseDown={(e) => startDrag(e, { id: widget.id, ...rect() })}
                onContextMenu={(e) => handleWidgetCtx(e, widget)}
              >
                <ErrorBoundary
                  fallback={(err, reset) => (
                    <WidgetErrorFallback
                      label={def.label ?? widget.type}
                      widgetId={widget.id}
                      error={err}
                      onRetry={reset}
                    />
                  )}
                >
                  {(() => {
                    // Test-only crash seam (webdriver builds only): lets the e2e
                    // force a render throw to prove the boundary isolates it. Inert
                    // in release; no real widget config carries `__crashTest`.
                    if (navigator.webdriver && widget.config?.__crashTest) {
                      throw new Error('crash test: forced widget render failure');
                    }
                    const C = def.component;
                    // Publish the pop-out affordance so the widget's own header
                    // can draw it (WidgetHeader [S-18]); the floating overlay
                    // below covers the modes that render no header actions.
                    const chrome: WidgetChrome = def.popOut && !props.editMode
                      ? { popOut: () => popOutWidget(widget, def) }
                      : {};
                    return <WidgetChromeContext.Provider value={chrome}>
                      {/* `_onConfigureWidget` / `_onPopOutWidget` /
                          `_onAddToContainer` are the container widgets' seam
                          ([S-19]): a stack renders children the grid never
                          sees, so it needs the grid's own chrome actions for
                          them. Inert for every other widget. */}
                      <C {...(widget.config ?? {})} headerMode={resolveHeaderMode(widget.config, def)} label={widget.label ?? ''} _widgetId={widget.id} _editMode={props.editMode} _onConfigure={() => props.onConfigure(widget)} _onConfigureWidget={(w: Widget) => props.onConfigure(w)} _onPopOutWidget={(w: Widget) => { const d = registry[w.type]; if (d?.popOut) popOutWidget(w, d); }} _onAddToContainer={() => props.onAddToContainer(widget.id)} />
                    </WidgetChromeContext.Provider>;
                  })()}
                </ErrorBoundary>

                {/* hacker intro: a canvas drawn over THIS tile while it plays,
                    then it fades as the widget decodes in ([S-23]). Renders on
                    every ARMED hacker mount ([S-27]): boot AND view switches,
                    never a placed widget or an un-armed grid remount. */}
                <Show when={anim() === 'hacker' && showIntro()}>
                  <HackerTile index={i()} durationMs={introMs()} />
                </Show>

                <Show when={props.editMode}>
                  <div class="widget-toolbar">
                    <button
                      class="toolbar-btn"
                      title={t('ctxMenu.configure')}
                      onMouseDown={(e) => e.stopPropagation()}
                      onClick={(e) => { e.stopPropagation(); props.onConfigure(widget); }}
                    ><Settings size={14} strokeWidth={1.5} /></button>
                    <button
                      class="toolbar-btn"
                      title={t('ctxMenu.copy')}
                      onMouseDown={(e) => e.stopPropagation()}
                      onClick={(e) => { e.stopPropagation(); copyWidget(widget); props.onStartClipboardPlacing?.('copy'); }}
                    ><Copy size={14} strokeWidth={1.5} /></button>
                    <button
                      class="toolbar-btn"
                      title={t('ctxMenu.cut')}
                      onMouseDown={(e) => e.stopPropagation()}
                      onClick={(e) => { e.stopPropagation(); cutWidget(widget); props.onStartClipboardPlacing?.('cut', widget.id); }}
                    ><Scissors size={14} strokeWidth={1.5} /></button>
                    <button
                      class="toolbar-btn remove"
                      title={t('ctxMenu.remove')}
                      onMouseDown={(e) => e.stopPropagation()}
                      onClick={async (e) => { e.stopPropagation(); if (window.confirm(t('ctxMenu.removeWidgetConfirm'))) layoutStore.removeWidget(widget.id); }}
                    ><X size={14} strokeWidth={1.5} /></button>
                  </div>
                </Show>

                {/* Non-edit chrome affordance for poppable widgets (registry
                    `popOut`): a hover-revealed "open in window" button, top-right.
                    Mirrors the context-menu action; both call popOutWidget.
                    Only for widgets whose header can't host it — a `normal`
                    header draws the button inline instead (WidgetHeader [S-18]),
                    where it can never overlap the header's own actions. */}
                <Show when={!props.editMode && def.popOut
                  && resolveHeaderMode(widget.config, def) !== 'normal'}>
                  <button
                    class="widget-popout-btn"
                    title={t('ctxMenu.openInWindow')}
                    onMouseDown={(e) => e.stopPropagation()}
                    onClick={(e) => { e.stopPropagation(); popOutWidget(widget, def); }}
                  ><ExternalLink size={14} strokeWidth={1.5} /></button>
                </Show>
              </div>

              <div
                class="corner-overlay"
                style={rectStyle(rect(), 0, screen().w, screen().h, deviceQ())}
                aria-hidden="true"
              >
                {/* A collapsed group offers no handles: they would sit on the
                    title bar and resize the AREA down to a sliver ([S-37]).
                    Expand it and the area is resizable again. */}
                <Show when={props.editMode && !collapsed()}>
                  <For each={corners}>
                    {(c) => (
                      <div
                        class={`resize-handle rh-${c.key}`}
                        style={`transform:rotate(${c.rotate}); cursor:${c.cursor};`}
                        onMouseDown={(e) => startResize(e, widget, c.key)}
                      >
                        <svg width="10" height="10" viewBox="0 0 10 10">
                          <line x1="2" y1="10" x2="10" y2="2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                          <line x1="6" y1="10" x2="10" y2="6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                        </svg>
                      </div>
                    )}
                  </For>
                  <For each={edges}>
                    {(c) => (
                      <div
                        class={`resize-handle resize-edge rh-${c.key}`}
                        style={`cursor:${c.cursor};`}
                        onMouseDown={(e) => startResize(e, widget, c.key)}
                      >
                        <svg width="10" height="10" viewBox="0 0 10 10" style={`transform:rotate(${c.rotate});`}>
                          <line x1="1" y1="5" x2="9" y2="5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                        </svg>
                      </div>
                    )}
                  </For>
                </Show>
              </div>

              {/* Marks the widget's FULL boundary (no spacing applied) with
                  square corners while edit mode is active. */}
              <Show when={props.editMode}>
                <div class="edit-bounds" style={rectStyle(rect(), 0, screen().w, screen().h, deviceQ())} aria-hidden="true">
                  <span class="bound-corner tl" />
                  <span class="bound-corner tr" />
                  <span class="bound-corner bl" />
                  <span class="bound-corner br" />
                </div>
              </Show>
            </>
          );
        }}
      </For>

      {/* The AREA's guide while one of its widgets is being moved/resized, or
          while a widget is being dragged into it ([S-40]). ONE line, on the
          group's outer boundary (gap=0, no corner brackets): the guide is a
          reference edge, and the bracket ring stacked outside the bound — over
          the group's own tile border a half-gap further in — read as three
          borders around one area. Sits under the dragged widget's own ghost. */}
      <Show when={guideGroup()}>
        {(g) => (
          <div class="ghost ghost-guide" style={rectStyle(g().rect, 0, screen().w, screen().h, deviceQ())}>
            <span class={`ghost-split gs-${g().side}`} style={ghostSplitStyle(g().side)} />
          </div>
        )}
      </Show>

      {/* Ghost spans the FULL logical rect (gap=0) so the true widget boundary
          is visible regardless of widget spacing; corner brackets mark it. */}
      <Show when={preview() && (drag() || resize() || props.placing)}>
        <div class="ghost" style={rectStyle(preview()!, 0, screen().w, screen().h, deviceQ())}>
          <span class="ghost-corner tl" />
          <span class="ghost-corner tr" />
          <span class="ghost-corner bl" />
          <span class="ghost-corner br" />
          {/* A group's ghost also marks where its TITLE BAR ends ([S-40]): the
              body is the only part widgets can occupy, so sizing or placing an
              area against the outer bound alone lands them under the bar. */}
          <Show when={ghostHeaderSide()}>
            {(side) => <span class={`ghost-split gs-${side()}`} style={ghostSplitStyle(side())} />}
          </Show>
        </div>
      </Show>

      <Show when={remoteGhost() && !drag() && !resize() && !props.placing}>
        <div class="ghost ghost-remote" style={rectStyle(remoteGhost()!, 0, screen().w, screen().h, deviceQ())}>
          <span class="ghost-corner tl" />
          <span class="ghost-corner tr" />
          <span class="ghost-corner bl" />
          <span class="ghost-corner br" />
        </div>
      </Show>
    </div>
  );
}

import { createMemo, ErrorBoundary, For, Show } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  Settings, ArrowUp, ArrowDown, ExternalLink, Ungroup, X, TriangleAlert,
  PanelTop, Plus,
} from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import type { Widget } from '../../../src/layout.ts';
import { t } from '../../../src/i18n.ts';
// Runtime registry import: the one intentional module cycle (registry →
// tabs/index → Tabs → registry). Safe: `registry` is only dereferenced at
// render time, long after both modules finished evaluating (ESM live binding).
import registry, { resolveHeaderMode, type HeaderMode } from '../../../src/widgets/registry.tsx';
import { resolveActiveTabId } from './tabsModel.ts';
import { previewChildren } from '../../../src/widgets/containerPreview.ts';
import { demoWidgetConfigs } from '../../../src/demo/fixtures.ts';
import './Tabs.css';

interface TabsProps {
  /** The selected tab, a child id ([S-02]). */
  activeChildId?: unknown;
  /** Picker-preview composition ([S-10]); ignored once the container is real. */
  _previewChildren?: unknown;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
  _onConfigureWidget?: (w: Widget) => void;
  _onPopOutWidget?: (w: Widget) => void;
  _onAddToContainer?: () => void;
}

export default function Tabs(props: TabsProps) {
  const layoutStore = useLayout();

  // Children = widgets parented to this container, in array order (src/SPEC [S-19]).
  // With no `_widgetId` there is no layout to read: that is the picker preview,
  // which composes its children from the demo fixture instead ([S-10]).
  const children = createMemo<Widget[]>(() =>
    props._widgetId
      ? layoutStore.layout.widgets.filter(w => w.parentId === props._widgetId)
      : previewChildren(props._previewChildren, type => ({
        ...(registry[type]?.defaultConfig ?? {}),
        ...(demoWidgetConfigs[type] ?? {}),
      })),
  );
  /** Preview mode: no store to write, no real children to act on. */
  const isPreview = () => !props._widgetId;
  // Exactly one active child ([S-02]).
  const activeTab = createMemo(() =>
    resolveActiveTabId(children().map(c => c.id), props.activeChildId),
  );

  // Config writes go through a full-config merge read from the store, since spread
  // props don't carry unrelated keys (layer, headerMode), and dropping them on
  // write would destroy user data ([S-03]).
  function patchConfig(partial: Record<string, unknown>) {
    if (!props._widgetId) return;
    const self = layoutStore.layout.widgets.find(w => w.id === props._widgetId);
    layoutStore.updateWidgetConfig(props._widgetId, { ...(self?.config ?? {}), ...partial });
  }

  function selectTab(child: Widget) {
    if (props._editMode) return;
    patchConfig({ activeChildId: child.id });
  }

  /** Does this child bring its own WidgetHeader? Then that live header renders
   *  above the body ([S-04]); only registry-noHeader / user-hidden headers fall
   *  back to headerMode 'none'. */
  function liveHeader(child: Widget): boolean {
    const def = registry[child.type];
    return def ? resolveHeaderMode(child.config, def) !== 'none' : false;
  }

  function rowCtx(e: MouseEvent, child: Widget, idx: number) {
    e.preventDefault();
    e.stopPropagation();
    if (isPreview()) return;
    const count = children().length;
    // A child inside the container keeps the chrome it has on the grid: a
    // poppable widget can still open in its own window ([S-07]). The grid owns
    // that action, so it arrives as `_onPopOutWidget`.
    const canPopOutWindow = !!registry[child.type]?.popOut && !!props._onPopOutWidget;
    showCtxMenu(e, [
      { icon: Settings, label: t('tabs.configure'), action: () => props._onConfigureWidget?.(child) },
      ...(canPopOutWindow
        ? [{ icon: ExternalLink, label: t('ctxMenu.openInWindow'), action: () => props._onPopOutWidget?.(child) }]
        : []),
      { icon: ArrowUp, label: t('tabs.moveUp'), disabled: idx === 0, action: () => layoutStore.reorderChild(child.id, -1) },
      { icon: ArrowDown, label: t('tabs.moveDown'), disabled: idx === count - 1, action: () => layoutStore.reorderChild(child.id, 1) },
      { icon: Ungroup, label: t('tabs.popOut'), action: () => layoutStore.clearWidgetParent(child.id) },
      { sep: true },
      {
        icon: X, label: t('tabs.remove'), danger: true,
        action: () => { if (window.confirm(t('ctxMenu.removeWidgetConfirm'))) layoutStore.removeWidget(child.id); },
      },
    ]);
  }

  // Only the active child is mounted; switching tabs unmounts the previous one
  // (no background polling cost). Each child gets its own ErrorBoundary so one
  // crashing widget degrades to a dead panel, not a dead container ([S-05]).
  function childBody(child: Widget, headerMode: () => HeaderMode) {
    const def = registry[child.type];
    if (!def) return null;
    // Preview children are stand-ins, not live widgets: the tab strip is what
    // identifies this container, and mounting a real child made the pane look
    // like an advert for that child instead. Mirrors stack + group [S-06].
    if (isPreview()) return <div class="tabs-child" role="tabpanel"><div class="tabs-ghost" /></div>;
    return (
      <div class="tabs-child" role="tabpanel">
        <ErrorBoundary
          fallback={(_err, reset) => (
            <div class="widget-error" role="alert">
              <TriangleAlert size={16} strokeWidth={1.5} class="widget-error-icon" />
              <div class="widget-error-text">
                <span class="widget-error-title">{child.label || def.label}</span>
                <span class="widget-error-sub">failed to render</span>
              </div>
              <button class="widget-error-retry" title={t('common.retry')} onClick={(e) => { e.stopPropagation(); reset(); }}>↻</button>
            </div>
          )}
        >
          <Dynamic
            component={def.component}
            {...(child.config ?? {})}
            headerMode={headerMode()}
            label={child.label ?? ''}
            _widgetId={child.id}
            _editMode={props._editMode}
            _onConfigure={() => props._onConfigureWidget?.(child)}
          />
        </ErrorBoundary>
      </div>
    );
  }

  return (
    <div class="tabs-widget" classList={{ 'has-children': children().length > 0 }}>
      <Show
        when={children().length > 0}
        fallback={
          /* An empty container stays VISIBLE at rest ([S-06]): one that paints
             nothing is indistinguishable from a broken widget, and empty is
             exactly what every new container starts as. The placeholder doubles
             as the way in ([S-11]) -- dragging a widget over the rect in edit
             mode is not discoverable on its own. */
          <div class="tabs-empty">
            <PanelTop size={18} strokeWidth={1.5} />
            {/* Drag-in is an EDIT-MODE affordance, so the hint only shows
                there; the button below works in either mode. */}
            <Show when={props._editMode}><span class="tabs-empty-hint">{t('tabs.empty')}</span></Show>
            <Show when={props._onAddToContainer}>
              <button
                class="tabs-empty-add"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => { e.stopPropagation(); props._onAddToContainer?.(); }}
              >
                <Plus size={13} strokeWidth={1.5} />
                <span>{t('tabs.addChild')}</span>
              </button>
            </Show>
          </div>
        }
      >
        {/* Static-label tab bar; the active child renders its OWN header below
            it ([S-04]). Right-click a tab opens the row menu ([S-07]). */}
        <div class="widget-header tabs-tabbar" role="tablist">
          <For each={children()}>
            {(child, i) => {
              const def = registry[child.type];
              return (
                <button
                  class="tabs-tab"
                  classList={{ active: activeTab() === child.id }}
                  role="tab"
                  aria-selected={activeTab() === child.id}
                  tabindex={props._editMode || isPreview() ? -1 : 0}
                  title={child.label || def?.label || child.type}
                  onClick={() => selectTab(child)}
                  onContextMenu={(e) => rowCtx(e, child, i())}
                >
                  <span class="tabs-tab-icon">{def?.icon()}</span>
                  <span class="tabs-tab-label">{child.label || def?.label || child.type}</span>
                </button>
              );
            }}
          </For>
        </div>
        <div class="tabs-body">
          <For each={children()}>
            {(child) => {
              // Header mode is read inside its OWN memo so a self-write to the
              // active child's config updates only the headerMode prop, never
              // re-running the Show body (which would rebuild childBody and
              // remount the child, losing focus/scroll/selection) (#80/[S-04]).
              const headerMode = createMemo<HeaderMode>(() =>
                liveHeader(child)
                  ? resolveHeaderMode(child.config, registry[child.type])
                  : 'none',
              );
              return (
                <Show when={activeTab() === child.id}>
                  {childBody(child, headerMode)}
                </Show>
              );
            }}
          </For>
        </div>
      </Show>
    </div>
  );
}

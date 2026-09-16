import { createMemo, ErrorBoundary, For, Show, Switch, Match } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  ChevronDown, Settings, ArrowUp, ArrowDown, ExternalLink, Ungroup, X, TriangleAlert,
  Layers, Plus,
} from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import type { Widget } from '../../../src/layout.ts';
import { t } from '../../../src/i18n.ts';
// Runtime registry import: the one intentional module cycle (registry →
// stack/index → Stack → registry). Safe: `registry` is only dereferenced at
// render time, long after both modules finished evaluating (ESM live binding).
import registry from '../../../src/widgets/registry.tsx';
import { resolveExpandedId } from './stackModel.ts';
import { previewChildren } from '../../../src/widgets/containerPreview.ts';
import { demoWidgetConfigs } from '../../../src/demo/fixtures.ts';
import './Stack.css';

interface StackProps {
  /** The one child the user opened ([S-03]); everything else is a title row. */
  expandedId?: unknown;
  /** Picker-preview composition ([S-12]); ignored once the stack is real. */
  _previewChildren?: unknown;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
  _onConfigureWidget?: (w: Widget) => void;
  _onPopOutWidget?: (w: Widget) => void;
  _onAddToContainer?: () => void;
}

export default function Stack(props: StackProps) {
  const layoutStore = useLayout();

  // Children = widgets parented to this stack, in array order ([S-02]).
  // With no `_widgetId` there is no layout to read: that is the picker preview,
  // which composes its children from the demo fixture instead ([S-12]).
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
  // Accordion: title rows, with AT MOST ONE child open ([S-03]).
  const expandedId = createMemo(() =>
    resolveExpandedId(children().map(c => c.id), props.expandedId),
  );
  const expanded = (child: Widget) => expandedId() === child.id;

  // Config writes go through a full-config merge read from the store, since spread
  // props don't carry unrelated keys (layer, headerMode), and dropping them
  // on write would destroy user data ([S-09]).
  function patchConfig(partial: Record<string, unknown>) {
    if (!props._widgetId) return;
    const self = layoutStore.layout.widgets.find(w => w.id === props._widgetId);
    layoutStore.updateWidgetConfig(props._widgetId, { ...(self?.config ?? {}), ...partial });
  }

  function rowClick(child: Widget) {
    if (props._editMode) return;
    // At most one child is open ([S-03]): clicking a closed row opens it AND
    // closes whichever was open; clicking the open one closes it.
    patchConfig({ expandedId: expanded(child) ? null : child.id });
  }


  /** Section-level click: toggles only when the click lands on the header
   *  band itself, never on the header's own controls (folder search input,
   *  action buttons), which must keep working inside the stack ([S-05]). */
  function handleSectionClick(e: MouseEvent, child: Widget) {
    if (props._editMode) return;
    const t = e.target as HTMLElement;
    if (!t.closest('.widget-header, .stack-row')) return;
    if (t.closest('button, input, select, textarea, a, [contenteditable="true"]')) return;
    rowClick(child);
  }

  /** Section-level context menu: the header band routes to the stack's row
   *  menu; body right-clicks stay with the child's own item menus. */
  function handleSectionCtx(e: MouseEvent, child: Widget, idx: number) {
    const t = e.target as HTMLElement;
    if (!t.closest('.widget-header, .stack-row')) return;
    rowCtx(e, child, idx);
  }

  function rowCtx(e: MouseEvent, child: Widget, idx: number) {
    e.preventDefault();
    e.stopPropagation();
    if (isPreview()) return;
    const count = children().length;
    // A child inside a stack keeps the chrome it has on the grid: a poppable
    // widget can still open in its own window ([S-08]). The grid owns that
    // action, so it arrives as `_onPopOutWidget`.
    const canPopOutWindow = !!registry[child.type]?.popOut && !!props._onPopOutWidget;
    showCtxMenu(e, [
      { icon: Settings, label: t('stack.configure'), action: () => props._onConfigureWidget?.(child) },
      ...(canPopOutWindow
        ? [{ icon: ExternalLink, label: t('ctxMenu.openInWindow'), action: () => props._onPopOutWidget?.(child) }]
        : []),
      { icon: ArrowUp, label: t('stack.moveUp'), disabled: idx === 0, action: () => layoutStore.reorderChild(child.id, -1) },
      { icon: ArrowDown, label: t('stack.moveDown'), disabled: idx === count - 1, action: () => layoutStore.reorderChild(child.id, 1) },
      { icon: Ungroup, label: t('stack.popOut'), action: () => layoutStore.clearWidgetParent(child.id) },
      { sep: true },
      {
        icon: X, label: t('stack.remove'), danger: true,
        action: () => { if (window.confirm(t('ctxMenu.removeWidgetConfirm'))) layoutStore.removeWidget(child.id); },
      },
    ]);
  }

  // Child render, mounted ONLY while the child is open ([S-04]): a closed child
  // costs nothing and, more importantly, is never laid out at a size it wasn't
  // built for. Config is never rewritten. Each child gets its own boundary so
  // one crashing widget degrades to a dead section, not a dead stack ([S-06]).
  function childBody(child: Widget) {
    const def = registry[child.type];
    if (!def) return null;
    // In the picker preview a child is a STAND-IN, not a live widget. Mounting
    // the real component made the pane advertise whatever happened to be
    // seeded — the stack's card was a full-size Notes editor, which reads as
    // "this is the Notes widget" rather than "this tile holds other widgets".
    // The area widget already answers this with ghost blocks (group [S-06]).
    if (isPreview()) return <div class="stack-child"><div class="stack-ghost" /></div>;
    return (
      <div class="stack-child">
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
            headerMode="none"
            label={child.label ?? ''}
            _widgetId={child.id}
            _editMode={props._editMode}
            _onConfigure={() => props._onConfigureWidget?.(child)}
          />
        </ErrorBoundary>
      </div>
    );
  }

  // The stack's own title row ([S-04]/[S-05]): icon + label + chevron. Click and
  // context menu are handled at the section level.
  function rowHeader(child: Widget) {
    const def = registry[child.type];
    return (
      <div
        class="widget-header stack-row"
        classList={{ active: expanded(child) }}
        role="button"
        aria-expanded={expanded(child)}
        tabindex={props._editMode || isPreview() ? -1 : 0}
        onKeyDown={(e) => {
          if (e.key !== 'Enter' && e.key !== ' ') return;
          e.preventDefault();
          rowClick(child);
        }}
      >
        <span class="stack-row-icon">{def?.icon()}</span>
        <span class="stack-row-title">{child.label || def?.label || child.type}</span>
        <span class="stack-row-chevron" classList={{ open: expanded(child) }}>
          <ChevronDown size={14} strokeWidth={1.5} />
        </span>
      </div>
    );
  }

  return (
    <div class="stack-widget" classList={{ 'is-edit': !!props._editMode, 'has-children': children().length > 0 }}>
      <Switch>
        <Match when={children().length === 0}>
          {/* An empty stack stays VISIBLE at rest ([S-07]): a container that
              paints nothing is indistinguishable from a broken widget, and an
              empty container is exactly what every new stack starts as. The
              placeholder doubles as the way in ([S-13]) -- dragging a widget
              over the rect in edit mode is not discoverable on its own. */}
          <div class="stack-empty">
            <Layers size={18} strokeWidth={1.5} />
            {/* Drag-in is an EDIT-MODE affordance, so the hint only shows
                there; the button below works in either mode. */}
            <Show when={props._editMode}><span class="stack-empty-hint">{t('stack.empty')}</span></Show>
            <Show when={props._onAddToContainer}>
              <button
                class="stack-empty-add"
                onMouseDown={(e) => e.stopPropagation()}
                onClick={(e) => { e.stopPropagation(); props._onAddToContainer?.(); }}
              >
                <Plus size={13} strokeWidth={1.5} />
                <span>{t('stack.addChild')}</span>
              </button>
            </Show>
          </div>
        </Match>

        <Match when={children().length > 0}>
          <For each={children()}>
            {(child, i) => (
              <div
                class="stack-section"
                classList={{ active: expanded(child) }}
                onClick={(e) => handleSectionClick(e, child)}
                onContextMenu={(e) => handleSectionCtx(e, child, i())}
              >
                {/* The row is ALWAYS the stack's own ([S-04]): same 40px band,
                    icon, title and chevron whether the child is open or shut.
                    The open child renders below it as a bare body. */}
                {rowHeader(child)}
                <Show when={expanded(child)}>{childBody(child)}</Show>
              </div>
            )}
          </For>
        </Match>
      </Switch>
    </div>
  );
}

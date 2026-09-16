import { For, Show, createMemo, createSignal } from 'solid-js';
import { Monitor, ExternalLink, ArrowUp, ArrowDown, Plus, GripVertical } from 'lucide-solid';
import ConfigTabs from './ConfigTabs.tsx';
import { findViewForWidget, getViewLayoutStore, type Widget } from '../layout.ts';
import registry from '../widgets/registry.tsx';
import { t } from '../i18n.ts';
import '../styles/config-base.css';
import './ChildListConfig.css';

// The config panel for a CONTAINER widget: lists the widgets nested inside it,
// with reorder + pop-out actions.
//
// Tabs and Stack are both containers and had byte-identical copies of this --
// their stylesheets differed only in the class prefix, and their components only
// in a config interface whose field neither of them actually read. ~200 lines of
// copy-paste for one behaviour. This is that behaviour, once.

export interface ChildListProps {
  /** i18n key for the section heading ("Tabs" / "Sections"). */
  labelKey: string;
  /** i18n key for the empty state. */
  emptyKey: string;
  /** i18n key for the "add a widget in here" button. */
  addKey: string;
  /** i18n key for the "take it back out" action (default: "Pop out"). */
  popOutKey?: string;
  /** Offer reordering (grip + arrows). Off for an AREA group, whose child order
   *  means nothing: those children sit where they sit on the desktop grid
   *  (src/SPEC [S-36]), so "move up" would be a control with no effect. */
  showReorder?: boolean;
  /** The container being configured; supplied by ConfigWindow. */
  widgetId?: string;
  /** Config-window hooks. `onAddToContainer` routes to the Add Widget page
   *  bound to this container ([S-34]); absent outside the config window. */
  globals?: { onAddToContainer?: (containerId: string) => void };
}

/**
 * The child list ROWS, with no tab chrome of their own. A container whose config
 * is only this list mounts `ChildListConfig` below; one that has settings of its
 * own (the area group: header edge, title, background) composes this inside its
 * own `ConfigTabs`, so the list still exists exactly once.
 */
export function ChildList(props: ChildListProps) {
  // The config window edits a widget that lives in some view's layout store,
  // so resolve it per access so reorder/pop-out route through the same store ops
  // the live grid uses (single path).
  const store = createMemo(() => {
    if (!props.widgetId) return null;
    const viewId = findViewForWidget(props.widgetId);
    return viewId ? getViewLayoutStore(viewId) : null;
  });
  const children = createMemo<Widget[]>(() =>
    store()?.layout.widgets.filter(w => w.parentId === props.widgetId) ?? [],
  );

  // ─── Drag to reorder ([S-35]) ─────────────────────────────────────────────
  // Only the grip is `draggable`, so the row's own buttons keep their normal
  // pointer behaviour. The list reorders LIVE on dragover (rows shuffle under
  // the cursor) rather than on drop, matching the other collection editors;
  // `moveChildTo` is a no-op for a hover that changes nothing, and it writes
  // through the SAME store op the grid and the row menus use, so the container
  // tile re-renders in step.
  const [dragId, setDragId] = createSignal<string | null>(null);
  const [dragOverId, setDragOverId] = createSignal<string | null>(null);
  const reorderable = () => props.showReorder !== false;

  function onDragStart(e: DragEvent, id: string) {
    setDragId(id);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  function onDragOver(e: DragEvent, id: string) {
    const src = dragId();
    if (!src || src === id) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
    setDragOverId(id);
    store()?.moveChildTo(src, id);
  }

  function endDrag() {
    setDragId(null);
    setDragOverId(null);
  }

  return (
    <>
      <span class="section-label">{t(props.labelKey)}</span>
          <Show
            when={children().length > 0}
            fallback={<span class="design-desc childcfg-empty">{t(props.emptyKey)}</span>}
          >
            <For each={children()}>
              {(child, i) => {
                const def = registry[child.type];
                return (
                  <div
                    class="design-row childcfg-row"
                    classList={{
                      'drag-src': dragId() === child.id,
                      'drag-over': dragId() !== null && dragOverId() === child.id,
                    }}
                    onDragOver={(e) => onDragOver(e, child.id)}
                    onDrop={(e) => { e.preventDefault(); endDrag(); }}
                  >
                    <Show when={reorderable()}>
                      <span
                        class="item-handle"
                        draggable={true}
                        title={t('display.dragReorder')}
                        onDragStart={(e) => onDragStart(e, child.id)}
                        onDragEnd={endDrag}
                      >
                        <GripVertical size={14} strokeWidth={1.5} />
                      </span>
                    </Show>
                    <div class="design-left">
                      <span class="design-label childcfg-name">
                        <span class="childcfg-icon">{def?.icon()}</span>
                        {child.label || def?.label || child.type}
                      </span>
                    </div>
                    <div class="childcfg-actions">
                      <Show when={reorderable()}>
                        <button
                          class="childcfg-icon-btn" title={t('childList.moveUp')} disabled={i() === 0}
                          onClick={() => store()?.reorderChild(child.id, -1)}
                        ><ArrowUp size={14} strokeWidth={1.5} /></button>
                        <button
                          class="childcfg-icon-btn" title={t('childList.moveDown')} disabled={i() === children().length - 1}
                          onClick={() => store()?.reorderChild(child.id, 1)}
                        ><ArrowDown size={14} strokeWidth={1.5} /></button>
                      </Show>
                      <button
                        class="childcfg-icon-btn" title={t(props.popOutKey ?? 'childList.popOut')}
                        onClick={() => store()?.clearWidgetParent(child.id)}
                      ><ExternalLink size={14} strokeWidth={1.5} /></button>
                    </div>
                  </div>
                );
              }}
            </For>
          </Show>

      {/* The creation path: a container is empty when it is added, and
          drag-drop alone left the user with an invisible box ([S-33]). */}
      <Show when={props.widgetId && props.globals?.onAddToContainer}>
        <button
          class="btn-add childcfg-add"
          onClick={() => props.globals?.onAddToContainer?.(props.widgetId!)}
        >
          <Plus size={14} strokeWidth={1.5} />
          <span>{t(props.addKey)}</span>
        </button>
      </Show>
    </>
  );
}

/** A container whose whole config IS the child list (stack, tabs). */
export default function ChildListConfig(props: ChildListProps) {
  return (
    <ConfigTabs tabs={[{ key: 'display', label: t('configTabs.display'), icon: Monitor }]}>
      {(tab) => <Show when={tab === 'display'}><ChildList {...props} /></Show>}
    </ConfigTabs>
  );
}

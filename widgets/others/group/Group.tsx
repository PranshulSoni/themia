import { createMemo, For, Show } from 'solid-js';
import { ChevronDown, Group as GroupIcon, Plus } from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import { groupCollapsePhase } from '../../../src/groupCollapse.ts';
import { groupHeaderSide, type Widget } from '../../../src/layout.ts';
import { VIEW_ICON_MAP } from '../../../src/components/IconPicker.tsx';
import { t } from '../../../src/i18n.ts';
import { groupAreaBackground } from './groupStyle.ts';
import './Group.css';

interface GroupProps {
  /** Rolled up? While true the grid hides every member and draws the title bar
   *  alone (src/SPEC [S-37]). */
  collapsed?: unknown;
  /** Which edge the title bar sits on ([S-11]); resolved by `groupHeaderSide`. */
  headerSide?: unknown;
  /** Own title, overriding the widget label and the registry name ([S-12]). */
  title?: unknown;
  /** Own icon, a `VIEW_ICON_MAP` key ([S-12]); empty → the group glyph. */
  icon?: unknown;
  /** Area surface ([S-13]); resolved by `groupAreaBackground`. */
  bgMode?: unknown;
  bgColor?: unknown;
  bgAlpha?: unknown;
  /** Picker-preview composition ([S-08]); ignored once the group is real. */
  _previewChildren?: unknown;
  label?: string;
  _widgetId?: string;
  _editMode?: boolean;
  _onAddToContainer?: () => void;
}

export default function Group(props: GroupProps) {
  const layoutStore = useLayout();
  /** Preview mode: no id, so no store to read members from or write config to. */
  const isPreview = () => !props._widgetId;

  // Members are the view's widgets pointing at this group ([S-02]). They are
  // NOT rendered here — the grid keeps drawing them at their own desktop
  // geometry; this widget only owns the area and the collapse state.
  const members = createMemo<Widget[]>(() =>
    props._widgetId
      ? layoutStore.layout.widgets.filter(w => w.parentId === props._widgetId)
      : [],
  );
  /** How many widgets the area holds — from the layout, or from the preview
   *  fixture in the picker pane where there is no layout ([S-08]). */
  const count = createMemo(() =>
    isPreview()
      ? (Array.isArray(props._previewChildren) ? props._previewChildren.length : 0)
      : members().length,
  );
  const collapsed = () => props.collapsed === true;
  /** The animation this group is playing, if any ([S-16]/src [S-43]). */
  const phase = () => (props._widgetId ? groupCollapsePhase(props._widgetId) : undefined);
  /** Is the AREA on screen? Not the same as `!collapsed()`: while the group is
   *  closing the flag is already true, and the area has to stay mounted for the
   *  tile to have something to shrink. Everything that draws the open state —
   *  the area, the divider — follows this; the chevron follows the flag, since
   *  a control has to answer the click at once. */
  const open = () => !collapsed() || phase() === 'out';
  // The grid resolves the side the same way for the collapsed rect ([S-39]), so
  // both read through the one validator rather than trusting the raw config.
  const side = () => groupHeaderSide({ config: { headerSide: props.headerSide } });
  const vertical = () => side() === 'left' || side() === 'right';
  /** Title: the user's own, else the widget label, else the registry name. */
  const title = () =>
    (typeof props.title === 'string' && props.title.trim())
      || props.label
      || t('widget.group.label');
  const iconKey = () => (typeof props.icon === 'string' ? props.icon : '');

  // Config writes merge over the STORED config, not over the spread props:
  // props don't carry unrelated keys (layer, headerMode) and writing only the
  // known ones would destroy them (stack [S-09]).
  function patchConfig(partial: Record<string, unknown>) {
    if (!props._widgetId) return;
    const self = layoutStore.layout.widgets.find(w => w.id === props._widgetId);
    layoutStore.updateWidgetConfig(props._widgetId, { ...(self?.config ?? {}), ...partial });
  }

  function toggle() {
    if (isPreview()) return;
    patchConfig({ collapsed: !collapsed() });
  }

  /** Header-band click. In edit mode the band is the group's drag handle, so
   *  only the chevron button toggles there ([S-03]); at rest the whole band is
   *  the target. */
  function headClick(e: MouseEvent) {
    if (props._editMode) return;
    const el = e.target as HTMLElement;
    if (el.closest('button')) return;
    toggle();
  }

  return (
    <div
      class="group-widget"
      classList={{
        collapsed: !open(),
        closing: phase() === 'out',
        opening: phase() === 'in',
        'is-edit': !!props._editMode,
        'is-empty': count() === 0,
        [`side-${side()}`]: true,
      }}
    >
      <div
        class="widget-header group-head"
        classList={{ vertical: vertical() }}
        role="button"
        aria-expanded={!collapsed()}
        tabindex={props._editMode || isPreview() ? -1 : 0}
        onClick={headClick}
        onKeyDown={(e) => {
          if (e.key !== 'Enter' && e.key !== ' ') return;
          e.preventDefault();
          toggle();
        }}
      >
        <span class="group-icon">
          {VIEW_ICON_MAP[iconKey()]?.(13) ?? <GroupIcon size={13} strokeWidth={1.5} />}
        </span>
        <span class="group-title">{title()}</span>
        <button
          class="group-chevron"
          classList={{ open: !collapsed() }}
          title={collapsed() ? t('group.expand') : t('group.collapse')}
          aria-label={collapsed() ? t('group.expand') : t('group.collapse')}
          onMouseDown={(e) => e.stopPropagation()}
          onClick={(e) => { e.stopPropagation(); toggle(); }}
        >
          <ChevronDown size={14} strokeWidth={1.5} />
        </button>
      </div>

      {/* The area itself: the surface the member widgets stand on. It draws
          nothing over them — members are the grid's own tiles, rendered after
          this one ([S-36]) — so this is only the backdrop plus, while the area
          is still empty, the ways to fill it ([S-07]). */}
      <Show when={open()}>
        <div
          class="group-area"
          style={(() => {
            const bg = groupAreaBackground(props);
            return bg ? `background: ${bg};` : '';
          })()}
        >
          <Show when={isPreview() && count() > 0}>
            <div class="group-ghosts" aria-hidden="true">
              <For each={Array.from({ length: count() })}>{() => <span class="group-ghost" />}</For>
            </div>
          </Show>

          <Show when={count() === 0}>
            <div class="group-empty">
              <GroupIcon size={18} strokeWidth={1.5} />
              <Show when={props._editMode}>
                <span class="group-empty-hint">{t('group.empty')}</span>
              </Show>
              <Show when={props._onAddToContainer}>
                <button
                  class="group-empty-add"
                  onMouseDown={(e) => e.stopPropagation()}
                  onClick={(e) => { e.stopPropagation(); props._onAddToContainer?.(); }}
                >
                  <Plus size={13} strokeWidth={1.5} />
                  <span>{t('group.addChild')}</span>
                </button>
              </Show>
            </div>
          </Show>
        </div>
      </Show>
    </div>
  );
}

import { createMemo, For, Show } from 'solid-js';
import { invoke } from '../../../src/ipc.ts';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { ArrowRightLeft, Copy, Trash2, Settings, LayoutGrid } from 'lucide-solid';
import {
  viewStore, switchView, duplicateView, deleteView, isDemoView,
} from '../../../src/views.ts';
import { showCtxMenu } from '../../../src/ctxMenuStore.ts';
import { ask } from '@tauri-apps/plugin-dialog';
import { t } from '../../../src/i18n.ts';
import { VIEW_ICON_MAP } from '../../../src/components/IconPicker.tsx';
import { viewInitials } from '../../../src/viewState.ts';
import { isPro } from '../../../src/license.ts';
import { logger } from '../../../src/debugLog.ts';
import { type ConfigTarget, encodeTarget } from '../../../src/config/target.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './ViewSwitcher.css';

const DBG = 'view-switcher';

export interface ViewSwitcherProps {
  visibleScreens?: string[];
  iconOnly?: boolean;
  label?: string;
  headerMode?: HeaderMode;
  _widgetId?: string;
  _editMode?: boolean;
}

export default function ViewSwitcher(props: ViewSwitcherProps) {
  const visibleIds = () => (props.visibleScreens ?? []) as string[];
  const iconOnly   = () => props.iconOnly ?? false;

  // Render in global view order, filtered by the visible whitelist (empty =
  // show all). Matches the desktop view bar and lets drag-reorder in the
  // config (which mutates the global order) flow through here.
  const views = createMemo(() => {
    const all = viewStore.list;
    const wanted = visibleIds();
    if (wanted.length === 0) return all;
    const set = new Set(wanted);
    const filtered = all.filter(v => set.has(v.id));
    return filtered.length ? filtered : all;
  });

  function openConfig(target: ConfigTarget) {
    try {
      const parentLabel = getCurrentWindow().label;
      invoke('open_config_window', {
        parentLabel,
        target: encodeTarget(target),
      }).catch(e => logger.error(DBG, `open_config_window failed: ${e}`));
    } catch (e) {
      logger.error(DBG, `getCurrentWindow failed: ${e}`);
    }
  }

  function handleSwitch(id: string) {
    if (props._editMode) return;
    switchView(id);
  }

  function handleViewCtx(e: MouseEvent, view: { id: string; name: string }) {
    e.preventDefault(); e.stopPropagation();
    const isActive = view.id === viewStore.activeId;
    const demo     = isDemoView(view.id);
    const canDelete = viewStore.list.filter(v => !isDemoView(v.id)).length > 1 && !demo;
    const canDup    = isPro() || viewStore.list.length < 2;

    showCtxMenu(e, [
      { icon: ArrowRightLeft, label: t('viewSwitcher.switchTo'), disabled: isActive, action: () => switchView(view.id) },
      { icon: Settings, label: t('viewSwitcher.configure'), disabled: !props._widgetId, action: () => {
        if (props._widgetId) openConfig({ kind: 'widget', widgetId: props._widgetId });
      } },
      { sep: true },
      { icon: Copy, label: t('common.duplicate'), disabled: !canDup, action: () => {
        const id = duplicateView(view.id);
        if (id) switchView(id);
      } },
      { icon: Trash2, label: t('view.deleteView.action'), danger: true, disabled: !canDelete, action: async () => {
        if (await ask(t('view.deleteView.ask', { name: view.name }), { title: t('view.deleteView.title'), kind: 'warning' })) {
          deleteView(view.id);
        }
      } },
    ]);
  }

  function renderIcon(view: { name: string; icon?: string }) {
    const key = view.icon;
    if (key && VIEW_ICON_MAP[key]) return VIEW_ICON_MAP[key](14);
    return <span class="view-switcher-letter">{viewInitials(view.name)}</span>;
  }

  return (
    <div class="view-switcher-widget" classList={{ 'icon-only': iconOnly() }}>
      <WidgetHeader
        mode={props.headerMode ?? 'none'}
        icon={LayoutGrid}
        title={props.label || 'Views'}
      />
      <div class="widget-body view-switcher-body">
      <div class="view-switcher-list">
        <For each={views()}>
          {(view) => {
            const isActive = () => view.id === viewStore.activeId;
            return (
              <button
                type="button"
                class="view-switcher-btn"
                classList={{ active: isActive() }}
                title={view.name}
                data-view-id={view.id}
                onClick={() => handleSwitch(view.id)}
                onContextMenu={(e) => handleViewCtx(e, view)}
              >
                <span class="view-switcher-icon">{renderIcon(view)}</span>
                <Show when={!iconOnly()}>
                  <span class="view-switcher-name">{view.name}</span>
                </Show>
              </button>
            );
          }}
        </For>
      </div>
      </div>
    </div>
  );
}

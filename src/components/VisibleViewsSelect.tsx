import { Show, createSignal } from 'solid-js';
import { ask } from '@tauri-apps/plugin-dialog';
import { Plus, Trash2 } from 'lucide-solid';
import {
  viewStore, addView, atFreeViewLimit, moveView, switchView, isDemoView, deleteView,
} from '../views.ts';
import { VIEW_ICON_MAP } from './IconPicker.tsx';
import MultiSelectDropdown, { type MultiSelectItem } from './MultiSelectDropdown.tsx';
import { viewInitials } from '../viewState.ts';
import { t } from '../i18n.ts';

interface VisibleViewsSelectProps {
  /** Raw whitelist of visible view ids. Empty array = show all (the default). */
  stored: string[];
  /** Persist a new raw whitelist. Empty array = show all. */
  onChange: (raw: string[]) => void;
  /** View kept as the single visible entry when "none" is chosen (an empty
   *  whitelist would read as "show all"). Defaults to the first view. */
  noneFallbackKey?: string;
  /** Label for the "show only the fallback view" header action. */
  noneActionLabel?: string;
}

/**
 * Visible-views picker shared by the Display settings page and the View Switcher
 * widget config. A MultiSelectDropdown with search, drag-reorder (global view
 * order), per-row delete, "all / only one" header actions, and an inline
 * create-view footer. The empty whitelist means "show all".
 */
export default function VisibleViewsSelect(props: VisibleViewsSelectProps) {
  const allIds = () => viewStore.list.map(v => v.id);
  // Always present in global view order so drag-reorder (which mutates the
  // global order via moveView) is reflected immediately.
  const effectiveVisible = (): string[] => {
    const all = allIds();
    const stored = props.stored.filter(k => all.includes(k));
    if (stored.length === 0) return all;
    const set = new Set(stored);
    return all.filter(id => set.has(id));
  };
  const isLastVisible = (id: string) => {
    const eff = effectiveVisible();
    return eff.length === 1 && eff[0] === id;
  };

  function handleVisibleChange(next: string[]) {
    if (next.length === 0) return;
    const all = allIds();
    const isAll = next.length === all.length && all.every(id => next.includes(id));
    props.onChange(isAll ? [] : next);
  }
  function handleReorder(fromKey: string, toKey: string) {
    const fromIdx = viewStore.list.findIndex(v => v.id === fromKey);
    const toIdx = viewStore.list.findIndex(v => v.id === toKey);
    if (fromIdx < 0 || toIdx < 0 || fromIdx === toIdx) return;
    if (isDemoView(fromKey) || isDemoView(toKey)) return;
    moveView(fromIdx, toIdx);
  }
  const selectAll = () => props.onChange([]);
  const selectNone = () => {
    const fallback = props.noneFallbackKey ?? viewStore.list[0]?.id;
    if (!fallback) return;
    props.onChange([fallback]);
  };

  // ── Create-view inline input ──
  const [addingView, setAddingView] = createSignal(false);
  const [newViewName, setNewViewName] = createSignal('');
  function confirmAdd(): void {
    if (!addingView()) return;
    const name = newViewName().trim();
    if (name) {
      const id = addView(name);
      if (id) switchView(id);
    }
    setAddingView(false);
    setNewViewName('');
  }
  function startAdd(): void {
    if (atFreeViewLimit()) return;
    setAddingView(true);
    setNewViewName('');
  }
  const addDisabled = () => atFreeViewLimit();

  // ── Delete view (global, affects every display) ──
  const canDelete = (id: string) => {
    if (isDemoView(id)) return false;
    return viewStore.list.filter(v => !isDemoView(v.id)).length > 1;
  };
  async function handleDelete(id: string, name: string) {
    if (!canDelete(id)) return;
    const ok = await ask(t('view.deleteView.ask', { name }), { title: t('view.deleteView.title'), kind: 'warning' });
    if (ok) deleteView(id);
  }

  const buildItems = (): MultiSelectItem[] => viewStore.list.map(v => ({
    key: v.id,
    label: v.name,
    reorderDisabled: isDemoView(v.id),
    rowTitle: isDemoView(v.id) ? t('display.demoCantReorder') : t('display.dragReorder'),
    checkboxDisabled: isLastVisible(v.id),
  }));
  const renderViewIcon = (item: MultiSelectItem) => {
    const v = viewStore.list.find(x => x.id === item.key);
    if (!v) return null;
    if (v.icon && VIEW_ICON_MAP[v.icon]) return VIEW_ICON_MAP[v.icon]!(14);
    return <span class="msd-letter">{viewInitials(v.name)}</span>;
  };
  const renderRowActions = (item: MultiSelectItem) => {
    const v = viewStore.list.find(x => x.id === item.key);
    if (!v) return null;
    const title = isDemoView(v.id)
      ? t('display.demoCantDelete')
      : !canDelete(v.id)
        ? t('display.cantDeleteLast')
        : t('display.deleteViewTip');
    return (
      <button
        type="button"
        class="msd-row-action-btn"
        title={title}
        disabled={!canDelete(v.id)}
        onClick={() => void handleDelete(v.id, v.name)}
      ><Trash2 size={13} strokeWidth={1.5} /></button>
    );
  };

  return (
    <MultiSelectDropdown
      items={buildItems()}
      visibleKeys={effectiveVisible()}
      onChange={handleVisibleChange}
      onReorder={handleReorder}
      renderIcon={renderViewIcon}
      searchPlaceholder={t('common.search')}
      triggerLabel={(keys) => {
        const all = allIds();
        if (keys.length === all.length && all.every(id => keys.includes(id))) {
          return t('display.visibleAll');
        }
        return keys
          .map(k => viewStore.list.find(v => v.id === k)?.name ?? k)
          .join(', ');
      }}
      headerActions={() => (
        <>
          <button class="msd-link" onClick={selectAll}>{t('display.visibleAll')}</button>
          <span class="msd-sep-dot">·</span>
          <button class="msd-link" onClick={selectNone}>{props.noneActionLabel ?? t('display.visibleOnlyAssigned')}</button>
        </>
      )}
      rowActions={renderRowActions}
      footer={() => (
        <Show
          when={addingView()}
          fallback={
            <button
              class="dvs-create"
              classList={{ disabled: addDisabled() }}
              onClick={startAdd}
              title={addDisabled() ? t('app.addViewLimit') : t('display.createViewTip')}
            ><Plus size={13} strokeWidth={1.75} /> {t('display.createView')}</button>
          }
        >
          <input
            class="dvs-create-input"
            value={newViewName()}
            placeholder={t('display.viewNamePlaceholder')}
            onInput={(e) => setNewViewName(e.currentTarget.value)}
            onBlur={confirmAdd}
            onKeyDown={(e) => {
              if (e.key === 'Enter') confirmAdd();
              if (e.key === 'Escape') { setAddingView(false); setNewViewName(''); }
            }}
            ref={(el) => { if (el) requestAnimationFrame(() => el.focus()); }}
          />
        </Show>
      )}
    />
  );
}

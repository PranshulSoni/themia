import { createSignal, createMemo, createEffect, onCleanup, For, Show, type Component, type JSX } from 'solid-js';
import { Dynamic, Portal } from 'solid-js/web';
import { ChevronDown, Check, GripVertical } from 'lucide-solid';
import { globalDesign } from '../design.ts';
import { popupMount } from '../popupLayer.ts';
import { nextIndexLinear } from '../input/kbdNav.ts';
import { t } from '../i18n.ts';
import './MultiSelectDropdown.css';
import './Dropdown.css';

export interface MultiSelectItem {
  key: string;
  label: string;
  icon?: Component<{ size?: number; strokeWidth?: number }>;
  reorderDisabled?: boolean;
  checkboxDisabled?: boolean;
  rowTitle?: string;
}

interface MultiSelectDropdownProps {
  items: MultiSelectItem[];
  visibleKeys: string[];
  onChange: (visibleKeys: string[]) => void;
  onReorder?: (fromKey: string, toKey: string) => void;
  placeholder?: string;
  searchable?: boolean;
  searchPlaceholder?: string;
  triggerLabel?: (visibleKeys: string[]) => string;
  renderIcon?: (item: MultiSelectItem) => JSX.Element;
  headerActions?: () => JSX.Element;
  rowActions?: (item: MultiSelectItem) => JSX.Element;
  footer?: () => JSX.Element;
  triggerClass?: string;
}

export default function MultiSelectDropdown(props: MultiSelectDropdownProps) {
  const [open, setOpen] = createSignal(false);
  const [panelStyle, setPanelStyle] = createSignal<Record<string, string>>({});
  const [query, setQuery] = createSignal('');
  const [focusedIdx, setFocusedIdx] = createSignal(-1);
  const [dragKey, setDragKey] = createSignal<string | null>(null);
  const [dragOverKey, setDragOverKey] = createSignal<string | null>(null);
  let triggerRef: HTMLButtonElement | undefined;
  let panelRef: HTMLDivElement | undefined;
  let searchRef: HTMLInputElement | undefined;

  const itemByKey = createMemo(() => {
    const m = new Map<string, MultiSelectItem>();
    for (const it of props.items) m.set(it.key, it);
    return m;
  });

  const visible = createMemo(() => {
    const known = itemByKey();
    return props.visibleKeys.filter(k => known.has(k));
  });
  const hidden = createMemo(() => {
    const set = new Set(visible());
    return props.items.filter(i => !set.has(i.key)).map(i => i.key);
  });

  const searchable = () => props.searchable !== false;

  const filterMatch = (key: string): boolean => {
    if (!searchable()) return true;
    const q = query().trim().toLowerCase();
    if (!q) return true;
    const it = itemByKey().get(key);
    return !!it && it.label.toLowerCase().includes(q);
  };
  const visibleFiltered = () => visible().filter(filterMatch);
  const hiddenFiltered  = () => hidden().filter(filterMatch);
  const dragEnabled     = () => !searchable() || query().trim().length === 0;
  // Flat keyboard-navigation order: the rows exactly as rendered (checked first,
  // then unchecked). Drives arrow-key focus and Enter/Space toggling.
  const orderedKeys     = () => [...visibleFiltered(), ...hiddenFiltered()];
  const focusedKey      = () => orderedKeys()[focusedIdx()] ?? null;

  const defaultLabel = (): string => {
    const labels = visible().map(k => itemByKey().get(k)?.label ?? k);
    if (labels.length === 0) return props.placeholder ?? '';
    return labels.join(', ');
  };
  const triggerLabel = () => props.triggerLabel ? props.triggerLabel(visible()) : defaultLabel();

  function handleClickOutside(e: MouseEvent) {
    if (triggerRef && triggerRef.contains(e.target as Node)) return;
    if (panelRef && panelRef.contains(e.target as Node)) return;
    close();
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); close(); triggerRef?.focus(); return; }
    if (e.key === 'Tab') { close(); return; }
    const keys = orderedKeys();
    // Enter toggles the focused row. Space also toggles, but only when there is
    // no search input eating it as a character.
    if (e.key === 'Enter' || (e.key === ' ' && !searchable())) {
      const i = focusedIdx();
      if (i >= 0 && i < keys.length) { e.preventDefault(); toggleKey(keys[i]); }
      return;
    }
    const next = nextIndexLinear(e.key, focusedIdx(), keys.length, /*wrap*/ false);
    if (next !== null && next !== focusedIdx()) {
      e.preventDefault();
      setFocusedIdx(next);
      queueMicrotask(() => panelRef?.querySelector('.msd-row.focused')?.scrollIntoView({ block: 'nearest' }));
    }
  }

  function close() {
    setOpen(false);
    setQuery('');
    endReorder();
    document.removeEventListener('mousedown', handleClickOutside);
    document.removeEventListener('keydown', handleKeyDown);
  }

  function open_() {
    if (!triggerRef) return;
    const s = globalDesign.uiScale || 1;
    const rect = triggerRef.getBoundingClientRect();
    const top = rect.bottom / s;
    const triggerTop = rect.top / s;
    const width = rect.width / s;
    const viewH = window.innerHeight / s;
    const viewW = window.innerWidth / s;
    const rightFromViewport = viewW - rect.right / s;
    const rows = visible().length + hidden().length + (hidden().length ? 1 : 0);
    const headerH = (props.headerActions ? 28 : 0) + (searchable() ? 33 : 0);
    const footerH = props.footer ? 40 : 0;
    const panelHeight = Math.min(rows * 28 + 8 + headerH + footerH, 380);
    const flipUp = top + panelHeight + 8 > viewH;
    const style: Record<string, string> = {
      right: `${rightFromViewport}px`,
      'min-width': `${Math.max(width, 220)}px`,
    };
    if (flipUp) {
      style.bottom = `${viewH - triggerTop + 4}px`;
      style.top = 'auto';
    } else {
      style.top = `${top + 4}px`;
      style.bottom = 'auto';
    }
    setPanelStyle(style);
    setQuery('');
    setFocusedIdx(-1);
    setOpen(true);
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
  }

  function toggle() {
    if (open()) close(); else open_();
  }

  function setVisible(next: string[]) {
    const known = itemByKey();
    props.onChange(next.filter(k => known.has(k)));
  }

  function toggleKey(key: string) {
    const it = itemByKey().get(key);
    if (it?.checkboxDisabled) return;
    const v = visible();
    if (v.includes(key)) setVisible(v.filter(k => k !== key));
    else setVisible([...v, key]);
  }

  function findRowKeyAt(clientX: number, clientY: number): string | null {
    const el = document.elementFromPoint(clientX, clientY) as HTMLElement | null;
    const row = el?.closest('.msd-row-draggable') as HTMLElement | null;
    return row?.dataset.msdKey ?? null;
  }

  function pointerMove(e: MouseEvent) {
    const src = dragKey();
    if (!src) return;
    const tgt = findRowKeyAt(e.clientX, e.clientY);
    setDragOverKey(tgt);
    if (!tgt || tgt === src) return;
    const tgtItem = itemByKey().get(tgt);
    if (tgtItem?.reorderDisabled) return;
    if (props.onReorder) {
      props.onReorder(src, tgt);
      return;
    }
    const list = visible().slice();
    const si = list.indexOf(src);
    const ti = list.indexOf(tgt);
    if (si < 0 || ti < 0 || si === ti) return;
    const [moved] = list.splice(si, 1);
    list.splice(ti, 0, moved);
    setVisible(list);
  }

  function pointerUp() {
    document.removeEventListener('mousemove', pointerMove);
    document.removeEventListener('mouseup', pointerUp);
    setDragKey(null);
    setDragOverKey(null);
  }

  function startReorder(e: MouseEvent, key: string) {
    if (e.button !== 0) return;
    if (!dragEnabled()) return;
    const it = itemByKey().get(key);
    if (it?.reorderDisabled) return;
    e.preventDefault();
    setDragKey(key);
    setDragOverKey(key);
    document.addEventListener('mousemove', pointerMove);
    document.addEventListener('mouseup', pointerUp);
  }

  function endReorder() {
    setDragKey(null);
    setDragOverKey(null);
    document.removeEventListener('mousemove', pointerMove);
    document.removeEventListener('mouseup', pointerUp);
  }

  createEffect(() => {
    if (!open()) return;
    queueMicrotask(() => searchRef?.focus());
    queueMicrotask(() => {
      if (!panelRef || !triggerRef) return;
      const s = globalDesign.uiScale || 1;
      const viewW = window.innerWidth / s;
      const rect = triggerRef.getBoundingClientRect();
      const panelW = panelRef.offsetWidth;
      const triggerRight = rect.right / s;
      if (triggerRight - panelW < 8) {
        const newRight = Math.max(8, viewW - panelW - 8);
        setPanelStyle({ ...panelStyle(), right: `${newRight}px` });
      }
    });
  });

  onCleanup(() => {
    document.removeEventListener('mousedown', handleClickOutside);
    document.removeEventListener('keydown', handleKeyDown);
    document.removeEventListener('mousemove', pointerMove);
    document.removeEventListener('mouseup', pointerUp);
  });

  // Hidden-section drag enabled only when caller manages full-list reorder.
  const hiddenDraggable = () => !!props.onReorder;

  const renderRowIcon = (item: MultiSelectItem) => {
    if (props.renderIcon) return props.renderIcon(item);
    if (item.icon) return <Dynamic component={item.icon} size={13} strokeWidth={1.5} />;
    return null;
  };

  const Row = (p: { key: string; checked: boolean; draggable: boolean }) => {
    const item = () => itemByKey().get(p.key);
    return (
      <div
        class="msd-row"
        classList={{
          'msd-row-draggable': p.draggable,
          'drag-src': dragKey() === p.key,
          'drag-over': dragKey() !== null && dragKey() !== p.key && dragOverKey() === p.key,
          focused: focusedKey() === p.key,
        }}
        role="option"
        aria-selected={p.checked}
        data-msd-key={p.key}
        title={item()?.rowTitle}
        onMouseDown={p.draggable ? (e) => startReorder(e, p.key) : undefined}
      >
        <span class="msd-handle" classList={{ 'is-disabled': !p.draggable }} title={p.draggable ? t('display.dragReorder') : undefined}>
          <Show when={p.draggable}>
            <GripVertical size={12} strokeWidth={1.5} />
          </Show>
        </span>
        <span
          class="msd-check"
          classList={{ checked: p.checked, disabled: !!item()?.checkboxDisabled }}
          onMouseDown={(e) => e.stopPropagation()}
          onClick={(e) => { e.stopPropagation(); toggleKey(p.key); }}
        >
          <Show when={p.checked}><Check size={10} strokeWidth={2.5} /></Show>
        </span>
        <span class="msd-icon">
          <Show when={item()}>
            {renderRowIcon(item()!)}
          </Show>
        </span>
        <span
          class="msd-label"
          onMouseDown={(e) => e.stopPropagation()}
          onClick={(e) => { e.stopPropagation(); toggleKey(p.key); }}
        >{item()?.label ?? p.key}</span>
        <Show when={props.rowActions && item()}>
          <span class="msd-row-actions" onMouseDown={(e) => e.stopPropagation()} onClick={(e) => e.stopPropagation()}>
            {props.rowActions!(item()!)}
          </span>
        </Show>
      </div>
    );
  };

  return (
    <>
      <button
        class={`dropdown-trigger msd-trigger${open() ? ' open' : ''}${props.triggerClass ? ' ' + props.triggerClass : ''}`}
        onClick={toggle}
        ref={triggerRef}
        aria-haspopup="listbox"
        aria-expanded={open()}
      >
        <span class="msd-trigger-label">{triggerLabel()}</span>
        <ChevronDown size={12} strokeWidth={1.5} class="dropdown-chevron" />
      </button>
      <Show when={open()}>
        <Portal mount={popupMount()}>
          <div class="dropdown-panel msd-panel" style={panelStyle()} ref={panelRef} role="listbox">
            <Show when={props.headerActions}>
              <div class="msd-header-actions">{props.headerActions!()}</div>
            </Show>
            <Show when={searchable()}>
              <input
                ref={searchRef}
                class="dropdown-search"
                type="text"
                value={query()}
                placeholder={props.searchPlaceholder ?? t('folder.columnsSearchPlaceholder')}
                onInput={(e) => { setQuery(e.currentTarget.value); setFocusedIdx(-1); }}
                autocomplete="off"
                spellcheck={false}
              />
            </Show>
            <Show when={visibleFiltered().length === 0 && hiddenFiltered().length === 0}>
              <div class="dropdown-empty">{t('common.noResults')}</div>
            </Show>
            <For each={visibleFiltered()}>
              {(key) => <Row key={key} checked={true} draggable={dragEnabled() && !itemByKey().get(key)?.reorderDisabled} />}
            </For>
            <Show when={hiddenFiltered().length > 0 && visibleFiltered().length > 0}>
              <div class="msd-sep" />
            </Show>
            <For each={hiddenFiltered()}>
              {(key) => <Row key={key} checked={false} draggable={dragEnabled() && hiddenDraggable() && !itemByKey().get(key)?.reorderDisabled} />}
            </For>
            <Show when={props.footer}>
              <div class="msd-footer">{props.footer!()}</div>
            </Show>
          </div>
        </Portal>
      </Show>
    </>
  );
}

import { createSignal, createEffect, For, Show } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import { open } from '@tauri-apps/plugin-dialog';
import { invoke } from '../../../src/ipc.ts';
import { getCurrentWindow } from '@tauri-apps/api/window';
import {
  Database, Globe, Folder as FolderIcon, GripVertical, X as XIcon,
  Monitor, LayoutGrid, List, Columns3, MousePointer2, MousePointerClick, FolderSearch,
  LayoutTemplate, Pencil, Plus,
} from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown from '../../../src/components/Dropdown.tsx';
import '../../../src/styles/config-base.css';
import { findQuickSet } from './quickSets.ts';
import {
  itemKind, displayName as itemDisplayName, normalizeUrl,
  type ShortcutItem,
} from './items.ts';
import type { ShortcutConfigData } from './Shortcut.tsx';
import { countAssignedLeaves } from './layout.ts';
import './ShortcutConfig.css';
import { t } from '../../../src/i18n.ts';

interface ShortcutConfigProps {
  config: ShortcutConfigData;
  onChange: (c: ShortcutConfigData) => void;
  /** Identity of the widget being configured, needed to pop out the layout
   *  editor into its own window. Supplied by the config window. */
  _widgetId?: string;
  _viewId?: string;
}

export default function ShortcutConfig(props: ShortcutConfigProps) {
  const cfg = () => props.config ?? {};
  const items = () => (cfg().items ?? []) as ShortcutItem[];
  const [dragId, setDragId] = createSignal<string | null>(null);
  const [dragOverId, setDragOverId] = createSignal<string | null>(null);

  // System (Windows shell) icons for file/folder AND shell-namespace rows,
  // mirroring the widget ([S-17]) so folders show their real folder icon, files
  // their associated app icon, and This PC / Recycle Bin the icons Explorer
  // uses. Cached per path for the panel lifetime.
  const [iconCache, setIconCache] = createSignal<Record<string, string>>({});
  const iconInFlight = new Set<string>();
  createEffect(() => {
    for (const it of items()) {
      const k = itemKind(it.path);
      if (k !== 'file' && k !== 'shell') continue;
      if (iconCache()[it.path] != null) continue;
      if (iconInFlight.has(it.path)) continue;
      iconInFlight.add(it.path);
      invoke('get_file_icon', { path: it.path, size: 32 })
        .then(src => setIconCache(prev => ({ ...prev, [it.path]: src })))
        .catch(() => setIconCache(prev => ({ ...prev, [it.path]: '' })))
        .finally(() => iconInFlight.delete(it.path));
    }
  });

  function update<K extends keyof ShortcutConfigData>(key: K, value: ShortcutConfigData[K]) {
    props.onChange({ ...cfg(), [key]: value });
  }

  function writeItems(next: ShortcutItem[]) {
    props.onChange({ ...cfg(), items: next });
  }

  function removeItem(id: string) { writeItems(items().filter(x => x.id !== id)); }

  // Pop the visual layout editor out into its own OS window (see Rust
  // `open_layout_window`). Requires this widget's id + view, which the config
  // window supplies via `_widgetId` / `_viewId`.
  function openLayoutEditor(itemId: string) {
    if (!props._widgetId || !props._viewId) return;
    invoke('open_layout_window', {
      parentLabel: getCurrentWindow().label,
      widgetId: props._widgetId,
      viewId: props._viewId,
      itemId,
    }).catch(() => {});
  }

  // Open the "Add shortcut" catalogue in its own window ([S-66]). Requires this
  // widget's id + view, which the config window supplies.
  function openAddWindow() {
    if (!props._widgetId || !props._viewId) return;
    invoke('open_shortcut_add_window', {
      parentLabel: getCurrentWindow().label,
      widgetId: props._widgetId,
      viewId: props._viewId,
    }).catch(() => {});
  }

  function iconFor(item: ShortcutItem) {
    const k = itemKind(item.path);
    if (k === 'layout') return <LayoutTemplate size={16} strokeWidth={1.5} />;
    if (k === 'url') return <Globe size={16} strokeWidth={1.5} />;
    if (k === 'shell') {
      const shellSrc = iconCache()[item.path];
      if (shellSrc) return <img class="item-icon-img" src={shellSrc} alt="" />;
      const qs = findQuickSet(item.path);
      if (qs) return <Dynamic component={qs.icon} size={16} strokeWidth={1.5} />;
      return <FolderIcon size={16} strokeWidth={1.5} />;
    }
    const src = iconCache()[item.path];
    if (src) return <img class="item-icon-img" src={src} alt="" />;
    return <FolderIcon size={16} strokeWidth={1.5} />;
  }

  function onDragStart(e: DragEvent, id: string) {
    setDragId(id);
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }
  function onDragOver(e: DragEvent, id: string) {
    const src = dragId();
    if (!src) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
    setDragOverId(id);
    if (src === id) return;
    // Live reorder: move the dragged item to the hovered slot immediately
    // rather than waiting for drop. For is keyed by id, so the rows shuffle
    // under the cursor as you drag.
    const list = items().slice();
    const srcIdx = list.findIndex(x => x.id === src);
    const tgtIdx = list.findIndex(x => x.id === id);
    if (srcIdx < 0 || tgtIdx < 0 || srcIdx === tgtIdx) return;
    const [moved] = list.splice(srcIdx, 1);
    list.splice(tgtIdx, 0, moved);
    writeItems(list);
  }
  function onDrop(e: DragEvent) {
    e.preventDefault();
    setDragId(null); setDragOverId(null);
  }

  function updateItemLabel(id: string, label: string) {
    const t = label.trim();
    writeItems(items().map(x => x.id === id ? { ...x, label: t || undefined } : x));
  }

  function updateItemPath(id: string, raw: string) {
    const t = raw.trim();
    if (!t) return;
    const next = /^https?:\/\//i.test(t) ? normalizeUrl(t) : t;
    writeItems(items().map(x => x.id === id ? { ...x, path: next } : x));
  }

  async function repickItem(id: string) {
    const item = items().find(x => x.id === id);
    if (!item) return;
    // file → file picker; folder/shell → directory picker.
    const selected = await open({ directory: itemKind(item.path) !== 'file', multiple: false });
    if (typeof selected === 'string') {
      writeItems(items().map(x => x.id === id ? { ...x, path: selected } : x));
      await invoke('push_windows_to_desktop');
    }
  }

  const viewMode = () => cfg().viewMode ?? 'grid';
  const isGrid = () => viewMode() === 'grid';

  return (
    <ConfigTabs tabs={[
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
      // Empty label → ConfigTabs renders the divider but no "Shortcuts" header;
      // the inner "Items" header is the only label for this section.
      { key: 'source',  label: '',        icon: Database },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          {/* Items, with the add button in the section header's top-right; the
              catalogue of what can be added lives in its own window ([S-66]).
              The list grows with the page (no inner scroll). */}
          <div class="field">
            <div class="shortcut-items-head">
              <span class="section-label">{t('folder.itemsHeader')}</span>
              <button
                class="btn-primary shortcut-add-btn"
                title={t('shortcut.addTitle')}
                onClick={openAddWindow}
              ><Plus size={14} strokeWidth={2} />{t('common.add')}</button>
            </div>
            <Show
              when={items().length > 0}
              fallback={<div class="shortcut-items-empty">{t('shortcut.addEmpty')}</div>}
            >
              <div class="items-list shortcut-items-list">
                <For each={items()}>
                  {(it) => (
                    <div
                      class="item-row"
                      classList={{ 'drag-src': dragId() === it.id, 'drag-over': dragOverId() === it.id }}
                      onDragOver={(e) => onDragOver(e, it.id)}
                      onDrop={(e) => onDrop(e)}
                    >
                      <span
                        class="item-handle"
                        draggable={true}
                        onDragStart={(e) => onDragStart(e, it.id)}
                        onDragEnd={() => { setDragId(null); setDragOverId(null); }}
                        title={t('display.dragReorder')}
                      >
                        <GripVertical size={14} strokeWidth={1.5} />
                      </span>
                      <span class="item-icon">{iconFor(it)}</span>
                      <div class="item-text">
                        <input
                          class="item-label"
                          type="text"
                          placeholder={itemDisplayName({ ...it, label: undefined })}
                          value={it.label ?? ''}
                          onChange={(e) => updateItemLabel(it.id, e.currentTarget.value)}
                        />
                        <Show
                          when={itemKind(it.path) === 'layout'}
                          fallback={
                            <input
                              class="item-path"
                              type="text"
                              spellcheck={false}
                              value={it.path}
                              onChange={(e) => updateItemPath(it.id, e.currentTarget.value)}
                            />
                          }
                        >
                          <span class="item-path item-layout-meta">
                            {(() => { const n = countAssignedLeaves(it.layout); return n === 1 ? t('shortcut.appCountOne', { n }) : t('shortcut.appCountOther', { n }); })()}
                          </span>
                        </Show>
                      </div>
                      <Show when={itemKind(it.path) === 'layout'}>
                        <button
                          class="item-repick"
                          onClick={() => openLayoutEditor(it.id)}
                          title={t('shortcut.editLayout')}
                        ><Pencil size={13} strokeWidth={1.5} /></button>
                      </Show>
                      <Show when={itemKind(it.path) === 'file' || itemKind(it.path) === 'shell'}>
                        <button
                          class="item-repick"
                          onClick={() => repickItem(it.id)}
                          title={t('shortcut.repick')}
                        ><FolderSearch size={13} strokeWidth={1.5} /></button>
                      </Show>
                      <button
                        class="item-remove"
                        onClick={() => removeItem(it.id)}
                        title={t('common.remove')}
                      ><XIcon size={12} strokeWidth={2} /></button>
                    </div>
                  )}
                </For>
              </div>
            </Show>
            {/* Second add affordance, at the END of the list and at the item
                rows' own height, so the gesture is where the eye already is
                after reading the list ([S-61]). */}
            <button class="btn-add shortcut-add-row" onClick={openAddWindow}>
              <Plus size={14} strokeWidth={2} />{t('shortcut.addTitle')}
            </button>
          </div>
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('folder.view.label')}</span>
              <span class="design-desc">{t('shortcut.viewDesc')}</span>
            </div>
            <Dropdown
              value={viewMode()}
              options={[
                { value: 'grid',    label: t('ctxMenu.grid'),    icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
                { value: 'list',    label: t('ctxMenu.list'),    icon: () => <List size={13} strokeWidth={1.5} /> },
                { value: 'details', label: t('ctxMenu.details'), icon: () => <Columns3 size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('viewMode', v)}
            />
          </div>

          <Show when={isGrid()}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('ctxMenu.itemSize')}</span>
                <span class="design-desc">{t('folder.itemSizeGrid.desc')}</span>
              </div>
              <Dropdown
                value={cfg().gridItemSize ?? 'medium'}
                options={[
                  { value: 'small',  label: t('folder.small') },
                  { value: 'medium', label: t('folder.medium') },
                  { value: 'large',  label: t('folder.large') },
                ]}
                onChange={(v) => update('gridItemSize', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.stretchHeight.label')}</span>
                <span class="design-desc">{t('folder.stretchHeight.desc')}</span>
              </div>
              <ToggleSwitch
                checked={cfg().gridStretchHeight ?? false}
                onLabel={t('toggle.yes')}
                offLabel={t('toggle.no')}
                onChange={(v) => update('gridStretchHeight', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('ctxMenu.iconOnly')}</span>
                <span class="design-desc">{t('shortcut.iconOnlyDesc')}</span>
              </div>
              <ToggleSwitch
                checked={cfg().gridIconOnly ?? false}
                onLabel={t('toggle.yes')}
                offLabel={t('toggle.no')}
                onChange={(v) => update('gridIconOnly', v)}
              />
            </div>
          </Show>

          <Show when={!isGrid()}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('ctxMenu.itemSize')}</span>
                <span class="design-desc">{t('folder.itemSizeRow.desc')}</span>
              </div>
              <Dropdown
                value={cfg().itemSize ?? 'medium'}
                options={[
                  { value: 'small',  label: t('folder.small') },
                  { value: 'medium', label: t('folder.medium') },
                  { value: 'large',  label: t('folder.large') },
                ]}
                onChange={(v) => update('itemSize', v)}
              />
            </div>
          </Show>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('folder.openOn.label')}</span>
              <span class="design-desc">{t('shortcut.openOnDesc')}</span>
            </div>
            <Dropdown
              value={cfg().clickMode ?? 'double'}
              options={[
                { value: 'double', label: t('folder.openDouble'), icon: () => <MousePointer2 size={13} strokeWidth={1.5} /> },
                { value: 'single', label: t('folder.openSingle'), icon: () => <MousePointerClick size={13} strokeWidth={1.5} /> },
              ]}
              onChange={(v) => update('clickMode', v)}
            />
          </div>


          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('folder.showExt.label')}</span>
              <span class="design-desc">{t('folder.showExt.desc')}</span>
            </div>
            <ToggleSwitch
              checked={cfg().showFileExtension ?? false}
              onLabel={t('toggle.shown')}
              offLabel={t('toggle.hidden')}
              onChange={(v) => update('showFileExtension', v)}
            />
          </div>

        </Show>
      </>}
    </ConfigTabs>
  );
}

import { Show } from 'solid-js';
import type { Accessor } from 'solid-js';
import { open } from '@tauri-apps/plugin-dialog';
import { invoke } from '../../../src/ipc.ts';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import ToggleSwitch from '../../../src/components/ToggleSwitch.tsx';
import Dropdown, { type DropdownOption } from '../../../src/components/Dropdown.tsx';
import { List, Columns3, LayoutGrid, Grid3x3, FolderOpen, ArrowRight, Monitor, Database, MousePointer2, MousePointerClick } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import ColumnMultiSelect from './ColumnMultiSelect.tsx';
import '../../../src/styles/config-base.css';
import './FolderViewConfig.css';

// ── Type definitions ──────────────────────────────────────────────────────
interface FolderViewConfigData {
  path?: string;
  viewMode?: string;
  gridStretchHeight?: boolean;
  gridItemSize?: 'small' | 'medium' | 'large';
  itemSize?: 'xs' | 'small' | 'medium' | 'large';
  detailsColumns?: string[] | Record<string, boolean>;
  navigationMode?: string;
  clickMode?: 'single' | 'double';
  filter?: string;
  showFileExtension?: boolean;
  showHidden?: boolean;
}

interface FolderViewConfigProps {
  config?: FolderViewConfigData;
  onChange: (config: FolderViewConfigData) => void;
}

const viewModes = (): DropdownOption[] => [
  { value: 'list',    label: t('ctxMenu.list'),     icon: () => <List size={13} strokeWidth={1.5} /> },
  { value: 'details', label: t('ctxMenu.details'),  icon: () => <Columns3 size={13} strokeWidth={1.5} /> },
  { value: 'grid',    label: t('ctxMenu.grid'),     icon: () => <LayoutGrid size={13} strokeWidth={1.5} /> },
  { value: 'icons',   label: t('ctxMenu.iconGrid'), icon: () => <Grid3x3 size={13} strokeWidth={1.5} /> },
];

/** grid (labelled) + icons (icon-only) share the tile-size / stretch options. */
const isGridLike = (v: string | undefined): boolean => v === 'grid' || v === 'icons';

export default function FolderViewConfig(props: FolderViewConfigProps) {
  const config: Accessor<FolderViewConfigData>  = () => props.config ?? {};

  function update(key: string, value: unknown): void {
    props.onChange({ ...config(), [key]: value });
  }

  async function pickFolder(): Promise<void> {
    const current = config().path;
    const selected = await open({ directory: true, multiple: false, defaultPath: current || undefined });
    if (selected) props.onChange({ ...config(), path: selected as string });
    await invoke('push_windows_to_desktop');
  }

  return (
    <div class="fv-config">
      <ConfigTabs tabs={[
        { key: 'source', label: t('configTabs.source'), icon: Database },
        { key: 'display', label: t('configTabs.display'), icon: Monitor },
      ]}>
        {(tab) => <>
          <Show when={tab === 'source'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.label.label')}</span>
                <span class="design-desc">{t('folder.label.desc')}</span>
              </div>
              <div class="fv-folder-pick">
                <span class="fv-folder-path">{config().path || t('widgetUi.noneSelected')}</span>
                <button onClick={pickFolder}>{t('widgetUi.browse')}</button>
              </div>
            </div>
          </Show>

          <Show when={tab === 'display'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.view.label')}</span>
                <span class="design-desc">{t('folder.view.desc')}</span>
              </div>
              <Dropdown
                value={config().viewMode ?? 'list'}
                options={viewModes()}
                onChange={(v) => update('viewMode', v)}
              />
            </div>

            <Show when={!isGridLike(config().viewMode ?? 'list')}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('folder.itemSizeRow.label')}</span>
                  <span class="design-desc">{t('folder.itemSizeRow.desc')}</span>
                </div>
                <Dropdown
                  value={config().itemSize ?? 'medium'}
                  options={[
                    { value: 'xs',     label: t('folder.xs') },
                    { value: 'small',  label: t('folder.small') },
                    { value: 'medium', label: t('folder.medium') },
                    { value: 'large',  label: t('folder.large') },
                  ]}
                  onChange={(v) => update('itemSize', v)}
                />
              </div>
            </Show>

            <Show when={isGridLike(config().viewMode ?? 'list')}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('folder.itemSizeGrid.label')}</span>
                  <span class="design-desc">{t('folder.itemSizeGrid.desc')}</span>
                </div>
                <Dropdown
                  value={config().gridItemSize ?? 'small'}
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
                  checked={config().gridStretchHeight ?? false}
                  onLabel={t('toggle.yes')}
                  offLabel={t('toggle.no')}
                  onChange={(v) => update('gridStretchHeight', v)}
                />
              </div>
            </Show>

            <Show when={(config().viewMode ?? 'list') === 'details'}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('folder.columns.label')}</span>
                  <span class="design-desc">{t('folder.columns.desc')}</span>
                </div>
                <ColumnMultiSelect
                  value={config().detailsColumns}
                  onChange={(visible) => update('detailsColumns', visible)}
                />
              </div>
            </Show>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.openIn.label')}</span>
                <span class="design-desc">{t('folder.openIn.desc')}</span>
              </div>
              <Dropdown
                value={config().navigationMode ?? 'explorer'}
                options={[
                  { value: 'explorer', label: t('folder.navExplorer'), icon: () => <FolderOpen size={13} strokeWidth={1.5} /> },
                  { value: 'navigate', label: t('folder.navNavigate'), icon: () => <ArrowRight size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update('navigationMode', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.openOn.label')}</span>
                <span class="design-desc">{t('folder.openOn.desc')}</span>
              </div>
              <Dropdown
                value={config().clickMode ?? 'double'}
                options={[
                  { value: 'double', label: t('folder.openDouble'), icon: () => <MousePointer2 size={13} strokeWidth={1.5} /> },
                  { value: 'single', label: t('folder.openSingle'), icon: () => <MousePointerClick size={13} strokeWidth={1.5} /> },
                ]}
                onChange={(v) => update('clickMode', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.filter.label')}</span>
                <span class="design-desc">{t('folder.filter.desc')}</span>
              </div>
              <input
                class="text-input"
                type="text"
                placeholder={t('folder.filterPlaceholder')}
                value={config().filter ?? ''}
                onInput={(e) => update('filter', e.currentTarget.value)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.showExt.label')}</span>
                <span class="design-desc">{t('folder.showExt.desc')}</span>
              </div>
              <ToggleSwitch
                checked={config().showFileExtension ?? false}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update('showFileExtension', v)}
              />
            </div>

            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('folder.showHidden.label')}</span>
                <span class="design-desc">{t('folder.showHidden.desc')}</span>
              </div>
              <ToggleSwitch
                checked={config().showHidden ?? false}
                onLabel={t('toggle.shown')}
                offLabel={t('toggle.hidden')}
                onChange={(v) => update('showHidden', v)}
              />
            </div>

          </Show>
        </>}
      </ConfigTabs>
    </div>
  );
}

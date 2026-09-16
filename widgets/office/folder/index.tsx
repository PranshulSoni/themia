import { lazy } from 'solid-js';
import { invoke } from '../../../src/ipc.ts';
import FolderView from './FolderView.tsx';
import {
  Folder, ExternalLink, List, Table, LayoutGrid, Grid3x3,
  Eye, EyeOff, Columns3,
} from 'lucide-solid';
import type { WidgetDefinition, ContextMenuItem } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { resolveColumns } from './columns.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const FolderViewConfig = lazy(() => import('./FolderViewConfig.tsx'));

// Context-menu items in this widget set `keepOpen` (consumed by the menu store)
// which is not part of the base ContextMenuItem shape.
type KeepOpenItem = ContextMenuItem & { keepOpen?: boolean };

const folder: WidgetDefinition = {
  component: FolderView,
  configComponent: FolderViewConfig,
  get label() { return t('widget.folder.label'); },
  get description() { return t('widget.folder.description'); },
  icon: () => <Folder size={20} strokeWidth={1.5} />,
  category: 'Office',
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    path: '', showHidden: false, viewMode: 'list', itemSize: 'medium',
    navigationMode: 'explorer', clickMode: 'double', filter: '', showFileExtension: false,
    gridStretchHeight: false, gridItemSize: 'small',
    detailsColumns: ['size', 'modified', 'type'],
  },
  contextActions: (config, setConfig, viewActions) => [
    {
      icon: ExternalLink, label: t('folder.openInExplorer'),
      disabled: !config.path,
      action: () => invoke('open_in_explorer', { path: String(config.path ?? '') }),
    },
    { sep: true },
    ...viewActions(config, setConfig, 'viewMode', [
      ['list',    t('ctxMenu.list'),     List],
      ['details', t('ctxMenu.details'),  Table],
      ['grid',    t('ctxMenu.grid'),     LayoutGrid],
      ['icons',   t('ctxMenu.iconGrid'), Grid3x3],
    ]),
    ...(config.viewMode === 'grid' || config.viewMode === 'icons' ? [
      { sep: true },
      { header: true, label: t('ctxMenu.gridSize') },
      ...(['small', 'medium', 'large'] as const).map(size => ({
        icon: LayoutGrid,
        label: size === 'small' ? t('folder.small') : size === 'medium' ? t('folder.medium') : t('folder.large'),
        active: (config.gridItemSize ?? 'small') === size,
        keepOpen: true,
        action: () => setConfig({ gridItemSize: size }),
      })),
    ] as KeepOpenItem[] : []),
    ...(config.viewMode !== 'grid' && config.viewMode !== 'icons' ? [
      { sep: true },
      { header: true, label: t('ctxMenu.itemSize') },
      ...(['xs', 'small', 'medium', 'large'] as const).map(size => ({
        icon: List,
        label: size === 'xs' ? t('folder.xs') : size === 'small' ? t('folder.small') : size === 'medium' ? t('folder.medium') : t('folder.large'),
        active: (config.itemSize ?? 'medium') === size,
        keepOpen: true,
        action: () => setConfig({ itemSize: size }),
      })),
    ] as KeepOpenItem[] : []),
    { sep: true },
    {
      icon: config.showHidden ? Eye : EyeOff,
      label: config.showHidden ? t('folder.hideHidden') : t('folder.showHidden_'),
      action: () => setConfig({ showHidden: !config.showHidden }),
    },
    {
      icon: config.showFileExtension ? Eye : EyeOff,
      label: config.showFileExtension ? t('folder.hideExt') : t('folder.showExt_'),
      action: () => setConfig({ showFileExtension: !config.showFileExtension }),
    },
    ...(config.viewMode === 'details' ? (() => {
      const toggleCol = (key: string) => {
        const visible = resolveColumns(config.detailsColumns).visible;
        const next = visible.includes(key)
          ? visible.filter(k => k !== key)
          : [...visible, key];
        setConfig({ detailsColumns: next });
      };
      const isVis = (key: string) => resolveColumns(config.detailsColumns).visible.includes(key);
      const ctxCols: Array<[string, string]> = [
        ['size',      t('folder.colSize')],
        ['modified',  t('folder.colModified')],
        ['type',      t('folder.colType')],
        ['cloudSync', t('folder.colCloudSync')],
        ['gitStatus', t('folder.colGitStatus')],
      ];
      return [
        { sep: true },
        { header: true, label: t('ctxMenu.columns') },
        ...ctxCols.map(([key, label]) => ({
          icon: Columns3,
          label,
          active: isVis(key),
          keepOpen: true,
          action: () => toggleCol(key),
        })),
      ] as KeepOpenItem[];
    })() : []),
  ],
};

export default folder;

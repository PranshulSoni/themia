import { lazy } from 'solid-js';
import Shortcut from './Shortcut.tsx';
import {
  Link, List, LayoutGrid, Table,
  Eye, EyeOff, MousePointer, MousePointerClick,
  StretchHorizontal,
} from 'lucide-solid';
import type { WidgetDefinition, ContextMenuItem } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ShortcutConfig = lazy(() => import('./ShortcutConfig.tsx'));

const shortcut: WidgetDefinition = {
  component: Shortcut,
  configComponent: ShortcutConfig,
  get label() { return t('widget.shortcut.label'); },
  get description() { return t('widget.shortcut.description'); },
  icon: () => <Link size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Office',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    items: [],
    viewMode: 'grid',
    itemSize: 'medium',
    gridItemSize: 'medium',
    gridStretchHeight: false,
    gridIconOnly: false,
    clickMode: 'double',
    showFileExtension: false,
    label: '',
  },
  contextActions: (config, setConfig, viewActionsFn) => [
    ...viewActionsFn(config, setConfig, 'viewMode', [
      ['grid',    t('ctxMenu.grid'),    LayoutGrid],
      ['list',    t('ctxMenu.list'),    List],
      ['details', t('ctxMenu.details'), Table],
    ]),
    ...((config.viewMode ?? 'grid') === 'grid' ? [
      { sep: true },
      { header: true, label: t('ctxMenu.gridSize') },
      ...(['small', 'medium', 'large'] as const).map(sz => ({
        icon: LayoutGrid,
        label: sz === 'small' ? t('folder.small') : sz === 'medium' ? t('folder.medium') : t('folder.large'),
        active: (config.gridItemSize ?? 'medium') === sz,
        keepOpen: true,
        action: () => setConfig({ gridItemSize: sz }),
      })),
      {
        icon: StretchHorizontal,
        label: config.gridStretchHeight ? t('ctxMenu.unstretchHeight') : t('ctxMenu.stretchHeight'),
        active: !!config.gridStretchHeight,
        action: () => setConfig({ gridStretchHeight: !config.gridStretchHeight }),
      },
      {
        icon: config.gridIconOnly ? Eye : EyeOff,
        label: config.gridIconOnly ? t('ctxMenu.showLabels') : t('ctxMenu.iconOnly'),
        active: !!config.gridIconOnly,
        action: () => setConfig({ gridIconOnly: !config.gridIconOnly }),
      },
    ] as ContextMenuItem[] : [
      { sep: true },
      { header: true, label: t('ctxMenu.itemSize') },
      ...(['small', 'medium', 'large'] as const).map(sz => ({
        icon: List,
        label: sz === 'small' ? t('folder.small') : sz === 'medium' ? t('folder.medium') : t('folder.large'),
        active: (config.itemSize ?? 'medium') === sz,
        keepOpen: true,
        action: () => setConfig({ itemSize: sz }),
      })),
    ] as ContextMenuItem[]),
    { sep: true },
    {
      icon: (config.clickMode ?? 'double') === 'single' ? MousePointerClick : MousePointer,
      label: (config.clickMode ?? 'double') === 'single' ? t('ctxMenu.openDouble') : t('ctxMenu.openSingle'),
      action: () => setConfig({ clickMode: (config.clickMode ?? 'double') === 'single' ? 'double' : 'single' }),
    },
    {
      icon: config.showFileExtension ? Eye : EyeOff,
      label: config.showFileExtension ? t('folder.hideExt') : t('folder.showExt_'),
      action: () => setConfig({ showFileExtension: !config.showFileExtension }),
    },
  ],
};

export default shortcut;

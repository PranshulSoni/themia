import { lazy } from 'solid-js';
import Apps from './Apps.tsx';
import { Rocket, RefreshCw, LayoutGrid, List, AlignJustify } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const AppsConfig = lazy(() => import('./AppsConfig.tsx'));

const apps: WidgetDefinition = {
  component: Apps,
  configComponent: AppsConfig,
  get label() { return t('widget.apps.label'); },
  get description() { return t('widget.apps.description'); },
  icon: () => <Rocket size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    viewMode: 'grid',
    hiddenCategories: [],
    hiddenApps: [],
  },
  contextActions: (config, setConfig, viewActions) => [
    {
      icon: RefreshCw, label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
    { sep: true },
    ...viewActions(config, setConfig, 'viewMode', [
      ['grid',    t('ctxMenu.grid'),    LayoutGrid],
      ['list',    t('ctxMenu.list'),    List],
      ['compact', t('ctxMenu.compact'), AlignJustify],
    ]),
  ],
};

export default apps;

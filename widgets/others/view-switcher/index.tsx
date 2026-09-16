import { lazy } from 'solid-js';
import ViewSwitcher from './ViewSwitcher.tsx';
import { MonitorCog, Eye, EyeOff } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ViewSwitcherConfig = lazy(() => import('./ViewSwitcherConfig.tsx'));

const viewSwitcher: WidgetDefinition = {
  component: ViewSwitcher,
  configComponent: ViewSwitcherConfig,
  get label() { return t('widget.viewSwitcher.label'); },
  get description() { return t('widget.viewSwitcher.description'); },
  icon: () => <MonitorCog size={20} strokeWidth={1.5} />,
  category: 'Themia',
  defaultHeaderMode: 'none',
  defaultSize: { width: 220, height: 110 },
  defaultConfig: {
    visibleScreens: [],
    iconOnly: false,
  },
  contextActions: (config, setConfig) => [
    {
      icon: config.iconOnly ? Eye : EyeOff,
      label: config.iconOnly ? t('ctxMenu.showLabels') : t('ctxMenu.iconOnly'),
      action: () => setConfig({ iconOnly: !config.iconOnly }),
    },
  ],
};

export default viewSwitcher;

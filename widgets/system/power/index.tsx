import { lazy } from 'solid-js';
import Power from './Power.tsx';
import { Power as PowerIcon, Tag } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { ACTION_DEFS } from './actions.ts';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const PowerConfig = lazy(() => import('./PowerConfig.tsx'));

const power: WidgetDefinition = {
  component: Power,
  configComponent: PowerConfig,
  get label() { return t('widget.power.label'); },
  get description() { return t('widget.power.description'); },
  icon: () => <PowerIcon size={20} strokeWidth={1.5} />,
  // A row of icons reads fine straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'System',
  // 40px header + 12px body padding + a ~56px tile, with slack: one row of the
  // four default actions, labels included, without the body scrolling ([S-10]).
  defaultSize: { width: 260, height: 120 },
  defaultConfig: {
    visibleActions: canonicalVisible(ACTION_DEFS),
    showLabels: true,
    confirm: true,
  },
  contextActions: (config, setConfig) => [
    {
      icon: Tag,
      label: t('power.labelsLabel'),
      active: config.showLabels !== false,
      action: () => setConfig({ showLabels: config.showLabels === false }),
    },
  ],
};

export default power;

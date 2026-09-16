import { lazy } from 'solid-js';
import ScreentimeWidget from './ScreentimeWidget.tsx';
import { Hourglass } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ScreentimeConfig = lazy(() => import('./ScreentimeConfig.tsx'));

const screentime: WidgetDefinition = {
  component: ScreentimeWidget,
  configComponent: ScreentimeConfig,
  get label() { return t('widget.screentime.label'); },
  get description() { return t('widget.screentime.description'); },
  icon: () => <Hourglass size={20} strokeWidth={1.5} />,
  category: 'System',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    label: '',
    maxApps: 7,
    visibleMetrics: canonicalVisible(METRIC_DEFS),
    // [S-46] period and style are separate axes.
    period: 'day',
    displayStyle: 'doughnut',
  },
};

export default screentime;

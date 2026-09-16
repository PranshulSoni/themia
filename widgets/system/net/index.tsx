import { lazy } from 'solid-js';
import NetWidget from './NetWidget.tsx';
import { Globe } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { REFRESH_DEFAULTS } from '../refreshInterval.ts';
import { GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const NetConfig = lazy(() => import('./NetConfig.tsx'));

const net: WidgetDefinition = {
  component: NetWidget,
  configComponent: NetConfig,
  get label() { return t('widget.net.label'); },
  get description() { return t('widget.net.description'); },
  icon: () => <Globe size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'System',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    interval: REFRESH_DEFAULTS.net,
    graphSpanSec: GRAPH_SPAN_DEFAULTS.net,
    label: '',
    visibleMetrics: canonicalVisible(METRIC_DEFS),
  },
};

export default net;

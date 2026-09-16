import { lazy } from 'solid-js';
import Ping from './Ping.tsx';
import { Activity, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { REFRESH_DEFAULTS } from '../refreshInterval.ts';
import { GRAPH_SPAN_DEFAULTS } from '../graphSpan.ts';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const PingConfig = lazy(() => import('./PingConfig.tsx'));

const ping: WidgetDefinition = {
  component: Ping,
  configComponent: PingConfig,
  get label() { return t('widget.ping.label'); },
  get description() { return t('widget.ping.description'); },
  icon: () => <Activity size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'System',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    host: { label: '', address: '' },
    interval: REFRESH_DEFAULTS.ping,
    graphSpanSec: GRAPH_SPAN_DEFAULTS.ping,
    label: '',
    visibleMetrics: canonicalVisible(METRIC_DEFS),
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('common.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default ping;

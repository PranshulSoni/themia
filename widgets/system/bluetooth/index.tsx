import { lazy } from 'solid-js';
import BluetoothWidget from './BluetoothWidget.tsx';
import { Bluetooth as BluetoothIcon, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { canonicalVisible } from '../../../src/widgets/metricSet.ts';
import { METRIC_DEFS } from './metrics.ts';
import { REFRESH_DEFAULTS } from '../refreshInterval.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const BluetoothConfig = lazy(() => import('./BluetoothConfig.tsx'));

const bluetooth: WidgetDefinition = {
  component: BluetoothWidget,
  configComponent: BluetoothConfig,
  get label() { return t('widget.bluetooth.label'); },
  get description() { return t('widget.bluetooth.description'); },
  icon: () => <BluetoothIcon size={20} strokeWidth={1.5} />,
  category: 'System',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    interval: REFRESH_DEFAULTS.bluetooth,
    visibleMetrics: canonicalVisible(METRIC_DEFS),
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default bluetooth;

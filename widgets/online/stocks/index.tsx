import { lazy } from 'solid-js';
import Stocks from './Stocks.tsx';
import { TrendingUp, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const StocksConfig = lazy(() => import('./StocksConfig.tsx'));

const stocks: WidgetDefinition = {
  component: Stocks,
  configComponent: StocksConfig,
  get label() { return t('widget.stocks.label'); },
  get description() { return t('widget.stocks.description'); },
  icon: () => <TrendingUp size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultSize: { width: 220, height: 330 },
  defaultConfig: {
    symbols: [],
    refreshMinutes: 5,
    showSparkline: true,
    sparkRange: '1d',
    areaFill: false,
    showMinMax: false,
    customColors: false,
    upColor: '#22c55e',
    downColor: '#ef4444',
    dimWhenClosed: true,
    showExtendedHours: false,
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default stocks;

import { lazy } from 'solid-js';
import WeatherForecast from './WeatherForecast.tsx';
import { CalendarRange as ForecastIcon, RefreshCw, Rows3, Columns3, LayoutGrid } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const WeatherForecastConfig = lazy(() => import('./WeatherForecastConfig.tsx'));

const weatherForecast: WidgetDefinition = {
  component: WeatherForecast,
  configComponent: WeatherForecastConfig,
  get label() { return t('widget.weatherForecast.label'); },
  get description() { return t('widget.weatherForecast.description'); },
  icon: () => <ForecastIcon size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 330, height: 330 },
  defaultConfig: {
    lat: null,
    lon: null,
    locationName: '',
    unit: 'celsius',
    days: 7,
    layout: 'rows',
    refreshMinutes: 60,
    hideLocation: false,
  },
  contextActions: (config, setConfig, viewActions) => [
    {
      icon: RefreshCw, label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
    { sep: true },
    ...viewActions(config, setConfig, 'layout', [
      ['rows',    t('ctxMenu.row'),     Rows3],
      ['columns', t('ctxMenu.columns'), Columns3],
      ['grid',    t('ctxMenu.grid'),    LayoutGrid],
    ]),
  ],
};

export default weatherForecast;

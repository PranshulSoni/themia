import { lazy } from 'solid-js';
import WeatherCurrent from './WeatherCurrent.tsx';
import { CloudSun, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const WeatherCurrentConfig = lazy(() => import('./WeatherCurrentConfig.tsx'));

const weatherCurrent: WidgetDefinition = {
  component: WeatherCurrent,
  configComponent: WeatherCurrentConfig,
  get label() { return t('widget.weatherCurrent.label'); },
  get description() { return t('widget.weatherCurrent.description'); },
  icon: () => <CloudSun size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    lat: null,
    lon: null,
    locationName: '',
    unit: 'celsius',
    refreshMinutes: 15,
    hideLocation: false,
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw, label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default weatherCurrent;

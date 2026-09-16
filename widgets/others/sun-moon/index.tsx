import { lazy } from 'solid-js';
import SunMoon from './SunMoon.tsx';
import { Sunrise, SunMedium, Hash, EyeOff, List } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const SunMoonConfig = lazy(() => import('./SunMoonConfig.tsx'));

const sunMoon: WidgetDefinition = {
  component: SunMoon,
  configComponent: SunMoonConfig,
  get label() { return t('widget.sunMoon.label'); },
  get description() { return t('widget.sunMoon.description'); },
  icon: () => <Sunrise size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultHeaderMode: 'compact',
  // Tall enough for the arc and the four rows without scrolling; the
  // container-query ladder handles every size below this.
  defaultSize: { width: 250, height: 330 },
  defaultConfig: {
    lat: 47.37,
    lon: 8.54,
    place: 'Zurich',
    sunPathMode: 'numbers',
    showEvents: false,
    label: '',
  },
  contextActions: (config, setConfig, viewActions) => [
    { header: true, label: t('sunMoon.sunPath.label') },
    ...viewActions(config, setConfig, 'sunPathMode', [
      ['numbers', t('sunMoon.pathMode.numbers'), Hash],
      ['plain',   t('sunMoon.pathMode.plain'),   SunMedium],
      ['hidden',  t('sunMoon.pathMode.hidden'),  EyeOff],
    ]),
    { sep: true },
    {
      icon: List,
      label: t('sunMoon.events.label'),
      active: config.showEvents !== false,
      action: () => setConfig({ showEvents: config.showEvents === false }),
    },
  ],
};

export default sunMoon;

import { lazy } from 'solid-js';
import Countdown from './Countdown.tsx';
import { Timer } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const CountdownConfig = lazy(() => import('./CountdownConfig.tsx'));

const countdown: WidgetDefinition = {
  component: Countdown,
  configComponent: CountdownConfig,
  get label() { return t('widget.countdown.label'); },
  get description() { return t('widget.countdown.description'); },
  icon: () => <Timer size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    timers: [],
    label: '',
  },
};

export default countdown;

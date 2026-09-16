import { lazy } from 'solid-js';
import Clock from './Clock.tsx';
import { Clock as ClockIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ClockConfig = lazy(() => import('./ClockConfig.tsx'));

const clock: WidgetDefinition = {
  component: Clock,
  configComponent: ClockConfig,
  get label() { return t('widget.clock.label'); },
  get description() { return t('widget.clock.description'); },
  icon: () => <ClockIcon size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  noHeader: true,
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    // Empty timezone → the current (local) zone, i.e. a plain local clock.
    timezone: '',
    customLabel: '',
    format: '24h',
    showSeconds: true,
    showDate: true,
    offsetMode: 'none',
    style: 'digital',
    align: 'center',
  },
};

export default clock;

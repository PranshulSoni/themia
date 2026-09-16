import { lazy } from 'solid-js';
import Mixer from './Mixer.tsx';
import { SlidersHorizontal } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { REFRESH_DEFAULTS } from '../refreshInterval.ts';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const MixerConfig = lazy(() => import('./MixerConfig.tsx'));

const mixer: WidgetDefinition = {
  component: Mixer,
  configComponent: MixerConfig,
  get label() { return t('widget.mixer.label'); },
  get description() { return t('widget.mixer.description'); },
  icon: () => <SlidersHorizontal size={20} strokeWidth={1.5} />,
  category: 'System',
  // A list of rows with sliders. A row is ~41px, so this holds the master row
  // plus four apps before it starts scrolling.
  defaultSize: { width: 280, height: 250 },
  defaultConfig: {
    headerMode: 'normal',
    label: '',
    interval: REFRESH_DEFAULTS.mixer,
    groupByApp: true,
    showMaster: true,
    showSystemSounds: false,
    onlyActive: false,
    showIcons: true,
  },
};

export default mixer;

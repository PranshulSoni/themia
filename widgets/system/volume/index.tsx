import { lazy } from 'solid-js';
import Volume from './Volume.tsx';
import { Volume2 } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const VolumeConfig = lazy(() => import('./VolumeConfig.tsx'));

const volume: WidgetDefinition = {
  component: Volume,
  configComponent: VolumeConfig,
  get label() { return t('widget.volume.label'); },
  get description() { return t('widget.volume.description'); },
  icon: () => <Volume2 size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'System',
  defaultSize: { width: 240, height: 60 },
  defaultConfig: {
    headerMode: 'none',
    label: '',
  },
};

export default volume;

import { lazy } from 'solid-js';
import Music from './Music.tsx';
import { Music as MusicIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const MusicConfig = lazy(() => import('./MusicConfig.tsx'));

const music: WidgetDefinition = {
  component: Music,
  configComponent: MusicConfig,
  get label() { return t('widget.music.label'); },
  get description() { return t('widget.music.description'); },
  icon: () => <MusicIcon size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  // [S-21] The title bar is pure chrome here — the track title right beneath it
  // already says what the widget is — so the slim label-led band is the default
  // and the 40px normal band is opt-in.
  defaultHeaderMode: 'compact',
  defaultSize: { width: 330, height: 220 },
  defaultConfig: {
    pollInterval: 1,
    showAlbumArt: true,
    showSpectrum: false,
    // [S-20] Default is the pre-existing behaviour: always visible.
    hideWhenIdle: 'never',
    label: '',
  },
};

export default music;

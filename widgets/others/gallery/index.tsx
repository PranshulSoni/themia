import { lazy } from 'solid-js';
import Gallery from './Gallery.tsx';
import { Image, LayoutGrid, Play, Shuffle, SquareStack, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GalleryConfig = lazy(() => import('./GalleryConfig.tsx'));

const gallery: WidgetDefinition = {
  component: Gallery,
  configComponent: GalleryConfig,
  get label() { return t('widget.gallery.label'); },
  get description() { return t('widget.gallery.description'); },
  icon: () => <Image size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultSize: { width: 330, height: 330 },
  defaultConfig: {
    source: 'folder',
    path: '',
    filePath: '',
    recursive: false,
    showCaption: true,
    mode: 'grid',
    fit: 'cover',
    maxImages: 12,
    slideshowInterval: 10,
    refreshInterval: 60,
    refreshNonce: 0,
  },
  contextActions: (config, setConfig, viewActions) => [
    // Layout switching only applies to the multi-image folder source.
    ...((config.source ?? 'folder') === 'folder' ? [
      ...viewActions(config, setConfig, 'mode', [
        ['grid', t('ctxMenu.grid'), LayoutGrid],
        ['slideshow', t('ctxMenu.slideshow'), Play],
        ['scatter', t('ctxMenu.scatter'), Shuffle],
        ['mosaic', t('ctxMenu.mosaic'), SquareStack],
      ]),
      { sep: true },
    ] : []),
    {
      icon: RefreshCw,
      label: t('ctxMenu.refreshImages'),
      action: () => setConfig({ ...config, refreshNonce: Date.now() }),
    },
  ],
};

export default gallery;

import { lazy } from 'solid-js';
import VideoStream from './VideoStream.tsx';
import { Video, RefreshCw, Pause, Play, RotateCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { resolveConfig, nextRotation, DEFAULT_FIT, DEFAULT_ROTATION, LIVE_REFRESH_SECONDS } from './videoStream.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const VideoStreamConfig = lazy(() => import('./VideoStreamConfig.tsx'));

const videoStream: WidgetDefinition = {
  component: VideoStream,
  configComponent: VideoStreamConfig,
  get label() { return t('widget.videoStream.label'); },
  get description() { return t('widget.videoStream.description'); },
  icon: () => <Video size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 320, height: 240 },
  defaultConfig: {
    url: '',
    label: '',
    fit: DEFAULT_FIT,
    rotation: DEFAULT_ROTATION,
    paused: false,
    refreshSeconds: LIVE_REFRESH_SECONDS,
  },
  contextActions: (config, setConfig) => {
    const cfg = resolveConfig(config);
    return [
      {
        icon: cfg.paused ? Play : Pause,
        label: cfg.paused ? t('videoStream.resume') : t('videoStream.pause'),
        action: () => setConfig({ paused: !cfg.paused }),
      },
      {
        icon: RotateCw,
        label: t('videoStream.rotate'),
        action: () => setConfig({ rotation: nextRotation(cfg.rotation) }),
      },
      {
        icon: RefreshCw,
        label: t('ctxMenu.refresh'),
        action: () => setConfig({ _refresh: Date.now() }),
      },
    ];
  },
};

export default videoStream;

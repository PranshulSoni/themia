import { lazy } from 'solid-js';
import Rss from './Rss.tsx';
import { Rss as RssIcon, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const RssConfig = lazy(() => import('./RssConfig.tsx'));

const rss: WidgetDefinition = {
  component: Rss,
  configComponent: RssConfig,
  get label() { return t('widget.rss.label'); },
  get description() { return t('widget.rss.description'); },
  icon: () => <RssIcon size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    feedIds: [],
    refreshMinutes: 15,
    maxItems: 20,
    fields: ['source', 'preview', 'date'],
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default rss;

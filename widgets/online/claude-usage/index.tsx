import { lazy } from 'solid-js';
import ClaudeUsage from './ClaudeUsage.tsx';
import { Gauge, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { DEFAULT_REFRESH_MINUTES } from './claudeUsage.ts';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ClaudeUsageConfig = lazy(() => import('./ClaudeUsageConfig.tsx'));

const claudeUsage: WidgetDefinition = {
  component: ClaudeUsage,
  configComponent: ClaudeUsageConfig,
  get label() { return t('widget.claudeUsage.label'); },
  get description() { return t('widget.claudeUsage.description'); },
  icon: () => <Gauge size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  providers: ['Claude'],
  defaultSize: { width: 240, height: 180 },
  defaultConfig: {
    accountIds: [],
    refreshMinutes: DEFAULT_REFRESH_MINUTES,
    label: '',
    showOpus: false,
    showSonnet: false,
  },
  contextActions: (_config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('common.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default claudeUsage;

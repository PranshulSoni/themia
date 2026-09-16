import { lazy } from 'solid-js';
import GitHubNotifications from './GitHubNotifications.tsx';
import { Bell, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GitHubNotificationsConfig = lazy(() => import('./GitHubNotificationsConfig.tsx'));

const githubNotifications: WidgetDefinition = {
  component: GitHubNotifications,
  configComponent: GitHubNotificationsConfig,
  get label() { return t('widget.githubNotifications.label'); },
  get description() { return t('widget.githubNotifications.description'); },
  icon: () => <Bell size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 330 },
  defaultConfig: {
    accountIds: [],
    refreshMinutes: 5,
    label: '',
    count: 5,
    includeRead: false,
    participatingOnly: false,
  },
  contextActions: (_config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default githubNotifications;

import { lazy } from 'solid-js';
import GitHubAssignedIssues from './GitHubAssignedIssues.tsx';
import { CircleAlert, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GitHubAssignedIssuesConfig = lazy(() => import('./GitHubAssignedIssuesConfig.tsx'));

const githubAssignedIssues: WidgetDefinition = {
  component: GitHubAssignedIssues,
  configComponent: GitHubAssignedIssuesConfig,
  get label() { return t('widget.githubAssignedIssues.label'); },
  get description() { return t('widget.githubAssignedIssues.description'); },
  icon: () => <CircleAlert size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    accountIds: [],
    refreshMinutes: 5,
    label: '',
    count: 8,
    includeMentioned: false,
  },
  contextActions: (_config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default githubAssignedIssues;

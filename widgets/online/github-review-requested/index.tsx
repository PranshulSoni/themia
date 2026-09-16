import { lazy } from 'solid-js';
import GitHubReviewRequested from './GitHubReviewRequested.tsx';
import { GitPullRequestArrow, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GitHubReviewRequestedConfig = lazy(() => import('./GitHubReviewRequestedConfig.tsx'));

const githubReviewRequested: WidgetDefinition = {
  component: GitHubReviewRequested,
  configComponent: GitHubReviewRequestedConfig,
  get label() { return t('widget.githubReviewRequested.label'); },
  get description() { return t('widget.githubReviewRequested.description'); },
  icon: () => <GitPullRequestArrow size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    accountIds: [],
    refreshMinutes: 5,
    label: '',
    count: 8,
    includeDrafts: false,
  },
  contextActions: (_config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default githubReviewRequested;

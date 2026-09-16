import { lazy } from 'solid-js';
import GitHub from './GitHub.tsx';
import { GitBranch, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const GitHubConfig = lazy(() => import('./GitHubConfig.tsx'));

const github: WidgetDefinition = {
  component: GitHub,
  configComponent: GitHubConfig,
  get label() { return t('widget.github.label'); },
  get description() { return t('widget.github.description'); },
  icon: () => <GitBranch size={20} strokeWidth={1.5} />,
  category: 'Online',
  defaultHeaderMode: 'compact',
  providers: ['GitHub'],
  defaultSize: { width: 220, height: 220 },
  defaultConfig: {
    repo: '',
    accountIds: [],
    refreshMinutes: 5,
    label: '',
    commitCount: 3,
    prCount: 3,
    prState: 'open',
    prSort: 'newest',
    issueCount: 3,
    pipelineCount: 5,
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default github;

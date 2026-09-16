import { lazy } from 'solid-js';
import OneDrive from './OneDrive.tsx';
import { Cloud, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const OneDriveConfig = lazy(() => import('./OneDriveConfig.tsx'));

const onedrive: WidgetDefinition = {
  component: OneDrive,
  configComponent: OneDriveConfig,
  get label() { return t('widget.onedrive.label'); },
  get description() { return t('widget.onedrive.description'); },
  icon: () => <Cloud size={20} strokeWidth={1.5} />,
  category: 'Office',
  providers: ['Microsoft'],
  defaultSize: { width: 330, height: 440 },
  defaultConfig: {
    accountId: '',
    fileCount: 15,
    showPath: 'recent',
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default onedrive;

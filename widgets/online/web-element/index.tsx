import { lazy } from 'solid-js';
import WebElement from './WebElement.tsx';
import { SquareMousePointer, RefreshCw } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { DEFAULT_REFRESH_MINUTES } from './webElement.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const WebElementConfig = lazy(() => import('./WebElementConfig.tsx'));

const webElement: WidgetDefinition = {
  component: WebElement,
  configComponent: WebElementConfig,
  get label() { return t('widget.webElement.label'); },
  get description() { return t('widget.webElement.description'); },
  icon: () => <SquareMousePointer size={20} strokeWidth={1.5} />,
  category: 'Online',
  // Pre-release: discoverable only in developer mode ([S-33]). Existing
  // instances keep rendering for everyone.
  devOnly: true,
  defaultSize: { width: 360, height: 260 },
  defaultConfig: {
    url: '',
    selector: '',
    label: '',
    refreshMinutes: DEFAULT_REFRESH_MINUTES,
  },
  contextActions: (config, setConfig) => [
    {
      icon: RefreshCw,
      label: t('ctxMenu.refresh'),
      action: () => setConfig({ _refresh: Date.now() }),
    },
  ],
};

export default webElement;

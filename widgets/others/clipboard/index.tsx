import { lazy } from 'solid-js';
import Clipboard from './Clipboard.tsx';
import { Clipboard as ClipIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ClipboardConfig = lazy(() => import('./ClipboardConfig.tsx'));

const clipboard: WidgetDefinition = {
  component: Clipboard,
  configComponent: ClipboardConfig,
  get label() { return t('widget.clipboard.label'); },
  get description() { return t('widget.clipboard.description'); },
  icon: () => <ClipIcon size={20} strokeWidth={1.5} />,
  category: 'Others',
  defaultSize: { width: 260, height: 280 },
  defaultConfig: {
    maxItems: 50,
    label: '',
  },
};

export default clipboard;

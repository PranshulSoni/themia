import { lazy } from 'solid-js';
import Scratchpad from './Scratchpad.tsx';
import { Pencil } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const ScratchpadConfig = lazy(() => import('./ScratchpadConfig.tsx'));

const scratchpad: WidgetDefinition = {
  component: Scratchpad,
  configComponent: ScratchpadConfig,
  get label() { return t('widget.scratchpad.label'); },
  get description() { return t('widget.scratchpad.description'); },
  icon: () => <Pencil size={20} strokeWidth={1.5} />,
  category: 'Others',
  defaultSize: { width: 260, height: 240 },
  defaultConfig: {
    image: '',
    strokeColor: '#60cdff',
    penSize: 4,
    label: '',
  },
};

export default scratchpad;

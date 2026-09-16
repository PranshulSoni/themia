import { lazy } from 'solid-js';
import Stack from './Stack.tsx';
import { Layers } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const StackConfig = lazy(() => import('./StackConfig.tsx'));

const stack: WidgetDefinition = {
  component: Stack,
  configComponent: StackConfig,
  get label() { return t('widget.stack.label'); },
  get description() { return t('widget.stack.description'); },
  icon: () => <Layers size={20} strokeWidth={1.5} />,
  category: 'Layout',
  defaultSize: { width: 280, height: 360 },
  defaultConfig: {
    // Titles only until the user opens a child; at most one at a time ([S-03]).
    expandedId: null,
  },
  // The stack paints no chrome of its own ([S-07]); its sections look like
  // widgets; the container is invisible. Rows replace child headers, so the
  // stack itself has no header either.
  noHeader: true,
  noChrome: true,
};

export default stack;

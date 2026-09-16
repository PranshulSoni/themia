import { lazy } from 'solid-js';
import Tabs from './Tabs.tsx';
import { PanelTop } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const TabsConfig = lazy(() => import('./TabsConfig.tsx'));

const tabs: WidgetDefinition = {
  component: Tabs,
  configComponent: TabsConfig,
  get label() { return t('widget.tabs.label'); },
  get description() { return t('widget.tabs.description'); },
  icon: () => <PanelTop size={20} strokeWidth={1.5} />,
  category: 'Layout',
  defaultSize: { width: 280, height: 360 },
  defaultConfig: {
    activeChildId: null,
  },
  // The container paints no chrome of its own ([S-06]); the tab bar + active
  // child stand in for a header; the container itself is invisible.
  noHeader: true,
  noChrome: true,
};

export default tabs;

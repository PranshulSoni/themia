import { lazy } from 'solid-js';
import WebSearch from './WebSearch.tsx';
import { Search } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const WebSearchConfig = lazy(() => import('./WebSearchConfig.tsx'));

const webSearch: WidgetDefinition = {
  component: WebSearch,
  configComponent: WebSearchConfig,
  get label() { return t('widget.webSearch.label'); },
  get description() { return t('widget.webSearch.description'); },
  icon: () => <Search size={20} strokeWidth={1.5} />,
  category: 'Others',
  defaultSize: { width: 320, height: 56 },
  defaultConfig: {
    engine: 'google',
    headerMode: 'none',
    label: '',
  },
};

export default webSearch;

import { lazy } from 'solid-js';
import Quote from './Quote.tsx';
import { Quote as QuoteIcon } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const QuoteConfig = lazy(() => import('./QuoteConfig.tsx'));

const quote: WidgetDefinition = {
  component: Quote,
  configComponent: QuoteConfig,
  get label() { return t('widget.quote.label'); },
  get description() { return t('widget.quote.description'); },
  icon: () => <QuoteIcon size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultSize: { width: 280, height: 160 },
  defaultConfig: {
    label: '',
  },
};

export default quote;

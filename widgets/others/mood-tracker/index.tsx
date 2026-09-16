import { lazy } from 'solid-js';
import MoodTracker from './MoodTracker.tsx';
import { Smile } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { DEFAULT_HISTORY_WEEKS, DEFAULT_STORAGE_MODE } from './mood.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const MoodTrackerConfig = lazy(() => import('./MoodTrackerConfig.tsx'));

const moodTracker: WidgetDefinition = {
  component: MoodTracker,
  configComponent: MoodTrackerConfig,
  get label() { return t('widget.moodTracker.label'); },
  get description() { return t('widget.moodTracker.description'); },
  icon: () => <Smile size={20} strokeWidth={1.5} />,
  category: 'Others',
  // Tall enough that the default 8-week heatmap draws readable cells under the
  // scale, the thought box and the summary line without scrolling.
  defaultSize: { width: 240, height: 300 },
  defaultConfig: {
    entries: [],
    storageMode: DEFAULT_STORAGE_MODE,
    historyWeeks: DEFAULT_HISTORY_WEEKS,
    showNote: true,
    showStats: true,
    label: '',
  },
};

export default moodTracker;

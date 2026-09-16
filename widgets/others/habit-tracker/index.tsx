import { lazy } from 'solid-js';
import HabitTracker from './HabitTracker.tsx';
import { ListChecks } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const HabitTrackerConfig = lazy(() => import('./HabitTrackerConfig.tsx'));

const habitTracker: WidgetDefinition = {
  component: HabitTracker,
  configComponent: HabitTrackerConfig,
  get label() { return t('widget.habitTracker.label'); },
  get description() { return t('widget.habitTracker.description'); },
  icon: () => <ListChecks size={20} strokeWidth={1.5} />,
  category: 'Others',
  // [S-10] A habit list is read at a glance and the rows carry their own
  // meaning, so the tall header is pure overhead here.
  defaultHeaderMode: 'compact',
  defaultSize: { width: 240, height: 240 },
  defaultConfig: {
    // [S-18] Empty: the widget lands on the device's first habit list on first
    // mount, creating one if there is none, and writes the id back here. The
    // habits themselves live in the device-wide pool, not in this bag.
    localListId: '',
    resetPeriod: 'daily',
    customMinutes: 30,
    // [S-15] Seven, so the default daily cadence reads as the last seven days.
    historyDots: 7,
    label: '',
  },
};

export default habitTracker;

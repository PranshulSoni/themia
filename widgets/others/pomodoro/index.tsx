import { lazy } from 'solid-js';
import Pomodoro from './Pomodoro.tsx';
import { Brain } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';
import { DEFAULT_ALARM_SOUND, DEFAULT_ALARM_VOLUME } from './alarmSound.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const PomodoroConfig = lazy(() => import('./PomodoroConfig.tsx'));

const pomodoro: WidgetDefinition = {
  component: Pomodoro,
  configComponent: PomodoroConfig,
  get label() { return t('widget.pomodoro.label'); },
  get description() { return t('widget.pomodoro.description'); },
  icon: () => <Brain size={20} strokeWidth={1.5} />,
  // Glanceable enough to read straight off the wallpaper, so it offers the
  // transparent background ([S-32]).
  canTransparent: true,
  category: 'Others',
  defaultSize: { width: 220, height: 200 },
  defaultConfig: {
    workMin: 25,
    breakMin: 5,
    // The alarm ships ON: a focus timer you have to watch is the problem it
    // exists to solve (#77). Existing widgets have no persisted key, so they
    // resolve to these same defaults rather than staying silent.
    alarmSound: DEFAULT_ALARM_SOUND,
    alarmVolume: DEFAULT_ALARM_VOLUME,
    label: '',
  },
};

export default pomodoro;

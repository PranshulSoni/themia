import { Show } from 'solid-js';
import { type Component } from 'solid-js';
import {
  AlarmClock, Bell, BellOff, BellRing, Bird, Brain, Drum, Droplet, Flower,
  Gamepad2, Gem, Guitar, Music, Siren, Waves,
} from 'lucide-solid';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import Dropdown, { type DropdownOption } from '../../../src/components/Dropdown.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { t } from '../../../src/i18n.ts';
import {
  playPomodoroAlarm,
  POMODORO_ALARM_SOUNDS,
  resolveAlarmSound,
  resolveAlarmVolume,
  type PomodoroAlarmSound,
} from './alarmSound.ts';
import '../../../src/styles/config-base.css';

interface PomodoroConfigData {
  workMin?: number;
  breakMin?: number;
  alarmSound?: PomodoroAlarmSound;
  alarmVolume?: number;
  [k: string]: unknown;
}

interface PomodoroConfigProps {
  config: PomodoroConfigData;
  onChange: (c: PomodoroConfigData) => void;
}

type IconComponent = Component<{ size: number; strokeWidth: number }>;

const ALARM_ICONS: Record<PomodoroAlarmSound, IconComponent> = {
  off: BellOff,
  chime: Music,
  marimba: Drum,
  crystal: Gem,
  pluck: Guitar,
  droplet: Droplet,
  birdsong: Bird,
  zen: Flower,
  bell: BellRing,
  gong: Waves,
  arcade: Gamepad2,
  digital: AlarmClock,
  buzzer: Siren,
};

// Derived from the closed set rather than listed again, so a new ringtone cannot
// exist without appearing here. A function, not a module const: the labels must
// re-read t() after a language change ([S-09]).
function alarmSoundOptions(): DropdownOption[] {
  return POMODORO_ALARM_SOUNDS.map((value) => {
    const Icon = ALARM_ICONS[value];
    return {
      value,
      label: t(`pomodoro.alarmSound.${value}`),
      icon: () => <Icon size={14} strokeWidth={1.5} />,
    };
  });
}

export default function PomodoroConfig(props: PomodoroConfigProps) {
  const work = () => props.config.workMin ?? 25;
  const brk = () => props.config.breakMin ?? 5;
  const alarm = () => resolveAlarmSound(props.config.alarmSound);
  const volume = () => resolveAlarmVolume(props.config.alarmVolume);

  // The alarm is chosen with the eyes but judged with the ears, so every change
  // to either control previews the cue it produces. Previewing the WORK cue
  // (the ascending motif) keeps the preview stable while the user compares
  // sounds. The shared player is single-flight, so held-down volume steps
  // restart the cue rather than piling up.
  const preview = (sound: PomodoroAlarmSound, volumePct: number) =>
    playPomodoroAlarm(sound, 'work', volumePct);

  // The hand-rolled clamp is gone: NumberStepper enforces min/max itself, which
  // is the whole reason the shared control exists.

  return (
    <ConfigTabs
      tabs={[
        { key: 'timing', label: t('pomodoro.timing'), icon: Brain },
        { key: 'alarm', label: t('pomodoro.alarm'), icon: Bell },
      ]}
    >
      {(tab) => (
        <>
          <Show when={tab === 'timing'}>
            {/* `.section-label` is a section HEADING, not a field label: using it
                here rendered an uppercase heading per field and left the rows with
                no description. These are `design-row`s like every other config.
                NumberStepper carries the unit, so it drops out of the label. */}
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('pomodoro.focusLength.label')}</span>
                <span class="design-desc">{t('pomodoro.focusLength.desc')}</span>
              </div>
              <NumberStepper
                value={work()}
                min={1}
                max={180}
                unit="min"
                onChange={(v) => props.onChange({ ...props.config, workMin: v })}
              />
            </div>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('pomodoro.breakLength.label')}</span>
                <span class="design-desc">{t('pomodoro.breakLength.desc')}</span>
              </div>
              <NumberStepper
                value={brk()}
                min={1}
                max={180}
                unit="min"
                onChange={(v) => props.onChange({ ...props.config, breakMin: v })}
              />
            </div>
          </Show>

          <Show when={tab === 'alarm'}>
            <div class="design-row">
              <div class="design-left">
                <span class="design-label">{t('pomodoro.alarmSound.label')}</span>
                <span class="design-desc">{t('pomodoro.alarmSound.desc')}</span>
              </div>
              <Dropdown
                value={alarm()}
                options={alarmSoundOptions()}
                onChange={(v) => {
                  const sound = resolveAlarmSound(v);
                  props.onChange({ ...props.config, alarmSound: sound });
                  preview(sound, volume());
                }}
              />
            </div>
            {/* Volume only exists to serve a sound, so it disappears with `off`
                rather than sitting there doing nothing. */}
            <Show when={alarm() !== 'off'}>
              <div class="design-row">
                <div class="design-left">
                  <span class="design-label">{t('pomodoro.alarmVolume.label')}</span>
                  <span class="design-desc">{t('pomodoro.alarmVolume.desc')}</span>
                </div>
                <NumberStepper
                  value={volume()}
                  min={0}
                  max={100}
                  step={5}
                  unit="%"
                  onChange={(v) => {
                    props.onChange({ ...props.config, alarmVolume: v });
                    preview(alarm(), v);
                  }}
                />
              </div>
            </Show>
          </Show>
        </>
      )}
    </ConfigTabs>
  );
}

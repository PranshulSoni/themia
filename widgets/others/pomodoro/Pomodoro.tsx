import { createSignal, createMemo, createEffect, on, Show } from 'solid-js';
import { Play, Pause, RotateCcw, Brain, Coffee } from 'lucide-solid';
import { createPolling } from '../../../src/createPolling';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { tickCountdown, type Phase } from './pomodoroTick.ts';
import {
  playPomodoroAlarm,
  resolveAlarmSound,
  resolveAlarmVolume,
  type PomodoroAlarmSound,
} from './alarmSound.ts';
import './Pomodoro.css';
import { t } from '../../../src/i18n.ts';

export interface PomodoroProps {
  workMin?: number;
  breakMin?: number;
  alarmSound?: PomodoroAlarmSound;
  alarmVolume?: number;
  headerMode?: HeaderMode;
  label?: string;
}

export default function Pomodoro(props: PomodoroProps) {
  const workSec = () => Math.max(1, Math.round(props.workMin ?? 25)) * 60;
  const breakSec = () => Math.max(1, Math.round(props.breakMin ?? 5)) * 60;

  const [phase, setPhase] = createSignal<Phase>('work');
  const [remaining, setRemaining] = createSignal(workSec());
  const [running, setRunning] = createSignal(false);

  // 1 Hz countdown, driven by the pure reducer in pomodoroTick.ts. Event-driven
  // enough for a timer (no wall-clock drift correction needed for a UX timer of
  // this kind).
  //
  // The flip is also the alarm ([S-06]): the widget only ever announces the
  // phase it is ENTERING, and only from the instance that is actually running,
  // so a multi-monitor setup showing the same widget twice cannot echo (run
  // state is per-window and ephemeral, [S-02]).
  createPolling(() => {
    if (!running()) return;
    const next = tickCountdown({ phase: phase(), remaining: remaining() }, workSec(), breakSec());
    setPhase(next.phase);
    setRemaining(next.remaining);
    if (next.flipped) {
      playPomodoroAlarm(
        resolveAlarmSound(props.alarmSound),
        next.phase,
        resolveAlarmVolume(props.alarmVolume),
      );
    }
  }, () => 1000);

  function toggle() { setRunning((r) => !r); }

  function reset() {
    setRunning(false);
    setPhase('work');
    setRemaining(workSec());
  }

  const total = createMemo(() => (phase() === 'work' ? workSec() : breakSec()));
  const pct = createMemo(() => Math.min(100, Math.max(0, (1 - remaining() / total()) * 100)));

  // Keep `remaining` synced to the current phase's full duration while the
  // countdown is idle. Changing workMin/breakMin in config must move the clock
  // and the progress bar together (BUG #72); without this, `remaining` stays
  // seeded at its mount value while `total()` tracks the live config, so `pct`
  // reports bogus elapsed progress. While running, the 1 Hz loop owns
  // `remaining` (and reloads it on phase flip), so the guard leaves it alone.
  createEffect(on(total, (t) => {
    if (!running()) setRemaining(t);
  }, { defer: true }));

  function fmt(s: number): string {
    const m = Math.floor(s / 60);
    const ss = s % 60;
    return `${m}:${String(ss).padStart(2, '0')}`;
  }

  return (
    <div class="pomodoro-widget" classList={{ 'is-break': phase() === 'break' }}>
      <WidgetHeader
        mode={props.headerMode ?? 'normal'}
        icon={Brain}
        title={props.label || 'Pomodoro'}
      />

      <div class="widget-body pomodoro-body">
        <div class="pomodoro-phase">
          <Show when={phase() === 'work'} fallback={<><Coffee size={13} strokeWidth={1.5} /> {t('widgetUi.break')}</>}>
            <Brain size={13} strokeWidth={1.5} /> {t('widgetUi.focus')}
          </Show>
        </div>

        <div class="pomodoro-time">{fmt(remaining())}</div>

        <div class="pomodoro-track">
          <div class="pomodoro-fill" style={`width:${pct()}%`} />
        </div>

        <div class="pomodoro-controls">
          <button class="pomodoro-btn pomodoro-primary" onClick={toggle} title={running() ? 'Pause' : 'Start'}>
            <Show when={running()} fallback={<Play size={16} strokeWidth={1.5} />}>
              <Pause size={16} strokeWidth={1.5} />
            </Show>
          </button>
          <button class="pomodoro-btn" onClick={reset} title={t('pomodoro.reset')}>
            <RotateCcw size={15} strokeWidth={1.5} />
          </button>
        </div>
      </div>
    </div>
  );
}

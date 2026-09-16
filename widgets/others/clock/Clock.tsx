import { createSignal, createMemo } from 'solid-js';
import { Show, Switch, Match } from 'solid-js/web';
import { createPolling } from '../../../src/createPolling';
import './Clock.css';

export type OffsetMode = 'none' | 'utc' | 'local';
export type ClockStyle = 'digital' | 'analog' | 'stacked';
export type ClockAlign = 'left' | 'center' | 'right';

interface ClockProps {
  timezone?: string;
  customLabel?: string;
  format?: '24h' | '12h';
  showSeconds?: boolean;
  showDate?: boolean;
  offsetMode?: OffsetMode;
  style?: string;
  align?: string;
}

export function cityFromTimezone(tz: string): string {
  if (!tz) return '';
  const last = tz.split('/').pop() ?? tz;
  return last.replace(/_/g, ' ');
}

function partsToMap(parts: Intl.DateTimeFormatPart[]): Record<string, string> {
  const m: Record<string, string> = {};
  for (const p of parts) m[p.type] = p.value;
  return m;
}

// Resolve a timezone's offset from UTC in minutes at instant `d`.
// Uses the GMT±H[:MM] string from `shortOffset` and parses it directly to
// avoid Date math that's sensitive to local-time interpretation.
function tzOffsetMinutes(d: Date, tz: string): number {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: tz,
      timeZoneName: 'shortOffset',
    }).formatToParts(d);
    const name = parts.find(p => p.type === 'timeZoneName')?.value ?? 'GMT';
    if (name === 'GMT') return 0;
    const m = name.match(/^GMT([+-])(\d{1,2})(?::(\d{2}))?$/);
    if (!m) return 0;
    const sign = m[1] === '-' ? -1 : 1;
    return sign * (parseInt(m[2], 10) * 60 + (m[3] ? parseInt(m[3], 10) : 0));
  } catch {
    return 0;
  }
}

function formatRelativeOffset(minutes: number): string {
  if (minutes === 0) return 'same as local';
  const sign = minutes > 0 ? '+' : '−';
  const abs = Math.abs(minutes);
  const h = Math.floor(abs / 60);
  const m = abs % 60;
  return m === 0 ? `${sign}${h}h local` : `${sign}${h}:${String(m).padStart(2, '0')}h local`;
}

export default function Clock(props: ClockProps) {
  // Local timezone is the user's resolved IANA zone (e.g. 'Europe/Zurich').
  const localTz = createMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone);
  // Empty timezone → the current (local) zone. This is what makes the plain
  // Clock a world clock pinned to "here".
  const timezone = () => props.timezone || localTz();
  const isLocal = () => !props.timezone || props.timezone === localTz();
  const format = () => props.format ?? '24h';
  const showSeconds = () => props.showSeconds ?? true;
  const showDate = () => props.showDate ?? true;
  const offsetMode = (): OffsetMode => props.offsetMode ?? 'none';
  const style = () => props.style ?? 'digital';
  const align = () => props.align ?? 'center';

  const [time, setTime] = createSignal(new Date());
  createPolling(() => setTime(new Date()), () => 1000);

  // Label shows the custom label if set; otherwise the city of a *non-local*
  // timezone. A clock on the local zone stays label-free (the classic look).
  const resolvedLabel = createMemo(() => {
    const custom = props.customLabel?.trim();
    if (custom && custom.length > 0) return custom;
    return isLocal() ? '' : cityFromTimezone(timezone());
  });

  const timeFmt = createMemo(() => new Intl.DateTimeFormat('en-US', {
    timeZone: timezone(),
    hour: '2-digit',
    minute: '2-digit',
    ...(showSeconds() ? { second: '2-digit' } : {}),
    hour12: format() === '12h',
    hourCycle: format() === '12h' ? 'h12' : 'h23',
  }));

  const displayParts = createMemo(() => partsToMap(timeFmt().formatToParts(time())));

  const formatted = createMemo(() => {
    const parts = displayParts();
    const hour = parts.hour ?? '00';
    const minute = parts.minute ?? '00';
    const second = parts.second;
    const dayPeriod = parts.dayPeriod;
    const main = showSeconds() && second
      ? `${hour}:${minute}:${second}`
      : `${hour}:${minute}`;
    return {
      main,
      ampm: format() === '12h' ? (dayPeriod ?? null) : null,
    };
  });

  // Stacked layout: hours over minutes. Strip the leading zero from 12h hours
  // so it reads "2" not "02"; keep zero-padding in 24h.
  const stacked = createMemo(() => {
    const parts = displayParts();
    const hourRaw = parts.hour ?? '00';
    return {
      hours: format() === '12h' ? String(parseInt(hourRaw, 10)) : hourRaw,
      minutes: parts.minute ?? '00',
      seconds: parts.second ?? '00',
      ampm: format() === '12h' ? (parts.dayPeriod ?? null) : null,
    };
  });

  // Numeric wall-clock time in the target timezone (24h), for analog hands.
  const numFmt = createMemo(() => new Intl.DateTimeFormat('en-US', {
    timeZone: timezone(),
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }));

  const angles = createMemo(() => {
    const p = partsToMap(numFmt().formatToParts(time()));
    const h = parseInt(p.hour ?? '0', 10) % 12;
    const m = parseInt(p.minute ?? '0', 10);
    const s = parseInt(p.second ?? '0', 10);
    return {
      hour: (h + m / 60) * 30,
      minute: (m + s / 60) * 6,
      // Second hand steps once per second off the same `time()` signal as the
      // other hands (no separate rAF sweep). Seconds-of-minute are identical in
      // every IANA zone, so `s * 6` is the exact angle. One repaint per second.
      second: s * 6,
    };
  });

  const dateStr = createMemo(() => new Intl.DateTimeFormat('en-US', {
    timeZone: timezone(),
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  }).format(time()));

  const offsetStr = createMemo(() => {
    const mode = offsetMode();
    if (mode === 'none') return '';
    if (mode === 'utc') {
      try {
        const parts = new Intl.DateTimeFormat('en-US', {
          timeZone: timezone(),
          timeZoneName: 'shortOffset',
        }).formatToParts(time());
        const name = parts.find(p => p.type === 'timeZoneName')?.value ?? '';
        if (name === 'GMT') return 'UTC';
        return name.replace(/^GMT/, 'UTC');
      } catch {
        return '';
      }
    }
    // mode === 'local'
    const widgetMin = tzOffsetMinutes(time(), timezone());
    const localMin = tzOffsetMinutes(time(), localTz());
    return formatRelativeOffset(widgetMin - localMin);
  });

  return (
    <div class={`clock-widget clock-widget--${style()} clock-widget--align-${align()}`}>
      <Show when={resolvedLabel()}>
        <div class="clock-widget__label">{resolvedLabel()}</div>
      </Show>

      <Switch>
        <Match when={style() === 'analog'}>
          <svg class="clock-widget__face" viewBox="0 0 100 100" aria-hidden="true">
            <circle class="clock-widget__face-ring" cx="50" cy="50" r="48" />
            {[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map((i) => (
              <line
                class="clock-widget__tick"
                classList={{ 'clock-widget__tick--major': i % 3 === 0 }}
                x1="50"
                y1="6"
                x2="50"
                y2={i % 3 === 0 ? 12 : 9}
                transform={`rotate(${i * 30} 50 50)`}
              />
            ))}
            {/* Hands rotate via CSS transform (pivoting on transform-origin set
                in CSS), NOT the SVG `rotate(a cx cy)` attribute. Mixing the two
                double-applies the centre and flings the hand off the face. */}
            <line
              class="clock-widget__hand clock-widget__hand--hour"
              x1="50" y1="50" x2="50" y2="28"
              style={{ transform: `rotate(${angles().hour}deg)` }}
            />
            <line
              class="clock-widget__hand clock-widget__hand--minute"
              x1="50" y1="50" x2="50" y2="16"
              style={{ transform: `rotate(${angles().minute}deg)` }}
            />
            <Show when={showSeconds()}>
              {/* Steps once per second off `time()`: quartz-style tick, 1 repaint/s. */}
              <line
                class="clock-widget__hand clock-widget__hand--second"
                x1="50" y1="56" x2="50" y2="14"
                style={{ transform: `rotate(${angles().second}deg)` }}
              />
            </Show>
            <circle class="clock-widget__hub" cx="50" cy="50" r="2.5" />
          </svg>
        </Match>

        <Match when={style() === 'stacked'}>
          <div class="clock-widget__stack">
            <span class="clock-widget__stack-line">{stacked().hours}</span>
            <span class="clock-widget__stack-line">{stacked().minutes}</span>
            <Show when={showSeconds()}>
              <span class="clock-widget__stack-seconds">{stacked().seconds}</span>
            </Show>
            <Show when={stacked().ampm}>
              <span class="clock-widget__stack-ampm">{stacked().ampm}</span>
            </Show>
          </div>
        </Match>

        <Match when={true}>
          {/* digital (default) layout */}
          <div class="clock-widget__time-row">
            <span class="clock-widget__time">{formatted().main}</span>
            <Show when={formatted().ampm}>
              <span class="clock-widget__ampm">{formatted().ampm}</span>
            </Show>
          </div>
        </Match>
      </Switch>

      <Show when={showDate()}>
        <div class="clock-widget__date">{dateStr()}</div>
      </Show>
      <Show when={offsetStr()}>
        <div class="clock-widget__offset">{offsetStr()}</div>
      </Show>
    </div>
  );
}

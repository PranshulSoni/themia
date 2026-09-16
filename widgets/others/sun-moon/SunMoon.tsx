import { createSignal, createMemo, Show } from 'solid-js';
import { Sunrise, Sunset, Clock } from 'lucide-solid';
import { createPolling } from '../../../src/createPolling';
import { sunTimes, moonInfo, nextSunEvent } from './sun.ts';
import SunPath, { MoonGlyph } from './SunPath.tsx';
import { isSunPathMode, type SunPathMode } from './sunPath.ts';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import './SunMoon.css';
import { t } from '../../../src/i18n.ts';

export interface SunMoonProps {
  lat?: number;
  lon?: number;
  place?: string;
  /** 'hidden' | 'plain' | 'numbers'. */
  sunPathMode?: string;
  showEvents?: boolean;
  /** Legacy boolean the mode replaced; still read so an existing widget keeps
   *  the choice it was set to. */
  showSunPath?: boolean;
  headerMode?: HeaderMode;
  label?: string;
}

export default function SunMoon(props: SunMoonProps) {
  const lat = () => props.lat ?? 47.37;
  const lon = () => props.lon ?? 8.54;
  // Validated at the trust boundary: config is user data on disk, so an
  // out-of-set value falls back to the default rather than reaching the DOM.
  const mode = (): SunPathMode => {
    if (isSunPathMode(props.sunPathMode)) return props.sunPathMode;
    if (props.showSunPath === false) return 'hidden';
    return 'numbers';
  };
  // The two settings are independent, with one guard: something has to be on,
  // so with no graphic the list stays whatever the events toggle says.
  const showEvents = () => props.showEvents !== false || mode() === 'hidden';

  // Re-read the clock every 30 s so the countdown ticks and the day rolls over.
  const [now, setNow] = createSignal(new Date());
  createPolling(() => setNow(new Date()), () => 30_000);

  const sun = createMemo(() => sunTimes(now(), lat(), lon()));
  const moon = createMemo(() => moonInfo(now()));
  const next = createMemo(() => nextSunEvent(now(), lat(), lon()));

  const fmt = (d: Date | null) =>
    d ? d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : '—';

  const dayLength = createMemo(() => {
    const s = sun();
    if (!s.sunrise || !s.sunset) return s.polar === 'day' ? '24h' : '0h';
    const mins = Math.round((s.sunset.getTime() - s.sunrise.getTime()) / 60_000);
    return `${Math.floor(mins / 60)}h ${mins % 60}m`;
  });

  // Time remaining, shown on whichever of the two rows is next -- there is no
  // separate banner, so the row that already names the event carries the
  // countdown too.
  const countdown = createMemo(() => {
    const n = next();
    if (!n) return null;
    const totalMin = Math.max(0, Math.round((n.at.getTime() - now().getTime()) / 60_000));
    const h = Math.floor(totalMin / 60);
    const m = totalMin % 60;
    return { kind: n.kind, text: h > 0 ? `${h}h ${m}m` : `${m}m` };
  });
  const relFor = (kind: 'sunrise' | 'sunset') => {
    const c = countdown();
    return c && c.kind === kind ? c.text : null;
  };

  return (
    <div class="sunmoon-widget" classList={{ 'no-arc': mode() === 'hidden', 'events-on': showEvents() }}>
      <WidgetHeader
        mode={props.headerMode ?? 'compact'}
        icon={Sunrise}
        title={props.label || props.place || t('widget.sunMoon.label')}
      />

      <div class="widget-body">
        <div class="sunmoon-body">
          <Show when={mode() !== 'hidden'}>
            <SunPath
              lat={lat()}
              lon={lon()}
              now={now()}
              numbers={mode() === 'numbers'}
              sunrise={fmt(sun().sunrise)}
              sunset={fmt(sun().sunset)}
              daylight={dayLength()}
              rel={countdown()}
            />
          </Show>

          {/* The same four numbers as a list. Independent of the graphic --
              either, both or (with the graphic off) at least this one. */}
          <div class="sunmoon-readout">
            <div class="sunmoon-row sunmoon-row-sunrise" classList={{ 'is-next': !!relFor('sunrise') }}>
              <span class="sunmoon-ico sunrise"><Sunrise size={16} strokeWidth={1.5} /></span>
              <span class="sunmoon-key">{t('widgetUi.sunrise')}</span>
              <Show when={relFor('sunrise')}>
                {(rel) => <span class="sunmoon-rel">({t('sunMoon.inTime', { n: rel() })})</span>}
              </Show>
              <span class="sunmoon-val">{fmt(sun().sunrise)}</span>
            </div>
            <div class="sunmoon-row sunmoon-row-sunset" classList={{ 'is-next': !!relFor('sunset') }}>
              <span class="sunmoon-ico sunset"><Sunset size={16} strokeWidth={1.5} /></span>
              <span class="sunmoon-key">{t('widgetUi.sunset')}</span>
              <Show when={relFor('sunset')}>
                {(rel) => <span class="sunmoon-rel">({t('sunMoon.inTime', { n: rel() })})</span>}
              </Show>
              <span class="sunmoon-val">{fmt(sun().sunset)}</span>
            </div>
            <div class="sunmoon-row sunmoon-row-daylight">
              <span class="sunmoon-ico"><Clock size={16} strokeWidth={1.5} /></span>
              <span class="sunmoon-key">{t('widgetUi.daylight')}</span>
              <span class="sunmoon-val">{dayLength()}</span>
            </div>
            <div class="sunmoon-row sunmoon-row-moon">
              <span class="sunmoon-ico moon"><MoonGlyph phase={moon().phase} southern={lat() < 0} /></span>
              <span class="sunmoon-key">{moon().name}</span>
              <span class="sunmoon-val">{Math.round(moon().illumination * 100)}%</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

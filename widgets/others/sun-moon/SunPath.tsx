import { createMemo, createUniqueId, Show } from 'solid-js';
import { Sunrise, Sunset, Clock } from 'lucide-solid';
import { pointAt, curvePath, areaPath, moonPhasePath, type PathGeometry } from './sunPath.ts';
import {
  subsolarPoint, sublunarPoint, skyPosition, skyPathSamples, altitudeAtHourAngle, moonInfo,
} from './sun.ts';
import { t } from '../../../src/i18n.ts';

// The day/night cycle: one horizon and the sun's actual path across the sky.
//
// The path is altitude against progress -- a flatter, lower curve than a
// semicircle -- rising on the left, peaking at the real noon altitude for this
// latitude and date, setting on the right, then carrying on underneath along a
// compressed dashed curve. Elapsed daylight is filled in behind the sun.
//
// The moon sits in the corner rather than on the sun's path: the two are not on
// the same track, and a moon drawn onto the solar arc reads as though they were.
// It keeps its real phase, so how much it shines is the drawing, and dims when
// it is below the horizon.
//
// The numbers are optional ([S-10]) and live in a strip below the drawing, in
// normal flow rather than absolutely positioned over it, so the SVG gives up
// exactly the height they need and nothing overlaps at any size. Each is
// centred on the point of the curve it belongs to: the two ends and the middle.
// They are HTML, not <text> inside the SVG, which would scale with the viewBox
// and so be unreadable small and gross large.

export interface SunPathProps {
  lat: number;
  lon: number;
  now: Date;
  /** Draw the sunrise/daylight/sunset strip and the moon's percentage. */
  numbers: boolean;
  sunrise: string;
  sunset: string;
  daylight: string;
  /** Time remaining to whichever event is next, tucked under that end. */
  rel: { kind: 'sunrise' | 'sunset'; text: string } | null;
}

const VIEW = '0 0 200 150';
const OBLIQUITY = 23.4397;

/** The moon at its current phase, small enough to sit in a text row. Same
 *  drawing as the corner badge, so the two can never disagree about how much of
 *  the moon is lit. */
export function MoonGlyph(props: { phase: number; southern?: boolean; size?: number }) {
  return (
    <svg
      class="sunmoon-glyph"
      width={props.size ?? 16}
      height={props.size ?? 16}
      viewBox="-8 -8 16 16"
      aria-hidden="true"
    >
      <circle class="sp-moon-dark" cx="0" cy="0" r="7" />
      <path class="sp-moon-lit" d={moonPhasePath(0, 0, 7, props.phase, props.southern)} />
    </svg>
  );
}

export default function SunPath(props: SunPathProps) {
  // Gradient ids are document-global, so two Sun & Moon widgets on one desktop
  // would otherwise share (and fight over) one set.
  const uid = createUniqueId();
  const id = (name: string) => `sp-${name}-${uid}`;
  const url = (name: string) => `url(#${id(name)})`;

  const sunGround = createMemo(() => subsolarPoint(props.now));
  const sun = createMemo(() => skyPosition(sunGround(), props.lat, props.lon));
  const moon = createMemo(() => moonInfo(props.now));
  const moonUp = createMemo(() => skyPosition(sublunarPoint(props.now), props.lat, props.lon).up);

  const geom = createMemo<PathGeometry>(() => {
    const dec = sunGround().lat;
    return {
      // 13% and 87% of the width: the label strip anchors on the same two
      // gridlines, so a time sits centred under the end of the curve it names.
      x0: 26,
      x1: 174,
      horizonY: 104,
      aboveH: 80,
      // Top of the axis: the highest the sun ever gets here, so the curve fills
      // the height on a good day and visibly sags on a short one, instead of
      // every day being squashed into the bottom of a 90° axis.
      axisMax: Math.min(90, 90 - Math.abs(props.lat) + OBLIQUITY),
      belowH: 18,
      // The nadir for this date, so the compressed underside always uses its
      // band rather than vanishing into the horizon in summer.
      nightDepth: Math.max(1, -altitudeAtHourAngle(180, dec, props.lat)),
    };
  });

  const dayCurve = createMemo(() => skyPathSamples(sunGround().lat, props.lat, false));
  const nightCurve = createMemo(() => skyPathSamples(sunGround().lat, props.lat, true));
  const elapsed = createMemo(() =>
    skyPathSamples(sunGround().lat, props.lat, false, 32, 0, sun().progress));
  const sunPos = createMemo(() => pointAt(sun(), geom()));

  const relFor = (kind: 'sunrise' | 'sunset') =>
    props.rel && props.rel.kind === kind ? props.rel.text : null;

  return (
    <div class="sunmoon-arc">
      <div class="sp-canvas">
        <svg viewBox={VIEW} role="img" aria-label={t('sunMoon.sunPath.label')}>
          <defs>
            <linearGradient id={id('day')} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" class="sp-day-top" />
              <stop offset="100%" class="sp-day-bottom" />
            </linearGradient>
            <radialGradient id={id('glow')} cx="50%" cy="50%" r="50%">
              <stop offset="0%" class="sp-glow-in" />
              <stop offset="100%" class="sp-glow-out" />
            </radialGradient>
          </defs>

          <Show when={sun().up}>
            <path class="sp-elapsed" d={areaPath(elapsed(), geom())} fill={url('day')} />
          </Show>

          <path class="sp-track" d={curvePath(dayCurve(), geom())} />
          <path class="sp-track sp-track-night" d={curvePath(nightCurve(), geom())} />

          <Show when={sun().up}>
            <path class="sp-track-done" d={curvePath(elapsed(), geom())} />
          </Show>

          <line class="sp-horizon" x1="8" y1={geom().horizonY} x2="192" y2={geom().horizonY} />

          <g classList={{ 'sp-body': true, 'sp-down': !sun().up }}>
            <circle class="sp-sun-glow" cx={sunPos().x} cy={sunPos().y} r="22" fill={url('glow')} />
            <circle class="sp-sun" cx={sunPos().x} cy={sunPos().y} r="10" />
          </g>
        </svg>

        {/* Off the path, in the corner: the moon does not travel the sun's arc,
            and drawing it there implies it does. Its right edge sits on the
            same gridline as the sunset time. */}
        <div class="sp-moon-badge" classList={{ 'sp-down': !moonUp() }}>
          <MoonGlyph phase={moon().phase} southern={props.lat < 0} size={20} />
          <Show when={props.numbers}>
            <span class="sp-moon-pct">{Math.round(moon().illumination * 100)}%</span>
          </Show>
        </div>
      </div>

      <Show when={props.numbers}>
        <div class="sp-labels">
          <div class="sp-label sp-label-rise">
            <span class="sp-line">
              <span class="sp-ico sunrise"><Sunrise size={12} strokeWidth={1.75} /></span>
              <span class="sp-time">{props.sunrise}</span>
            </span>
            <Show when={relFor('sunrise')}>
              {(rel) => <span class="sp-rel">{t('sunMoon.inTime', { n: rel() })}</span>}
            </Show>
          </div>

          <div class="sp-label sp-label-day">
            <span class="sp-line">
              <span class="sp-ico"><Clock size={12} strokeWidth={1.75} /></span>
              <span class="sp-time">{props.daylight}</span>
            </span>
          </div>

          <div class="sp-label sp-label-set">
            <span class="sp-line">
              <span class="sp-ico sunset"><Sunset size={12} strokeWidth={1.75} /></span>
              <span class="sp-time">{props.sunset}</span>
            </span>
            <Show when={relFor('sunset')}>
              {(rel) => <span class="sp-rel">{t('sunMoon.inTime', { n: rel() })}</span>}
            </Show>
          </div>
        </div>
      </Show>
    </div>
  );
}

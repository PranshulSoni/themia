# Sun & Moon: SPEC

Experimental widget. Local sunrise/sunset + moon phase, computed
offline from a configured location. Mirror `[S-NN]` tags in tests.

- **[S-01]** Config: `lat`, `lon` (defaults Zurich 47.37 / 8.54) and `place`
  (the header label) are all set by picking a city in the shared
  `LocationSearch`; the panel offers no raw coordinate entry.
- **[S-02]** `sunTimes` uses the standard sunrise equation (`sun.ts`); returns
  local-time-rendered `Date`s. Sunrise precedes sunset at temperate latitudes.
- **[S-03]** Polar day/night (the sun never sets/rises) returns `sunrise/sunset =
  null` and `polar: 'day' | 'night'`; the UI shows `—` and 24h/0h daylight.
- **[S-04]** `moonInfo` derives phase (0..1) and illumination (0..1) from the
  synodic month off a known new moon; the phase name is one of 8 buckets.
- **[S-05]** Header toggled via the widget context menu.
- **[S-06]** The day `sunTimes` reports is the *solar* day at `lon`, not the UTC
  day: sunrise/sunset belong to the local day at the configured location in both
  hemispheres and at every longitude (Sydney +151° and Honolulu -158° included).
- **[S-07]** `nextSunEvent` returns the earliest event strictly after `now`,
  scanning the solar day before/of/after it; it never reports a past event, so
  the countdown never sticks at `0m`.
- **[S-08]** `sublunarPoint` (`sun.ts`) gives the ground point the moon is
  currently overhead, from its instantaneous right ascension and declination
  against GMST — not the rounded day index the sunrise equation uses, which
  would freeze it for a whole day.
- **[S-09]** The header defaults to `compact`.
- **[S-15]** Time remaining is shown inline on whichever of the sunrise/sunset
  rows is next — `Sunrise  (in 4h 11m)  06:15` — and that row is emphasised.
  There is no separate next-event banner.

## Day/night cycle

- **[S-10]** Two independent settings, on the config Display tab and the widget
  context menu. `sunPathMode` is a closed set — `numbers` (graphic with its
  labels, the default), `plain` (graphic alone), `hidden` — validated through
  `isSunPathMode` on read, because config is user data on disk. `showEvents`
  toggles the row list. Either, both, or neither: with the graphic hidden the
  rows are forced on, since a widget showing nothing is never the answer.
  `skyPosition` (`sun.ts`) places any
  body from its hour angle and altitude: `up` says which side of the horizon,
  `progress` runs 0 (rise) → 0.5 (crossing the meridian) → 1 (set), normalised
  by that body's own `semiDiurnalArc`, so the ends land exactly on rise and set.
- **[S-11]** The drawn curve is the sun's **real path, not a semicircle**: it
  plots altitude (`altitudeAtHourAngle`) against progress. It therefore peaks at
  the true noon altitude `90 − |lat − dec|`, crosses zero exactly at
  `±semiDiurnalArc`, flattens in winter and towers in summer. At Zurich in
  August it sits at 0.61 of its peak a quarter of the way through the day, where
  a semicircle would be at 0.71. Elapsed daylight is filled in behind the sun.
  The altitude axis tops out at the highest the sun ever reaches at that
  latitude (`90 − |lat| + 23.44°`), so the curve uses the height available while
  the seasonal difference stays visible.
- **[S-12]** The night curve carries on the same way round the sky — 0 just
  after setting (right), 0.5 at the nadir, 1 about to rise (left) — because a
  body sets on the right, passes underneath and comes back up on the left.
  Drawing it left-to-right would have the sun rising where it just set. Its
  depth is compressed against the daylight scale on purpose (`nightDepth`
  normalises it): a true-to-scale winter midnight would be deeper than the whole
  widget, and nobody reads a number off it.
- **[S-13]** `moonPhasePath` draws the lit limb for a phase in 0..1 (0 new, 0.5
  full). It is mirrored for a southern observer, because a waxing moon is lit on
  the right from Zurich and on the left from Sydney. The same path draws both
  the corner badge and the row glyph, so the two can never disagree, and the
  unlit part keeps a hairline rim so a thin crescent still reads as a moon. The
  moon is drawn *beside* the graphic, never on the solar arc: the two do not
  share a track, and putting it there implies they do. It dims below the horizon.
- **[S-14]** Responsive. **The graphic is never dropped** — the user asked for
  it, so it is sized by flexbox down to a 46px floor and keeps drawing, with
  `preserveAspectRatio` keeping it legible all the way down. Only optional
  extras shed: the countdown, then the daylight label, then the label icons and
  the moon percentage, then the whole number strip; and among the rows the moon,
  then daylight, then — only while the graphic is there to carry them — sunset
  and sunrise. With the list on its own those two always survive. The body never
  scrolls at any size.
- **[S-16]** In `numbers` mode the graphic carries them itself: sunrise under
  the left end, daylight in the middle, sunset under the right, the countdown
  beneath whichever of the two is next, and the moon's lit percentage beside its
  disc. Each is centred on the gridline of the curve point it names — 13%, 50%
  and 87% — the same anchors as `x0`/`x1` and the moon badge. The row list is
  independent of all that: under a graphic it sits at the bottom, on its own it
  sits at the top, since nothing above it pushes it down.
- **[S-17]** Labels are HTML in normal flow under the SVG, not `<text>` inside
  it: SVG text scales with the viewBox, so it would be unreadable on a small
  widget and oversized on a large one. As HTML it keeps the design system's
  sizes and `--font-scale`, and the drawing gives up exactly the height the
  strip needs instead of having it floated on top.

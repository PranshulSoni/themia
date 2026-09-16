# Clock: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

The Clock is timezone-aware: it defaults to the current (local) zone but can render any IANA zone, so it also serves as a world clock (the former standalone World Clock widget was merged in).

## Display
- [S-01] Renders time at the configured `timezone` using `Intl.DateTimeFormat` (no manual offset arithmetic).
- [S-02] Updates time once per second via `createPolling`.
- [S-03] In 12h mode, shows 12-hour clock with separate AM/PM indicator next to the time.
- [S-04] In 24h mode, zero-pads hours, minutes, seconds to two digits.
- [S-05] When `showDate` is on, renders date below time as `Weekday, Month D` (en-US locale) formatted in the widget's timezone.
- [S-06] Widget renders without a header (registry `noHeader: true`).
- [S-07] Label row above the time shows `config.customLabel` if set; otherwise the city derived from a *non-local* IANA timezone (last `/` segment, underscores → spaces). The widget cannot use a `label` config key, because that name is reserved by WidgetGrid for the widget-header title and would be overridden.
- [S-08] When `offsetMode` is `utc`, renders the UTC offset (e.g. `UTC+2`) computed via `timeZoneName: 'shortOffset'` and `GMT` normalised to `UTC`.
- [S-16] When `offsetMode` is `local`, renders the offset relative to the user's local timezone (e.g. `+8h local`, `−5h local`, `same as local`). Local zone resolved via `Intl.DateTimeFormat().resolvedOptions().timeZone`.
- [S-17] An empty/unset `timezone` resolves to the user's current (local) IANA zone: the plain local-clock case.
- [S-18] On the local zone with no `customLabel`, the label row is omitted entirely (classic clean clock look).

## Styles
- [S-10] `style` config selects clock layout: `digital` (default), `analog`, `stacked`. Root class is `clock-widget clock-widget--<style>`. Label (top) and date/offset (bottom) render across all styles.
- [S-11] `digital` style: large time row (`.clock-widget__time`) with optional AM/PM, the original default layout.
- [S-12] `analog` style: SVG clock face (`.clock-widget__face`) with hour, minute, and second hands rotated to the current time in the widget's timezone; second hand hidden when `showSeconds` is off. All hands rotate via CSS `transform` (pivoting on `transform-origin: 50px 50px`), never the SVG `rotate(a cx cy)` attribute; mixing the two double-applies the centre and flings hands off the face. All hands, including the second hand, derive their angle from the single once-per-second `time()` signal (S-02); the second hand steps (quartz-style tick, `s * 6`), not a smooth sweep. This caps the analog clock at one repaint per second; a free-running rAF sweep repainted the blurred widget ~60×/s and was a heavy idle-CPU drain.
- [S-13] `stacked` style: hours and minutes rendered as two large stacked lines (`.clock-widget__stack-line`); `:SS` seconds appended under minutes when `showSeconds` on; AM/PM shown in 12h.
- [S-25] `align` config places the clock horizontally: `left` / `center` (default) / `right`, via root class `clock-widget--align-<align>` (controls flex cross-axis alignment + `text-align`). Applies to all styles. Left/right add 14px horizontal edge padding so content (label, time, date) lines up off the edge rather than flush against it.

## Configuration
- [S-09] `timezone`: searchable Dropdown of IANA zones sourced from `Intl.supportedValuesOf('timeZone')`, prefixed with a `Current: <city>` option (empty value) that pins to the local zone.
- [S-15] `style` Dropdown selects one of the layouts (S-10).
- [S-19] `customLabel`: text input. Empty → falls back to derived city (non-local zones only); placeholder shows the derived city of the active/local zone.
- [S-20] `format`: segmented `24h` / `12h` toggle switching time display format.
- [S-21] `showSeconds` toggle appends/removes the `:SS` segment.
- [S-22] `showDate` toggle shows or hides the date line.
- [S-23] `offsetMode` Dropdown: `none` / `utc` / `local`.
- [S-26] `align` Dropdown: `left` / `center` / `right`.
- [S-24] Defaults: `timezone` empty (local), `customLabel` empty, `style` `digital`, `align` `center`, `format` `24h`, `showSeconds` true, `showDate` true, `offsetMode` `none`.

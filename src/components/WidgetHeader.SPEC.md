# Widget Header Spec

Cross-cutting. Defines the single shared header used by every widget. Behavior
tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or
reuse existing tags. When behavior is removed, delete the bullet but don't reuse
its tag.

Component: `src/components/WidgetHeader.tsx` (+ `WidgetHeader.css`).
Config control: `src/components/ConfigTabs.tsx` via `widgetHeaderContext.tsx`,
wired in `src/ConfigWindow.tsx`.

## Modes
- [S-01] `HeaderMode` is the closed set `'normal' | 'compact' | 'none'` (TS union,
  not loose string). No other value is valid anywhere in config, IPC, or storage.
- [S-02] `normal`: 40px-tall band, bottom border (`var(--border)`), icon + title on
  the left, `actions` slot on the right (`margin-left:auto`). Title is 12px / weight
  600 / ellipsis. Matches the legacy folder header.
- [S-03] `compact`: auto height (hugs text), no bottom border, no `actions` slot
  rendered, UPPERCASE label 11px / weight 600 / letter-spacing 0.06em. Matches the
  legacy weather header.
- [S-04] `none`: renders nothing (no DOM node, no reserved space). Equivalent to the
  legacy `hideHeader: true`.

## Slots / props
- [S-05] `icon`: optional lucide component reference (not JSX). Rendered at 13px /
  strokeWidth 1.5 in `normal`; hidden in `compact` (compact is label-led).
- [S-06] `title`: string. Empty string falls back to the caller-supplied default;
  caller passes `props.label || <registry default>`.
- [S-07] `secondary`: optional JSX shown in BOTH `normal` and `compact` (e.g. weather
  location, folder git branch). Hidden by the widget's own narrow container queries
  as before.
- [S-08] `actions`: optional JSX rendered ONLY in `normal` (right-aligned). In
  `compact` and `none` the slot is not inserted into the DOM.
- [S-09] `titleOverride`: optional JSX that replaces the title text in `normal` (e.g.
  folder inline search input). Ignored in `compact`.
- [S-10] The header root keeps class `widget-header` (so the global background/blur
  tokens in `App.css` apply) plus `data-header-mode="normal|compact"`. CSS owns all
  per-mode layout via the `[data-header-mode]` attribute; per-widget CSS sets no
  header `background`.

## Configuration
- [S-11] Each widget's header mode is the config field `headerMode: HeaderMode`,
  spread to the display component as `props.headerMode`. A widget with no explicit
  value resolves to its registry `defaultHeaderMode` (else `'normal'`).
- [S-12] Registry gains `defaultHeaderMode?: HeaderMode`. Weather-current and
  weather-forecast default to `'compact'`; every other widget defaults to `'normal'`.
- [S-13] Registry `noHeader: true` widgets (e.g. calculator, clock) force `'none'`
  and the config's ENTIRE Header section is hidden for them — section label, style
  row, title field and trailing divider. No "this widget has no header" copy is
  rendered; a setting that doesn't apply is absent, not explained.
- [S-14] Config Display tab shows a single 3-segment control (Normal / Compact /
  Hidden) in place of the old Shown/Hidden toggle, rendered once for all widgets via
  `ConfigTabs`. The label text input is disabled when mode is `none`.

## Migration / back-compat
- [S-15] The effective mode is resolved at render (and config) time by
  `resolveHeaderMode(config, def)` in `registry.tsx`; no destructive rewrite of
  stored configs (user data, incl. `label` overrides, is never deleted). Legacy
  `hideHeader: true` keeps meaning `'none'`. When the user picks a mode, only
  `headerMode` is written going forward (the stale `hideHeader` key is dropped from
  that config on the same write).
- [S-16] Resolution order: registry `noHeader` forces `'none'`; else explicit
  `config.headerMode` (only if a valid member of the set) wins; else legacy
  `config.hideHeader === true` → `'none'`; else registry `defaultHeaderMode`; else
  `'normal'`. An out-of-set persisted `headerMode` is ignored (falls through) with a
  loud `console.warn`, never a half-built state.
- [S-17] A widget rendered in the pop-out window (`src/WidgetWindow`) is forced to
  `'none'`, overriding both config and registry: that window is decorated and its
  OS title bar already carries the widget's label, so a widget header would show
  the same title a second time. The window IS the header.
- [S-18] The header draws the grid's chrome affordances itself. `WidgetGrid`
  publishes them through `WidgetChromeContext` (`src/widgetChrome.ts`) and the
  header renders them at the END of `.wh-actions`, after the widget's own
  actions — currently just the "open in window" button for a registry-`popOut`
  widget. The context is deliberately not a prop: threading `_onPopOut` through
  ~50 widget components just to reach the shared header would touch every widget
  for one button. Absent context (pop-out window, config previews, stack/tab
  children, dev harnesses) renders no chrome, which is correct in all of them.
  Because the button is a real flex item it can never overlap what the widget put
  in its actions slot; the grid's floating overlay is used only for the modes
  that have no actions slot (see layout SPEC [S-29]).

# Settings Design System: Spec

Cross-cutting. Defines the shared vocabulary every settings and widget-config
surface is built from. Behavior tags are stable IDs. When adding behavior,
append a new [S-NN]; never renumber or reuse existing tags. When behavior is
removed, delete the bullet but don't reuse its tag.

Base stylesheet: `src/styles/config-base.css` (scoped under `.cfg`).
Shell: `src/ConfigWindow.tsx` (+ `ConfigWindow.css`) mounts one of the page
bodies — `SettingsModal.tsx` / `DisplaySettingsModal.tsx` / `ViewSettingsModal.tsx`
— or, for a widget, `ConfigTabs.tsx`. All four render the same root:
`<div class="settings-content cfg">`.
Shared controls: `Dropdown` / `MultiSelectDropdown` / `IconPicker` /
`NumberStepper` / `ToggleSwitch` / `ColorPicker`.

This spec was written after a consistency audit found the vocabulary had
silently forked (see Non-goals). It pins the rules that were being violated, so
the fork can't reopen.

## Control states
- [S-01] Every interactive control that can render `disabled` has a disabled
  style declaring BOTH reduced `opacity` and a non-default `cursor`. A control
  that accepts a `disabled` prop but paints identically to its enabled state is
  a defect: the user cannot tell the click will do nothing. This is enforced by
  construction — `settingsControls.test.ts` scans every `<button>` in `src/` for
  a `disabled` attribute and requires a matching disabled rule for its class, so
  a new unstyled disabled button fails the suite rather than shipping.
- [S-02] The disabled treatment is uniform: `opacity: 0.5` and
  `cursor: not-allowed`. Values are not chosen per component. (Pre-spec the
  codebase carried 0.35 / 0.4 / 0.5 / 0.7 and split `default` vs `not-allowed`.)
  ONE documented exception: `.msd-row-action-btn` uses `opacity: 0` because there
  the opacity is functional, not decorative — the control is meant to be
  invisible until its row is hovered.
- [S-03] A disabled control does not respond to hover. Every `:hover` rule on a
  control that can render disabled is guarded with `:not(:disabled)`, so a
  disabled button never lights up under the cursor and contradicts [S-01].

## Widget-local config CSS
- [S-04] A widget's config stylesheet may POSITION a shared control inside its
  own layout (`width`, `max-width`, `flex`, `margin`, `overflow`, …) but must not
  RESTYLE its appearance (`background`, `border`, `padding`, `font-*`, `color`,
  `box-shadow`, …). Appearance belongs to the component that owns the class, so
  one widget cannot make a shared control look different from the same control
  everywhere else. Enforced by `settingsControls.test.ts`, which reads the
  primary class of every rule in `widgets/**/*Config.css` and fails when a class
  owned by `config-base.css` or `src/components/*.css` is given an appearance
  property.
  (The bug: `CalendarConfig.css` re-declared eight `AccountToggleList` classes at
  equal specificity, so import order decided the winner and the values had
  already drifted — badge 9px vs 10px, row padding 8px/10px vs 10px/12px, and
  `min-height: 60px` dropped. Calendar's account rows rendered visibly unlike the
  same rows in the other ten configs that use the component.)

## Theming
- [S-05] The accent is user-configurable (`settings.style.primaryColor`), so no
  stylesheet may hardcode its DEFAULT value. `--primary` and its derived tokens
  (`--primary-subtle-bg`, `--primary-subtle-border`, `--primary-selection-bg`,
  `--primary-foreground`) are the only way to reference the accent; a tint the
  tokens don't cover is built with `color-mix(in srgb, var(--primary) N%, …)`,
  never a pre-mixed literal. `design.ts` is the single place the default value
  `#60cdff` / `#0078d4` appears. Enforced by `settingsTheme.test.ts`.
  (The bug: 18 sites hardcoded the default accent or a hand-mixed shade of it.
  A user who picked a red accent still got blue info-log chips, a blue update
  button, and a primary button that flipped back to stock blue on hover.)
- [S-06] Text or an icon sitting ON an accent-filled surface uses
  `--primary-foreground`, which `applyGlobalDesign()` picks as black or white by
  the accent's luminance. Never a literal chosen to contrast the DEFAULT accent.
  (The bug: the ToggleSwitch knob was `#0a1a26`, a navy picked to read against
  the default cyan. Any dark accent made the knob vanish into the track — on the
  single most-used control in Settings.)

## Cascade
- [S-07] `.settings-content` and `.cfg` are always on the SAME element (every
  page body and `ConfigTabs` renders `class="settings-content cfg"`), so the two
  must never declare the same property at equal specificity — the winner would
  be decided by whichever stylesheet the bundler happens to emit last, not by
  intent. The shared base (`display`, `flex`, `flex-direction`, `overflow-y`,
  `min-height`, `max-width`) is declared ONCE, by `.cfg`. The config pane's
  deliberately denser spacing is declared once at `.settings-content.cfg`
  (0,2,0), where it wins by specificity. Enforced by `settingsControls.test.ts`,
  which fails if a bare `.settings-content` rule declares any property `.cfg`
  also declares.
  (The bug: both declared `padding` (18px vs 14px 18px), `gap` (12px vs 10px)
  and `overflow-y`, at equal specificity, on every config page in the app.
  Measured in a browser, `.settings-content` was winning — config-base.css loads
  first via the registry→ConfigTabs chain, SettingsModal.css second — so 14px
  18px / 10px is the spacing users actually see, and is what the specificity fix
  preserves.)

## i18n
- [S-08] Every user-visible string on a config surface goes through `t()`,
  including the chrome `ConfigTabs` renders on EVERY widget config (the Header
  section, the Style/Title rows, and the Normal/Compact/Hidden mode labels) and
  the section labels each widget passes as its tab list.
- [S-09] A translated label is never held in a module-level `const`. Module
  initialisers run ONCE at import, so a `const tabs = [{ label: t('…') }]` is
  frozen in whatever language the app booted in and silently ignores a language
  change. Option/tab lists are FUNCTIONS that return the array, so the labels
  re-read `t()` on each render (the pattern `introAnimOptions()` in
  `SettingsModal.tsx` already established). Enforced by
  `settingsControls.test.ts`.
  (The bug: nine widget configs held their tab list in a module const, so
  switching language left their section labels in the boot language.)

- [S-20] [S-08] is enforced by scanning the SOURCE, not the locale files:
  `tests/i18nScan.ts` + `src/i18nScan.test.ts` fail on an English literal in a
  user-facing slot (`.design-label`, `.design-desc`, `.field-hint`,
  `.add-error`, `.section-label`, and the `placeholder` / `title` /
  `aria-label` / `onLabel` / `offLabel` / `searchPlaceholder` attributes).
  Brand and protocol names are exempt; `src/demo` and `src/dev` are skipped
  because their harnesses never ship.
  (The bug — issue #91: a key-parity test cannot see this class at all. zh had
  100% parity with en and a Chinese user still read English, because ~240
  strings had never reached the locale files: no key existed to be missing.
  Three `t()` calls also referenced keys nobody had defined, so those rows
  rendered the raw dotted path — `sunMoon.place.label` — in every language.)

- [S-21] Every shipped locale is COMPLETE, not a deep-partial. Each locale file
  is annotated `: Messages` (the full message shape), so adding a key to `en.ts`
  is a type error until all nine locales carry it — a missing translation can no
  longer resolve quietly to English. `locales.test.ts` asserts the same keyset
  per locale, and additionally that every `{placeholder}` survives translation
  (parity cannot see a dropped token: the key is present, just wrong).
  (The bug — issue #91: `PartialMessages` made omission legal, so de/es/fr/it/
  pt/pl/ja drifted to roughly half of `en` with every test green. A German user
  read the entire onboarding guide in English.)

- [S-10] The design-row vocabulary (`.design-row`, `.design-left`,
  `.design-label`, `.design-desc`, `.design-val*`, `.design-range-wrap`) is
  declared ONLY by `config-base.css`, scoped under `.cfg`. No other stylesheet
  redeclares it. Enforced by `settingsControls.test.ts`.
  (The bug: `App.css` carried a second, unscoped copy of the whole system. Every
  render site is inside a `.cfg`, so the scoped copy won on every shared property
  and the unscoped one was dead weight — which is precisely why the two drifted
  unnoticed: `.design-val` min-width 32px vs 24px, and the App.css
  `.design-label` had lost the `display:inline-flex` that SettingsModal's "active
  now" pill relies on.)

## Controls
- [S-22] A config panel does not re-create its body when a setting changes. Only
  the control that owns a value re-renders; the surrounding sections, and every
  other control, keep their DOM nodes. `ConfigTabs` reads `props.tabs` through a
  memo compared by CONTENT (`sameSections`, `configSections.ts`) and renders it
  with `<For>`, so a panel that derives its section list from its own config
  (To Do drops "Refresh" in local mode, todo [S-47]) — and therefore rebuilds
  that array on every write — reconciles to nothing. Tests:
  `configSections.test.ts` (`S-22_`).
  (The bug, issue nwalser/themia-releases#80 follow-up: the sections were
  rendered with a plain `props.tabs.map()`. `props.tabs` is a getter, so that one
  expression tracked the config bag, and ONE setting change tore down and rebuilt
  every section — measured at 20 teardown/rebuild mutations under
  `.settings-content.cfg` per write. The panel flashed on every click, and the
  damage went past cosmetic: a control destroyed mid-gesture never received its
  own `pointerup`, so a pressed `NumberStepper` leaked its 50ms repeat timer and
  drove the value to its maximum on its own, with nothing holding the button.)
- [S-16] A bounded number is rendered with `NumberStepper`, never a raw
  `<input type="number">`. The stepper enforces min/max itself, carries the unit,
  and matches the control scale — so a config that uses a raw input also
  hand-rolls a clamp, bakes the unit into the label, and drifts. Enforced by
  `settingsControls.test.ts`.
  (The bug: a bounded number was rendered FOUR ways — `NumberStepper` (~26
  sites), a raw `input.num-input` (11 sites), a hand-built
  `design-val-wrap` + raw input, and a `Dropdown` over a numeric enum. Sharpest
  case: `GalleryConfig` used `NumberStepper` for two of its numbers and
  hand-built the third, so two "interval in seconds" fields in one panel looked
  and behaved differently.)
- [S-17] `.section-label` is a SECTION heading (12px, uppercase). It is never the
  label for a single control — that is `.design-row` > `.design-left` >
  `.design-label` + `.design-desc`.
  (The bug: seven configs used it per-field, so those panels rendered an
  uppercase heading above every control and carried no descriptions at all.)

- [S-18] A widget's config stylesheet scopes every selector under its OWN root
  class. It never declares a bare `.cfg <shared-name>` rule: `.cfg` is the
  global config scope, so such a rule lands on every other config that happens
  to use the same class name. A widget that wants to override a documented base
  class does so under its own class (e.g.
  `.cfg .items-list.shortcut-items-list`). Enforced by
  `settingsControls.test.ts`.
  (The bug: `.cfg .items-list` and `.cfg .item-remove` — shared collection-editor
  primitives — were declared by countdown, habit-tracker AND shortcut, so
  whichever stylesheet loaded last won for the whole app. countdown's and
  habit-tracker's copies were byte-identical; shortcut wanted a growing list
  rather than a scrolling box and had to fight the leak with a
  higher-specificity rule, with a comment documenting the workaround. Both
  primitives now live in `config-base.css`.)

## Buttons
- [S-11] There is ONE button family, declared once by `config-base.css` under
  `.cfg`, named by ROLE: `btn-primary` (the one action the surface exists for),
  `btn-secondary` (every other action), `btn-danger` (destructive), `btn-add`
  (appends to a collection editor; dashed, ends the list), `btn-reset` (clears an
  overridden value back to inherited). The names are roles, not dialog actions:
  `save-btn`/`cancel-btn` only described one dialog's buttons, so every button
  that was neither had to borrow the wrong name — "Export settings" was marked up
  as a `cancel-btn`. A role name survives being reused, which is the point of a
  shared vocabulary.
  Every role renders at ONE scale: `min-height: 28px`, `font-size: 13px` — the
  scale `config-base.css`'s own comment already asked for ("Match the
  height/scale of the other controls (Dropdown trigger = 28px)"), matching
  `.text-input` and `NumberStepper`. A variant of a role (accent CTA, icon-only)
  compounds onto it (`.cfg .btn-secondary.upgrade-btn`) so it wins on
  specificity rather than on emit order ([S-07]) or `!important`.
  The icon-only variant is the shared `btn-icon` modifier (square, 28px, no
  padding, solid border) — declared by `config-base.css` alongside the roles,
  because a widget config may not restyle a role it doesn't own ([S-04]), so a
  variant that changes appearance lives with its family and the widget only
  USES it (e.g. the Shortcut config's `btn-add btn-icon` add button).
  Enforced by `settingsControls.test.ts`.
  (The bug: the settings surfaces had TWO parallel vocabularies and used the
  documented one nowhere. `config-base.css` defined
  save/cancel/danger/add/reset for widget configs + ConnectWindow +
  LayoutPickerWindow; `App.css` defined an undocumented `.design-toggle` (21
  sites) + `.design-reset-btn` (4) that the whole app settings page used
  instead. They were three scales — save ~38px/15px, cancel ~36px/15px,
  design-toggle ~24px/12px, stepper 24px/11px — so one settings row could stack
  three button heights, and none matched the 28px baseline. `.design-toggle.on`
  read as a selected state but all three sites were the primary action
  (subscribe / activate license / install update). `.danger-btn` hardcoded
  `rgba(239,68,68,.3)`/`.08`, the pre-mixed DEFAULT accent, against [S-05].)
  Two names look like they belong to this family and do NOT: `.view-add-btn`
  (views-bar chrome) and `.design-btn` (edit-bar chrome). Neither ever renders
  inside a `.cfg`. Rename by exact class token — a `/\badd-btn\b/` also matches
  inside `view-add-btn`.

## Design presets
- [S-12] A preset is a named bundle of exactly the 13 APPEARANCE fields:
  `theme`, `primaryColor`, `fontFamily`, `widgetBgColor`, `widgetBgOpacity`,
  `headerBgColor`, `headerBgOpacity`, `widgetBorderStyle`, `widgetBorderWidth`,
  `widgetBorderColor`, `widgetBorderOpacity`, `cornerRadius`, `widgetGap`
  (`PRESET_FIELDS` in `designPresets.ts`). It does NOT own `uiScale`,
  `fontScale`, or any animation/sound field: those are the user's accessibility
  and motion preferences, and resetting someone's 150% zoom or re-enabling
  motion they turned off in order to apply a colour scheme would be hostile.
  Every preset declares every owned field — no partial bundles.
- [S-13] Selecting a preset OVERRIDES the current value of each owned field, in
  ONE atomic read-modify-write (`setGlobalDesignFields`). Not 13 separate
  writes: that would be 13 storage round-trips and 13 cross-window broadcasts,
  and would leave the design observably half-applied in between.
- [S-14] The picker shows the preset whose owned fields ALL equal the live
  design, else "Custom". Changing any owned field drops it to Custom; changing
  an unowned one (zoom, motion) does not. Presets are mutually distinguishable,
  so the reported match is unambiguous. A "Custom" tile sits after the named
  presets and is the ACTIVE one exactly when no preset matches
  (`activeDesignPreset() === null`); it previews the live design and is a
  non-interactive status indicator (no bundle to apply), so the user can see at a
  glance that they've drifted onto their own look.
- [S-15] `PresetId` is a closed union. An unknown id — from a stale persisted
  value or across a trust boundary — is ignored with a loud log, never applied
  in part. A corrupt value in an owned field reads as Custom rather than
  crashing the picker.

## Global font (issue #62/#64)
- [S-19] The font picker is a SEARCHABLE dropdown offering the four curated
  presets (`system` / `sans` / `serif` / `mono`) first, then EVERY installed
  system-font family (from the `list_system_fonts` command; empty on non-Windows
  or failure, so the picker degrades to just the presets). `fontFamily` is
  therefore an OPEN set — a preset id OR any family name — persisted as a plain
  string, NOT a closed enum, because installed fonts are unbounded and
  machine-specific. `resolveFontStack` (in `design.ts`) maps it to a CSS stack: a
  preset → its curated stack; any other value → that family QUOTED with the
  system stack as a fallback tail; an empty or CSS-injection-bearing value → the
  system stack (a refusal, so a hand-edited/imported value can never break out of
  the `--font-family` declaration). The installed list is fetched once and
  memoized (`systemFonts.ts`).

## Reduce effects
- [S-21] `reduceEffects` is a boolean in `GlobalDesign`, **off by default** (the
  frosted-glass look is the design). When on, `applyGlobalDesign` puts a
  `data-reduce-effects` attribute on `:root`, and the single rule in
  `src/styles/reduce-effects.css` sets `backdrop-filter: none !important` on
  every element. One rule, not ~20 per-component opt-outs, so surfaces added
  later are covered without anyone remembering to.
  - The effect must be **removed**, never reduced to `blur(0px)`: a
    backdrop-filter of any radius still promotes its element to its own
    compositing layer and still makes the compositor read back what is behind it
    each frame. That readback is the cost.
  - `!important` is required and intended: the rule must beat per-component
    declarations that are more specific than a `:root` attribute selector, and
    "the user asked for no effects" is not a component's to override.
  - It is **not** an owned field of any design preset ([S-13]/[S-14]), on the
    same grounds as zoom and motion: it expresses the user's hardware, not a
    look, so applying a preset must not silently switch the glass back on.
  - The toggle lives next to game mode in Settings, since both answer "make
    Themia cost less right now".

## Non-goals
- This spec does not cover widget *display* styling, only settings/config
  surfaces. A widget's own `Widget.css` may style its own look freely.

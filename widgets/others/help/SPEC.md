# Help & Getting Started: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Always shows a header row (LifeBuoy icon + label "Help & Getting Started"). No hide toggle.
- [S-02] Renders an intro paragraph on the Basics tab describing Themia's desktop widget model in plain, non-technical language.
- [S-04] Renders a tip on the Basics tab footer reminding the user this widget can be removed from its right-click menu.
- [S-05] Content is written for non-technical users: it explains actions via menus, buttons, and clicks. It does NOT teach keyboard shortcuts (no `<kbd>` shortcut list, no "press Ctrl+…" instructions).

## Tabs
- [S-06] Renders a top segmented control with four tabs in order: Basics, Widgets, Views, Customize. Default active tab is Basics. (The former "Shortcuts" tab, [S-03]/[S-09], was removed.)
- [S-07] Clicking a tab switches the body content; the selected tab has `aria-selected="true"` and an active style.
- [S-08] Basics tab content: welcome paragraph, "Key concepts" bullet list (edit mode, views, right-click, multi-monitor), removal tip.
- [S-10] Widgets tab content: explains add, configure, move/resize, copy/paste, and remove (all via menus/buttons, no shortcuts), plus the four registry categories (Office, Online, System, Others).
- [S-11] Views tab content: explains multi-view, switching via the view bar, per-monitor assignment, and view name context menu actions (in edit mode: duplicate, view settings — where rename/delete live).
- [S-12] Customize tab content: explains Settings → Style widget appearance (colors, opacity, corner radius, border), theme/language/UI scale, per-view grid (columns, rows, gap).

## Configuration
- [S-13] No configuration options. `defaultConfig` is empty; the Configure panel shows the "no options" placeholder.

## Localization
- [S-14] All guide prose comes from the locale files (`help.*` keys), never from
  literals in the component — issue #91: the guide was the largest untranslated
  surface in the app. Emphasis travels with the translation as `*term*` /
  `_ui element_` markers, tokenised by `src/i18nRich.ts` into `<strong>`/`<em>`
  nodes. A locale string is text, never markup: it is not parsed as HTML and
  never reaches `innerHTML`, and an unpaired marker stays literal.

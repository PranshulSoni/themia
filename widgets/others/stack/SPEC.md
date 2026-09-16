# Stack Widget Spec

A chrome-less container widget that holds other widgets ("children") inside its
rect and rolls them up to their header rows (accordion). The containment DATA
model (parentId, sanitize, remove-pops-children, drag-in/pop-out) is pinned in
`src/SPEC.md` [S-19]–[S-22]; this spec pins the stack's own presentation.

Owning files: `Stack.tsx`, `StackConfig.tsx`, `stackModel.ts`, `index.tsx`.
Mirror these `[S-NN]` tags in test names.

## Identity

- **[S-01]** The stack is **accordion-only**. The tabbed container is a
  SEPARATE, standalone widget (`widgets/others/tabs`, type `tabs`), not a mode
  of the stack. There is no `mode` config and no mode switcher; presentation is
  fixed by the widget type (one path, no flags).
- **[S-02]** Children are the view's widgets with `parentId === <stack id>`, in
  `widgets`-array order (src/SPEC.md [S-19]). Reorder goes through
  `moveChildTo` (src/SPEC [S-35]): **drag the grip** in the config panel, or
  Move up / Move down from the row context menu and that same panel.

## Expansion state

- **[S-03]** A filled stack shows **title rows**, with **at most ONE child open**
  at a time. Every child starts closed; clicking a row opens that child AND
  closes whichever was open; clicking the open child's row closes it, leaving
  the stack all rows again. This is what makes the stack worth its space (N
  widgets in one tile's footprint, each one click away) and it is the only rule
  that keeps the layout deterministic: the rows take a fixed 40px each and the
  ONE open child gets everything left over, instead of K open children splitting
  the remainder into slivers that shrink as the user opens more. The state is
  therefore a single id, not a set: the persisted `expandedId: string | null`,
  resolved by the pure read `resolveExpandedId`. A stale id (child popped out or
  removed) resolves to "nothing open" and is never rewritten, so it re-applies
  if that child comes back; junk resolves the same way.
- **[S-15]** `expandedId` is the ONLY expansion input. Early builds persisted
  `collapsedIds` (the inverse set) and then `expandedIds` (a multi-open set);
  both keys are inert, so a stack carrying either starts closed like any other.
  The stack never shipped outside developer mode, so there is no user state to
  preserve, and reading a second, contradictory key would buy a parallel code
  path for nothing.
- **[S-09]** Toggling persists `expandedId` via a full-config merge read from
  the layout store (spread props don't carry unrelated keys like `layer`;
  writing only the known keys would destroy them).

## Rendering

- **[S-04]** **Every child's row is the stack's OWN** (`.stack-row`: 40px band,
  icon + label + chevron), identical whether the child is open or shut. An open
  child renders BELOW its row as a bare body — passed `headerMode="none"`, never
  by rewriting its config — and a closed child is **not mounted at all**.
  Nothing about a row may depend on a child's internal DOM, on its header mode,
  or on laying it out at row height. Two bugs came from breaking that rule, and
  both are the same mistake:
  - the closed row used to BE the child's live WidgetHeader, with the section
    clipped to a 40px band and `.widget-body` hidden. A child squeezed to 40px
    trips its own size-responsive CSS — weather-current hides its header under
    `@container (max-height: 80px)` — so the row rendered **blank**.
  - the OPEN child then kept its own header as the row. A `compact` header
    (weather, forecast) is a 21px uppercase label with no icon and no chevron,
    so opening such a child made the 40px row it was clicked on visibly
    disappear and left a tiny, unrecognisable strip as the only way to close it.
  A stack row is chrome the STACK owns; a child's header is chrome the CHILD
  owns for its own tile. Borrowing one for the other couples every stack row to
  every widget's private layout decisions.
  Not mounting closed children also means a closed child costs nothing: no
  timers, no polling, no fetches — which matters because closed is the DEFAULT
  state ([S-03]).
  The trade: a child's own header actions are not shown while it sits in a stack
  (e.g. the folder widget's inline search, WidgetHeader [S-09]). Accepted — the
  row is the container's, uniform rows are the point of an accordion, and the
  full chrome is one "Open in window" / pop-out away ([S-08]).
- **[S-05]** Rows show the child's registry icon + label
  (`widget.label || registry default`) + a chevron that turns when open.
  Clicking a row toggles that child; clicks inside the open child's BODY never
  do (the handler only acts on a click that lands on the row band itself, so a
  child's own buttons, inputs and links keep working). Rows are `role="button"`
  + `aria-expanded`, focusable outside edit mode, and Enter/Space toggle. Row
  clicks are inert in edit mode (the grid owns the pointer there). Rows have
  **no hover state**; real widget headers have none (cursor change only).
- **[S-06]** Each child body is wrapped in its own `ErrorBoundary`; one
  crashing child degrades to a dead section, never a dead stack.
- **[S-07]** The stack paints no chrome of its own (registry `noChrome` →
  grid suppresses the `.widget` border). **With children** it renders as ONE
  card built from the app design tokens: outer border/radius from
  `--widget-border-*`/`--radius`, the card SURFACE from `--card` + the widget
  blur (`.widget-body` parity — with every child closed the rows fill only the
  top of the tile, and without it the rest of the card was a transparent hole
  showing the wallpaper inside a border), flat edge-to-edge header rows (folder
  parity) separated by `var(--border)`, section spacing from the global
  `--item-gap` token like every other list widget, header surface from the
  shared `widget-header` class. No color/radius/border literals. **When empty**
  it is NOT invisible: it paints a placeholder card (card surface + dashed
  `--widget-border-color` edge + the [S-13] button) in **both** modes. An
  invisible tile is indistinguishable from a broken widget, and empty is what
  every stack starts as; the placeholder also has to read on ANY wallpaper
  without the grid's edit chrome behind it, which is why it carries the card
  surface rather than a bare `--border` outline (8% white was invisible). The
  "drag widgets here" line inside it shows in edit mode only — drag-in is an
  edit-mode affordance. **In edit mode** the grid additionally marks the whole
  rect as a drop area: an accent-tinted dashed bound + faint accent wash; while
  a dragged widget hovers it, the bound goes solid accent + glow ([S-22]), and
  that rule must out-specify the dashed edit bound.
- **[S-08]** Row context menu: Configure (routes to the child's own config
  window), Move up / Move down (disabled at the ends), Pop out
  (`clearWidgetParent`), Remove (confirm + `removeWidget`).

## Filling the stack

- **[S-13]** A stack is EMPTY when it is created, so "drag a widget onto it in
  edit mode" ([S-22]) cannot be the only way in: it is invisible unless the user
  is already in edit mode, already knows the gesture, and already has a widget
  to spare. Three entry points open the Add Widget page bound to this stack
  (`add:<id>`, src/SPEC [S-33]/[S-34]), which creates the picked widget INSIDE
  it with no click-to-place step:
  - the **empty state's** "Add widget" button, drawn in BOTH modes ([S-07]);
  - the **config panel's** "Add widget" button (`ChildListConfig`), which is
    where the user lands right after adding the stack;
  - **"Add widget inside"** on the stack's own grid context menu, listed first
    because holding widgets is what a container is for.
  Only the grid can open a config window, so the button reaches it through
  `_onAddToContainer`; without that prop (preview render) it is not drawn.

## Picker preview

- **[S-12]** Mounted with no `_widgetId` (the Add-Widget picker's preview pane)
  there is no layout to read children from, so the stack composes them from the
  demo fixture's `_previewChildren` instead (`containerPreview.ts`, ids
  `preview-<n>`, config = registry default + `demoWidgetConfigs`). Without it
  the pane would advertise the stack with an EMPTY stack: a placeholder box next
  to every other widget's live preview, which sells nothing. Preview children
  are display-only: row context menus are suppressed and config writes are
  already no-ops (no `_widgetId`). The fixture opens exactly ONE child so the
  pane shows the accordion (title rows plus one body), not just a list.

## Availability

- **[S-10]** (removed) The stack shipped generally available: the `devOnly`
  pre-release gate was dropped from the registry (`index.tsx`), so the widget is
  discoverable in the Add-Widget picker for all users regardless of
  `globalDesign.devMode`. It shipped together with the creation path ([S-13])
  and the picker preview ([S-12]), which are what made an empty container
  usable. Existing instances were never gated -- the flag gated DISCOVERY only.
- **[S-11]** Library category: the stack lives under its own **`Layout`**
  category in the Add-Widget picker (alongside the tabs container), NOT under
  `Others`. `Layout` is a known, ordered picker category
  (`src/components/pickerGroups.ts`), placed after `Themia` and before `Others`.

## Config tree

- **[S-14]** In the config window's view tree, a stack's children are listed
  **nested under the stack**, not as its siblings, in `widgets`-array order --
  the same order the tile renders them in, so the tree and the tile always
  agree (`configTreeRows.ts`, shared with tabs). The nesting is visual +
  `aria-level`; ArrowLeft on a nested row jumps to its container (not straight
  to the view), ArrowRight on a container row descends into its first child. A
  container row's context menu also carries "Add widget inside" ([S-13]) -- the
  tree is where its children are listed, so it is where you reach for another
  one. A child whose `parentId` cannot be honoured is listed top-level rather
  than dropped: the tree is the only route to a widget's config, so a bad
  reference must never make a widget unreachable.

## Limits / licensing

- Children remain first-class widgets in the layout array, so the free-tier
  widget count includes them automatically (no special-casing).

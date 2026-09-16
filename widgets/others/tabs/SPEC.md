# Tabs Widget Spec

A chrome-less container widget that holds other widgets ("children") inside its
rect and shows exactly ONE at a time, switched via a tab bar. It is a sibling of
the **stack** widget (accordion presentation): a SEPARATE, standalone
implementation, not a mode of the stack. The containment DATA model (parentId,
sanitize, remove-pops-children, drag-in/pop-out) is pinned in `src/SPEC.md`
[S-19]–[S-22]; this spec pins the tabs container's own presentation.

Owning files: `Tabs.tsx`, `TabsConfig.tsx`, `tabsModel.ts`, `index.tsx`.
Mirror these `[S-NN]` tags in test names.

## Children

- **[S-01]** Children are the view's widgets with `parentId === <tabs id>`, in
  `widgets`-array order (src/SPEC.md [S-19]). Reorder goes through
  `moveChildTo` (src/SPEC [S-35]): **drag the grip** in the config panel, or
  Move up / Move down from the tab context menu and that same panel.

## Active tab

- **[S-02]** The container keeps exactly one active child: persisted
  `activeChildId`, resolved by the pure read `resolveActiveTabId` (a member id
  wins, else the first child, since a tab bar always has an active tab (empty
  container → null). A stale id is ignored, never rewritten, so re-adding the
  child re-activates it.
- **[S-03]** Selecting a tab persists `activeChildId` via a full-config merge
  read from the layout store (spread props don't carry unrelated keys like
  `layer`; writing only the known keys would destroy them).

## Rendering

- **[S-04]** Only the ACTIVE child is mounted; switching tabs unmounts the
  previous one (no background polling cost). The active child renders its OWN
  live WidgetHeader below the tab bar when its resolved header mode is not
  `'none'`; a registry-`noHeader` / user-hidden child renders bodyless-header
  (`'none'`). Tab labels are static (the child's `label` or registry default) and
  they do NOT mirror the child's live header text. Configs are never rewritten.
- **[S-05]** Each active child body is wrapped in its own `ErrorBoundary`; a
  crashing child degrades to a dead panel, never a dead container. Tabs have
  **no hover state** (cursor change only), for parity with real widget headers; the
  active tab is marked with an accent underline (`var(--primary)`). Clicks are
  inert in edit mode (the grid owns the pointer there).
- **[S-06]** The container paints no chrome of its own (registry `noChrome` →
  grid suppresses the `.widget` border). **With children** it renders as ONE
  card built from the app design tokens: outer border/radius from
  `--widget-border-*`/`--radius`, tab bar surface from the shared `widget-header`
  class, separators from `var(--border)`. No color/radius/border literals. **When
  empty** it is NOT invisible: it paints a placeholder card (card surface +
  dashed `--widget-border-color` edge + the [S-11] button) in **both** modes. An
  invisible tile is indistinguishable from a broken widget, and empty is what
  every container starts as; the placeholder also has to read on ANY wallpaper
  without the grid's edit chrome behind it. The "drag widgets here" line inside
  it shows in edit mode only -- drag-in is an edit-mode affordance. **In edit
  mode** the shared grid chrome additionally marks the rect as a drop area: an
  accent-tinted dashed bound + faint accent wash (`.widget.no-chrome.edit`),
  going solid accent + glow while a dragged widget hovers it
  (`.stack-drop-hover`, src/SPEC [S-22]).
- **[S-07]** Tab context menu: Configure (routes to the child's own config
  window), Move up / Move down (disabled at the ends), Pop out
  (`clearWidgetParent`), Remove (confirm + `removeWidget`).

## Filling the container

- **[S-11]** The container is EMPTY when it is created, so "drag a widget onto
  it in edit mode" ([S-22]) cannot be the only way in: it is invisible unless the
  user is already in edit mode, already knows the gesture, and already has a
  widget to spare. Three entry points open the Add Widget page bound to it
  (`add:<id>`, src/SPEC [S-33]/[S-34]), which creates the picked widget INSIDE
  it with no click-to-place step: the **empty state's** "Add widget" button,
  drawn in BOTH modes ([S-06]), the **config panel's** "Add widget" button
  (`ChildListConfig`), and **"Add widget inside"** on its own grid context menu.
  Only the grid can open a config window, so the button reaches it through
  `_onAddToContainer`; without that prop (preview render) it is not drawn.

## Picker preview

- **[S-10]** Mounted with no `_widgetId` (the Add-Widget picker's preview pane)
  there is no layout to read children from, so the container composes them from
  the demo fixture's `_previewChildren` instead (`containerPreview.ts`, ids
  `preview-<n>`, config = registry default + `demoWidgetConfigs`). Without it
  the pane would advertise the container with an EMPTY one: a placeholder box
  next to every other widget's live preview, which sells nothing. Preview
  children are display-only: tab
  context menus are suppressed and config writes are already no-ops (no
  `_widgetId`). The fixture pre-selects a middle tab so the pane shows a real
  tab bar with an inactive/active split.

## Availability

- **[S-08]** (removed) The tabs container shipped generally available: the
  `devOnly` pre-release gate was dropped from the registry (`index.tsx`), so the
  widget is discoverable in the Add-Widget picker for all users regardless of
  `globalDesign.devMode`. It shipped together with the creation path ([S-11])
  and the picker preview ([S-10]), which are what made an empty container
  usable. Existing instances were never gated -- the flag gated DISCOVERY only.
- **[S-09]** Library category: the tabs container lives under its own
  **`Layout`** category in the Add-Widget picker (alongside the stack), NOT
  under `Others`. `Layout` is a known, ordered picker category
  (`src/components/pickerGroups.ts`), placed after `Themia` and before `Others`.

## Config tree

- **[S-12]** In the config window's view tree, the container's children are
  listed **nested under it**, not as its siblings, in `widgets`-array order:
  the same order the tab bar shows them in (`configTreeRows.ts`, shared with the
  stack). See stack SPEC [S-14] for the keyboard + context-menu rules.

## Limits / licensing

- Children remain first-class widgets in the layout array, so the free-tier
  widget count includes them automatically (no special-casing).

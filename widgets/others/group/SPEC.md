# Group (Area) Widget Spec

A collapsible **area** on the desktop. Widgets dropped into it belong to it and
keep their own place on the grid; collapsing the area hides them.

The containment DATA model (parentId, sanitize, remove-pops-children, join /
leave by drop, collapse, move-with-members) is pinned in `src/SPEC.md`
[S-19]–[S-22] and [S-36]–[S-38]; this spec pins the group's own presentation.

Owning files: `Group.tsx`, `GroupConfig.tsx`, `index.tsx`.
Mirror these `[S-NN]` tags in test names.

## Identity

- **[S-01]** The group is an **area**, not a shelf. Unlike stack (accordion) and
  tabs (tab bar) it never lays its members out: they are the grid's own tiles,
  at the position and size the user gave them, and the group only owns the area
  rect + the collapse state (src/SPEC [S-36]). There is no "arrangement" config
  and no layout mode.
- **[S-02]** Members are the view's widgets with `parentId === <group id>`. Their
  ORDER carries no meaning here (position is geometry), so the group offers no
  reordering — the config panel hides the grip and the up/down actions ([S-07]).

## Title bar

- **[S-03]** The group draws one title bar: icon + label (`widget.label`, else
  the registry name) + chevron. Clicking the **band** toggles collapse at rest;
  in edit mode the band is the group's drag handle, so only the **chevron
  button** toggles there. The chevron works in BOTH modes on purpose — a
  collapsed group hides its members, and edit mode is exactly where the user
  needs them back. The band is `role="button"` + `aria-expanded`, focusable
  outside edit mode, and Enter/Space toggle.
- **[S-04]** The bar is **32px**, kept in step with `GROUP_HEADER_PX`
  (`src/layout.ts`): that constant is what the grid shortens a collapsed group's
  rendered rect to (src/SPEC [S-37]), so a mismatch would clip the bar or leave a
  gap under it. Its surface comes from the shared `widget-header` class, so it
  matches every other widget header under the user's design settings.
  - The 32px is measured **from the tile's OUTER edge, in both states**, which
    is what makes the bar pixel-identical open and shut. `GROUP_HEADER_PX` is
    what the grid shrinks the whole collapsed TILE to ([S-37]), borders
    included, so the bar's own box is 32 minus the borders standing between it
    and that edge: the tile's border on the NEAR side always, plus one more on
    the FAR side when the tile's border is off and the bar draws its own ring
    ([S-15]) — the ring then lands exactly where the collapsed one does. Either
    way the bar's CONTENT box comes out at `32 − 2×border`, one border in from
    the tile: same size, same place, so the icon and title never shift when the
    group opens.
  - **Collapsed, the bar fills the tile's CONTENT box** (`flex: 1 1 auto` +
    `min-width/height: 0`). A fixed band overflows that box by one border width
    per side and the tile's `overflow: hidden` clips the far edge away — the
    bar's own border, on whichever edge the flex direction pushes the overflow
    toward (bottom for a top header, left for a right-hand one), plus 2px of the
    bar's contents. The `min-*` are load-bearing: a flex item's automatic
    minimum size never shrinks below its own preferred size, which pinned the
    SIDEWAYS bar ([S-11], orthogonal writing mode) back at 32px even with
    shrink enabled.
- **[S-05]** The bar shows **no member count**, in either state. A collapsed
  group is a title the user chose ([S-12]) sitting where they put it; a badge
  counting what is folded away is bookkeeping they did not ask for, and it made
  every collapsed area look like an inbox. What is inside is one click away.

## The area

- **[S-06]** Expanded, the group paints a faint wash (`--card-hover`) inside the
  widget border: enough to show what belongs together, not enough to fight the
  member widgets' own card surfaces or hide the wallpaper between them. It
  renders nothing over the members — they are separate grid tiles drawn after
  the group (src/SPEC [S-36]).
- **[S-07]** An **empty** area is not invisible: it keeps the bar and draws a
  dashed placeholder with the "Add widget" button, in both modes (stack [S-07]'s
  reasoning — a container that paints nothing reads as a broken widget, and empty
  is what every group starts as). The "drag widgets in here" hint shows in edit
  mode only, since drag-in is an edit-mode gesture.

## Picker preview

- **[S-08]** Mounted with no `_widgetId` (the Add-Widget picker's preview pane)
  there is no grid to hold members, so — unlike stack/tabs, which compose live
  preview children — the group draws **stand-in blocks** sized off the demo
  fixture's `_previewChildren` count. Without them the pane would advertise the
  widget with an empty box.

## Config

- **[S-09]** The config panel carries the group's own settings ([S-11]–[S-13])
  and then the shared container child list — the same `ChildList` rows stack and
  tabs use, with reordering off ([S-02]) and the pop-out action relabelled
  "Remove from group", since the widget does not move when it leaves, it just
  stops belonging. (`ChildListConfig` is that list plus a `ConfigTabs` wrapper,
  for containers whose whole config IS the list; the group composes the rows
  inside its own tab instead, so the list exists once.) Collapse is NOT mirrored
  there: the title bar owns it ([S-03]), and a second control writing the same
  field would be the parallel path this codebase keeps out.
- **[S-10]** Library category: **Layout**, alongside stack and tabs
  (`src/components/pickerGroups.ts`).

## Configurable chrome

- **[S-11]** The title bar's **edge** is configurable (`headerSide`:
  top/bottom/left/right, src/SPEC [S-39]). One flex direction per side is the
  whole implementation — no second header component — and a left/right bar is
  the same band with `writing-mode: **sideways-lr**`, so the title, count and
  chevron lay out along the new axis from the same markup (hence logical
  `margin-inline-start`, not `margin-left`). `sideways-lr` runs the line
  **bottom-to-top**, the way a side tab label reads; `vertical-rl` runs it
  downward, and a 180° transform on the band would mirror the borders onto the
  wrong edge and reverse the flex order with the text. Inline-start becomes the
  BOTTOM, so the icon stays at the bottom and the chevron at the top.
  The chevron points AWAY from the bar while closed (at the space the area opens
  into) and back at it while open; the divider always faces the body.
  The divider is drawn from the **same `--widget-border-*` tokens as the tile's
  border**, not from a fixed 1px `--border`: collapsed, that tile border IS the
  line the divider has to match — same width, same colour, same distance from
  the outer edge ([S-04]) — and a divider of its own put a second, differently
  weighted line one border-width off it. A widget border of 0 therefore means no
  divider either, which is how every other widget separates its header from its
  body: by surface, not by a line.
- **[S-12]** The group carries its **own title and icon**: `config.title` (else
  the widget label, else the registry name) and `config.icon` (a `VIEW_ICON_MAP`
  key from the shared IconPicker, else the group glyph). An area is a thing the
  user names — "Work", "Media" — so the name is a first-class setting rather
  than a rename buried elsewhere.
- **[S-13]** The area's **background** is configurable (`groupAreaBackground`,
  pure), over exactly two modes: `default` keeps the design-token wash
  (`--card-hover`) and `solid` takes a colour + alpha from the shared
  ColorPicker. `default` returns **null** rather than a copy of the token, so an
  untouched group keeps following the user's theme — and so the transparent
  switch below can clear the wash from CSS, which an inline value would outrank.
  Junk — a bad mode, a non-hex colour, an out-of-range alpha, or a `gradient` /
  `none` left over from a dropped mode — falls back per field instead of
  emitting a broken CSS value.
  The row is labelled **Fill**, not "Background": the shared transparent switch
  renders its own "Background" section in the same panel, and two controls under
  one name in one panel is how a user ends up toggling the wrong one.
  - **Clearing the area is the shared transparent-background switch**
    (registry `canTransparent` → `config.transparentBg`, src/SPEC [S-32]), not a
    third fill mode. A "None" fill cleared the area and left the title bar
    frosted on top of a see-through region; the switch takes the bar's fill with
    it, which is what "transparent" has to mean for a widget whose whole body is
    a surface. `.widget.bare` clears `--card` / `--header-bg` for every widget,
    but the area paints `--card-hover` (a hover token, not a surface token), so
    the group clears that itself. An explicitly picked `solid` colour is INLINE
    and outranks the switch: the switch drops the theme default, it does not
    overrule a colour the user chose.
  - The switch does **not** take the border. [S-32] drops it for a normal widget
    because a transparent widget should have no visible box; a group IS a box —
    the region its members stand in — and it is the one widget with its own
    border setting ([S-15]), so `bare` decides the fills and `borderMode`
    decides the border.
- **[S-13a]** **The hit-mask follows paint, not layout.** A group whose area is
  see-through publishes only its **title-bar band** to the window region
  (`data-mask-rect` on the tile → `maskBand` in `hitMask.ts`, src/SPEC [S-17]);
  masking the whole tile left an invisible window over the desktop the user can
  see through the group — wallpaper visible, the icons under it dead. Decided by
  `groupAreaPaints` (pure): the theme wash paints unless the transparent switch
  cleared it, a `solid` fill paints unless its alpha is 0 (the user got there
  with the slider, and the mask follows what they see, not which mode they
  picked). Two carve-outs: a **collapsed** group is already the bar alone
  ([S-37]), and an **empty** area keeps the full rect, because its "add a
  widget" placeholder is real paint and clipping it would hide the way to fill
  the group. The band is published in app-layer CSS px, like the tile's own
  inline rect, so nothing measures the DOM — a `getBoundingClientRect` would
  fold in the intro animation's transform, which is exactly what [S-17] avoids.
- **[S-15]** The **border is the grid tile's own** (`.widget`, from
  `--widget-border-*`), not a second one drawn inside it. The group therefore
  does NOT set registry `noChrome`: that makes the tile's border transparent, and
  the group's own border then sits one border-width in from where every other
  widget's border sits — a group that never quite lines up with the widgets in
  it. `config.borderMode` (`theme` | `none`, `groupBorderMode`) only decides
  whether that one border is painted; `none` adds `.no-widget-border` to the
  tile, and edit mode still draws the dashed accent bound so an unbordered area
  stays findable and draggable.
  - `none` hides the border **around the CONTENT only: the title bar keeps
    one.** What the setting is for is an area that melts into the wallpaper, and
    a bar with no edge at all stops reading as a bar. The header draws it from
    the same `--widget-border-*` tokens, inside the tile's (now transparent)
    border slot, with the outer corners rounded to `--radius` so the ring follows
    the tile's `overflow: hidden` clip instead of being cut square by it. It
    closes on **all four sides**, including the edge facing the body, and so
    REPLACES the divider ([S-11]) on that edge — a ring that stopped at the
    divider read as a bar missing its bottom border. Accepted consequence: with
    the widget border width at 0 (the default) `none` leaves the bar with no
    outline and no divider, which is what "no border" on a design that has no
    borders should look like. This is not the second border [S-15] rejects: it
    exists only while the tile's own border is off, so there is never more
    than one.
- **[S-16]** Opening and closing are **animated** (src/SPEC [S-43] owns the
  mechanism: the phase, the tile transition, the member fade and the hit-mask
  hold). What the group itself owns is what the phase means to its own markup:
  while it is CLOSING the area stays mounted (the tile needs something to
  shrink) and its wash fades, so the last frames are empty rather than a sliver
  of surface snapping out; opening fades the wash back in. The area, the divider
  and the header's corner rounding follow that visual state, not the raw flag.
  The **chevron follows the flag** instead — it is a control, and a control
  answers the click at once — as does `aria-expanded`.
- **[S-14]** The area has **no padding**. Members are grid tiles laid over the
  panel, not children of it, so any inset would draw an edge widgets cannot line
  up with; the panel's inner edge IS the usable area. (The picker preview's
  stand-in blocks keep an inset of their own — they stand in for widgets that
  would carry the grid's spacing.)

## Limits / licensing

- Members remain first-class widgets in the layout array, so the free-tier
  widget count includes them automatically (no special-casing).

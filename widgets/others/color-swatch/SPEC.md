# Color: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Renders a full-bleed rectangle whose `background` is driven by the selected color config.
- [S-02] Widget renders without a header (registry `noHeader: true`).
- [S-03] In `solid` mode, background is `rgba()` derived from hex `color` and `alpha` (0–1).
- [S-04] In `gradient` mode, background is `linear-gradient(<angle>deg, rgba(colorA,alphaA), rgba(colorB,alphaB))`.

## Configuration
- [S-05] `mode`: segmented Solid / Gradient.
- [S-06] Solid mode exposes a single ColorPicker bound to `color` + `alpha`.
- [S-07] Gradient mode exposes two ColorPickers bound to `colorA`/`alphaA` (Start) and `colorB`/`alphaB` (End).
- [S-08] Gradient mode exposes an `angle` numeric input (0–360°, default 135).
- [S-09] `layer`: Dropdown In front / Behind (default `front`), controlling widget stacking relative to other widgets.
- [S-10] Outside edit mode, `layer: 'behind'` applies `z-index: -1` to the widget wrapper and `layer: 'front'` applies `z-index: 3`; in edit mode no layer z-index is applied so resize handles stay reachable.

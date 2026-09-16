# Scratchpad: SPEC

Experimental widget. Freehand canvas sketch persisted as a PNG data
URL in config (`image`). Mirror `[S-NN]` tags in tests.

- **[S-01]** Pointer drag draws a stroke. The backing store is sized to the
  element box × devicePixelRatio.
- **[S-02]** A toolbar offers a preset colour palette (`COLORS`), three pen sizes
  (`SIZES`), an eraser, and clear-all. The current colour/size persist to config
  (`strokeColor`, `penSize`); the eraser is an ephemeral mode.
- **[S-03]** The eraser cuts existing pixels (`globalCompositeOperation =
  destination-out`) at ~2.5× the pen size; selecting a colour returns to pen mode.
- **[S-04]** On pointer-up the canvas is serialized (`toDataURL`) and persisted
  via `updateWidgetConfig` (merged onto existing config).
- **[S-05]** On mount and on resize the saved image is repainted scaled to the
  current canvas box (`ResizeObserver`).
- **[S-06]** Clear empties the canvas and persists the empty state. Persist
  failures are logged, never thrown.
- **[S-07]** A blank pad (no persisted `image`) shows a centred "Draw here" hint
  (pencil icon + label) overlaying the canvas; it never intercepts pointer input
  (`pointer-events: none`) and is hidden once an `image` is present.

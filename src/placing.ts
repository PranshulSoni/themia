/**
 * Widget "placing" mode state: the user has picked a widget (from the picker)
 * or a clipboard entry and is about to click a grid cell to drop it. Shared by
 * App.tsx (owns the signal), WidgetGrid.tsx, and the grid drag manager so the
 * shape is defined once.
 */
// `size` is the widget's fractional size on THIS screen ([S-01]); the ghost
// renders at it and a click commits the top-left at the cursor.
export type PlacingState =
  | { fromClipboard: true; mode: 'copy' | 'cut'; sourceId?: string; size: { w: number; h: number } }
  | { fromClipboard?: false; type: string; size: { w: number; h: number }; defaultConfig: Record<string, unknown> };

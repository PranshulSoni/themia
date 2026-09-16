// Pure keyboard-action classifier for the Gallery slideshow (SPEC [S-29]/[S-37]).
//
// Extracted so the "never steal a key while the user is typing" guard is a
// single, unit-testable source of truth. The bug behind themia-releases #53 was
// the gallery's GLOBAL `document` keydown calling `preventDefault()` on Space:
// widgets live in WS_EX_NOACTIVATE windows, so that listener only ever fired
// while a text input elsewhere in the same window (notes/todo) was focused, and
// preventing default there ate the space character out of that input. Routing
// through the widget-keyboard hook plus this guard means the gallery acts only
// when it actually owns keyboard focus and no text input is focused.

export type GalleryKeyAction = 'prev' | 'next' | 'togglePause';

/**
 * Map a key to a slideshow action, or `null` when it must be ignored.
 * Ignored when: not in slideshow mode, a text input is focused (the user is
 * typing, so the input owns the key), or the key isn't a slideshow control
 * ([S-29] Left/Right → prev/next, Space → toggle pause).
 */
export function galleryKeyAction(
  key: string,
  mode: string,
  textInputFocused: boolean,
): GalleryKeyAction | null {
  if (mode !== 'slideshow') return null;
  if (textInputFocused) return null;
  switch (key) {
    case 'ArrowLeft': return 'prev';
    case 'ArrowRight': return 'next';
    case ' ': return 'togglePause';
    default: return null;
  }
}

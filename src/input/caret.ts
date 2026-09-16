// Caret placement for the widgets' inline editors.
//
// A widget swaps its display `<span>` for an `<input>`/`<textarea>` when the
// user clicks the text. Focusing that editor with `el.select()` threw away the
// one thing the click had already said — WHERE in the text the user meant to
// type — and armed the next keystroke to replace the whole value. These helpers
// carry the click's character offset across the swap instead, so the caret
// lands where the user pointed and nothing is selected.
//
// Keyboard routes (F2, the row menu's Rename) have no click point: they pass
// `null` and get the caret at the END of the value, which is still not a
// select-all.

/** Clamp a caret offset into `[0, length]`. A non-finite offset (a hit test
 *  that returned nothing useful) parks the caret at the end. */
export function clampCaret(offset: number, length: number): number {
  if (!Number.isFinite(offset)) return length;
  return Math.min(length, Math.max(0, Math.trunc(offset)));
}

/** The two spellings of the same hit test. `caretPositionFromPoint` is the
 *  standard one; `caretRangeFromPoint` is Blink's older name, still what some
 *  WebView2 builds ship. Either resolves a viewport point to a text offset. */
interface CaretCapableDocument {
  caretPositionFromPoint?: (x: number, y: number) => { offset: number } | null;
  caretRangeFromPoint?: (x: number, y: number) => { startOffset: number } | null;
}

/**
 * Character offset within the clicked text node at viewport point (`x`, `y`),
 * clamped to `length`. Returns `null` when neither hit test is available or the
 * point resolved to nothing — the caller then falls back to the end of the
 * value rather than guessing a position.
 */
export function caretOffsetFromPoint(x: number, y: number, length: number): number | null {
  const doc = document as Document & CaretCapableDocument;
  try {
    const pos = doc.caretPositionFromPoint?.(x, y);
    if (pos) return clampCaret(pos.offset, length);
    const range = doc.caretRangeFromPoint?.(x, y);
    if (range) return clampCaret(range.startOffset, length);
  } catch {
    // ignore: both APIs are optional and throw on nothing-hit in some builds;
    // an unusable hit test is exactly the `null` case below.
  }
  return null;
}

/**
 * Focus an inline editor and place a COLLAPSED caret at `offset` (or at the end
 * of the value when `offset` is `null`). Never selects the value: replacing the
 * whole text has to be the user's own Ctrl+A, not a side effect of clicking it.
 */
export function focusWithCaret(
  el: HTMLInputElement | HTMLTextAreaElement,
  offset: number | null,
): void {
  el.focus();
  const at = offset === null ? el.value.length : clampCaret(offset, el.value.length);
  el.setSelectionRange(at, at);
}

// ---------------------------------------------------------------------------
// Notes undo/redo.
//
// The editor rebuilds its DOM from source on every change, which discards the
// browser's native undo stack, so we keep our own. It is deliberately
// timer-free: consecutive single-character typing is coalesced by comparing the
// two strings, not by measuring how fast the user typed. Same input, same
// stack, every run.
// ---------------------------------------------------------------------------

export interface Snapshot {
  text: string;
  start: number;
  end: number;
}

/** Cap on retained states; a note is small, but paste loops shouldn't grow it forever. */
const LIMIT = 200;

/**
 * True when `next` is `prev` plus exactly one non-newline character — the shape
 * of an ordinary keystroke. Runs of these collapse into a single undo step;
 * Enter starts a new one, so undo lands on line boundaries.
 */
export function isTypingRun(prev: string, next: string): boolean {
  if (next.length !== prev.length + 1) return false;
  let i = 0;
  while (i < prev.length && prev[i] === next[i]) i++;
  if (next[i] === '\n') return false;
  return prev.slice(i) === next.slice(i + 1);
}

export class History {
  private past: Snapshot[] = [];
  private future: Snapshot[] = [];
  private current: Snapshot;
  private lastWasTyping = false;

  constructor(text: string) {
    this.current = { text, start: text.length, end: text.length };
  }

  /** The state the editor is showing. */
  get state(): Snapshot { return this.current; }

  /** Reset to `text` with no history (external overwrite, e.g. "Clear notes"). */
  reset(text: string): void {
    this.past = [];
    this.future = [];
    this.lastWasTyping = false;
    this.current = { text, start: text.length, end: text.length };
  }

  /** Record an edit. No-ops when the text is unchanged (caret moves only). */
  record(text: string, start: number, end: number): void {
    if (text === this.current.text) {
      this.current = { text, start, end };
      return;
    }
    this.future = [];
    const typing = isTypingRun(this.current.text, text);
    if (!(typing && this.lastWasTyping)) {
      this.past.push(this.current);
      if (this.past.length > LIMIT) this.past.shift();
    }
    this.lastWasTyping = typing;
    this.current = { text, start, end };
  }

  undo(): Snapshot | null {
    const prev = this.past.pop();
    if (!prev) return null;
    this.future.push(this.current);
    this.current = prev;
    this.lastWasTyping = false;
    return prev;
  }

  redo(): Snapshot | null {
    const next = this.future.pop();
    if (!next) return null;
    this.past.push(this.current);
    this.current = next;
    this.lastWasTyping = false;
    return next;
  }
}

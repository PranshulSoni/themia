# Notes: Spec

Behavior tags are stable IDs. When adding behavior, append a new [S-NN]; never renumber or reuse existing tags. When behavior is removed, delete the bullet but don't reuse its tag.

## Display
- [S-01] Renders a full-surface `contenteditable` editor with "Type your notes here..." placeholder shown while the note is empty.
- [S-02] Shows a widget header with StickyNote icon unless `hideHeader` is on. The title is the widget's own `label` when set, else the note's title, else "Notes" — so a desktop of Notes widgets is readable at a glance without naming each window by hand.
- [S-03] `fontSize` config sets the `--notes-font-size` custom property; the rendered size is that value times `--font-scale` (default 13px).
- [S-04] Spellcheck is disabled on the editor.

## Configuration
- [S-05] `fontSize`: numeric input (min 1) plus range slider (10–20); manual entry coerced to integer ≥ 1, defaults back to 13 on invalid input.
- [S-06] "Clear notes" empties the selected note's body, behind a native confirm — the text is unrecoverable and the button sits one row under the font stepper.
- [S-07] `hideHeader` toggle hides the title bar.
- [S-35] The **Source** tab shows the same `AccountToggleList` every account-bound widget shows, with "On this device" as its leading row (Mood Tracker [S-16]). Notes have no account backend, so this is the ONE source and its switch is disabled rather than inviting a click that could only take the widget's storage away; `providerFilter` is empty, so no account rows are fetched.
- [S-38] The note picker renders **inside that source row's card**, through the leading option's `extra` slot — the counterpart of the calendar widget's per-account `perAccountExtra` picker, and for the same reason: which note to show is a question *about* that source, not a setting beside it. One row per note with a pick dot, an editable title (committed on blur / Enter, not per keystroke, since the title round-trips through disk), and a remove button behind a native confirm. "New note" appends one and selects it. The pool is device-wide shared state, so creating, renaming or deleting reaches every widget already showing that note; only the *selection* is a config value.
- [S-36] Deleting the note a widget shows leaves that widget in a "this note was deleted" state rather than silently handing it another note.

## Data / backend
A note is a **document the app owns**, not a property of the widget showing it.
The store lives on the Rust side under `<app_data_dir>/notes/`: one `<id>.md`
per note holding the markdown byte for byte, plus an `index.json`
(`schema_version` + `{id, title, updated_at}` rows) for the metadata a picker
needs. The widget's config carries only `noteId`, so a note outlives the widget,
the view, and the window it was created in, and two widgets can show one note.

- [S-08] Content persists by calling `notes_write(noteId, content)` with a 400ms debounce after each keystroke. It is also flushed on editor blur, on unmount, and on `beforeunload` — the write is IPC rather than a synchronous storage set, so a pending timer would otherwise die with the page.
- [S-09] An incoming body from the store overwrites local text only when the change did not originate from local typing: the load effect tracks `noteId` and the store revision alone, reads the local signal through `untrack`, and yields to an unflushed local edit (last write wins, and ours has not landed yet).
- [S-30] The store is single-writer per file and atomic: every write is temp-file + rename, and each read-modify-write of the index runs under one process-wide lock. A write broadcasts `notes-changed` to *other* windows only — bumping the local revision would make the writer re-read from disk on every debounced save.
- [S-31] `notes_read`/`notes_write` reject an id that is not the 36-char UUID shape, so an id arriving over IPC can never be interpolated into a path outside the notes directory. A write to a note that is not in the index fails rather than resurrecting a deleted note as an untitled orphan, and a widget pointing at a deleted note drops its pending write instead of recreating it.
- [S-32] A widget with no `noteId` mints one on first mount and writes the id back to its config. A widget predating the store carries its text inline in `content`; that text seeds the new note, and `content` is only dropped from config once the note exists, so a failed create leaves the old text untouched and the next mount retries.
- [S-33] Corrupt or unknown-schema `index.json` is rebuilt by adopting every `<id>.md` on disk: ids (and so every widget's link) and text survive, only titles fall back to "Untitled". Index rows whose content file is gone are pruned. A missing index on first run is silent, not an error.
- [S-34] Cross-window convergence is event-driven: a store change broadcasts `notes-changed` (sync.ts BroadcastChannel), carrying the note id for a body change and nothing for a list change. Widgets re-read only when the id is theirs or absent.
- [S-37] `notesStore.ts` is the only door to the `notes_*` commands. Neither the widget nor its config panel invokes one directly, so the cross-window broadcast cannot be forgotten at a call site.

## Interactions
- [S-10] Typing in the editor updates the local signal immediately and schedules the debounced persistence.

## Pop-out window
- [S-11] Notes opts into the generic widget pop-out (registry `popOut`): the "Open in window" chrome context-menu action opens the note in its own decorated OS window via `open_widget_window` (see src/WidgetWindow).
- [S-12] The pop-out window and the grid tile are two views of ONE note: both bind to the same `noteId`, so the body round-trips through the note store ([S-08]) and reaches the other window via the `notes-changed` broadcast. Presentation config (`fontSize`) still rides the shared per-view layout store (localStorage + storage events). Editing in either place shows up in the other.
- [S-13] The pop-out window is a singleton across all poppable widgets: re-invoking pop-out focuses the existing window and retargets it via the `widget-window-navigate` event instead of spawning a duplicate.

## Markdown
The note is stored as plain markdown source and edited *in place*: syntax markers stay
on screen (dimmed) while their effect is applied to the surrounding text. There is no
separate preview mode and no rich-text state — the source string is the only truth.

- [S-14] The editor is a projection of `parseNote(content)`: one `.md-line` element per source line, rebuilt from source on every change, never read back for formatting state.
- [S-15] Block syntax, one line each: `#`/`##`/`###` headings (a space after the hashes is required, so `#tag` stays plain), `- `/`* `/`+ ` bullets, `1. `/`1) ` ordered items, `> ` quotes, `---`/`***`/`___` rules, and ```` ``` ````/`~~~` fenced blocks (every line between fences renders as code).
- [S-16] Inline syntax: `**bold**`, `*italic*`, `_italic_`, `~~strike~~`, `` `code` ``. Underscore emphasis only opens/closes at a word boundary, so `snake_case_name` is left alone. Code spans never nest.
- [S-17] Task lists: `- [ ] ` / `- [x] `. A checked item's content renders struck-through and dimmed, and clicking the box toggles it (length-preserving, so the caret does not move). The box is drawn as a lucide icon — `Square` / `SquareCheck` — laid over its own source text, which stays in the DOM but transparent: the icon carries `data-deco` and is skipped by both the text walker and the caret walker, so [S-18] still holds and every offset is unchanged. The icon is `pointer-events: none`, so a click on it lands on the span that owns the toggle. The icon nodes are built in `Notes.tsx` and cloned per checkbox, keeping `editorDom.ts` free of Solid/lucide (it must stay importable by the unit runner, which has no JSX transform); without an icon factory the brackets simply render as text.
- [S-18] A delimiter with no partner on the same line (`**oops`) degrades to literal text. Invariant: the spans of a line, concatenated, are exactly the source line — the renderer never adds, drops or rewrites a character.
- [S-19] Enter continues the current list marker (`- `, `- [ ] `, and `1. ` with the number incremented); pressing it on an item holding nothing but its marker clears the marker instead of nesting deeper. Shift+Enter always inserts a plain newline.
- [S-20] Tab / Shift+Tab indent / outdent every line the selection touches by two spaces; nesting depth styles the line up to six levels.
- [S-21] Ctrl+B / Ctrl+I wrap the selection in `**` / `*`, and unwrap it when it is already wrapped (inside or just outside the selection). With no selection they insert an empty pair and place the caret between the markers.
- [S-22] Ctrl+Z / Ctrl+Shift+Z (and Ctrl+Y) undo / redo. History is kept by the widget because re-projecting the DOM discards the browser's native stack; consecutive single-character insertions coalesce into one undo step, with no wall-clock timing involved.
- [S-23] Paste and drag-drop insert plain text only (`text/plain`, CRLF normalised) at the drop point; the browser's rich-text insertion and its Ctrl+U underline are suppressed. Dragging the editor's own selection is refused, since contenteditable would copy rather than move it.
- [S-24] While an IME composition is open the editor stops re-projecting, and resumes on `compositionend`, so composed text is not torn out mid-composition.

## Formatting toolbars
- [S-25] A formatting bar is always present: heading, bold, italic, strikethrough, code, bullet list, numbered list, task list, quote. Icon-only 20px buttons; the row scrolls horizontally rather than wrapping.
- [S-29] Placement follows the header mode, so the bar never costs a band of its own where a header already exists. `normal` puts it in the header's actions slot (the header's right padding tightens and the title ellipses away first, so all nine buttons still fit the 220px default width). `none` has no header to host it — the pop-out window's mode (WidgetHeader [S-17]) — so it gets a standalone row above the editor. `compact` shows NO bar: compact is the mode chosen to reclaim vertical space, and a toolbar band would hand it straight back. The selection bubble [S-26] is unaffected and remains the way to format in compact mode.
- [S-26] Selecting a range inside the editor raises a bubble over the selection carrying the four commands that wrap text (bold, italic, strikethrough, code). It is centred on the first rect of the selection, sits above it, flips below when the selection is on the first line, and is clamped inside the widget. It hides on collapse and on blur.
- [S-27] Both toolbars run the same operations as the keyboard shortcuts, on the current selection — or, when the note isn't focused, on the last selection seen inside it. Block commands (list / numbered / task / quote) apply to every line the selection touches, replace any other block marker rather than stacking, and strip themselves when every touched line already has them; the heading button cycles H1 -> H2 -> H3 -> body. Buttons light up for the formatting in force at the caret; a checked task's strikethrough is the line's state, not a mark, so it does not light the strikethrough button.
- [S-28] Toolbar buttons suppress mousedown so the editor keeps focus and its selection; a command run while the editor is unfocused focuses it first, so the caret is restored where the command left it.

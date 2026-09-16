// Notes [S-09] store -> local-text sync, [S-30..S-34] the local notes store,
// and the focus-store contract [S-01] depends on.
//
// The bug the untrack rules pin: the sync effect used to read the local `text`
// signal while tracking, which subscribed the effect to our own typing. The
// stored value still holds the last PERSISTED text for 400ms after a keystroke,
// so every keystroke was immediately "restored" to the stale value and the
// editor could not be typed in at all. The store moved from the widget's config
// bag to a Rust-owned file, but the hazard is identical, so the rules moved
// with it.
//
// There is no Solid renderer in the unit suite (vitest.config has no
// vite-plugin-solid, so .tsx never gets transformed), so this is a SOURCE scan —
// the pattern from configTabsHeader.test.ts / configWindowBackground.test.ts. It
// pins the guards themselves rather than the rendered behaviour.

import { describe, it, expect } from 'vitest';
import { readSource } from '../../../tests/cssRules.ts';

const NOTES = 'widgets/others/notes/Notes.tsx';
const STORE = 'widgets/others/notes/notesStore.ts';
const FOCUS_STORE = 'src/input/focusStore.ts';

/** Body of a named function declared in `src`, ending at the first closing
 *  brace sitting at the same indentation the declaration does — so this works
 *  for both a module-level function and a method nested in the component. */
function fnBody(src: string, name: string): string {
  const text = readSource(src).replace(/\r\n/g, '\n');
  const decl = new RegExp(`^([ \\t]*)(?:export )?(?:async )?function ${name}\\(`, 'm');
  const m = decl.exec(text);
  expect(m, `no function ${name} in ${src}`).toBeTruthy();
  const rest = text.slice(m!.index);
  const end = rest.indexOf(`\n${m![1]}}`);
  expect(end, `could not find the end of ${name}`).toBeGreaterThan(0);
  return rest.slice(0, end);
}

/** The createEffect that decides which note is loaded. Matched on the STATEMENT
 *  (`const id = props.noteId`), not the bare identifier: the prose above an
 *  effect mentions it too, and a comment must not be able to satisfy a guard. */
function loadEffect(): string {
  const src = readSource(NOTES).replace(/\r\n/g, '\n');
  const chunk = src.split('createEffect(').slice(1).find(b => b.includes('const id = props.noteId'));
  expect(chunk, 'no createEffect reads props.noteId any more').toBeTruthy();
  const end = chunk!.indexOf('\n  });');
  expect(end, 'could not find the end of the load effect').toBeGreaterThan(0);
  return chunk!.slice(0, end);
}

describe('[S-09] the note-load path must not track local typing', () => {
  it('reads the local text through untrack', () => {
    expect(fnBody(NOTES, 'resolve')).toContain('untrack(text)');
  });

  it('never calls the text signal while tracking', () => {
    // `setText(...)` is fine; a bare `text()` read is what subscribes the
    // effect to our own keystrokes.
    expect(fnBody(NOTES, 'resolve')).not.toMatch(/[^a-zA-Z_.]text\(\)/);
  });

  it('the load effect tracks the note id and the store revision, and nothing else', () => {
    const body = loadEffect();
    expect(body).toContain('props.noteId');
    expect(body).toContain('notesRevision()');
    // Everything the effect DOES happens inside untrack, so a signal touched
    // while resolving can never re-trigger the load.
    expect(body).toContain('untrack(');
  });

  it('still applies an incoming body, so an external change wins', () => {
    expect(fnBody(NOTES, 'resolve')).toContain('setText(body)');
  });
});

describe('[S-30] a body write must not make the writer re-read', () => {
  it('writeNote broadcasts to other windows only, never bumping the local revision', () => {
    const body = fnBody(STORE, 'broadcastBody');
    expect(body).toContain('postTransient');
    // A local bump would re-run the load effect on every debounced save,
    // turning each keystroke burst into a disk round trip racing the next one.
    expect(body).not.toContain('setNotesRevision');
  });

  it('list-shape changes DO bump locally, so the picker refreshes', () => {
    const body = fnBody(STORE, 'broadcast');
    expect(body).toContain('setNotesRevision');
  });
});

describe('[S-31] a deleted note is never resurrected by a pending write', () => {
  it('flushPending refuses to write when the note is gone or unbound', () => {
    const body = fnBody(NOTES, 'flushPending');
    expect(body).toMatch(/if \(!id \|\| status\(\) === 'missing'\) return;/);
  });
});

describe('[S-32] migrating a pre-store widget must not leave its text behind', () => {
  it('bindNote seeds the new note from the legacy content and drops the key', () => {
    const body = fnBody(NOTES, 'bindNote');
    // Seeded from the legacy inline text...
    expect(body).toContain('props.content');
    expect(body).toContain('createNote(');
    // ...and `content` is destructured OUT of the config that gets written
    // back, so a second migration can't overwrite the note with stale text.
    expect(body).toMatch(/const \{ content: _legacy, \.\.\.rest \}/);
    expect(body).toContain('noteId: meta.id');
  });

  it('only drops the legacy key once the note actually exists', () => {
    const body = fnBody(NOTES, 'bindNote');
    const created = body.indexOf('createNote(');
    const dropped = body.indexOf('const { content: _legacy');
    expect(created).toBeGreaterThan(-1);
    // A failed create must leave the old text exactly where it was.
    expect(dropped).toBeGreaterThan(created);
    expect(body).toMatch(/if \(!meta\)[\s\S]{0,80}return;/);
  });
});

describe('[S-34] a widget re-reads only for a change that concerns it', () => {
  it('ignores a body change broadcast for another note', () => {
    const src = readSource(NOTES).replace(/\r\n/g, '\n');
    const handler = src.slice(src.indexOf("onTransient('notes-changed'"));
    // `msg.id` present and not ours: someone else's note. `msg.id` absent: a
    // list change, which may be OUR note being deleted, so it always applies.
    expect(handler).toMatch(/if \(msg\.id && msg\.id !== props\.noteId\) return;/);
    expect(handler).toContain('applyRemoteChange()');
  });
});

describe('[S-35] the source reads like every other widget\'s source', () => {
  const CONFIG = 'widgets/others/notes/NotesConfig.tsx';

  it('uses the shared AccountToggleList with an "on this device" leading row', () => {
    const src = readSource(CONFIG);
    expect(src).toContain("key: 'source'");
    expect(src).toContain('<AccountToggleList');
    expect(src).toContain("notes.storage.local");
    // No account backend exists for notes, so no account rows are fetched...
    expect(src).toMatch(/providerFilter=\{\[\]\}/);
    // ...and the one source cannot be switched off.
    expect(src).toContain('disabled: true');
  });
});

describe('[S-38] the note picker lives inside the source card', () => {
  const CONFIG = 'widgets/others/notes/NotesConfig.tsx';

  it('hangs off the leading option\'s extra slot, not a sibling row', () => {
    const src = readSource(CONFIG);
    expect(src).toContain('extra: notePicker');
    expect(src).toContain('notes-doc-list');
    // A `design-row` here would put it beside the source instead of in it.
    expect(src).not.toMatch(/design-row stacked/);
  });

  it('the toggle list renders a leading option\'s extra content', () => {
    // Without this the slot silently drops whatever a caller passes.
    const list = readSource('src/components/AccountToggleList.tsx');
    expect(list).toContain('opt.extra?.()');
  });

  it('offers the note rows and the add button', () => {
    const src = readSource(CONFIG);
    expect(src).toContain('notesStore.notes');
    expect(src).toContain('btn-add');
  });

  it('only the SELECTION is a config value; the pool goes to the store', () => {
    const src = readSource(CONFIG);
    // Creating / renaming / deleting reach every widget showing that note...
    expect(src).toMatch(/createNote\(|renameNote\(|deleteNote\(/);
    // ...while `update` only ever writes the pointer.
    expect(src).toContain("update('noteId'");
  });

  it('renames on commit, not on every keystroke', () => {
    // Per-keystroke would round-trip the title through disk and re-set `value`
    // from the store mid-word, moving the caret.
    const src = readSource(CONFIG).replace(/\r\n/g, '\n');
    const row = src.slice(src.indexOf('notes-doc-name'));
    expect(row).toContain('onChange=');
    expect(row.slice(0, row.indexOf('/>'))).not.toContain('onInput=');
  });

  it('deleting a note asks first', () => {
    // The text is unrecoverable, so this goes through the same native dialog
    // the to-do list pool and the mood log use.
    expect(fnBody(CONFIG, 'removeNote')).toContain('await ask(');
    expect(fnBody(CONFIG, 'clearNote')).toContain('await ask(');
  });
});

describe('[S-36] a widget whose note was deleted says so', () => {
  it('renders the gone state instead of an editor it cannot save', () => {
    const src = readSource(NOTES);
    expect(src).toContain("status() === 'missing'");
    expect(src).toContain('notes.noteGone');
  });
});

describe('[S-37] the store is the only door to the notes backend', () => {
  it('the widget never invokes a notes_* command directly', () => {
    // Every mutation has to go through notesStore, which is what guarantees the
    // cross-window broadcast can't be forgotten at a call site.
    expect(readSource(NOTES)).not.toMatch(/notes_(list|read|write|create|rename|delete)/);
    expect(readSource('widgets/others/notes/NotesConfig.tsx'))
      .not.toMatch(/notes_(list|read|write|create|rename|delete)/);
  });
});

describe('[S-01] the editor is text input as far as the app is concerned', () => {
  it('renders contenteditable="true" verbatim', () => {
    // focusStore matches `[contenteditable="true"]` for hover pre-activation;
    // `contenteditable` with no value, or "plaintext-only", would miss it.
    expect(readSource(NOTES)).toContain('contenteditable="true"');
  });

  it('the focus store counts a contenteditable as a focused input', () => {
    // Without this the WH_KEYBOARD_LL hook keeps eating the editor's keys and
    // app hotkeys fire while the user types.
    expect(readSource(FOCUS_STORE)).toMatch(/isInputEl[\s\S]{0,400}isContentEditable/);
  });
});

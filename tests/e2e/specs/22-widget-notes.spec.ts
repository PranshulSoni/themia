/**
 * Notes widget.
 *
 * Covers:
 *  - typing into the editor persists the source into the NOTE STORE via
 *    `notes_write` (debounced ~400ms); the widget config only holds the
 *    `noteId` that points at it [S-08][S-32].
 *  - reload preserves the typed content.
 *  - config `fontSize` drives the rendered text size [S-03].
 *  - markdown source is projected into styled lines [S-14][S-15][S-17], and the
 *    source itself is what gets stored [S-18].
 */
import { expect } from 'chai';
import { browser, $ } from '@wdio/globals';
import {
  resetAppState, pickWidget, clickGridCell, waitForPersist, waitForReady,
  configModalIsOpen, closeConfigModal, LS_KEYS, attachToAppWindow,
  readNote, writeNote, leaveEditMode,
} from '../helpers/app.js';

interface W { id: string; type: string; config?: Record<string, any> }

async function readLastNotesWidget(): Promise<W | null> {
  return browser.execute((prefix: string) => {
    const keys = Object.keys(localStorage).filter(k => k.startsWith(prefix));
    let last: any = null;
    for (const k of keys) {
      try {
        const v = JSON.parse(localStorage.getItem(k) ?? '{}');
        if (!Array.isArray(v.widgets)) continue;
        for (const w of v.widgets) {
          if (w.type === 'notes') last = w;
        }
      } catch {}
    }
    return last;
  }, LS_KEYS.layoutPrefix);
}

async function setLastWidgetConfig(patch: Record<string, any>): Promise<void> {
  await browser.execute((prefix: string, p: Record<string, any>) => {
    const keys = Object.keys(localStorage).filter(k => k.startsWith(prefix));
    for (const k of keys) {
      try {
        const v = JSON.parse(localStorage.getItem(k) ?? '{}');
        if (!Array.isArray(v.widgets) || v.widgets.length === 0) continue;
        const last = v.widgets[v.widgets.length - 1];
        last.config = { ...(last.config ?? {}), ...p, _refresh: Date.now() };
        localStorage.setItem(k, JSON.stringify(v));
      } catch {}
    }
  }, LS_KEYS.layoutPrefix, patch);
}

/**
 * The source the widget actually persisted.
 *
 * [S-08]/[S-32]: a note's body lives in the note STORE (`notes_write`), not in
 * the widget config — the config only carries the `noteId` that points at it.
 * Reading `config.content` here would only ever see the legacy inline bag,
 * which is dropped as soon as the note exists.
 */
async function storedNoteSource(): Promise<string | null> {
  const w = await readLastNotesWidget();
  const id = (w?.config?.noteId ?? '') as string;
  if (!id) return null;
  return browser.execute(async (noteId: string) => {
    const internals = (window as any).__TAURI_INTERNALS__;
    const invoke = internals?.__originalInvoke ?? internals?.invoke;
    if (!invoke) return null;
    return (await invoke('notes_read', { id: noteId })) as string | null;
  }, id);
}

/** Wait for the debounced `notes_write` to land, then assert on it. */
async function expectStoredNote(expected: string): Promise<void> {
  await browser.waitUntil(async () => (await storedNoteSource()) === expected, {
    timeout: 3_000,
    timeoutMsg: `the note store never held ${JSON.stringify(expected)}`,
  });
}

/** `data-kind` of every projected line, in order. */
async function lineKinds(): Promise<string[]> {
  return browser.execute(() => {
    const el = document.querySelector('.notes-widget__editor');
    if (!el) return [];
    return Array.from(el.children).map(c => (c as HTMLElement).dataset.kind ?? '');
  });
}

describe('widget: Notes', () => {
  beforeEach(async () => {
    await resetAppState();
    await pickWidget('Notes');
    await clickGridCell(2, 2);
    await waitForPersist();
    if (await configModalIsOpen()) await closeConfigModal();
    await $('.notes-widget__editor').waitForDisplayed({ timeout: 3_000 });
  });

  it('[S-08] typed text is persisted to the note store', async () => {
    await writeNote('hello from e2e');

    // debounce is 400ms in Notes.tsx; wait past it.
    await browser.pause(600);
    await waitForPersist();

    const w = await readLastNotesWidget();
    expect(w, 'notes widget present').to.not.equal(null);
    await expectStoredNote('hello from e2e');
  });

  it('persists across page reload', async () => {
    await writeNote('survive the reload');
    await browser.pause(600);
    await waitForPersist();

    await browser.refresh();
    await waitForReady();
    await $('.notes-widget__editor').waitForDisplayed({ timeout: 3_000 });
    expect(await readNote()).to.equal('survive the reload');
  });

  it('[S-03] fontSize config drives the rendered text size', async () => {
    await setLastWidgetConfig({ fontSize: 18 });
    await browser.refresh();
    await attachToAppWindow();
    await waitForReady();
    await $('.notes-widget__editor').waitForDisplayed({ timeout: 3_000 });

    const size = await browser.execute(() => {
      const el = document.querySelector<HTMLElement>('.notes-widget__editor');
      if (!el) return '';
      // fontSize sets the custom property; --font-scale multiplies it.
      return el.style.getPropertyValue('--notes-font-size');
    });
    expect(size).to.equal('18px');
  });

  it('[S-14][S-15] markdown source is projected into styled lines', async () => {
    await writeNote('# Title\n- [ ] milk\n> quoted');
    await browser.pause(100);
    expect(await lineKinds()).to.deep.equal(['h1', 'task', 'quote']);
  });

  it('[S-18] the stored note is the markdown source, not rendered text', async () => {
    await writeNote('**bold** and `code`');
    await browser.pause(600);
    await waitForPersist();

    await expectStoredNote('**bold** and `code`');
  });

  it('[S-25][S-29] the formatting bar lives in the header with every command', async () => {
    const labels = await browser.execute(() => Array.from(
      document.querySelectorAll('.notes-widget .wh-actions .notes-toolbar .notes-toolbar__btn'),
    ).map(b => b.getAttribute('aria-label')));
    expect(labels).to.deep.equal([
      'Heading', 'Bold', 'Italic', 'Strikethrough', 'Code',
      'Bullet list', 'Numbered list', 'Task list', 'Quote',
    ]);
  });

  it('[S-27] a toolbar command rewrites the source and persists', async () => {
    await leaveEditMode();
    await writeNote('milk');
    await browser.pause(100);
    await $('.notes-widget .notes-toolbar__btn[aria-label="Bullet list"]').click();
    await browser.pause(600);
    await waitForPersist();

    await expectStoredNote('- milk');
  });

  it('[S-17] clicking a checkbox toggles the task in the source', async () => {
    // Edit mode covers each widget with a drag overlay, so a real click only
    // reaches the checkbox once the grid is back in normal mode.
    await leaveEditMode();
    await writeNote('- [ ] milk');
    await browser.pause(100);
    await $('.notes-widget__editor .md-box').click();
    await browser.pause(600);
    await waitForPersist();

    await expectStoredNote('- [x] milk');
  });
});

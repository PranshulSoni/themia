import { createSignal, createEffect, createMemo, createRoot, onCleanup, onMount, untrack, Show } from 'solid-js';
import { StickyNote, Square, SquareCheck } from 'lucide-solid';
import { useLayout } from '../../../src/viewContext.tsx';
import WidgetHeader from '../../../src/components/WidgetHeader.tsx';
import type { HeaderMode } from '../../../src/widgets/registry.tsx';
import { onTransient } from '../../../src/sync.ts';
import { t } from '../../../src/i18n.ts';
import {
  notesStore, notesRevision, loadNotes, readNote, writeNote, createNote, applyRemoteChange,
} from './notesStore.ts';
import { parseNote } from './markdown.ts';
import { renderLines, readText, getCaret, setCaret, lineIndexOf } from './editorDom.ts';
import {
  pressEnter, shiftIndent, toggleWrap, insertText, toggleTaskLine,
  toggleBlock, cycleHeading, activeMarks, type Edit,
} from './editing.ts';
import NotesToolbar, { BAR_KEYS, BUBBLE_KEYS, type FormatKey } from './NotesToolbar.tsx';
import { History } from './history.ts';
import './Notes.css';

// Task-checkbox icons, built once and cloned per checkbox. They live here
// rather than in editorDom.ts for two reasons: that module is the pure DOM
// bridge and stays free of Solid/lucide so the unit runner can import it, and
// lucide-solid resolves to JSX source which only the app's build transforms.
// `createRoot` + immediate dispose: the nodes are static, so no computation
// should outlive their creation.
let boxTemplates: { on: Node; off: Node } | null = null;

function boxIcon(checked: boolean): Node {
  if (!boxTemplates) {
    boxTemplates = createRoot((dispose) => {
      const on = <SquareCheck size={13} strokeWidth={1.5} /> as unknown as Node;
      const off = <Square size={13} strokeWidth={1.5} /> as unknown as Node;
      dispose();
      return { on, off };
    });
  }
  const node = (checked ? boxTemplates.on : boxTemplates.off).cloneNode(true) as Element;
  node.classList.add('md-box-icon');
  // `data-deco` keeps it out of readText and out of every caret offset
  // (editorDom); `contenteditable=false` keeps the browser from letting the
  // caret walk into the SVG.
  node.setAttribute('data-deco', '');
  node.setAttribute('contenteditable', 'false');
  return node;
}

const RENDER_OPTS = { boxIcon };

interface NotesProps {
  /** Which note in the local store this widget shows. Empty on a widget that
   *  has not been bound yet — a fresh drop, or one predating the store. */
  noteId?: string;
  /** LEGACY: the inline note text from before notes became documents. Read
   *  exactly once, by the migration in `bindNote`, then dropped from config. */
  content?: string;
  fontSize?: number;
  headerMode?: HeaderMode;
  label?: string;
  _widgetId?: string;
}

/** Widgets currently minting a note, keyed by widget id. Without this the
 *  editor and its config panel — both mounted in one window, both seeing an
 *  empty `noteId` — would each create a note and one would be orphaned. */
const binding = new Set<string>();

// The note source is the single source of truth; the contenteditable is a
// projection of `parseNote(text)` rebuilt on every change (see editorDom.ts).
// Nothing reads formatting state off the DOM, so the markdown the user sees and
// the string we persist can never drift apart.
export default function Notes(props: NotesProps) {
  const layoutStore = useLayout();
  const fontSize = () => props.fontSize ?? 13;
  const [text, setText] = createSignal('');
  // `loading` until the store answers; `missing` once the note this widget
  // points at is gone (deleted from another window). The two are distinct on
  // purpose — persisting into a note that no longer exists would resurrect it.
  const [status, setStatus] = createSignal<'loading' | 'ready' | 'missing'>('loading');

  let editorEl!: HTMLDivElement;
  let bodyEl!: HTMLDivElement;
  const history = new History('');

  // Last selection seen INSIDE the editor. It outlives blur on purpose: the top
  // toolbar has to act somewhere when the note isn't focused.
  const [sel, setSel] = createSignal<{ start: number; end: number }>({ start: 0, end: 0 });
  const marks = createMemo(() => activeMarks(text(), sel().start, sel().end));
  // Where the selection bubble should sit, in `.widget-body` CSS pixels; null
  // means no ranged selection, so no bubble.
  const [anchor, setAnchor] = createSignal<{ cx: number; top: number; bottom: number } | null>(null);
  const [bubbleEl, setBubbleEl] = createSignal<HTMLDivElement | null>(null);
  // Selection to restore after the next projection. Null means "leave the
  // caret alone" (an external change, e.g. the config panel's Clear notes).
  let pendingSel: { start: number; end: number } | null = null;
  // Set while an IME composition is open: re-rendering mid-composition tears
  // out the nodes the IME is editing and drops the composed text.
  let composing = false;

  let debounceTimer: ReturnType<typeof setTimeout> | null = null;
  // Latest value awaiting a debounced write; flushed on blur and on unmount so
  // a note typed in the last 400ms isn't silently lost.
  let pendingVal: string | null = null;
  // Guards against an in-flight load landing after a newer one (note switched
  // mid-read), which would show the previous note's body.
  let loadSeq = 0;

  // Keep the store's list warm: the header reads this note's title from it.
  createEffect(() => { notesRevision(); void loadNotes(); });

  // Resolve which note we show, and pull its body. Tracks `props.noteId` (the
  // config panel can repoint this widget at another note) and `notesRevision`
  // (another window changed the store).
  //
  // `untrack` around the body: this effect must depend on those two ALONE.
  // Reading `text` while tracking would subscribe it to our own typing, and it
  // would then restore every keystroke back to the last persisted value.
  createEffect(() => {
    const id = props.noteId ?? '';
    notesRevision();
    untrack(() => { void resolve(id); });
  });

  async function resolve(id: string): Promise<void> {
    const my = ++loadSeq;
    if (!id) { await bindNote(); return; }
    const body = await readNote(id);
    if (my !== loadSeq) return; // superseded by a newer load
    if (body === null) { setStatus('missing'); return; }
    setStatus('ready');
    // Our own unsaved edit outranks the incoming body: last write wins, and
    // ours has not landed yet. It will re-broadcast when it does.
    if (pendingVal !== null) return;
    if (body === untrack(text)) return;
    history.reset(body);
    pendingSel = null;
    setText(body);
  }

  /**
   * First run for this widget: mint a note and record its id in config.
   *
   * This is also the migration off the old model — a widget that predates the
   * store carries its text inline in `content`, and that text seeds the new
   * note. `content` is only dropped from config once the note exists, so a
   * failed create leaves the old text exactly where it was and the next mount
   * retries.
   */
  async function bindNote(): Promise<void> {
    const wid = props._widgetId;
    // No widget id means nowhere to write the binding back to (an Add-Widget
    // preview, a dev harness). Render whatever text we were handed instead of
    // minting a note nothing could ever reach again.
    if (!wid) {
      const seed = props.content ?? '';
      history.reset(seed);
      setText(seed);
      setStatus('ready');
      return;
    }
    if (binding.has(wid)) return;
    binding.add(wid);
    try {
      const seed = props.content ?? '';
      const meta = await createNote(props.label || t('widget.notes.label'), seed);
      if (!meta) { setStatus('missing'); return; }
      // Deliberately NOT guarded on a load sequence number. The note now
      // EXISTS, so the binding has to be recorded or it is orphaned on disk
      // with nothing pointing at it. `binding` already excludes a competing
      // bind for this widget, and `createNote` bumps the store revision, which
      // re-enters the load effect — the very re-entry a sequence guard here
      // would mistake for a stale load, leaving `noteId` unwritten forever.
      const current = layoutStore.layout.widgets.find(w => w.id === wid)?.config ?? {};
      // Rebuild without `content`: the text lives in the note now, and leaving
      // a stale copy behind would make the next migration overwrite the note.
      const { content: _legacy, ...rest } = current as Record<string, unknown>;
      void _legacy;
      layoutStore.updateWidgetConfig(wid, { ...rest, noteId: meta.id });
      history.reset(seed);
      setText(seed);
      setStatus('ready');
    } finally {
      binding.delete(wid);
    }
  }

  // Project the source into the editor. Runs for local edits and external
  // changes alike, so there is exactly one path from text to DOM.
  createEffect(() => {
    const source = text();
    if (composing) return;
    renderLines(editorEl, parseNote(source), RENDER_OPTS);
    const sel = pendingSel;
    pendingSel = null;
    if (sel && editorEl.ownerDocument.activeElement === editorEl) {
      setCaret(editorEl, sel.start, sel.end);
    }
  });

  // Selection tracking drives both toolbars: which buttons light up, and
  // whether the bubble is showing. `selectionchange` is the only event that
  // fires for every way a selection can change (mouse, keyboard, Select All).
  function onSelectionChange() {
    // The listener is registered during setup; refs land at mount.
    if (!editorEl || !bodyEl) return;
    const selection = editorEl.ownerDocument.getSelection();
    const inside = !!selection && selection.rangeCount > 0
      && editorEl.contains(selection.getRangeAt(0).commonAncestorContainer);
    if (!inside) { setAnchor(null); return; }
    const caret = getCaret(editorEl);
    if (caret) setSel(caret);
    setAnchor(anchorFor(selection));
  }

  /** Selection rect -> `.widget-body` coordinates, or null when collapsed. */
  function anchorFor(selection: Selection) {
    if (selection.isCollapsed) return null;
    const range = selection.getRangeAt(0);
    const rects = range.getClientRects();
    // First rect, not the union: a selection spanning lines should anchor to
    // where it starts, not to the middle of the whole block.
    const rect = rects.length > 0 ? rects[0] : range.getBoundingClientRect();
    const body = bodyEl.getBoundingClientRect();
    // Both rects are post-transform viewport pixels; CSS offsets inside the
    // body are pre-transform. Measuring the body's own scale covers every host
    // (grid `--monitor-scale`, config window `--ui-scale`, plain browser).
    const scale = body.width / (bodyEl.offsetWidth || 1) || 1;
    return {
      cx: (rect.left + rect.width / 2 - body.left) / scale,
      top: (rect.top - body.top) / scale,
      bottom: (rect.bottom - body.top) / scale,
    };
  }

  // Place the bubble once it has been measured: centred over the selection,
  // above it by preference, flipped below when the selection is on the first
  // line, and always clamped inside the widget (which clips its overflow).
  createEffect(() => {
    const at = anchor();
    const el = bubbleEl();
    if (!at || !el) return;
    const maxX = Math.max(4, bodyEl.offsetWidth - el.offsetWidth - 4);
    const x = Math.min(Math.max(4, at.cx - el.offsetWidth / 2), maxX);
    // Stay within the editor's band, not the body's: above the editor is the
    // formatting bar, and covering it would hide the buttons the bubble echoes.
    const minY = editorEl.offsetTop + 2;
    const above = at.top - el.offsetHeight - 6;
    const y = above >= minY ? above : at.bottom + 6;
    el.style.left = `${x}px`;
    el.style.top = `${Math.max(minY, Math.min(y, bodyEl.offsetHeight - el.offsetHeight - 2))}px`;
  });

  const doc = document;
  doc.addEventListener('selectionchange', onSelectionChange);
  onCleanup(() => doc.removeEventListener('selectionchange', onSelectionChange));

  // Another window changed the store: re-read so two widgets on one note (or a
  // grid widget and its pop-out) converge.
  onMount(() => {
    const off = onTransient('notes-changed', (msg) => {
      // A body change elsewhere only matters if it is OUR note; a list change
      // (no id) always matters, since it may be our note being deleted.
      if (msg.id && msg.id !== props.noteId) return;
      applyRemoteChange();
    });
    onCleanup(off);
  });

  // Flush any pending debounced write on unmount so a note typed in the last
  // 400ms isn't lost when the widget is removed / the view switches. The write
  // is now IPC rather than a synchronous localStorage set, so it is also
  // flushed on blur and on window teardown — the two moments a pending 400ms
  // timer would otherwise die with the page.
  onCleanup(() => { flushPending(); });

  const flushOnUnload = () => flushPending();
  window.addEventListener('beforeunload', flushOnUnload);
  onCleanup(() => window.removeEventListener('beforeunload', flushOnUnload));

  // Persist the pending value immediately (called by the debounce timer, on
  // blur, and on teardown). Idempotent: clears the timer/pending state and
  // no-ops if nothing is pending.
  function flushPending() {
    if (debounceTimer) { clearTimeout(debounceTimer); debounceTimer = null; }
    if (pendingVal === null) return;
    const val = pendingVal;
    pendingVal = null;
    const id = props.noteId;
    // Unbound or deleted: there is nothing to write into. Dropping the value is
    // correct — writing would resurrect a note the user deleted.
    if (!id || status() === 'missing') return;
    void writeNote(id, val);
  }

  function schedulePersist(val: string) {
    pendingVal = val;
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(flushPending, 400);
  }

  /** Commit new source + selection, then persist. The only write path. */
  function commit(next: string, start: number, end: number) {
    history.record(next, start, end);
    pendingSel = { start, end };
    setSel({ start, end });
    if (next === text()) {
      // Source unchanged but the DOM may hold nodes the browser built (or the
      // caret needs moving), so re-project explicitly rather than waiting on a
      // signal that will not fire.
      renderLines(editorEl, parseNote(next), RENDER_OPTS);
      setCaret(editorEl, start, end);
      pendingSel = null;
    } else {
      setText(next);
    }
    schedulePersist(next);
  }

  function apply(edit: Edit) {
    commit(edit.text, edit.start, edit.end);
  }

  /** Run a toolbar command. Same operations the keyboard shortcuts use. */
  function runFormat(key: FormatKey) {
    // Focus first: the projection only restores the caret while the editor is
    // the active element, and the bar is usable before the note is focused.
    editorEl.focus();
    const at = getCaret(editorEl) ?? sel();
    const source = text();
    switch (key) {
      case 'heading': apply(cycleHeading(source, at.start, at.end)); break;
      case 'bold': apply(toggleWrap(source, at.start, at.end, '**')); break;
      case 'italic': apply(toggleWrap(source, at.start, at.end, '*')); break;
      case 'strike': apply(toggleWrap(source, at.start, at.end, '~~')); break;
      case 'code': apply(toggleWrap(source, at.start, at.end, '`')); break;
      default: apply(toggleBlock(source, at.start, at.end, key));
    }
  }

  function onInput() {
    if (composing) return;
    // The browser has already applied the keystroke; read it back and let the
    // projection re-derive the styling around it.
    const next = readText(editorEl);
    const sel = getCaret(editorEl);
    commit(next, sel?.start ?? next.length, sel?.end ?? next.length);
  }

  function onCompositionStart() { composing = true; }

  function onCompositionEnd() {
    composing = false;
    onInput();
  }

  function onKeyDown(e: KeyboardEvent) {
    if (composing) return;
    const mod = e.ctrlKey || e.metaKey;
    const key = e.key.toLowerCase();

    if (mod && (key === 'z' || key === 'y')) {
      e.preventDefault();
      const snap = (key === 'y' || e.shiftKey) ? history.redo() : history.undo();
      if (!snap) return;
      pendingSel = { start: snap.start, end: snap.end };
      if (snap.text === text()) {
        renderLines(editorEl, parseNote(snap.text));
        setCaret(editorEl, snap.start, snap.end);
        pendingSel = null;
      } else {
        setText(snap.text);
      }
      schedulePersist(snap.text);
      return;
    }

    const sel = getCaret(editorEl);
    if (!sel) return;

    if (mod && (key === 'b' || key === 'i' || key === 'u')) {
      // `u` has no markdown equivalent, but the browser's native underline
      // would inject a <u> the projection would then throw away.
      e.preventDefault();
      if (key === 'u') return;
      apply(toggleWrap(text(), sel.start, sel.end, key === 'b' ? '**' : '*'));
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      // Shift+Enter is a plain break: no list marker is carried over.
      apply(e.shiftKey
        ? insertText(text(), sel.start, sel.end, '\n')
        : pressEnter(text(), sel.start, sel.end));
      return;
    }
    if (e.key === 'Tab') {
      e.preventDefault();
      apply(shiftIndent(text(), sel.start, sel.end, e.shiftKey ? -1 : 1));
    }
  }

  function onPaste(e: ClipboardEvent) {
    e.preventDefault();
    const pasted = e.clipboardData?.getData('text/plain');
    if (!pasted) return;
    const sel = getCaret(editorEl);
    if (!sel) return;
    apply(insertText(text(), sel.start, sel.end, pasted.replace(/\r\n?/g, '\n')));
  }

  // Dragging the editor's own selection is refused outright. contenteditable
  // would treat it as a copy (the source text stays put) unless we also
  // implemented the matching cut, and a half-move is worse than no move.
  function onDragStart(e: DragEvent) {
    e.preventDefault();
  }

  function onDrop(e: DragEvent) {
    e.preventDefault();
    const dropped = e.dataTransfer?.getData('text/plain');
    if (!dropped) return;
    const doc = editorEl.ownerDocument as Document & {
      caretRangeFromPoint?: (x: number, y: number) => Range | null;
    };
    // Move the caret to where the text was dropped before computing offsets.
    const range = doc.caretRangeFromPoint?.(e.clientX, e.clientY);
    if (range) {
      const selection = doc.getSelection();
      selection?.removeAllRanges();
      selection?.addRange(range);
    }
    const sel = getCaret(editorEl);
    if (!sel) return;
    apply(insertText(text(), sel.start, sel.end, dropped.replace(/\r\n?/g, '\n')));
  }

  function onClick(e: MouseEvent) {
    const target = e.target as HTMLElement | null;
    const box = target?.closest?.('.md-box');
    if (!box || !editorEl.contains(box)) return;
    const index = lineIndexOf(editorEl, box);
    if (index < 0) return;
    const next = toggleTaskLine(text(), index);
    if (next === null) return;
    // `[ ]` <-> `[x]` is length-preserving, so the caret the click just placed
    // stays valid.
    const sel = getCaret(editorEl);
    commit(next, sel?.start ?? 0, sel?.end ?? 0);
  }

  // The note's own title, so a desktop of Notes widgets is readable at a
  // glance. An explicit widget `label` still wins — that is the user naming
  // this window, not the document.
  const noteTitle = () => notesStore.notes.find(n => n.id === props.noteId)?.title ?? '';
  const headerMode = () => props.headerMode ?? 'normal';
  // The header band doubles as the formatting row when it can hold one.
  const toolbarInHeader = () => headerMode() === 'normal';
  // `none` has no header to host the bar, so it gets a standalone row — that is
  // the pop-out window's mode. `compact` gets no bar at all: compact is the mode
  // you pick to reclaim vertical space, and a toolbar band would hand it back.
  const toolbarInBody = () => headerMode() === 'none';

  return (
    <div class="notes-widget">
      <WidgetHeader
        mode={headerMode()}
        icon={StickyNote}
        title={props.label || noteTitle() || t('widget.notes.label')}
        actions={toolbarInHeader()
          ? <NotesToolbar keys={BAR_KEYS} marks={marks()} onRun={runFormat} />
          : undefined}
      />
      <div class="widget-body" ref={bodyEl}>
        <Show when={toolbarInBody()}>
          <NotesToolbar keys={BAR_KEYS} marks={marks()} onRun={runFormat} />
        </Show>
        {/* The note this widget points at is gone (deleted in another window
            or straight off disk). Shown INSTEAD of the editor, but the editor
            stays mounted underneath: every effect here holds `editorEl` as a
            ref, and unmounting it would leave the projection writing into a
            detached node. */}
        <Show when={status() === 'missing'}>
          <div class="notes-widget__gone">{t('notes.noteGone')}</div>
        </Show>
        <div
          ref={editorEl}
          class="notes-widget__editor"
          classList={{ 'is-empty': text() === '', 'is-gone': status() === 'missing' }}
          style={{ '--notes-font-size': `${fontSize()}px` }}
          contenteditable="true"
          spellcheck={false}
          data-placeholder={t('notes.placeholder')}
          onInput={onInput}
          onKeyDown={onKeyDown}
          onPaste={onPaste}
          onDragStart={onDragStart}
          onDrop={onDrop}
          onClick={onClick}
          onCompositionStart={onCompositionStart}
          onCompositionEnd={onCompositionEnd}
          onBlur={() => { setAnchor(null); flushPending(); }}
        />
        <Show when={anchor()}>
          <NotesToolbar
            elRef={setBubbleEl}
            class="notes-toolbar--bubble"
            keys={BUBBLE_KEYS}
            marks={marks()}
            onRun={runFormat}
          />
        </Show>
      </div>
    </div>
  );
}

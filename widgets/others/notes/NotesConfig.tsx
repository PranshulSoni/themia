import { Show, For, createEffect, onMount, onCleanup } from 'solid-js';
import { ask } from '@tauri-apps/plugin-dialog';
import { Monitor, Database, Circle, CheckCircle2, HardDrive, X as XIcon } from 'lucide-solid';
import AccountToggleList from '../../../src/components/AccountToggleList.tsx';
import ConfigTabs from '../../../src/components/ConfigTabs.tsx';
import NumberStepper from '../../../src/components/NumberStepper.tsx';
import { onTransient } from '../../../src/sync.ts';
import {
  notesStore, notesRevision, loadNotes, createNote, renameNote, deleteNote, writeNote,
  applyRemoteChange, type NoteMeta,
} from './notesStore.ts';
import '../../../src/styles/config-base.css';
import './NotesConfig.css';
import { t } from '../../../src/i18n.ts';

interface NotesConfigData {
  noteId?: string;
  /** LEGACY: pre-store inline text. Never written here; the widget migrates it
   *  into a real note and drops the key. */
  content?: string;
  fontSize?: number;
}

interface NotesConfigProps {
  config: NotesConfigData;
  onChange: (c: NotesConfigData) => void;
}

export default function NotesConfig(props: NotesConfigProps) {
  function update<K extends keyof NotesConfigData>(key: K, value: NotesConfigData[K]) {
    props.onChange({ ...props.config, [key]: value });
  }

  // [S-13] Stateless: every persisted control reads straight from props.config,
  // so a change made elsewhere shows here instead of being reverted by a stale
  // local copy. The note POOL is shared state and lives in the store, not here;
  // only the selection is a config value.
  const fontSize = () => props.config.fontSize ?? 13;
  const noteId = () => props.config.noteId ?? '';

  createEffect(() => { notesRevision(); void loadNotes(); });

  // The config window is its own OS window, so a note created or renamed from a
  // widget lands here only through the broadcast.
  onMount(() => {
    const off = onTransient('notes-changed', () => applyRemoteChange());
    onCleanup(off);
  });

  async function addNote(): Promise<void> {
    const meta = await createNote(t('notes.newName'));
    if (meta) update('noteId', meta.id);
  }

  async function removeNote(note: NoteMeta): Promise<void> {
    // A note's text is the whole point of it and nothing can bring it back, so
    // deleting confirms through the same native dialog the to-do list pool and
    // the mood log use.
    const ok = await ask(t('notes.deleteConfirm', { name: note.title }), {
      title: t('notes.deleteTitle'),
      kind: 'warning',
    });
    if (!ok) return;
    if (!(await deleteNote(note.id))) return;
    // The widget showing it falls back to its "note is gone" state rather than
    // being silently handed someone else's note.
    if (noteId() === note.id) update('noteId', '');
  }

  async function clearNote(): Promise<void> {
    const id = noteId();
    if (!id) return;
    const ok = await ask(t('notes.clearConfirm'), { title: t('notes.clearLabel'), kind: 'warning' });
    if (!ok) return;
    await writeNote(id, '');
  }

  // The pool of notes on this device, and which one this widget shows. Rendered
  // inside the "On this device" card via the AccountToggleList slot. Creating or
  // renaming here reaches every widget already displaying the note; deleting is
  // a device-wide action, not this widget's private cleanup.
  const notePicker = () => (
    <div class="notes-doc-list">
      <span class="notes-doc-list-label">{t('notes.notesLabel')}</span>
      <Show when={notesStore.notes.length > 0}>
        {/* Settings.SPEC [S-18]/[S-11]: the shared collection-editor
            primitive, ended by a `btn-add`. Only the ROW is ours. */}
        <div class="items-list">
          <For each={notesStore.notes}>
            {(note) => (
              <div class={`notes-doc ${noteId() === note.id ? 'active' : ''}`}>
                <button
                  class="notes-doc-pick"
                  title={t('notes.selectNote')}
                  aria-label={t('notes.selectNote')}
                  onClick={() => update('noteId', note.id)}
                >
                  <Show when={noteId() === note.id} fallback={<Circle size={15} strokeWidth={1.5} />}>
                    <CheckCircle2 size={15} strokeWidth={1.5} />
                  </Show>
                </button>
                {/* Committed on blur / Enter rather than per keystroke: the
                    title round-trips through disk, and re-setting `value` from
                    the store mid-word would move the caret. */}
                <input
                  class="notes-doc-name"
                  type="text"
                  value={note.title}
                  placeholder={t('notes.namePlaceholder')}
                  onChange={(e) => { void renameNote(note.id, e.currentTarget.value); }}
                />
                <button
                  class="item-remove"
                  title={t('common.delete')}
                  aria-label={t('common.delete')}
                  onClick={() => { void removeNote(note); }}
                ><XIcon size={12} strokeWidth={2} /></button>
              </div>
            )}
          </For>
        </div>
      </Show>
      <button class="btn-add" onClick={() => { void addNote(); }}>{t('notes.newNote')}</button>
    </div>
  );

  return (
    <ConfigTabs tabs={[
      { key: 'source', label: t('configTabs.source'), icon: Database },
      { key: 'display', label: t('configTabs.display'), icon: Monitor },
    ]}>
      {(tab) => <>
        <Show when={tab === 'source'}>
          {/* [S-35] The same source list every account-bound widget shows, with
              "On this device" as its leading row. Notes have no account
              backend, so this is the ONE source: its switch is disabled rather
              than inviting a click that could only take the widget's storage
              away. `providerFilter` is empty, so no account rows are fetched.
              Mirrors the Mood Tracker.

              The note picker rides in that row's own card, because picking a
              note is a question ABOUT this source — the same slot the calendar
              widget hangs its per-account calendar picker in. */}
          <AccountToggleList
            providerFilter={[]}
            providerBadges={{}}
            accountIds={() => []}
            onToggle={() => { /* ignore: no account sources for notes */ }}
            leadingOptions={[{
              id: 'local',
              name: t('notes.storage.local'),
              badge: t('notes.storage.localBadge'),
              sublabel: t('notes.storage.localDesc'),
              icon: <HardDrive class="provider-logo" size={20} strokeWidth={1.5} />,
              active: () => true,
              disabled: true,
              onToggle: () => { /* ignore: the only source, never switched off */ },
              extra: notePicker,
            }]}
          />
        </Show>

        <Show when={tab === 'display'}>
          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('notes.fontSizeLabel')}</span>
              <span class="design-desc">{t('notes.fontSizeDesc')}</span>
            </div>
            <NumberStepper
              value={fontSize()}
              min={10}
              max={20}
              step={1}
              unit="px"
              onChange={(v) => update('fontSize', v)}
            />
          </div>

          <div class="design-row">
            <div class="design-left">
              <span class="design-label">{t('notes.clearLabel')}</span>
              <span class="design-desc">{t('notes.clearDesc')}</span>
            </div>
            <button
              class="btn-danger"
              disabled={!noteId()}
              onClick={() => { void clearNote(); }}
            >{t('notes.clearLabel')}</button>
          </div>

        </Show>
      </>}
    </ConfigTabs>
  );
}

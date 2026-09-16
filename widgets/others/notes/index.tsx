import { lazy } from 'solid-js';
import Notes from './Notes.tsx';
import { StickyNote } from 'lucide-solid';
import type { WidgetDefinition } from '../../../src/widgets/registry.tsx';
import { t } from '../../../src/i18n.ts';

// [R1] Config panels render only inside the config window, but the registry
// is imported by every overlay renderer. Loading this lazily keeps it out of
// the overlay's chunk.
const NotesConfig = lazy(() => import('./NotesConfig.tsx'));

const notes: WidgetDefinition = {
  component: Notes,
  configComponent: NotesConfig,
  get label() { return t('widget.notes.label'); },
  get description() { return t('widget.notes.description'); },
  icon: () => <StickyNote size={20} strokeWidth={1.5} />,
  category: 'Others',
  defaultSize: { width: 220, height: 330 },
  popOut: true,
  defaultConfig: {
    // Empty: the widget mints a note in the local store on first mount and
    // writes its id back here. Seeding an id would hand every new widget the
    // same note.
    noteId: '',
    fontSize: 13,
  },
};

export default notes;

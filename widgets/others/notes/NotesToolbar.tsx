import { For, type Component } from 'solid-js';
import { Dynamic } from 'solid-js/web';
import {
  Heading, Bold, Italic, Strikethrough, Code, List, ListOrdered, ListTodo, Quote,
} from 'lucide-solid';
import { t } from '../../../src/i18n.ts';
import type { Marks } from './editing.ts';

/** Every formatting command the two toolbars can offer. */
export type FormatKey =
  | 'heading' | 'bold' | 'italic' | 'strike' | 'code'
  | 'ul' | 'ol' | 'task' | 'quote';

interface Spec {
  icon: Component<{ size?: number; strokeWidth?: number }>;
  /** i18n key under `widget.notes.fmt`. */
  label: string;
  /** Whether this command is in force at the caret. */
  isActive: (m: Marks) => boolean;
}

const SPECS: Record<FormatKey, Spec> = {
  heading: { icon: Heading, label: 'heading', isActive: (m) => m.heading > 0 },
  bold: { icon: Bold, label: 'bold', isActive: (m) => m.bold },
  italic: { icon: Italic, label: 'italic', isActive: (m) => m.italic },
  strike: { icon: Strikethrough, label: 'strike', isActive: (m) => m.strike },
  code: { icon: Code, label: 'code', isActive: (m) => m.code },
  ul: { icon: List, label: 'bullet', isActive: (m) => m.block === 'ul' },
  ol: { icon: ListOrdered, label: 'numbered', isActive: (m) => m.block === 'ol' },
  task: { icon: ListTodo, label: 'task', isActive: (m) => m.block === 'task' },
  quote: { icon: Quote, label: 'quote', isActive: (m) => m.block === 'quote' },
};

/** The persistent bar above the editor: every command, in reading order. */
export const BAR_KEYS: FormatKey[] = [
  'heading', 'bold', 'italic', 'strike', 'code', 'ul', 'ol', 'task', 'quote',
];

/** The selection bubble: only the commands that wrap the selected text. */
export const BUBBLE_KEYS: FormatKey[] = ['bold', 'italic', 'strike', 'code'];

interface NotesToolbarProps {
  keys: FormatKey[];
  marks: Marks;
  onRun: (key: FormatKey) => void;
  class?: string;
  /** Not named `ref`: Solid gives component `ref` props special handling. */
  elRef?: (el: HTMLDivElement) => void;
}

export default function NotesToolbar(props: NotesToolbarProps) {
  return (
    <div
      ref={props.elRef}
      class={`notes-toolbar${props.class ? ' ' + props.class : ''}`}
      // The editor must keep both focus and its selection: a toolbar command
      // acts on the selection, and the browser would clear it on mousedown.
      onMouseDown={(e) => e.preventDefault()}
    >
      <For each={props.keys}>
        {(key) => {
          const spec = SPECS[key];
          const title = () => t(`widget.notes.fmt.${spec.label}`);
          return (
            <button
              type="button"
              class="notes-toolbar__btn"
              classList={{ active: spec.isActive(props.marks) }}
              title={title()}
              aria-label={title()}
              aria-pressed={spec.isActive(props.marks)}
              onClick={() => props.onRun(key)}
            >
              <Dynamic component={spec.icon} size={13} strokeWidth={1.5} />
            </button>
          );
        }}
      </For>
    </div>
  );
}

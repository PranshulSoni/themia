import { createStore, reconcile } from 'solid-js/store';
import { onStorageKey, sharedStorage } from './sync.ts';
import { logger } from './debugLog.ts';
import {
  loadPersisted,
  savePersistedBare,
  objectSchema,
  isArrayOf,
  isString,
  isOneOf,
  type PersistedSchema,
} from './persistedStore.ts';

interface ClipboardState {
  paths: string[];
  mode: 'copy' | 'cut';
}

const FILE_CLIPBOARD_KEY = 'file-clipboard';

// Schema'd persistence (1.12): validates that `paths` is a string[] and `mode`
// is one of the two literals, so a corrupt/poisoned value falls back to an empty
// clipboard instead of being trusted. Legacy bare blobs load + re-save enveloped.
const SCHEMA: PersistedSchema<ClipboardState> = objectSchema<ClipboardState>({
  version: 1,
  defaults: () => ({ paths: [], mode: 'copy' }),
  fields: { paths: isArrayOf(isString), mode: isOneOf('copy', 'cut') },
});

function loadFromStorage(): ClipboardState {
  return loadPersisted(FILE_CLIPBOARD_KEY, SCHEMA, localStorage, {
    onCorrupt: (m) => logger.error('fileClipboard', m),
  });
}

const [clipboard, _setClipboard] = createStore<ClipboardState>(loadFromStorage());
export { clipboard };

export function setClipboard(state: Partial<ClipboardState>): void {
  _setClipboard(state);
  // Bare format ({paths,mode}): the key is synced cross-window and read raw.
  savePersistedBare(
    FILE_CLIPBOARD_KEY,
    { paths: clipboard.paths, mode: clipboard.mode },
    sharedStorage,
    { onError: (m) => logger.error('fileClipboard', m) },
  );
}

// Sync from other windows via storage events
onStorageKey(FILE_CLIPBOARD_KEY, () => {
  _setClipboard(reconcile(loadFromStorage()));
});

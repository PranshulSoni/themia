import { createSignal } from 'solid-js';

interface FolderSelection {
  widgetId: string;
  path: string;
}

const [selectedFolder, setSelectedFolderSignal] = createSignal<FolderSelection | null>(null);
export { selectedFolder };

export function selectFolder(widgetId: string, path: string): void {
  setSelectedFolderSignal({ widgetId, path });
}

export function clearSelectedFolder(): void {
  setSelectedFolderSignal(null);
}

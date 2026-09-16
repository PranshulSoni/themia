import { save, open, ask } from '@tauri-apps/plugin-dialog';
import { writeTextFile, readTextFile } from '@tauri-apps/plugin-fs';
import { getVersion } from '@tauri-apps/api/app';
import { layoutKey, isDemoView, MONITOR_KEY_SCHEMA_KEY } from './views.ts';
import { TODO_LOCAL_LISTS_KEY } from '../widgets/office/todo/localLists.ts';
import { validate, type ThemiaExport } from './importValidate.ts';

async function collectExportData(): Promise<ThemiaExport> {
  const screens: { id: string; name: string }[] = (() => {
    try {
      const raw = localStorage.getItem('widget-screens');
      if (!raw) return [{ id: 'default', name: 'Default' }];
      return (JSON.parse(raw) as { id: string; name: string }[]).filter(s => !isDemoView(s.id));
    } catch { return [{ id: 'default', name: 'Default' }]; }
  })();

  // Keyed by MONITOR ID ([MI-01]: the stable device interface path), not the
  // `\\.\DISPLAYn` ordinal it used to be (#70). Both this map and the
  // visible-screens map below round-trip their keys verbatim, so they stay
  // correct as long as the keys are stable — which is the point of the #70 fix.
  // A file exported by a pre-#70 build still carries ordinals; the boot-time
  // [MI-03] migration in views.ts re-keys them on the next launch (importing
  // triggers a reload), and the marker it writes is deliberately NOT part of an
  // export, so imported ordinals always get re-migrated on the target machine.
  const monitorScreenMap: Record<string, string> = (() => {
    try { return JSON.parse(localStorage.getItem('widget-monitor-screen-map') ?? '{}'); } catch { return {}; }
  })();

  const monitorVisibleScreensMap: Record<string, string[]> = (() => {
    try {
      const raw = JSON.parse(localStorage.getItem('widget-screen-selector-visible-map') ?? '{}');
      const out: Record<string, string[]> = {};
      for (const k of Object.keys(raw)) {
        if (Array.isArray(raw[k])) out[k] = raw[k].filter((x: unknown) => typeof x === 'string');
      }
      return out;
    } catch { return {}; }
  })();

  const globalDesign: Record<string, unknown> = (() => {
    try { return JSON.parse(localStorage.getItem('widget-global-design') ?? '{}'); } catch { return {}; }
  })();

  // To Do's device lists (todo [S-57]) live in their own key rather than inside
  // a widget's config, so they'd be the one thing an export silently dropped —
  // the layouts would come back pointing at lists that were never carried over.
  const todoLocalLists: unknown = (() => {
    try { return JSON.parse(localStorage.getItem(TODO_LOCAL_LISTS_KEY) ?? '[]'); } catch { return []; }
  })();

  const layouts: Record<string, unknown> = {};
  for (const view of screens) {
    const key = layoutKey(view.id);
    const raw = localStorage.getItem(key);
    if (raw) {
      try { layouts[key] = JSON.parse(raw); } catch {
        // ignore: a corrupt per-view layout key is skipped so export still
        // produces a valid file with the remaining views' layouts.
      }
    }
  }

  const appVersion = await getVersion();

  // Strip legacy per-layout bgImage paths; they're machine-specific and the
  // feature was removed. Older settings files may still contain them; just
  // drop on export so re-import is clean.
  for (const layoutData of Object.values(layouts)) {
    if (layoutData && typeof layoutData === 'object') {
      delete (layoutData as Record<string, unknown>).bgImage;
      delete (layoutData as Record<string, unknown>).bgImageFit;
    }
  }

  return {
    version: 3,
    appVersion,
    exportedAt: new Date().toISOString(),
    screens,
    monitorScreenMap,
    monitorVisibleScreensMap,
    defaultScreenForNewDisplays: localStorage.getItem('widget-default-screen-for-new-displays'),
    globalDesign,
    layouts,
    todoLocalLists,
    editMode: localStorage.getItem('themia-edit-mode') === 'true',
    autoCheckUpdates: localStorage.getItem('themia-auto-check-updates') !== 'false',
  };
}

export async function exportSettings(): Promise<void> {
  const data = await collectExportData();
  const path = await save({
    defaultPath: 'themia-settings.json',
    filters: [{ name: 'JSON', extensions: ['json'] }],
  });
  if (!path) return;
  await writeTextFile(path, JSON.stringify(data, null, 2));
}

export async function importSettings(): Promise<void> {
  const path = await open({
    multiple: false,
    filters: [{ name: 'Themia Settings', extensions: ['json'] }],
  });
  if (!path) return;

  const raw = await readTextFile(path as string);
  const data = validate(JSON.parse(raw));

  const confirmed = await ask('This will replace all your current views, layouts, and design settings. Continue?', {
    title: 'Import Settings',
    kind: 'warning',
  });
  if (!confirmed) return;

  applyImport(data, localStorage);
  window.location.reload();
}

/**
 * Apply a validated export to `storage`: the data-loss-critical write path,
 * extracted from `importSettings` so it can be characterized. Clears stale
 * layout / per-screen-design / last-known-good-backup keys, then writes the
 * imported screens, design, per-view layouts and flags. Parameterized on
 * `storage` (production passes `localStorage`).
 */
export function applyImport(data: ThemiaExport, storage: Storage): void {
  // Clear stale layout + per-screen design keys. The per-screen design model
  // is gone (migrateV3 wipes these at boot), but importing into a half-migrated
  // install benefits from the same defensive wipe.
  const keysToRemove: string[] = [];
  for (let i = 0; i < storage.length; i++) {
    const key = storage.key(i);
    if (!key) continue;
    if (key.startsWith('widget-layout')) keysToRemove.push(key);
    else if (key === 'widget-design' || key.startsWith('widget-design-')) keysToRemove.push(key);
    // Drop last-known-good backups (persistedStore `__bak__*`): the imported
    // values supersede them, and a stale backup could otherwise resurrect
    // pre-import state on a later corrupt-primary recovery.
    else if (key.startsWith('__bak__')) keysToRemove.push(key);
  }
  keysToRemove.forEach(k => storage.removeItem(k));

  // Write imported data
  storage.setItem('widget-screens', JSON.stringify(data.screens));
  storage.setItem('widget-monitor-screen-map', JSON.stringify(data.monitorScreenMap));
  if (data.monitorVisibleScreensMap && Object.keys(data.monitorVisibleScreensMap).length > 0) {
    storage.setItem('widget-screen-selector-visible-map', JSON.stringify(data.monitorVisibleScreensMap));
  } else {
    storage.removeItem('widget-screen-selector-visible-map');
  }
  if (data.defaultScreenForNewDisplays) {
    storage.setItem('widget-default-screen-for-new-displays', data.defaultScreenForNewDisplays);
  } else {
    storage.removeItem('widget-default-screen-for-new-displays');
  }
  storage.setItem('widget-global-design', JSON.stringify(data.globalDesign));

  for (const [key, value] of Object.entries(data.layouts)) {
    // Strip legacy bg fields from older export files, feature removed.
    if (value && typeof value === 'object') {
      delete (value as Record<string, unknown>).bgImage;
      delete (value as Record<string, unknown>).bgImageFit;
    }
    storage.setItem(key, JSON.stringify(value));
  }
  // To Do's device lists (todo [S-57]). Written only when the file carries them:
  // a file exported before device lists existed cannot express "no lists", so
  // clearing on absence would delete tasks the import was never asked about.
  if (data.todoLocalLists !== undefined) {
    storage.setItem(TODO_LOCAL_LISTS_KEY, JSON.stringify(data.todoLocalLists));
  }
  storage.setItem('themia-edit-mode', String(data.editMode));
  storage.setItem('themia-auto-check-updates', String(data.autoCheckUpdates));
  // Re-arm the #70 monitor-key migration. The imported maps may be keyed on
  // pre-#70 `\\.\DISPLAYn` ordinals (a file exported by an older build), and a
  // target machine that already migrated carries the stamp that makes the
  // boot-time [MI-03] pass skip. Dropping it makes the reload at the end of
  // importSettings re-key the imported maps onto stable ids.
  storage.removeItem(MONITOR_KEY_SCHEMA_KEY);
  // Mark both migrations done so boot-time migrators don't touch the imported
  // state (which is already in v3 shape).
  storage.setItem('widget-design-migration-v2', '1');
  storage.setItem('widget-design-migration-v3', '1');
}

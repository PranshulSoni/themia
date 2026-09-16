// Pure (icon-free) helpers for Windows `shell:` namespace paths. Kept separate
// from quickSets.ts so modules like items.ts can classify/label shell paths
// without importing lucide-solid (which pulls in the Solid renderer and can't
// load in a plain node/unit-test context).

/** Friendly labels for the quick-set shell paths, single-sourced here and
 *  reused by quickSets.ts (which pairs each with an icon). Insertion order is
 *  the order the config "Quick folders" grid renders them in. */
export const SHELL_LABELS: Record<string, string> = {
  'shell:RecycleBinFolder':    'Recycle Bin',
  'shell:MyComputerFolder':    'This PC',
  'shell:Downloads':           'Downloads',
  'shell:Personal':            'Documents',
  'shell:Desktop':             'Desktop',
  'shell:My Pictures':         'Pictures',
  'shell:UsersFilesFolder':    'Home',
  'shell:ControlPanelFolder':  'Control Panel',
  'shell:NetworkPlacesFolder': 'Network',
};

export function isShellPath(path: string | undefined | null): boolean {
  return !!path && path.startsWith('shell:');
}

/** Friendly label for a known quick-set shell path, else undefined. */
export function shellLabel(path: string | undefined | null): string | undefined {
  return path ? SHELL_LABELS[path] : undefined;
}

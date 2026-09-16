import type { Component } from 'solid-js';
import { HardDrive, Clock, FileType, Cloud, GitBranch, Hash } from 'lucide-solid';
import { t } from '../../../src/i18n.ts';

export interface ColumnDef {
  key: string;
  label: () => string;
  icon: Component<{ size?: number; strokeWidth?: number }>;
  defaultOn: boolean;
  width: string;
}

export const COLUMN_DEFS: ColumnDef[] = [
  { key: 'size',      label: () => t('folder.detailSize'),     icon: HardDrive, defaultOn: true,  width: '64px'  },
  { key: 'modified',  label: () => t('folder.detailModified'), icon: Clock,     defaultOn: true,  width: '106px' },
  { key: 'type',      label: () => t('folder.detailType'),     icon: FileType,  defaultOn: true,  width: '54px'  },
  { key: 'cloudSync', label: () => t('folder.detailCloud'),    icon: Cloud,     defaultOn: false, width: '30px'  },
  { key: 'gitStatus', label: () => t('folder.detailGit'),      icon: GitBranch, defaultOn: false, width: '110px' },
  { key: 'itemCount', label: () => t('folder.detailItems'),    icon: Hash,      defaultOn: false, width: '50px'  },
];

export const COLUMN_KEYS: string[] = COLUMN_DEFS.map(c => c.key);
export const CANONICAL_VISIBLE: string[] = COLUMN_DEFS.filter(c => c.defaultOn).map(c => c.key);

export function getColumnDef(key: string): ColumnDef | undefined {
  return COLUMN_DEFS.find(c => c.key === key);
}

export interface ResolvedColumns {
  visible: string[];
  hidden: string[];
}

/**
 * Resolve the saved `detailsColumns` config into ordered visible/hidden lists.
 *
 * Accepts both the new shape (`string[]`) and the legacy shape (`Record<string,boolean>`).
 */
export function resolveColumns(raw: unknown): ResolvedColumns {
  let visible: string[];
  if (Array.isArray(raw)) {
    visible = raw.filter((k): k is string => typeof k === 'string' && COLUMN_KEYS.includes(k));
  } else if (raw && typeof raw === 'object') {
    const rec = raw as Record<string, unknown>;
    visible = COLUMN_KEYS.filter(k => {
      const v = rec[k];
      if (v === undefined) return getColumnDef(k)!.defaultOn;
      return !!v;
    });
  } else {
    visible = [...CANONICAL_VISIBLE];
  }
  const hidden = COLUMN_KEYS.filter(k => !visible.includes(k));
  return { visible, hidden };
}

export function isColumnVisible(raw: unknown, key: string): boolean {
  return resolveColumns(raw).visible.includes(key);
}

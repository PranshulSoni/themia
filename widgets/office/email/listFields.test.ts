import { describe, it, expect } from 'vitest';
import { resolveMetrics, type MetricDef } from '../../../src/widgets/metricSet.ts';

// Mirrors LIST_FIELD_DEFS from columns.ts but with a dummy icon so the suite
// stays lucide-free and node-importable (same convention as metricSet.test.ts).
// Keep keys + defaultOn in sync with columns.ts.
const noopIcon = (() => null) as unknown as MetricDef['icon'];
const LIST_FIELDS: MetricDef[] = [
  { key: 'subject', label: () => 'Subject', icon: noopIcon, defaultOn: true },
  { key: 'preview', label: () => 'Preview', icon: noopIcon, defaultOn: true },
  { key: 'account', label: () => 'Account', icon: noopIcon, defaultOn: true },
];

// [S-43] list/compact view field selection. Mirrors the render-time gating in
// Email.tsx so the contract is pinned:
//   subject → field on
//   preview → field on AND viewMode === 'list'
//   account → field on (issue #45c: no longer gated on a multi-account feed;
//             shows whenever the user enabled it, matching Row view)
function gate(raw: unknown, opts: { viewMode: 'list' | 'compact' }) {
  const v = new Set(resolveMetrics(raw, LIST_FIELDS).visible);
  return {
    subject: v.has('subject'),
    preview: v.has('preview') && opts.viewMode === 'list',
    account: v.has('account'),
  };
}

describe('email list fields [S-43]', () => {
  it('defaults (undefined) show subject, preview, and account', () => {
    expect(resolveMetrics(undefined, LIST_FIELDS).visible).toEqual([
      'subject', 'preview', 'account',
    ]);
  });

  it('list view: all fields on shows everything', () => {
    expect(gate(['subject', 'preview', 'account'], { viewMode: 'list' }))
      .toEqual({ subject: true, preview: true, account: true });
  });

  it('hiding preview keeps subject and account', () => {
    expect(gate(['subject', 'account'], { viewMode: 'list' }))
      .toEqual({ subject: true, preview: false, account: true });
  });

  it('hiding subject keeps preview and account', () => {
    expect(gate(['preview', 'account'], { viewMode: 'list' }))
      .toEqual({ subject: false, preview: true, account: true });
  });

  it('preview never renders in compact view even when selected', () => {
    expect(gate(['subject', 'preview', 'account'], { viewMode: 'compact' }).preview)
      .toBe(false);
  });

  it('issue #45c: account tag shows for a single-account feed when selected', () => {
    // Was gated on a multi-account feed; now it shows whenever the field is on.
    expect(gate(['subject', 'account'], { viewMode: 'list' }).account).toBe(true);
    expect(gate(['subject', 'account'], { viewMode: 'compact' }).account).toBe(true);
  });

  it('account can be turned off', () => {
    expect(gate(['subject', 'preview'], { viewMode: 'list' }).account).toBe(false);
  });

  it('ignores unknown keys', () => {
    expect(resolveMetrics(['subject', 'bogus'], LIST_FIELDS).visible).toEqual(['subject']);
  });
});

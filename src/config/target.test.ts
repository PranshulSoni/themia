import { describe, it, expect } from 'vitest';
import { encodeTarget, parseTarget, analyticsPath, type ConfigTarget, type AppSettingsPage } from './target.ts';

const APP_PAGES: AppSettingsPage[] = ['general', 'style', 'license', 'accounts', 'updates', 'logs'];

describe('config target encode/parse', () => {
  const roundTrips: ConfigTarget[] = [
    ...APP_PAGES.map((page): ConfigTarget => ({ kind: 'app', page })),
    { kind: 'view', viewId: 'view-123' },
    { kind: 'display', monitorId: 'monitor-0' },
    { kind: 'widget', widgetId: 'clock-1' },
    { kind: 'add' },
    // Add-into-container ([S-34]): the picker bound to one container widget.
    { kind: 'add', into: 'stack-1712345678' },
  ];

  for (const t of roundTrips) {
    it(`round-trips ${JSON.stringify(t)}`, () => {
      expect(parseTarget(encodeTarget(t))).toEqual(t);
    });
  }

  it('round-trips an id that itself contains a colon (split on first only)', () => {
    const t: ConfigTarget = { kind: 'widget', widgetId: 'weird:id:with:colons' };
    expect(parseTarget(encodeTarget(t))).toEqual(t);
  });

  // Tolerant parse: garbage must yield null so callers fall back to a default,
  // never a half-built object (boundary-validation invariant).
  it.each([
    ['null', null],
    ['undefined', undefined],
    ['empty', ''],
    ['no separator', 'garbage'],
    ['unknown kind', 'bogus:x'],
    ['empty rest', 'view:'],
    ['invalid app page', 'app:not-a-page'],
  ])('returns null for %s input', (_label, raw) => {
    expect(parseTarget(raw as string | null | undefined)).toBeNull();
  });

  it('S-34_bare "add" parses without a container, so the plain picker never binds one', () => {
    expect(parseTarget('add')).toEqual({ kind: 'add' });
    expect((parseTarget('add') as { into?: string }).into).toBeUndefined();
  });

  it('accepts every declared app page', () => {
    for (const page of APP_PAGES) {
      expect(parseTarget(`app:${page}`)).toEqual({ kind: 'app', page });
    }
  });
});

describe('analyticsPath', () => {
  // analytics [S-23] — the config window reports which PAGE was opened, never
  // which view/monitor/widget instance. Those ids are per-install values (a
  // monitor id is derived from the device name, a view name is user-typed), so
  // only the closed sets survive: the settings page and the widget TYPE.
  it('S-23_keeps the closed sets and drops every per-install id', () => {
    expect(analyticsPath({ kind: 'app', page: 'license' })).toBe('settings/license');
    expect(analyticsPath({ kind: 'add' })).toBe('add-widget');
    expect(analyticsPath({ kind: 'widget', widgetId: 'notes-1735689600000-3' })).toBe('widget/notes');

    // ids that would identify an install must not appear anywhere in the path
    expect(analyticsPath({ kind: 'view', viewId: 'v-7f3a91' })).toBe('view');
    expect(analyticsPath({ kind: 'display', monitorId: '\\\\.\\DISPLAY2#My-Monitor' })).toBe('display');
  });

  it('S-23_covers every target kind', () => {
    const targets: ConfigTarget[] = [
      { kind: 'app', page: 'general' },
      { kind: 'view', viewId: 'a' },
      { kind: 'display', monitorId: 'a' },
      { kind: 'widget', widgetId: 'clock-1-0' },
      { kind: 'add', toTray: true },
    ];
    for (const t of targets) {
      const path = analyticsPath(t);
      expect(path, `${t.kind} must map to a non-empty path`).toBeTruthy();
    }
  });
});

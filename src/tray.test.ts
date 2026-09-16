// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';

// Deliberately imports `trayLayout.ts`, not `tray.ts`: the latter pulls in the
// widget registry, whose `.tsx` modules a plain unit test cannot load.
import {
  clampFlyoutSize, resolveFlyoutSize, resolveTrayIconKey, resolveTrayName,
  FLYOUT_MIN, FLYOUT_MAX,
} from './trayLayout.ts';
import { isTrayEnv, prepareTrayIconSvg, trayIconColor } from './trayIcon.ts';
import { encodeTarget, parseTarget } from './config/target.ts';

function svg(attrs: Record<string, string>): SVGElement {
  const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
  return el;
}

describe('Tray [S-07] flyout size', () => {
  it('falls back to the widget type default when nothing is stored', () => {
    expect(resolveFlyoutSize(undefined, { width: 220, height: 330 }))
      .toEqual({ width: 220, height: 330 });
    expect(resolveFlyoutSize({}, { width: 220, height: 330 }))
      .toEqual({ width: 220, height: 330 });
  });

  it('prefers the stored per-instance size', () => {
    expect(resolveFlyoutSize({ trayWidth: 300, trayHeight: 240 }, { width: 220, height: 330 }))
      .toEqual({ width: 300, height: 240 });
  });

  it('treats a corrupt stored size as absent, not as zero', () => {
    // A hand-edited / damaged config restores the author's default rather than
    // collapsing the flyout or clamping to an arbitrary edge.
    const fallback = { width: 220, height: 330 };
    expect(resolveFlyoutSize({ trayWidth: '300', trayHeight: null }, fallback)).toEqual(fallback);
    expect(resolveFlyoutSize({ trayWidth: NaN, trayHeight: Infinity }, fallback)).toEqual(fallback);
  });

  it('clamps to the flyout bounds in both directions', () => {
    expect(clampFlyoutSize({ width: 10, height: 10 })).toEqual(FLYOUT_MIN);
    expect(clampFlyoutSize({ width: 9999, height: 9999 })).toEqual(FLYOUT_MAX);
    // A stored size outside the bounds is corrected on read, not trusted.
    expect(resolveFlyoutSize({ trayWidth: 5000, trayHeight: 1 }, FLYOUT_MIN))
      .toEqual({ width: FLYOUT_MAX.width, height: FLYOUT_MIN.height });
  });

  it('rounds to whole pixels', () => {
    expect(clampFlyoutSize({ width: 220.4, height: 330.6 }))
      .toEqual({ width: 220, height: 331 });
  });
});

describe('Tray [S-14] icon and name overrides', () => {
  const known = (k: string) => ['star', 'coffee'].includes(k);

  it('falls back to the widget type\'s own icon when nothing is chosen', () => {
    expect(resolveTrayIconKey(undefined, known)).toBe('');
    expect(resolveTrayIconKey({}, known)).toBe('');
    expect(resolveTrayIconKey({ trayIcon: '' }, known)).toBe('');
  });

  it('uses a chosen catalogue icon', () => {
    expect(resolveTrayIconKey({ trayIcon: 'star' }, known)).toBe('star');
  });

  it('falls back rather than drawing nothing when the stored icon is unknown', () => {
    // A retired catalogue entry must degrade to the widget's own icon, not to
    // an empty bitmap the backend would reject.
    expect(resolveTrayIconKey({ trayIcon: 'retired-icon' }, known)).toBe('');
    expect(resolveTrayIconKey({ trayIcon: 42 }, known)).toBe('');
  });

  it('falls back to the caller-resolved title when no tray name is set', () => {
    expect(resolveTrayName(undefined, 'Notes')).toBe('Notes');
    expect(resolveTrayName({}, 'Notes')).toBe('Notes');
    // Whitespace is "unset", not a blank tooltip.
    expect(resolveTrayName({ trayName: '   ' }, 'Notes')).toBe('Notes');
    expect(resolveTrayName({ trayName: 7 }, 'Notes')).toBe('Notes');
  });

  it('uses the tray name when set, trimmed', () => {
    expect(resolveTrayName({ trayName: 'Shopping' }, 'Notes')).toBe('Shopping');
    expect(resolveTrayName({ trayName: '  Shopping  ' }, 'Notes')).toBe('Shopping');
  });
});

describe('Tray [S-03] icon rasterization source', () => {
  it('picks a colour the taskbar cannot swallow', () => {
    expect(trayIconColor(false)).toBe('#ffffff');
    expect(trayIconColor(true)).toBe('#000000');
  });

  it('pins size and colour, because currentColor has nothing to inherit', () => {
    const out = prepareTrayIconSvg(
      svg({ width: '20', height: '20', stroke: 'currentColor', 'stroke-width': '1.5' }),
      16,
      '#ffffff',
    );
    expect(out).toContain('width="16"');
    expect(out).toContain('height="16"');
    expect(out).toContain('stroke="#ffffff"');
    expect(out).not.toContain('currentColor');
    expect(out).toContain('xmlns="http://www.w3.org/2000/svg"');
  });

  it('raises a hairline stroke to stay legible at icon size', () => {
    const out = prepareTrayIconSvg(svg({ 'stroke-width': '1.5' }), 16, '#ffffff');
    expect(out).toContain('stroke-width="2"');
  });

  it('leaves an already-heavy stroke alone', () => {
    const out = prepareTrayIconSvg(svg({ 'stroke-width': '3' }), 16, '#ffffff');
    expect(out).toContain('stroke-width="3"');
  });

  it('does not mutate the element it was handed', () => {
    const source = svg({ width: '20', 'stroke-width': '1.5' });
    prepareTrayIconSvg(source, 16, '#ffffff');
    expect(source.getAttribute('width')).toBe('20');
    expect(source.getAttribute('stroke-width')).toBe('1.5');
  });
});

describe('Tray [S-02] tray_env is validated at the IPC boundary', () => {
  it('rejects every shape that is not a usable env', () => {
    expect(isTrayEnv({ iconSize: 16, lightTheme: false })).toBe(true);
    // The preview/demo IPC seam resolves undefined; an unvalidated read of that
    // threw mid-sync and left an unhandled rejection behind.
    expect(isTrayEnv(undefined)).toBe(false);
    expect(isTrayEnv(null)).toBe(false);
    expect(isTrayEnv({})).toBe(false);
    expect(isTrayEnv({ iconSize: 16 })).toBe(false);
    expect(isTrayEnv({ iconSize: 0, lightTheme: false })).toBe(false);
    expect(isTrayEnv({ iconSize: 16.5, lightTheme: false })).toBe(false);
    expect(isTrayEnv({ iconSize: '16', lightTheme: false })).toBe(false);
  });
});

describe('Tray [S-08] config targets', () => {
  it('round-trips "add to the tray" without colliding with "add to a container"', () => {
    const encoded = encodeTarget({ kind: 'add', toTray: true });
    expect(parseTarget(encoded)).toEqual({ kind: 'add', toTray: true });
    // A container id is `<type>-<millis>-<seq>`, so it can never be the sentinel.
    expect(parseTarget('add:stack-123-0')).toEqual({ kind: 'add', into: 'stack-123-0' });
    expect(parseTarget('add')).toEqual({ kind: 'add' });
  });
});

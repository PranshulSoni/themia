// @vitest-environment jsdom
//
// [S-46] Cross-window license propagation.
//
// Regression for "Pro is active but the 11th widget is still refused": the tier
// lives in a per-window store filled once at mount, activation only ever runs
// in the CONFIG window, and nothing told the desktop overlays. Settings showed
// Pro while `atFreeWidgetLimit()` in the overlay kept the paywall up until the
// app was restarted.
//
// The bus is faked (a real BroadcastChannel never delivers to its own sender,
// so one module instance can't play both windows).

import { describe, it, expect, vi, beforeEach } from 'vitest';

const h = vi.hoisted(() => ({
  invoke: vi.fn(),
  posted: [] as { type: string }[],
  handlers: new Map<string, ((msg: unknown) => void)[]>(),
}));

vi.mock('@tauri-apps/api/core', () => ({
  invoke: (cmd: string, args?: Record<string, unknown>) => h.invoke(cmd, args),
}));
vi.mock('./analytics.ts', () => ({ reportPurchase: vi.fn() }));
vi.mock('./debugLog.ts', () => ({
  logger: { error: vi.fn(), warn: vi.fn(), info: vi.fn(), debug: vi.fn() },
}));
vi.mock('./sync.ts', () => ({
  postTransient: (msg: { type: string }) => { h.posted.push(msg); },
  onTransient: (type: string, fn: (msg: unknown) => void) => {
    if (!h.handlers.has(type)) h.handlers.set(type, []);
    h.handlers.get(type)!.push(fn);
    return () => {};
  },
}));

import { initLicense, activateLicense, deactivateLicense, isPro, licenseState } from './license.ts';

const PRO_STATUS = {
  tier: 'pro', activated: true, key: '****-****-****-ABCD',
  instance_id: 'inst-1', valid_until: null, error: null,
};
const FREE_STATUS = {
  tier: 'free', activated: false, key: null,
  instance_id: null, valid_until: null, error: null,
};

/** Deliver the broadcast the way another window's post would arrive. */
function deliverLicenseChanged(): void {
  for (const fn of h.handlers.get('license-changed') ?? []) fn({ type: 'license-changed' });
}

beforeEach(() => {
  h.invoke.mockReset();
  h.posted.length = 0;
});

describe('[S-46] license changes reach every window', () => {
  it('activation broadcasts license-changed', async () => {
    h.invoke.mockResolvedValue(PRO_STATUS);
    await activateLicense('SOME-KEY');
    expect(h.posted).toContainEqual({ type: 'license-changed' });
  });

  it('a failed activation broadcasts nothing', async () => {
    h.invoke.mockResolvedValue({ ...FREE_STATUS, error: 'invalid key' });
    await activateLicense('BAD-KEY');
    expect(h.posted).toEqual([]);
  });

  it('deactivation broadcasts license-changed', async () => {
    h.invoke.mockResolvedValue(undefined);
    await deactivateLicense();
    expect(h.posted).toContainEqual({ type: 'license-changed' });
  });

  // The bug: this window booted free, the config window activated Pro, and this
  // window never heard about it.
  it('a window that booted free upgrades to pro on the broadcast', async () => {
    h.invoke.mockResolvedValueOnce(FREE_STATUS); // license_validate at mount
    await initLicense();
    expect(isPro()).toBe(false);

    h.invoke.mockResolvedValueOnce(PRO_STATUS);
    deliverLicenseChanged();

    await vi.waitFor(() => expect(isPro()).toBe(true));
    expect(licenseState.activated).toBe(true);
  });

  // Reading the CACHED status keeps the fan-out offline: the activating window
  // already wrote the cache, so n windows must not each re-hit Lemon Squeezy.
  it('the refresh reads the cached status, not the network validate', async () => {
    h.invoke.mockResolvedValueOnce(PRO_STATUS);
    deliverLicenseChanged();

    await vi.waitFor(() => expect(h.invoke).toHaveBeenCalled());
    expect(h.invoke.mock.calls.map(c => c[0])).toEqual(['license_status']);
  });

  it('a downgrade broadcast drops this window back to free', async () => {
    h.invoke.mockResolvedValueOnce(PRO_STATUS);
    deliverLicenseChanged();
    await vi.waitFor(() => expect(isPro()).toBe(true));

    h.invoke.mockResolvedValueOnce(FREE_STATUS);
    deliverLicenseChanged();
    await vi.waitFor(() => expect(isPro()).toBe(false));
  });

  // A transient IPC error must never demote a paying user mid-session.
  it('a failed refresh keeps the current tier', async () => {
    h.invoke.mockResolvedValueOnce(PRO_STATUS);
    deliverLicenseChanged();
    await vi.waitFor(() => expect(isPro()).toBe(true));

    h.invoke.mockRejectedValueOnce(new Error('ipc down'));
    deliverLicenseChanged();

    await vi.waitFor(() => expect(h.invoke).toHaveBeenCalledTimes(2));
    expect(isPro()).toBe(true);
  });
});

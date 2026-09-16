import { createStore, type SetStoreFunction, type Store } from 'solid-js/store';
import { invoke as realInvoke } from '@tauri-apps/api/core';
import { reportPurchase } from './analytics.ts';
import { postTransient, onTransient } from './sync.ts';
import { logger } from './debugLog.ts';

declare global {
  interface Window {
    __licenseInvoke?: { setInvoke: (fn: typeof realInvoke | null) => void };
    __license?: { licenseState: Store<LicenseState>; setLicenseState: SetStoreFunction<LicenseState> };
  }
}

// Test-only override mirroring src/demo/invoke.ts. Tauri 2 makes the
// IPC `invoke` non-writable so e2e specs can't stub it at the window
// level. Exposing a setter via `window.__licenseInvoke` lets specs
// intercept newsletter / activation calls.
let invokeOverride: (typeof realInvoke) | null = null;
async function invoke<T = unknown>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  if (invokeOverride) return invokeOverride<T>(cmd, args);
  return realInvoke<T>(cmd, args);
}
if (__THEMIA_TEST_HOOKS__ && typeof window !== 'undefined' && navigator.webdriver) {
  window.__licenseInvoke = {
    setInvoke: (fn: typeof realInvoke | null) => { invokeOverride = fn; },
  };
}

/** Closed set of license tiers (1.13/B4, no stringly tiers). */
export type LicenseTier = 'free' | 'pro';

/** Narrow the raw `tier` string from the Rust IPC boundary to a known tier.
 *  Anything that isn't the exact recognized paid tier degrades to `free`; a
 *  corrupt/unknown value must never accidentally unlock Pro. */
function asTier(raw: string): LicenseTier {
  return raw === 'pro' ? 'pro' : 'free';
}

interface LicenseStatus {
  // Raw value from the `license_*` IPC commands: untrusted, narrowed via
  // `asTier` before it reaches the store.
  tier: string;
  activated: boolean;
  key: string | null;
  instance_id: string | null;
  valid_until: string | null;
  error: string | null;
}

interface LicenseState extends Omit<LicenseStatus, 'tier'> {
  tier: LicenseTier;
  loading: boolean;
}

export const [licenseState, setLicenseState] = createStore<LicenseState>({
  tier: 'free',
  activated: false,
  key: null,
  instance_id: null,
  valid_until: null,
  error: null,
  loading: true,
});

export function isPro(): boolean {
  return licenseState.tier === 'pro';
}

export async function initLicense(): Promise<void> {
  setLicenseState({ loading: true, error: null });

  // Test override: WebDriver sessions always run as Pro so end-to-end tests
  // can exercise gated features (multi-view, widget-count > 5, etc.).
  // `navigator.webdriver` is W3C-spec (set only under real automation), AND the
  // whole block is gated on `__THEMIA_TEST_HOOKS__`, a build-time constant that
  // is `false` in a release binary, so this pro-unlock is dead-code-eliminated
  // and can never ship as a backdoor.
  if (__THEMIA_TEST_HOOKS__ && typeof navigator !== 'undefined' && navigator.webdriver) {
    setLicenseState({
      tier: 'pro',
      activated: true,
      key: null,
      instance_id: null,
      valid_until: null,
      error: null,
      loading: false,
    });
    // Expose a minimal test hook for e2e specs. Guarded on
    // `navigator.webdriver`, never present under normal use.
    try {
      window.__license = { licenseState, setLicenseState };
    } catch { /* ignore in non-browser contexts */ }
    return;
  }

  wireLicenseBus();

  try {
    const status = await invoke<LicenseStatus>('license_validate');
    setLicenseState({ ...status, tier: asTier(status.tier), loading: false });
  } catch (e) {
    setLicenseState({ tier: 'free', activated: false, loading: false, error: String(e) });
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Cross-window tier propagation ([S-46])
// ──────────────────────────────────────────────────────────────────────────────
//
// Every window has its own JS heap and so its own `licenseState`, filled once
// at mount. Activation only ever happens in the CONFIG window, so without a
// broadcast the desktop overlays keep the tier they booted with: Settings shows
// Pro while `atFreeWidgetLimit()` in the overlay still refuses the 11th widget
// until the app is restarted.

let licenseBusWired = false;

function wireLicenseBus(): void {
  if (licenseBusWired) return;
  licenseBusWired = true;
  onTransient('license-changed', () => { void refreshLicense(); });
}

/** Re-read the tier from the CACHED (offline) status. The window that changed
 *  the license has already written the encrypted cache through Rust, so the
 *  other windows must not each re-hit Lemon Squeezy. A failure keeps the
 *  current tier: a transient IPC error must never demote a paying user. */
async function refreshLicense(): Promise<void> {
  try {
    const status = await invoke<LicenseStatus>('license_status');
    setLicenseState({ ...status, tier: asTier(status.tier), loading: false });
  } catch (e) {
    logger.error('license', `cross-window refresh failed: ${String(e)}`);
  }
}

export async function activateLicense(key: string): Promise<boolean> {
  setLicenseState({ loading: true, error: null });
  try {
    const status = await invoke<LicenseStatus>('license_activate', { key });
    const tier = asTier(status.tier);
    setLicenseState({ ...status, tier, loading: false });
    // The tier is the product; the key itself never goes near analytics.
    if (status.activated) reportPurchase(tier);
    // Every other window is still on the tier it booted with ([S-46]).
    if (status.activated) postTransient({ type: 'license-changed' });
    return status.activated;
  } catch (e) {
    setLicenseState('loading', false);
    setLicenseState('error', String(e));
    return false;
  }
}

export async function deactivateLicense(): Promise<boolean> {
  setLicenseState({ loading: true, error: null });
  try {
    await invoke('license_deactivate');
    setLicenseState({ tier: 'free', activated: false, key: null, instance_id: null, valid_until: null, error: null, loading: false });
    postTransient({ type: 'license-changed' });
    return true;
  } catch (e) {
    setLicenseState('loading', false);
    setLicenseState('error', String(e));
    return false;
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// Newsletter subscription (Lemon Squeezy)
// ──────────────────────────────────────────────────────────────────────────────

interface NewsletterState {
  email: string;
  loading: boolean;
  subscribed: boolean;
  error: string | null;
}

const NEWSLETTER_LS_KEY = 'themia-newsletter-subscribed';

function loadInitialNewsletter(): NewsletterState {
  try {
    const saved = localStorage.getItem(NEWSLETTER_LS_KEY);
    if (saved) return { email: saved, loading: false, subscribed: true, error: null };
  } catch {/* ignore */}
  return { email: '', loading: false, subscribed: false, error: null };
}

export const [newsletterState, setNewsletterState] = createStore<NewsletterState>(loadInitialNewsletter());

export async function subscribeNewsletter(email: string): Promise<boolean> {
  const trimmed = email.trim();
  if (!trimmed || !trimmed.includes('@')) {
    setNewsletterState({ error: 'Invalid email' });
    return false;
  }
  setNewsletterState({ loading: true, error: null });
  try {
    await invoke('newsletter_subscribe', { email: trimmed });
    setNewsletterState({ loading: false, subscribed: true, email: trimmed, error: null });
    try { localStorage.setItem(NEWSLETTER_LS_KEY, trimmed); } catch {/* ignore */}
    return true;
  } catch (e) {
    setNewsletterState({ loading: false, error: String(e) });
    return false;
  }
}

export function resetNewsletter(): void {
  try { localStorage.removeItem(NEWSLETTER_LS_KEY); } catch {/* ignore */}
  setNewsletterState({ email: '', loading: false, subscribed: false, error: null });
}

// [DI-04] Adopting the live desktop-icon state into the Settings toggle.
//
// Windows owns this setting: the user can flip it from the desktop context menu
// at any time, including while the config window sits open on the General page.
// The stored `desktopIconsVisible` is a first-paint cache, so the toggle has to
// re-read reality on every occasion it could have gone stale — not just once
// when the panel mounts.
//
// This is the codebase's one sanctioned wall-clock poll. The shell broadcasts
// NOTHING when desktop icons are toggled (the state is the list-view window's
// visibility, [DI-02]), so there is no event to hang a listener on. The poll is
// therefore a display-freshness mechanism, never a correctness one: nothing
// waits on it, and dropping every tick only leaves the toggle showing the
// cached value it would have shown anyway. It is scoped to the one page that
// renders the toggle and stopped whenever that page is away or the window is
// hidden, so it never runs in the background.

/** How often the General page re-reads the shell state while it is visible. */
export const DESKTOP_ICONS_POLL_MS = 1000;

/**
 * [DI-04] Pure adoption decision. `live` is what the shell reports (`null` =
 * unreadable, e.g. non-Windows or the shell view could not be resolved).
 * Returns the value to write, or `null` for "leave the cache alone" — so an
 * unreadable state never clobbers what we have, and an agreeing state never
 * writes (which would persist + broadcast the design store on every tick).
 */
export function desktopIconsAdoption(live: boolean | null, stored: boolean): boolean | null {
  return typeof live === 'boolean' && live !== stored ? live : null;
}

type ListenerTarget = Pick<Window, 'addEventListener' | 'removeEventListener'>;

export interface DesktopIconsWatchOptions {
  /** Reads the live shell state (`get_desktop_icons`). */
  read: () => Promise<boolean | null>;
  /** The cached value currently shown by the toggle. */
  stored: () => boolean;
  /** Writes an adopted value into the design store. */
  apply: (visible: boolean) => void;
  /** Read failures are logged, never surfaced — the toggle keeps its cache. */
  onError?: (e: unknown) => void;
  intervalMs?: number;
  win?: ListenerTarget;
  doc?: ListenerTarget & { readonly visibilityState: DocumentVisibilityState };
}

export interface DesktopIconsWatcher {
  /** Re-read the shell state and adopt it if it disagrees with the cache. */
  refresh: () => void;
  /**
   * Turn the poll on while the page showing the toggle is up, off otherwise.
   * Turning it on refreshes immediately rather than waiting out a first tick.
   */
  poll: (on: boolean) => void;
  dispose: () => void;
}

/**
 * [DI-04] Keep the Settings toggle honest about a state Themia does not own.
 * Three triggers, cheapest first: window focus and visibility return (the user
 * must leave this window to reach the desktop context menu), plus a 1s poll
 * while the General page is up, which catches a change made without this window
 * ever losing focus (second monitor, another app, a script).
 */
export function watchDesktopIcons(opts: DesktopIconsWatchOptions): DesktopIconsWatcher {
  const win = opts.win ?? window;
  const doc = opts.doc ?? document;
  const intervalMs = opts.intervalMs ?? DESKTOP_ICONS_POLL_MS;

  let timer: ReturnType<typeof setInterval> | null = null;
  let polling = false;
  // Single-flight: a tick never stacks a second read on a slow one.
  let inFlight = false;

  const refresh = () => {
    if (inFlight) return;
    inFlight = true;
    opts.read()
      .then((live) => {
        const next = desktopIconsAdoption(live, opts.stored());
        if (next !== null) opts.apply(next);
      })
      .catch((e) => opts.onError?.(e))
      .finally(() => { inFlight = false; });
  };

  // The poll runs only when it is both wanted and useful: a hidden window
  // cannot show the toggle, so ticking there is pure waste.
  const syncTimer = () => {
    const wanted = polling && doc.visibilityState === 'visible';
    if (wanted && timer === null) {
      timer = setInterval(refresh, intervalMs);
    } else if (!wanted && timer !== null) {
      clearInterval(timer);
      timer = null;
    }
  };

  const onFocus = () => refresh();
  const onVisibility = () => {
    if (doc.visibilityState === 'visible') refresh();
    syncTimer();
  };

  win.addEventListener('focus', onFocus);
  doc.addEventListener('visibilitychange', onVisibility);

  return {
    refresh,
    poll: (on) => {
      if (on === polling) return;
      polling = on;
      if (on) refresh();
      syncTimer();
    },
    dispose: () => {
      polling = false;
      syncTimer();
      win.removeEventListener('focus', onFocus);
      doc.removeEventListener('visibilitychange', onVisibility);
    },
  };
}

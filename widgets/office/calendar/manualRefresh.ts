// Manual-refresh wiring for the calendar widget [S-40].
//
// The context-menu Refresh action (and the config window on close) bumps the
// `_refresh` config token. The calendar must react by re-fetching events AND
// forcing the birthday cache past its daily TTL, the only path allowed to
// force contact enumeration. The mount run is deliberately skipped: the main
// fetch effect covers first load, and a `_refresh` token persisted in config
// from a previous session must not force enumeration on app start [S-38].

import { createEffect, untrack } from 'solid-js';

export function createManualRefresh(
  token: () => number | undefined,
  onRefresh: () => void,
): void {
  createEffect<number | undefined>((prev) => {
    const cur = token();
    if (cur !== undefined && cur !== prev) untrack(onRefresh);
    return cur;
  }, untrack(token));
}

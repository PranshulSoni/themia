// Connect-provider discovery gate.
//
// Kept in its own JSX-free module so it can be unit-tested without importing the
// catalog (providers.tsx carries Solid JSX in its `hint` fields). Both connect
// pickers (ConnectWindow, AccountManager) filter through here.

import type { ConnectProvider } from './providers.tsx';

/** Pre-release gate: a provider marked `devOnly` is hidden from the connect
 *  pickers unless developer mode is on. Mirrors a widget's registry `devOnly`:
 *  the flag gates DISCOVERY only; existing connections always keep working. */
export function visibleProviders(list: ConnectProvider[], devMode: boolean): ConnectProvider[] {
  return list.filter(p => devMode || !p.devOnly);
}

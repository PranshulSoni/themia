// The one way the app checks for and installs its own updates.
//
// NOT `@tauri-apps/plugin-updater`: the plugin builds its HTTP client inside
// the Rust plugin, with no seam for the Windows proxy resolver that every other
// outbound request goes through, and it declares reqwest without
// `system-proxy` - so its check and download ignored the user's proxy entirely
// (netproxy SPEC [PX-09]). The Rust side owns `updater_check`/`updater_install`
// instead, and the plugin's own IPC commands are ungranted in
// `capabilities/default.json` so they cannot be called from here by accident.
import { invoke } from './ipc.ts';

/** An update the user can install. Release notes come from `changelog.ts`. */
export interface UpdateInfo {
  version: string;
}

/** Resolves to the available update, or `null` when already up to date. */
export function checkForUpdate(): Promise<UpdateInfo | null> {
  return invoke('updater_check');
}

/**
 * Download and install the update found by the last `checkForUpdate`.
 *
 * On Windows this hands off to the installer and exits, so the returned promise
 * normally never settles on success - callers show "restart to finish" once it
 * has been awaited without throwing.
 */
export function installUpdate(): Promise<void> {
  return invoke('updater_install');
}

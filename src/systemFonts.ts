// Cached loader for the installed system-font family names (issue #64), used by
// the Settings font picker. The set of installed fonts doesn't change in a way
// that matters mid-session, so the call is made once and the promise memoized —
// reopening Settings doesn't re-hit the backend. Failures resolve to an empty
// list (logged), so the picker degrades to just the curated presets rather than
// throwing.
import { invoke } from './demo/invoke.ts';
import { logger } from './debugLog.ts';

let cache: Promise<string[]> | null = null;

export function loadSystemFonts(): Promise<string[]> {
  if (!cache) {
    cache = invoke('list_system_fonts')
      .then((fonts) => (Array.isArray(fonts) ? fonts : []))
      .catch((e) => {
        logger.warn('design', `list_system_fonts failed: ${String(e)}`);
        return [];
      });
  }
  return cache;
}

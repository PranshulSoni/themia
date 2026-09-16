/**
 * Trust-boundary parse for the rejection value of `autostart_enable` /
 * `autostart_disable` (#69/#79, spec [AS-05]).
 *
 * Rust rejects with `{ kind, message }` where `kind` is a closed set. Anything
 * else reaching this function — a plugin error, a stubbed invoke, a future Rust
 * change — is NOT half-trusted into a typed object: it degrades to `other`,
 * whose UI shows the raw text and offers no advice we can't back up. Claiming
 * "run as administrator" on a failure we didn't classify would be the same dead
 * end #79 reported, just with more words.
 */

/** Closed set; mirrors `AutostartErrorKind` in `src-tauri/src/lib.rs`. */
export type AutostartErrorKind = 'elevationRequired' | 'other';

export interface AutostartError {
  kind: AutostartErrorKind;
  /** Raw backend diagnostic (schtasks op + exit code + stderr). May be ''. */
  message: string;
}

const KINDS: readonly AutostartErrorKind[] = ['elevationRequired', 'other'];

/** Best-effort human text for a value that isn't our typed payload. */
function describeUnknown(e: unknown): string {
  if (typeof e === 'string') return e;
  if (e instanceof Error) return e.message;
  if (e === null || e === undefined) return '';
  try {
    return JSON.stringify(e) ?? String(e);
  } catch {
    // ignore: cyclic / non-serializable value — String() is the fallback text
    return String(e);
  }
}

export function parseAutostartError(e: unknown): AutostartError {
  if (e && typeof e === 'object' && !(e instanceof Error)) {
    const o = e as Record<string, unknown>;
    if (typeof o.kind === 'string' && (KINDS as readonly string[]).includes(o.kind)) {
      return {
        kind: o.kind as AutostartErrorKind,
        message: typeof o.message === 'string' ? o.message : '',
      };
    }
  }
  return { kind: 'other', message: describeUnknown(e) };
}

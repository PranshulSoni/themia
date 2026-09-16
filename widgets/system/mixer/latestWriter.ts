// Single-flight "newest value wins" writer for slider drags ([S-11]).
//
// A drag emits an input event per mouse move — ~100/s. `mixer_set_volume` is an
// async command that re-enumerates the endpoint's audio sessions on a blocking
// thread, so firing one per event queues a hundred of them and the last write
// can land long after the user let go, snapping the slider back to a stale
// value. Intermediate values are worthless anyway: only the newest one is true.
//
// Event-driven, not timed: the next write is sent when the previous one
// RESOLVES, so there is no wall-clock throttle to tune (CLAUDE.md: no new
// wall-clock timing in correctness paths).

export interface LatestWriter<T> {
  /** Queue `value`; it is sent now if nothing is in flight, else it replaces
   *  whatever was waiting. */
  (value: T): void;
}

export function createLatestWriter<T>(
  send: (value: T) => Promise<unknown>,
  onError?: (err: unknown) => void,
): LatestWriter<T> {
  let inFlight = false;
  // A one-slot box rather than a bare `T | null`: `undefined`/`0`/`false` are
  // all legitimate values to write.
  let pending: { value: T } | null = null;

  async function pump(): Promise<void> {
    inFlight = true;
    try {
      while (pending) {
        const next = pending.value;
        pending = null;
        try {
          await send(next);
        } catch (err) {
          onError?.(err);
        }
      }
    } finally {
      inFlight = false;
    }
  }

  return (value: T) => {
    pending = { value };
    if (!inFlight) void pump();
  };
}

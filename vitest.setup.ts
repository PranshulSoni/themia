// Vitest global setup.
//
// Stores like sync.ts construct a `BroadcastChannel` at import time. Node 22 has
// a real global BroadcastChannel, but a real one opens a libuv handle that keeps
// the worker alive (and would deliver cross-test messages). For unit tests we
// don't exercise real cross-window delivery, so replace it with an inert
// EventTarget-backed stub. This lets store modules import cleanly under both the
// default `node` env and per-file `// @vitest-environment jsdom` opt-in.

class BroadcastChannelStub extends EventTarget {
  readonly name: string;
  onmessage: ((ev: MessageEvent) => void) | null = null;
  constructor(name: string) {
    super();
    this.name = name;
  }
  // Inert: tests that need real delivery should drive handlers directly.
  postMessage(_message: unknown): void {
    /* ignore: no cross-window delivery in unit tests */
  }
  close(): void {
    /* ignore: nothing to tear down */
  }
}

globalThis.BroadcastChannel =
  BroadcastChannelStub as unknown as typeof globalThis.BroadcastChannel;

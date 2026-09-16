/// <reference types="node" />
// Drift guard for the typed IPC layer (1.22). Parses the Rust
// `generate_handler!` list and checks the hand-maintained `Commands` map against
// it, so a renamed/removed command (or a typo in `Commands`) fails here instead
// of at runtime. Also a compile-time smoke that the typed `invoke` enforces
// command names + arg/return shapes.
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { TYPED_COMMANDS, invoke } from './ipc.ts';

function rustCommandSet(): Set<string> {
  const src = readFileSync(new URL('../src-tauri/src/lib.rs', import.meta.url), 'utf8');
  const m = src.match(/generate_handler!\s*\[([\s\S]*?)\]/);
  if (!m) throw new Error('generate_handler! block not found in lib.rs');
  const block = m[1].replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
  // Commands may be registered module-qualified (e.g. `drag_drop::start_native_drag`);
  // Tauri exposes the callable name as the final `::` segment, so strip the path.
  const names = block
    .split(/[\s,]+/)
    .map((s) => (s.includes('::') ? s.slice(s.lastIndexOf('::') + 2) : s))
    .filter((s) => /^[a-z_][a-z0-9_]*$/i.test(s));
  return new Set(names);
}

describe('ipc: typed command map vs Rust generate_handler!', () => {
  const rust = rustCommandSet();

  it('parses a sane number of Rust commands (~140)', () => {
    expect(rust.size).toBeGreaterThan(100);
  });

  it('every typed command exists in the Rust handler (no typos / stale names)', () => {
    const missing = TYPED_COMMANDS.filter((c) => !rust.has(c));
    expect(missing, `typed commands absent from generate_handler!: ${missing.join(', ')}`).toEqual([]);
  });

  it('reports typed-command coverage (informational)', () => {
    const pct = Math.round((TYPED_COMMANDS.length / rust.size) * 100);
    console.log(`[ipc] typed ${TYPED_COMMANDS.length}/${rust.size} commands (${pct}%)`);
    expect(TYPED_COMMANDS.length).toBeGreaterThan(0);
  });
});

// Compile-time smoke (never executed): the typed `invoke` must accept valid calls
// and reject invalid ones. tsc validates the `@ts-expect-error` lines.
async function _typeSmoke(): Promise<void> {
  const loc: string = await invoke('get_system_locale');           // no-arg form
  const id: string | null = await invoke('get_monitor_id', { label: 'w' });
  await invoke('open_url', { url: 'https://example.com' });
  void loc; void id;
  // @ts-expect-error: unknown command name
  await invoke('not_a_real_command');
  // @ts-expect-error: missing required args
  await invoke('open_url');
  // @ts-expect-error: wrong arg type
  await invoke('set_ssrf_bypass', { enabled: 'yes' });
}
void _typeSmoke;

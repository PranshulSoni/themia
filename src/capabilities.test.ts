// Every detached window must be listed in the Tauri capability set.
//
// Capabilities apply per WINDOW LABEL. A window whose label matches no entry in
// `capabilities/default.json` gets NO plugin or core permissions at all, and the
// failure is silent: `invoke` rejects in the webview and the UI simply does
// nothing. That is exactly how the "Add shortcut" window shipped — its icons
// (custom commands, ungated) rendered fine, while its File / Folder buttons
// (`dialog:allow-open`), its Escape close (`core:window:allow-close`) and its
// navigate listener (`core:event`) were all denied with no visible error.
//
// So: the labels declared in `DetachedWindowSpec`s are the source of truth, and
// this test fails when one of them is not covered by the capability's window
// patterns.

import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();

function read(rel: string): string {
  return readFileSync(join(ROOT, rel), 'utf8');
}

/** Labels of every `DetachedWindowSpec` declared in the Rust entry point. */
function detachedWindowLabels(): string[] {
  const src = read('src-tauri/src/lib.rs');
  const out: string[] = [];
  const re = /DetachedWindowSpec\s*\{\s*\n?\s*label:\s*"([^"]+)"/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(src)) !== null) out.push(m[1]);
  return out;
}

/** Window patterns the default capability applies to (may contain `*`). */
function capabilityWindows(): string[] {
  const json = JSON.parse(read('src-tauri/capabilities/default.json'));
  return json.windows as string[];
}

function matches(pattern: string, label: string): boolean {
  const rx = new RegExp('^' + pattern.split('*').map(p =>
    p.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$');
  return rx.test(label);
}

describe('Tauri capabilities cover every detached window', () => {
  it('guards the scanner: the specs are found', () => {
    const labels = detachedWindowLabels();
    expect(labels.length, 'no DetachedWindowSpec labels parsed').toBeGreaterThanOrEqual(4);
    expect(labels).toContain('config');
  });

  it('every detached window label matches a capability window pattern', () => {
    const patterns = capabilityWindows();
    const uncovered = detachedWindowLabels()
      .filter(label => !patterns.some(p => matches(p, label)));
    // A window listed here has no permissions at all: its plugin calls reject
    // silently and its buttons do nothing.
    expect(uncovered).toEqual([]);
  });

  it('the widget windows themselves stay covered', () => {
    const patterns = capabilityWindows();
    expect(patterns.some(p => matches(p, 'monitor-1'))).toBe(true);
  });
});

import { describe, it, expect } from 'vitest';
import type { ConnectProvider } from './providers.tsx';
import { visibleProviders } from './visibility.ts';

// A minimal, JSX-free provider so this test never pulls the real catalog
// (providers.tsx carries Solid JSX in its `hint` fields, which vitest, running
// without the solid transform, cannot compile).
function provider(over: Partial<ConnectProvider>): ConnectProvider {
  return { key: 'x', label: 'X', blurb: '', uses: [], group: 'service', kind: 'form', ...over };
}

const claude = provider({ key: 'claude', label: 'Claude Code', devOnly: true });
const github = provider({ key: 'github', label: 'GitHub' });

// [S-16] Pre-release gate: a provider marked `devOnly` is hidden from the
// connect pickers unless developer mode is on. The flag gates DISCOVERY only.
describe('visibleProviders [S-16]', () => {
  it('hides devOnly providers when developer mode is off', () => {
    const visible = visibleProviders([github, claude], false);
    expect(visible).toEqual([github]);
  });

  it('reveals devOnly providers when developer mode is on', () => {
    const visible = visibleProviders([github, claude], true);
    expect(visible).toEqual([github, claude]);
  });

  it('never hides providers without the devOnly flag', () => {
    expect(visibleProviders([github], false)).toEqual([github]);
    expect(visibleProviders([github], true)).toEqual([github]);
  });

  it('preserves catalog order', () => {
    const list = [claude, github];
    expect(visibleProviders(list, true)).toEqual([claude, github]);
  });
});

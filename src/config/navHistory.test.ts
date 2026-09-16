import { describe, it, expect } from 'vitest';
import { createNavHistory } from './navHistory.ts';
import type { AppSettingsPage, ConfigTarget } from './target.ts';

const app = (page: AppSettingsPage): ConfigTarget => ({ kind: 'app', page });

describe('createNavHistory', () => {
  it('starts empty: no current, cannot go back or forward', () => {
    const h = createNavHistory();
    expect(h.current()).to.equal(null);
    expect(h.canBack()).to.equal(false);
    expect(h.canForward()).to.equal(false);
    expect(h.back()).to.equal(null);
    expect(h.forward()).to.equal(null);
  });

  it('push records entries; back/forward walk the stack', () => {
    const h = createNavHistory();
    h.push(app('general'));
    h.push(app('style'));
    h.push(app('accounts'));

    expect(h.current()).to.deep.equal(app('accounts'));
    expect(h.canBack()).to.equal(true);
    expect(h.canForward()).to.equal(false);

    expect(h.back()).to.deep.equal(app('style'));
    expect(h.back()).to.deep.equal(app('general'));
    expect(h.canBack()).to.equal(false);
    expect(h.back()).to.equal(null); // clamped at the start

    expect(h.forward()).to.deep.equal(app('style'));
    expect(h.canForward()).to.equal(true);
  });

  it('pushing while back in history truncates the forward tail', () => {
    const h = createNavHistory();
    h.push(app('general'));
    h.push(app('style'));
    h.push(app('accounts'));
    h.back(); // → style
    h.back(); // → general

    h.push(app('license')); // truncates [style, accounts]
    expect(h.current()).to.deep.equal(app('license'));
    expect(h.canForward()).to.equal(false);
    expect(h.back()).to.deep.equal(app('general'));
    expect(h.forward()).to.deep.equal(app('license'));
  });

  it('coalesces a consecutive duplicate of the current entry', () => {
    const h = createNavHistory();
    h.push(app('general'));
    h.push(app('general')); // no-op
    expect(h.canBack()).to.equal(false);

    h.push(app('style'));
    h.push(app('style')); // no-op
    expect(h.back()).to.deep.equal(app('general'));
    expect(h.forward()).to.deep.equal(app('style'));
    expect(h.canForward()).to.equal(false);
  });

  it('dedup keys on encoded identity, not object identity (view ids)', () => {
    const h = createNavHistory();
    h.push({ kind: 'view', viewId: 'v1' });
    h.push({ kind: 'view', viewId: 'v1' }); // same id, different object → no-op
    expect(h.canBack()).to.equal(false);
    h.push({ kind: 'view', viewId: 'v2' });
    expect(h.canBack()).to.equal(true);
  });
});

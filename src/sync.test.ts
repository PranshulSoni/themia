// @vitest-environment jsdom
//
// Characterization of the cross-window transient bus (extracted seam). Pins:
//  - a message reaches OTHER windows, stamped with the sender id;
//  - a window never handles its own broadcast (hub-level + sender-id filter);
//  - only the registered message type is routed;
//  - unsubscribe stops delivery.
// Imports sync.ts (which builds a real BroadcastChannel + a storage listener at
// module load), so it runs under jsdom with the BroadcastChannel stub.

import { describe, it, expect, vi } from 'vitest';
import { createTransientBus, type TransientChannel } from './sync.ts';

// In-process hub modelling BroadcastChannel: a message posted on one channel is
// delivered to every OTHER channel sharing the hub, never back to the sender.
class Hub {
  channels: FakeChannel[] = [];
  broadcast(from: FakeChannel, msg: unknown): void {
    for (const c of this.channels) if (c !== from) c.receive(msg);
  }
}
class FakeChannel implements TransientChannel {
  private listeners: ((e: MessageEvent) => void)[] = [];
  constructor(private hub: Hub) {
    hub.channels.push(this);
  }
  postMessage(msg: unknown): void {
    this.hub.broadcast(this, msg);
  }
  addEventListener(_type: 'message', fn: (e: MessageEvent) => void): void {
    this.listeners.push(fn);
  }
  receive(msg: unknown): void {
    for (const l of this.listeners) l({ data: msg } as MessageEvent);
  }
}

describe('sync: transient bus routing', () => {
  it('delivers to another window, stamped with the sender id', () => {
    const hub = new Hub();
    const a = createTransientBus(new FakeChannel(hub), 'A');
    const b = createTransientBus(new FakeChannel(hub), 'B');
    const seen = vi.fn();
    b.on('accounts-changed', seen);
    a.post({ type: 'accounts-changed' });
    expect(seen).toHaveBeenCalledTimes(1);
    expect(seen).toHaveBeenCalledWith({ type: 'accounts-changed', sender: 'A' });
  });

  it('a window never handles its own broadcast', () => {
    const hub = new Hub();
    const a = createTransientBus(new FakeChannel(hub), 'A');
    const own = vi.fn();
    a.on('accounts-changed', own);
    a.post({ type: 'accounts-changed' });
    expect(own).not.toHaveBeenCalled();
  });

  it('ignores an inbound message whose sender id equals our own', () => {
    const hub = new Hub();
    const chB = new FakeChannel(hub);
    const b = createTransientBus(chB, 'B');
    const handler = vi.fn();
    b.on('accounts-changed', handler);
    chB.receive({ type: 'accounts-changed', sender: 'B' }); // looks self-sent
    expect(handler).not.toHaveBeenCalled();
  });

  it('routes only the registered type', () => {
    const hub = new Hub();
    const a = createTransientBus(new FakeChannel(hub), 'A');
    const b = createTransientBus(new FakeChannel(hub), 'B');
    const accounts = vi.fn();
    b.on('accounts-changed', accounts);
    a.post({ type: 'picker-placing-end' });
    expect(accounts).not.toHaveBeenCalled();
  });

  it('unsubscribe stops further delivery', () => {
    const hub = new Hub();
    const a = createTransientBus(new FakeChannel(hub), 'A');
    const b = createTransientBus(new FakeChannel(hub), 'B');
    const seen = vi.fn();
    const off = b.on('accounts-changed', seen);
    a.post({ type: 'accounts-changed' });
    off();
    a.post({ type: 'accounts-changed' });
    expect(seen).toHaveBeenCalledTimes(1);
  });

  // 1.24: the storage-write mirror reliably carries a shared localStorage change
  // to other windows over the BroadcastChannel (independent of the flaky native
  // `storage` event). The receiver re-dispatches it to its onStorageKey handlers.
  it('storage-write mirrors a shared write (key + value) to other windows', () => {
    const hub = new Hub();
    const writer = createTransientBus(new FakeChannel(hub), 'writer');
    const reader = createTransientBus(new FakeChannel(hub), 'reader');
    const got = vi.fn();
    reader.on('storage-write', got);

    writer.post({ type: 'storage-write', key: 'widget-layout', value: '{"v":1}' });
    expect(got).toHaveBeenCalledWith({ type: 'storage-write', key: 'widget-layout', value: '{"v":1}', sender: 'writer' });

    // removal travels as value: null
    writer.post({ type: 'storage-write', key: 'widget-layout', value: null });
    expect(got).toHaveBeenLastCalledWith({ type: 'storage-write', key: 'widget-layout', value: null, sender: 'writer' });
  });

  it('the writing window never re-applies its own storage-write', () => {
    const hub = new Hub();
    const writer = createTransientBus(new FakeChannel(hub), 'writer');
    createTransientBus(new FakeChannel(hub), 'reader');
    const ownApply = vi.fn();
    writer.on('storage-write', ownApply);
    writer.post({ type: 'storage-write', key: 'k', value: 'v' });
    expect(ownApply).not.toHaveBeenCalled();
  });
});

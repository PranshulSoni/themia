import { describe, it, expect, vi } from 'vitest';
import { createLatestWriter } from './latestWriter.ts';

/** A send whose resolution the test controls, so ordering is asserted without
 *  any wall-clock waiting. */
function deferredSend<T>() {
  const calls: T[] = [];
  const resolvers: (() => void)[] = [];
  const send = (value: T) => {
    calls.push(value);
    return new Promise<void>((resolve) => resolvers.push(resolve));
  };
  return { calls, resolvers, send };
}

describe('createLatestWriter [S-11]', () => {
  it('sends the first value immediately', () => {
    const { calls, send } = deferredSend<number>();
    createLatestWriter(send)(0.5);
    expect(calls).toEqual([0.5]);
  });

  it('collapses a burst of drag values into one trailing write', async () => {
    const { calls, resolvers, send } = deferredSend<number>();
    const write = createLatestWriter(send);

    write(0.1);            // goes out at once
    write(0.2);            // …these three only queue
    write(0.3);
    write(0.4);
    expect(calls).toEqual([0.1]);

    resolvers[0]();        // the in-flight write lands
    await Promise.resolve();
    await Promise.resolve();

    // The NEWEST value, not 0.2: the intermediate points are worthless.
    expect(calls).toEqual([0.1, 0.4]);
  });

  it('goes quiet once the queue drains, and restarts on the next drag', async () => {
    const { calls, resolvers, send } = deferredSend<number>();
    const write = createLatestWriter(send);

    write(0.1);
    resolvers[0]();
    await Promise.resolve();
    await Promise.resolve();
    expect(calls).toEqual([0.1]);

    write(0.9);
    expect(calls).toEqual([0.1, 0.9]);
  });

  it('a rejected write is reported and does NOT wedge the queue', async () => {
    const onError = vi.fn();
    let n = 0;
    const send = vi.fn(() => (n++ === 0 ? Promise.reject(new Error('boom')) : Promise.resolve()));
    const write = createLatestWriter(send, onError);

    write(0.1);
    await Promise.resolve();
    await Promise.resolve();
    expect(onError).toHaveBeenCalledTimes(1);

    // The regression this guards: an unhandled rejection used to leave
    // `inFlight` true forever, so every later slider move was silently dropped.
    write(0.7);
    expect(send).toHaveBeenCalledTimes(2);
  });
});

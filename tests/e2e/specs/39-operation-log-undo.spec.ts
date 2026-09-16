/**
 * App-wide operation log / Ctrl+Z undo.
 *
 * The op log (src/operationLog.ts) records reversible file operations
 * performed by any widget. Ctrl+Z pops the newest entry and dispatches the
 * reverse Tauri command. A toast reports the result. The log is persisted
 * to localStorage under 'op-log' and synchronised across widget windows
 * via storage events.
 *
 * Strategy:
 *  - Seed an op-log entry directly in localStorage (skip the folder-widget
 *    UI, which has its own widget spec).
 *  - Install a recording invoke stub.
 *  - Trigger Ctrl+Z via browser.keys().
 *  - Assert the correct Tauri command fired with the expected args, that
 *    the log shrank by one, and that the toast appeared.
 */
import { expect } from 'chai';
import { browser } from '@wdio/globals';
import { resetAppState, waitForConfigWindow, switchToMain, closeAllConfigWindows } from '../helpers/app.js';

const OP_LOG_KEY = 'op-log';

type OpEntry = Record<string, any>;

async function seedLog(entries: OpEntry[]): Promise<void> {
  // Populate via the webdriver-only bridge exposed by src/operationLog.ts.
  // Manual StorageEvent dispatch did not reliably update the Solid store
  // in WebView2; the direct setter bypasses that path.
  const result = await browser.execute((data: any) => {
    const hook = (window as any).__opLog;
    if (!hook?.setEntries) {
      return { ok: false, reason: 'bridge-missing', webdriver: (navigator as any)?.webdriver };
    }
    hook.setEntries(data);
    const peeked = hook.peek?.();
    return { ok: true, peekedId: peeked?.id ?? null, total: data.length };
  }, entries);
  if (!result.ok) {
    throw new Error(`seedLog failed: ${JSON.stringify(result)}`);
  }
  if (entries.length && !result.peekedId) {
    throw new Error(`seedLog populated ${entries.length} entries but peek() returned null; store update didn't stick`);
  }
}

async function readLog(): Promise<OpEntry[]> {
  return browser.execute((key: string) => {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      return parsed.entries ?? [];
    } catch { return []; }
  }, OP_LOG_KEY);
}

// Recording stub: every invoke fired from inside operationLog.ts is pushed
// to window.__calls and, if the cmd is in `mapping`, the mapped value is
// returned. Unmapped cmds throw; tests always list every fs_* they expect.
//
// NOTE: Tauri 2 defines `__TAURI_INTERNALS__.invoke` as a non-writable,
// non-configurable property, so stubbing at the window level silently fails.
// `operationLog.ts` exposes a `setInvoke` hook via `window.__opLog` (only
// under webdriver) that replaces the invoke used inside performUndo.
async function stubWithRecorder(mapping: Record<string, any>): Promise<void> {
  await browser.execute((m: Record<string, any>) => {
    const w = window as any;
    w.__calls = [];
    const hook = w.__opLog;
    if (!hook?.setInvoke) throw new Error('__opLog.setInvoke not exposed; ensure webdriver flag and rebuild debug binary');
    hook.setInvoke(async (cmd: string, args: any) => {
      // Deep-clone to strip SolidJS store proxies, otherwise array fields
      // (paths/items) serialise as `undefined` when read back via webdriver.
      const plain = args == null ? args : JSON.parse(JSON.stringify(args));
      w.__calls.push({ cmd, args: plain });
      if (Object.prototype.hasOwnProperty.call(m, cmd)) return m[cmd];
      throw new Error(`unmapped invoke in test: "${cmd}"`);
    });
  }, mapping);
}

async function recordedCalls(): Promise<Array<{ cmd: string; args: any }>> {
  return browser.execute(() => (window as any).__calls ?? []);
}

async function clearCalls(): Promise<void> {
  await browser.execute(() => { (window as any).__calls = []; });
}

async function pressUndo(): Promise<void> {
  // Window-level keydown path: mimics the JS fallback for Ctrl+Z.
  // (The Rust app-key bridge is not reachable from webdriver.)
  await browser.keys(['Control', 'z']);
}

async function toastText(): Promise<string | null> {
  return browser.execute(() => {
    const el = document.querySelector('.app-toast');
    return el ? (el.textContent ?? '').trim() : null;
  });
}

describe('operation log: Ctrl+Z undo', () => {
  beforeEach(async () => {
    await resetAppState();
  });


  it('starts empty and Ctrl+Z is a no-op (toast reports nothing to undo)', async () => {
    await stubWithRecorder({});
    await pressUndo();

    // Toast should appear with "Nothing to undo". Tolerant wait: may not
    // appear instantly if the hotkey dispatch is async.
    await browser.waitUntil(async () => {
      const t = await toastText();
      return !!t && /nothing to undo/i.test(t);
    }, { timeout: 2_000, timeoutMsg: 'expected "Nothing to undo" toast' });

    // No fs_* command should have fired.
    const calls = await recordedCalls();
    const fsCalls = calls.filter(c => c.cmd.startsWith('fs_'));
    expect(fsCalls, `unexpected fs_* calls: ${JSON.stringify(fsCalls)}`).to.have.lengthOf(0);
  });

  it('undoes a rename by invoking fs_rename with the original name', async () => {
    await stubWithRecorder({ fs_rename: null });
    await seedLog([{
      id: 'r1', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/new-name.txt',
      originalPath: 'C:/test/old-name.txt',
    }]);
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_rename');
    }, { timeout: 2_000, timeoutMsg: 'fs_rename never fired' });

    const call = (await recordedCalls()).find(c => c.cmd === 'fs_rename');
    expect(call?.args).to.include({ path: 'C:/test/new-name.txt', newName: 'old-name.txt' });

    // Log entry popped.
    await browser.waitUntil(async () => (await readLog()).length === 0, { timeout: 2_000 });

    // Success toast.
    const t = await toastText();
    expect(t, `toast was: ${t}`).to.match(/undid rename/i);
  });

  it('undoes a delete by invoking fs_restore_from_trash with stored tokens', async () => {
    await stubWithRecorder({ fs_restore_from_trash: null });
    const tokens = [
      { original_path: 'C:/test/a.txt', name: 'a.txt', deleted_at_ms: 111 },
      { original_path: 'C:/test/b.txt', name: 'b.txt', deleted_at_ms: 111 },
    ];
    await seedLog([{
      id: 'd1', ts: Date.now(), kind: 'delete',
      dir: 'C:/test', tokens,
    }]);
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_restore_from_trash');
    }, { timeout: 2_000, timeoutMsg: 'fs_restore_from_trash never fired' });

    const call = (await recordedCalls()).find(c => c.cmd === 'fs_restore_from_trash');
    expect(call?.args?.items).to.deep.equal(tokens);
    expect(await readLog()).to.have.lengthOf(0);
  });

  it('undoes a cut/move by moving files back to their source dir (fs_paste cut=true)', async () => {
    await stubWithRecorder({ fs_paste: null });
    await seedLog([{
      id: 'm1', ts: Date.now(), kind: 'paste-cut',
      srcDir: 'C:/src', destDir: 'C:/dest',
      movedPaths: ['C:/dest/x.txt', 'C:/dest/y.txt'],
      originalPaths: ['C:/src/x.txt', 'C:/src/y.txt'],
    }]);
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_paste');
    }, { timeout: 2_000, timeoutMsg: 'fs_paste never fired' });

    const call = (await recordedCalls()).find(c => c.cmd === 'fs_paste');
    expect(call?.args).to.deep.include({
      paths: ['C:/dest/x.txt', 'C:/dest/y.txt'],
      destDir: 'C:/src',
      cut: true,
    });
    expect(await readLog()).to.have.lengthOf(0);
  });

  it('undoes a paste-copy by permanently deleting the created copies', async () => {
    await stubWithRecorder({ fs_delete_permanent: null });
    await seedLog([{
      id: 'c1', ts: Date.now(), kind: 'paste-copy',
      destDir: 'C:/dest',
      createdPaths: ['C:/dest/x.txt', 'C:/dest/y.txt'],
    }]);
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_delete_permanent');
    }, { timeout: 2_000, timeoutMsg: 'fs_delete_permanent never fired' });

    const call = (await recordedCalls()).find(c => c.cmd === 'fs_delete_permanent');
    expect(call?.args?.paths).to.deep.equal(['C:/dest/x.txt', 'C:/dest/y.txt']);
    expect(await readLog()).to.have.lengthOf(0);
  });

  it('undoes a create by trashing the created item (fs_delete)', async () => {
    await stubWithRecorder({ fs_delete: [] });
    await seedLog([{
      id: 'n1', ts: Date.now(), kind: 'create',
      dir: 'C:/test',
      createdPath: 'C:/test/New Folder',
    }]);
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_delete');
    }, { timeout: 2_000, timeoutMsg: 'fs_delete never fired' });

    const call = (await recordedCalls()).find(c => c.cmd === 'fs_delete');
    expect(call?.args?.paths).to.deep.equal(['C:/test/New Folder']);
    expect(await readLog()).to.have.lengthOf(0);
  });

  it('pops one entry per Ctrl+Z (LIFO, newest first)', async () => {
    await stubWithRecorder({ fs_rename: null });
    await seedLog([
      {
        id: 'r1', ts: 1, kind: 'rename',
        dir: 'C:/test', fromPath: 'C:/test/b.txt', originalPath: 'C:/test/a.txt',
      },
      {
        id: 'r2', ts: 2, kind: 'rename',
        dir: 'C:/test', fromPath: 'C:/test/d.txt', originalPath: 'C:/test/c.txt',
      },
    ]);
    await clearCalls();

    await pressUndo();
    await browser.waitUntil(async () => (await readLog()).length === 1, { timeout: 2_000 });

    // The newest entry (r2 → rename d→c) should have been reverted first.
    const firstCall = (await recordedCalls()).find(c => c.cmd === 'fs_rename');
    expect(firstCall?.args).to.include({ path: 'C:/test/d.txt', newName: 'c.txt' });

    await clearCalls();
    await pressUndo();
    await browser.waitUntil(async () => (await readLog()).length === 0, { timeout: 2_000 });

    const secondCall = (await recordedCalls()).find(c => c.cmd === 'fs_rename');
    expect(secondCall?.args).to.include({ path: 'C:/test/b.txt', newName: 'a.txt' });
  });

  it('a failing undo keeps the entry (retryable) and shows an error toast with the backend message', async () => {
    // Rename fails (e.g. file already exists at target). See note in
    // stubWithRecorder about the setInvoke hook.
    await browser.execute(() => {
      const w = window as any;
      w.__calls = [];
      const hook = w.__opLog;
      if (!hook?.setInvoke) throw new Error('__opLog.setInvoke not exposed');
      hook.setInvoke(async (cmd: string, args: any) => {
        w.__calls.push({ cmd, args });
        if (cmd === 'fs_rename') throw new Error('already exists');
        throw new Error(`unmapped invoke in test: "${cmd}"`);
      });
    });

    await seedLog([{
      id: 'r1', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/new-name.txt',
      originalPath: 'C:/test/old-name.txt',
    }]);
    await clearCalls();

    await pressUndo();

    // Error toast shown, including the backend message so the user can
    // diagnose the failure.
    await browser.waitUntil(async () => {
      const t = await toastText();
      return !!t && /could not undo/i.test(t) && /already exists/i.test(t);
    }, { timeout: 2_000, timeoutMsg: 'expected "Could not undo ... already exists" toast' });

    // Entry remains in the log so the user can retry after fixing the issue.
    expect(await readLog()).to.have.lengthOf(1);
  });

  it('Ctrl+Z is suppressed while an input is focused (does not pop the log)', async () => {
    await stubWithRecorder({});
    await seedLog([{
      id: 'r1', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/b.txt',
      originalPath: 'C:/test/a.txt',
    }]);

    // Open the widget picker; its search input auto-focuses. The picker
    // is now a separate Tauri window (kind="add"); we must switch the
    // webdriver session into it so the Ctrl+Z keystroke is dispatched in
    // the same context as the focused input; that's the window whose
    // `inputFocused()` accessor must report true and suppress the hotkey.
    await switchToMain();
    await browser.keys(['Control', 'w']);
    await waitForConfigWindow('add', 5_000);
    // Ensure the auto-focused picker search actually has focus.
    await browser.waitUntil(async () => {
      return browser.execute(() => {
        const ae = document.activeElement as HTMLElement | null;
        const tag = ae?.tagName?.toLowerCase() ?? '';
        return tag === 'input' || tag === 'textarea';
      });
    }, { timeout: 2_000, timeoutMsg: 'picker search input never received focus' });

    // clearCalls() runs in the current (picker) window; __calls lives in
    // the main window where the recording stub fired. Switch to main to
    // reset there, then come back to picker to send the suppressed Ctrl+Z.
    await switchToMain();
    await clearCalls();
    await waitForConfigWindow('add', 2_000);
    await pressUndo();
    await browser.pause(200);

    // Log must be intact; hotkey was suppressed by inputFocused() in the
    // picker window. Read via main since op-log writes happen on the window
    // that owns the store; the localStorage entries are shared.
    await switchToMain();
    expect(await readLog()).to.have.lengthOf(1);
    const calls = await recordedCalls();
    expect(calls.some(c => c.cmd === 'fs_rename'), 'fs_rename should not have fired').to.equal(false);
    // Clean up the picker window so subsequent specs start fresh.
    try { await closeAllConfigWindows(); } catch {}
  });

  it('leaves a foreign-window pending-delete entry alone and toasts "in progress"', async () => {
    // A pending delete started in another widget window arrives via storage
    // sync with pending:true but without an in-memory promise here. Ctrl+Z
    // must not pop the entry or invoke fs_restore_from_trash; just toast.
    await stubWithRecorder({});
    await seedLog([{
      id: 'dp1', ts: Date.now(), kind: 'delete',
      dir: 'C:/test',
      paths: ['C:/test/slow.txt'],
      tokens: [],
      pending: true,
    }]);
    await clearCalls();

    await pressUndo();
    await browser.pause(200);

    // Entry must remain (it's still in flight somewhere).
    expect(await readLog()).to.have.lengthOf(1);
    const calls = await recordedCalls();
    expect(calls.some(c => c.cmd === 'fs_restore_from_trash'), 'fs_restore_from_trash must not fire for pending').to.equal(false);

    const t = await toastText();
    expect(t, `toast was: ${t}`).to.match(/in progress/i);
  });

  // -------------------------------------------------------------------------
  // Cross-window dedup.
  //
  // Regression guard for the bug where `win.emit("app-key", ...)` in the
  // Rust keyboard hook broadcast to *every* webview (Tauri 2's Emitter trait
  // is defined on Manager, so calling .emit on a specific window still fans
  // out to all). Result: every widget window ran undoLast concurrently, the
  // first invoked fs_* and popped the entry, the rest hit stale state and
  // surfaced "file not found" style errors.
  //
  // The JS defense is a per-entry-id claim in localStorage. Windows that
  // don't own the claim no-op silently. These tests simulate a foreign
  // window's claim and verify the local window bails out.
  // -------------------------------------------------------------------------

  async function plantForeignClaim(entryId: string): Promise<void> {
    await browser.execute((id: string) => {
      const hook = (window as any).__opLog;
      if (!hook?.claimKey || !hook?.claimToken) {
        throw new Error('__opLog claim hooks missing; rebuild debug binary');
      }
      const key = hook.claimKey(id);
      const myToken = hook.claimToken();
      // Use a distinct token to impersonate a different window. Fresh ts so
      // the claim is inside the TTL window.
      localStorage.setItem(key, JSON.stringify({ token: myToken + '-foreign', ts: Date.now() }));
    }, entryId);
  }

  it('skips undo silently when another window already holds the claim', async () => {
    await stubWithRecorder({ fs_rename: null });
    await seedLog([{
      id: 'claim-r1', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/new.txt',
      originalPath: 'C:/test/old.txt',
    }]);
    await plantForeignClaim('claim-r1');
    await clearCalls();

    await pressUndo();
    await browser.pause(250);

    // No fs_rename fired; the foreign claim made us stand down.
    const calls = await recordedCalls();
    expect(calls.some(c => c.cmd === 'fs_rename'), 'fs_rename must not fire when another window holds the claim').to.equal(false);
    // Log entry left intact; the claim owner will pop it.
    expect(await readLog(), 'log must be untouched when another window owns undo').to.have.lengthOf(1);
    // No toast; we don't want every non-winning monitor flashing a toast.
    const t = await toastText();
    expect(t, `toast must be empty when deferring to another window, got: ${t}`).to.satisfy((s: string | null) => s == null || s === '');
  });

  it('allows undo when the claim has expired (stale lock from a crashed window)', async () => {
    await stubWithRecorder({ fs_rename: null });
    await seedLog([{
      id: 'claim-r2', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/new2.txt',
      originalPath: 'C:/test/old2.txt',
    }]);
    // Plant a stale claim (older than the 60s TTL); a crashed window would
    // leave this behind. A healthy window must still be able to undo.
    await browser.execute((id: string) => {
      const hook = (window as any).__opLog;
      const key = hook.claimKey(id);
      localStorage.setItem(key, JSON.stringify({ token: 'zombie', ts: Date.now() - 120_000 }));
    }, 'claim-r2');
    await clearCalls();

    await pressUndo();

    await browser.waitUntil(async () => {
      const calls = await recordedCalls();
      return calls.some(c => c.cmd === 'fs_rename');
    }, { timeout: 2_000, timeoutMsg: 'fs_rename must fire through a stale claim' });

    expect(await readLog()).to.have.lengthOf(0);
  });

  it('deduplicates concurrent undoLast calls in the same window (single fs_* fires)', async () => {
    // Simulates the broadcast bug's effect within one process: if two
    // handlers somehow race to undo the same entry, only one reverse cmd
    // must fire. Protects against dispatch-path regressions (e.g. a future
    // hotkey source that also calls undoLast).
    await stubWithRecorder({ fs_rename: null });
    await seedLog([{
      id: 'race-r1', ts: Date.now(), kind: 'rename',
      dir: 'C:/test',
      fromPath: 'C:/test/race-new.txt',
      originalPath: 'C:/test/race-old.txt',
    }]);
    await clearCalls();

    // Fire two undoLast() calls with no await between them. Concurrency is
    // in JS, so the second starts while the first is mid-claim.
    await browser.execute(() => {
      const hook = (window as any).__opLog;
      (window as any).__raceResults = Promise.all([hook.undo(), hook.undo()]);
    });
    await browser.pause(300);

    const calls = await recordedCalls();
    const renameCalls = calls.filter(c => c.cmd === 'fs_rename');
    expect(renameCalls.length, `expected exactly one fs_rename, got ${renameCalls.length}`).to.equal(1);
    expect(await readLog()).to.have.lengthOf(0);
  });
});

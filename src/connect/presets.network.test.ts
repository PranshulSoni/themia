// ---------------------------------------------------------------------------
// Live preset-health suite — [S-48]
//
// Opt-in: excluded from `npm run check` by the `**/*.network.test.ts` exclude in
// vitest.config.ts, and run on its own with
//
//     npm run test:presets
//
// Server settings published by mail providers drift (a host is retired, a DAV
// path moves). This suite contacts every endpoint in the catalog the same way
// the app's Rust probe does — an IMAP greeting, an unauthenticated PROPFIND —
// and fails when one stops answering, so a stale preset is caught here instead
// of by a user staring at "connection failed".
//
// No credentials are sent and nothing is written; these are the same anonymous
// handshakes any mail client performs before logging in.
//
// A failure here is data, not a flake to retry away: check the provider's
// current documentation and update src/connect/presets.ts.
// ---------------------------------------------------------------------------
import { describe, it, expect } from 'vitest';
import tls from 'node:tls';
import { CONNECT_PRESETS, type ConnectPreset } from './presets.ts';

const TIMEOUT_MS = 20_000;

/** Dial IMAPS and read the greeting. A server that speaks IMAP opens with
 *  `* OK` (or `* PREAUTH`); anything else means the host is no longer an IMAP
 *  endpoint. Mirrors `imap_connect` in widgets/office/email/src/lib.rs. */
function imapGreeting(host: string, port: number): Promise<string> {
  return new Promise((resolve, reject) => {
    const socket = tls.connect({ host, port, servername: host }, () => {
      socket.setEncoding('utf8');
    });
    socket.setTimeout(TIMEOUT_MS - 2_000);
    let buf = '';
    const done = (fn: () => void) => { socket.destroy(); fn(); };
    socket.on('data', (chunk: string) => {
      buf += chunk;
      if (buf.includes('\r\n') || buf.includes('\n')) done(() => resolve(buf.trim()));
    });
    socket.on('timeout', () => done(() => reject(new Error(`timed out after ${TIMEOUT_MS - 2_000}ms`))));
    socket.on('error', (e: Error) => done(() => reject(e)));
    socket.on('end', () => done(() => reject(new Error('closed before greeting'))));
  });
}

const PROPFIND_BODY =
  '<?xml version="1.0" encoding="UTF-8"?>' +
  '<D:propfind xmlns:D="DAV:"><D:prop><D:current-user-principal/></D:prop></D:propfind>';

/** Reachability statuses for an *unauthenticated* PROPFIND. 401/403 is the
 *  healthy answer: the endpoint exists and demands credentials. 404/405 means
 *  the path moved — exactly the drift this suite exists to catch. Mirrors
 *  `classify_dav_status` in widgets/office/email/src/lib.rs. */
const DAV_OK = new Set([200, 207, 401, 403, 301, 302, 307, 308]);

async function davStatus(url: string): Promise<{ status: number; dav: string | null }> {
  const res = await fetch(url, {
    method: 'PROPFIND',
    headers: { Depth: '0', 'Content-Type': 'application/xml; charset=utf-8' },
    body: PROPFIND_BODY,
    redirect: 'manual',
    signal: AbortSignal.timeout(TIMEOUT_MS - 2_000),
  });
  return { status: res.status, dav: res.headers.get('dav') };
}

const imapPresets = CONNECT_PRESETS.filter(p => p.probe.kind === 'imap');
const davPresets = CONNECT_PRESETS.filter(p => p.probe.kind === 'dav');
const templated = CONNECT_PRESETS.filter(p => p.probe.kind === 'none');

function target(p: ConnectPreset): string {
  return p.probe.kind === 'imap' ? `${p.probe.host}:${p.probe.port}`
    : p.probe.kind === 'dav' ? p.probe.url
      : '(self-hosted)';
}

describe('S-48 preset endpoints are still reachable', () => {
  describe('IMAP', () => {
    for (const p of imapPresets) {
      it.concurrent(`${p.label} — ${target(p)}`, async () => {
        const greeting = await imapGreeting(p.probe.kind === 'imap' ? p.probe.host : '', p.probe.kind === 'imap' ? p.probe.port : 0);
        expect(greeting, `${p.id} greeting: ${greeting}`).toMatch(/^\* (OK|PREAUTH)/);
      }, TIMEOUT_MS);
    }
  });

  describe('DAV', () => {
    for (const p of davPresets) {
      it.concurrent(`${p.label} — ${target(p)}`, async () => {
        const url = p.probe.kind === 'dav' ? p.probe.url : '';
        const { status, dav } = await davStatus(url);
        // A `DAV:` response header is proof on its own; otherwise the status
        // has to be one a live collection actually returns.
        const ok = dav !== null || DAV_OK.has(status);
        expect(ok, `${p.id} answered HTTP ${status}${dav ? ` (DAV: ${dav})` : ''}`).toBe(true);
      }, TIMEOUT_MS);
    }
  });

  it('reports which presets were skipped, so coverage is never overstated', () => {
    // Self-hosted presets have no generic endpoint to contact; naming them keeps
    // "all green" from reading as "all verified".
    console.log(`skipped ${templated.length} self-hosted preset(s): ${templated.map(p => p.id).join(', ')}`);
    expect(imapPresets.length + davPresets.length + templated.length).toBe(CONNECT_PRESETS.length);
  });
});

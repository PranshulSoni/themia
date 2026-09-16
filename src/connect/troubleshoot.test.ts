import { describe, it, expect } from 'vitest';
import { troubleshoot } from './troubleshoot.ts';

// The exact strings the Rust backend returns for the Claude OAuth flow
// (widgets/office/email/src/lib.rs :: complete_claude_auth / map_usage_error).
// Keep these in sync with the backend — the whole point of troubleshoot() is to
// turn these opaque strings into provider-correct guidance.
const CLAUDE_401 = 'Claude token expired or invalid — reconnect the Claude account';
const CLAUDE_403 =
  'Claude token lacks a required permission: OAuth token does not meet scope requirement user:profile';
const CLAUDE_SIGNIN_FAILED = 'Claude sign-in failed: invalid_grant';
const CLAUDE_EXPIRED = 'This sign-in expired. Start again.';

// Regression for issue #72: a rejected Claude Code token showed GitHub OAuth
// scope advice ("repo, read:user, notifications") because the scope branch
// matched any error containing the word "token", regardless of provider. No
// Claude error may ever surface GitHub scope advice.
describe('troubleshoot — Claude Code (#72)', () => {
  it('never shows GitHub OAuth scopes for any Claude error', () => {
    for (const err of [CLAUDE_401, CLAUDE_403, CLAUDE_SIGNIN_FAILED, CLAUDE_EXPIRED]) {
      const blob = (() => {
        const t = troubleshoot('claude', err);
        return [t.title, ...t.tips].join(' ').toLowerCase();
      })();
      expect(blob).not.toContain('read:user');
      expect(blob).not.toContain('notifications');
      expect(blob).not.toContain('token is missing permissions');
    }
  });

  it('maps a 403 scope failure to usage-access guidance mentioning user:profile', () => {
    const t = troubleshoot('claude', CLAUDE_403);
    expect(t.title).toBe('Usage access was denied');
    expect(t.tips.join(' ')).toContain('user:profile');
  });

  it('routes token-exchange and re-auth failures to sign-in guidance', () => {
    for (const err of [CLAUDE_401, CLAUDE_SIGNIN_FAILED, CLAUDE_EXPIRED]) {
      const t = troubleshoot('claude', err);
      expect(t.title).toBe('Sign-in didn’t complete');
      expect(t.tips.join(' ').toLowerCase()).toContain('code#state');
    }
  });

  it('lets a Claude network error fall through to the reachability branch', () => {
    const t = troubleshoot('claude', 'Cannot reach Anthropic: dns error');
    expect(t.title).toBe('Couldn’t reach the server');
  });
});

// Issue #71: CalDAV/CardDAV discovery. The backend now walks .well-known →
// principal → home-set. Its two new outcome strings must map to the right
// guidance, and a genuine 401 must still route to sign-in advice (never masked).
// Keep these literals in sync with dav_discover_collection in the Rust backend.
const CAL_NONE = "Connected, but no calendar was found there. Enter your calendar's full CalDAV URL.";
const CARD_NONE = "Connected, but no address book was found there. Enter your address book's full CardDAV URL.";
const DAV_401 = 'Server returned HTTP 401, check URL and credentials';

describe('troubleshoot — CalDAV/CardDAV discovery (#71)', () => {
  it('maps "no calendar found" to discovery guidance, not an auth error', () => {
    const t = troubleshoot('caldav', CAL_NONE);
    expect(t.title).toBe('Nothing found to sync');
    const blob = t.tips.join(' ').toLowerCase();
    expect(blob).toContain('base address');
    expect(blob).toContain('calendar');
  });

  it('maps "no address book found" to discovery guidance', () => {
    const t = troubleshoot('carddav', CARD_NONE);
    expect(t.title).toBe('Nothing found to sync');
    expect(t.tips.join(' ').toLowerCase()).toContain('address book');
  });

  it('still routes a real DAV 401 to sign-in guidance', () => {
    const t = troubleshoot('caldav', DAV_401);
    expect(t.title).toBe('Sign-in was rejected');
    const blob = t.tips.join(' ').toLowerCase();
    expect(blob).toContain('app password');
  });
});

// The GitHub scope path must keep working — the fix gates it to GitHub, it does
// not remove it.
describe('troubleshoot — GitHub scopes still work', () => {
  it('flags a GitHub token missing scopes', () => {
    const t = troubleshoot('github', 'Token is missing the required scope');
    expect(t.title).toBe('Token is missing permissions');
    expect(t.tips.join(' ')).toContain('read:user');
  });

  it('gives GitHub scope-aware auth advice on a 403', () => {
    const t = troubleshoot('github', 'HTTP 403 Forbidden');
    expect(t.title).toBe('Sign-in was rejected');
    expect(t.tips.join(' ')).toContain('read:user');
  });
});

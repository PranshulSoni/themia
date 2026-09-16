// ---------------------------------------------------------------------------
// Connection troubleshooting
//
// The backend add commands (widgets/office/email/src/lib.rs) connect to verify
// before saving and return human-readable error strings on failure. This maps
// those strings to concrete, provider-aware next steps the user can act on.
// ---------------------------------------------------------------------------

export interface Troubleshooting {
  /** Short headline for the failure. */
  title: string;
  /** Actionable tips, most-likely-cause first. */
  tips: string[];
}

const has = (hay: string, ...needles: string[]): boolean => {
  const l = hay.toLowerCase();
  return needles.some(n => l.includes(n.toLowerCase()));
};

export function troubleshoot(providerKey: string, error: string): Troubleshooting {
  const tips: string[] = [];

  // ── Claude Code (OAuth) ──
  // Its backend messages are provider-specific: a 403 scope failure, a token
  // exchange / re-auth failure, or a network error. Handle them here so a Claude
  // error never falls into the GitHub/IMAP auth advice below (#72).
  if (providerKey === 'claude') {
    if (has(error, 'permission', 'scope requirement', 'user:profile')) {
      tips.push('Anthropic didn’t grant the permission the usage endpoint needs (user:profile).');
      tips.push('Reconnect and approve access on the consent screen — don’t skip any requested permission.');
      return { title: 'Usage access was denied', tips };
    }
    if (has(error, 'sign-in', 'reconnect', 'expired', 'state mismatch', 'no access token', 'unexpected response')) {
      tips.push('Reopen the sign-in page, approve access, then copy the whole code the page shows.');
      tips.push('Paste the entire value (it looks like code#state) with no extra spaces.');
      tips.push('If it keeps failing, close this window and start the connection again.');
      return { title: 'Sign-in didn’t complete', tips };
    }
    // Network / other errors fall through to the generic branches below.
  }

  // ── Authentication failures ──
  if (has(error, 'login failed', 'http 401', 'http 403', '401', '403', 'unauthorized', 'authentication')) {
    if (providerKey === 'github' || providerKey === 'github-pat') {
      tips.push('Check the token is correct and not expired.');
      tips.push('The token needs the repo, read:user, and notifications scopes.');
      tips.push('For GitHub Enterprise, set the API base URL to your server.');
    } else if (providerKey === 'imap' || providerKey === 'caldav' || providerKey === 'carddav') {
      tips.push('Double-check the username and password.');
      tips.push('If your provider uses 2-factor auth, create an app password and use that instead of your normal password.');
      tips.push('Gmail/Outlook require an app password; your account password will be rejected.');
    } else {
      tips.push('Re-check your credentials and try again.');
    }
    return { title: 'Sign-in was rejected', tips };
  }

  // ── DAV discovery: connected, but no collection found ──
  // The backend walks .well-known → principal → home-set, so a base/server URL
  // usually resolves. When it connects but exposes no calendar/address book,
  // the user likely pointed at the wrong space or must paste the exact URL.
  if (has(error, 'no calendar was found', 'no address book was found')) {
    if (providerKey === 'caldav') {
      tips.push('Your server address connected, but no calendar was found for this account.');
      tips.push('Try your server’s base address (e.g. https://cloud.example.com) — we’ll discover your calendars.');
      tips.push('Or paste one calendar’s full URL, copied from your provider’s calendar settings.');
    } else {
      tips.push('Your server address connected, but no address book was found for this account.');
      tips.push('Try your server’s base address (e.g. https://cloud.example.com) — we’ll discover your address books.');
      tips.push('Or paste one address book’s full URL, copied from your provider’s contacts settings.');
    }
    return { title: 'Nothing found to sync', tips };
  }

  // ── Reachability / network / TLS ──
  if (has(error, 'cannot connect', 'cannot reach', 'connection failed', 'timed out', 'timeout', 'dns', 'no such host', 'refused')) {
    if (providerKey === 'imap') {
      tips.push('Verify the IMAP server address (e.g. imap.gmail.com) and port (usually 993).');
    } else if (providerKey === 'caldav' || providerKey === 'carddav') {
      tips.push('Verify the full server URL, including https:// and the correct path.');
    }
    tips.push('Confirm you have an internet connection.');
    tips.push('A firewall, VPN, or antivirus may be blocking the connection. Try disabling it briefly.');
    return { title: 'Couldn’t reach the server', tips };
  }

  // ── TLS / certificate ──
  if (has(error, 'tls', 'certificate', 'ssl', 'handshake')) {
    tips.push('The server’s security certificate could not be validated.');
    tips.push('Check the server address is exactly right (no typos, correct port).');
    tips.push('If this is a self-hosted server, make sure its certificate is valid.');
    return { title: 'Secure connection failed', tips };
  }

  // ── GitHub scope-specific ──
  // Scopes are a GitHub concept; gate to github so other providers whose error
  // merely contains "token" (e.g. Claude) never get GitHub scope advice (#72).
  if ((providerKey === 'github' || providerKey === 'github-pat') && has(error, 'scope', 'token')) {
    tips.push('The token is missing required scopes.');
    tips.push('Regenerate it with repo, read:user, and notifications selected.');
    return { title: 'Token is missing permissions', tips };
  }

  // ── HTTP 5xx ──
  if (has(error, 'http 5', '500', '502', '503')) {
    tips.push('The server reported an internal error.');
    tips.push('Wait a moment and try again. This is usually temporary.');
    return { title: 'The server had a problem', tips };
  }

  // ── Fallback ──
  tips.push('Re-check each field for typos and try again.');
  tips.push('If the problem persists, the full error below can help pinpoint the cause.');
  return { title: 'Couldn’t complete the connection', tips };
}

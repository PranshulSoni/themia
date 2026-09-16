export function providerLabel(provider: string): string {
  switch (provider) {
    case 'microsoft': return 'Microsoft';
    case 'google': return 'Google';
    case 'imap': return 'IMAP';
    case 'caldav': return 'CalDAV';
    case 'carddav': return 'CardDAV';
    case 'github': return 'GitHub';
    case 'claude': return 'Claude';
    case 'rss': return 'RSS';
    default: return provider;
  }
}

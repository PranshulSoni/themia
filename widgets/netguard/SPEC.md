# SSRF guard: SPEC

Scope: the outbound-request policy in `widgets/netguard`. Applies to every
request built from a user-supplied URL or host (RSS, CalDAV/CardDAV, self-hosted
GitHub, IMAP, drag-drop downloads, `http_get`). Proxy *resolution* is a separate
concern and lives in `../netproxy` ([PX-NN]); the two meet only at [NG-07].

Tags are `[NG-NN]`; tests mirror them as `ngNN_*`.

## Threat

Widgets let users point at arbitrary URLs. Without a guard, a hostile feed URL -
or a tricked user - can make the app fetch `http://127.0.0.1/...`,
`http://169.254.169.254/...` (cloud metadata), or any other host reachable from
the machine but not from the internet. Classic SSRF, with the app as the
confused deputy.

## Behaviors

- **[NG-01] Default-deny private and reserved addresses.** Loopback, RFC1918
  (10/8, 172.16/12, 192.168/16), link-local (169.254/16, which covers the
  169.254.169.254 metadata endpoint), CGNAT (100.64/10), documentation ranges,
  0.0.0.0/8, 240/4 reserved and broadcast are blocked. For IPv6: loopback,
  unspecified, unique-local (fc00::/7), link-local (fe80::/10), documentation
  (2001:db8::/32), and IPv4-mapped forms of all of the above - `::ffff:127.0.0.1`
  must not be a way around the v4 rules.

- **[NG-02] http and https only.** Any other scheme is rejected outright, so a
  `file://` or `gopher://` URL cannot be smuggled through a widget.

- **[NG-03] Every resolved address is checked, not just the first.** A hostname
  with several A records is blocked if *any* of them is private. A host that
  resolves to no addresses is rejected.

- **[NG-04] One global bypass, default secure.** Many users legitimately run
  self-hosted CalDAV / RSS / GitHub / IMAP on their LAN, so a single opt-in
  ("Allow private network access") disables the policy. The runtime default is
  the secure one, so there is never a window at startup where the policy is off
  before the frontend has pushed the setting.

- **[NG-05] The guard is one crate with one flag, linked once.** The `BYPASS`
  static must be shared by every caller (`src-tauri`'s command wrappers AND the
  `widget-email` fetch paths), which a single crate in the final binary is what
  guarantees.

- **[NG-06] Guarding is blocking and happens before the connection.** Checks do
  DNS, so callers run them on a blocking context or via `spawn_blocking`, and
  always *before* the socket is opened - never as a post-hoc audit.

- **[NG-07] A host we cannot resolve is blocked, UNLESS a proxy will resolve it
  for us.** Direct-connection targets that do not resolve are rejected: the
  connection would fail anyway, and rejecting gives the better message.

  But when [PX-01] says the request goes through a proxy, Themia never resolves
  the host at all - the proxy does. On a split-DNS corporate network the local
  resolver legitimately cannot answer for external names, so treating "cannot
  resolve" as "block" made every user-supplied URL fail behind a proxy with a
  misleading `Cannot resolve host` error. Behind a proxy, an unresolvable host
  is therefore ALLOWED and the proxy decides.

  This is a real, bounded relaxation, and it is bounded by how proxies are
  actually configured: PAC scripts and bypass lists return DIRECT for internal
  hosts essentially always, and a DIRECT verdict puts the full guard back on.
  So the guard stays armed exactly where SSRF matters (internal targets) and
  relaxes only where the proxy's own policy is already the arbiter. Hosts that
  DO resolve are still fully checked, proxy or not, and IP literals are always
  checked since they need no DNS.

  This applies to `check_host_port` (IMAP) on the same terms as `check_url`.
  It did not originally: IMAP always dialled its own socket, so Themia really
  did resolve the host and a failure was a genuine one. Once IMAP gained a
  CONNECT tunnel ([PX-08]) that reasoning expired, and the rule became uniform.
  The lesson is worth keeping: this exemption is keyed on *who resolves the
  host*, not on which protocol is asking.

## Test coverage

`cargo test -p widget-netguard`. [NG-07]'s proxied branch is covered by seeding
a proxy decision for the test host; the unproxied branch by an `.invalid` host,
which RFC 2606 guarantees will never resolve.

## Manual / live verification (not reachable by CI)

1. Behind a real split-DNS corporate proxy, a public RSS/CalDAV URL must fetch
   rather than fail with `Cannot resolve host` ([NG-07]).
2. With "Allow private network access" off and no proxy, a LAN CalDAV URL must
   still be blocked ([NG-01], [NG-04]).

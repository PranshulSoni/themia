# Proxy resolution: SPEC

Scope: how Themia decides whether an outbound request goes direct or through a
proxy. Covers `widgets/netproxy` and the client factories that consume it. The
SSRF policy is a separate concern and lives in `../netguard` ([NG-NN]); the two
interact only where noted in [PX-07].

Tags are `[PX-NN]`; tests mirror them as `pxNN_*`.

## Why this crate exists

The Windows proxy page (Settings > Network & Internet > Proxy) is **not a
network setting**. It writes a handful of registry values and intercepts
nothing. An HTTP proxy is a rewrite of the request itself - direct is `GET
/thing` to the origin, proxied is `GET http://host/thing` to the proxy, and
HTTPS needs a `CONNECT` tunnel first - so the OS cannot apply it to a socket
underneath an app without terminating that app's TLS. Every app must read the
setting and opt in.

Apps on the OS HTTP stack (WinINET/WinHTTP) get this free. **Themia's WebView2
windows are in that camp** - weather, GitHub, favicons and the Bing image have
honored every proxy shape, including PAC, since day one. **Themia's Rust side is
not**: it brings reqwest / ureq / a raw IMAP socket, so it must ask.

Before this crate, it asked badly. reqwest ships `system-proxy` in its default
features, whose Windows branch reads `ProxyEnable`/`ProxyServer`/`ProxyOverride`
and nothing else. That covers the **Manual proxy setup** box and silently
ignores the **Automatic proxy setup** box (WPAD + setup script), which is what
most corporate IT actually configures. Issue #65 is that gap.

## Behaviors

- **[PX-01] Windows is the only source of truth.** Themia has no proxy setting
  of its own and must never grow one that shadows the OS. Every decision comes
  from `WinHttpGetIEProxyConfigForCurrentUser` for the current user - the same
  configuration Edge reads. There is one resolver, in one crate, linked once
  into the final binary, for the same reason `netguard` is one crate: every
  outbound caller must get the same answer.

- **[PX-02] The automatic box is resolved by Windows, not reimplemented.**
  WPAD auto-detect and a PAC setup script both go through
  `WinHttpGetProxyForUrl`, which fetches the script, executes its JavaScript per
  URL, and caches it on our session handle. We do not parse or run PAC
  ourselves. A script's `DIRECT` verdict is honored as DIRECT; a failure to get
  any answer (no WPAD server, unreachable or broken script) is **not** a verdict
  and falls through to [PX-03].

- **[PX-03] The manual box is the fallback.** `ProxyServer` is honored in both
  legal shapes (`host:port` for everything, or `http=a:1;https=b:2`
  per-protocol). A scheme absent from a per-protocol list means DIRECT for that
  scheme, never another protocol's proxy. `ProxyOverride` is honored, including
  `*` wildcards and the `<local>` token (any dotless hostname). Bypass matching
  is case-insensitive and anchored at both ends, so a `corp.com` exemption
  cannot be ridden by `evilcorp.com`.

- **[PX-04] Decisions are cached per scheme+host, and invalidatable.** A PAC
  call is blocking and a widget may poll every tick, so the answer is cached.
  Path is deliberately not part of the key (PAC scripts branch on host in
  practice; keying on path would mean a WinHTTP call per URL). `invalidate()`
  drops the cache when the OS reports the configuration changed, so a user who
  fixes their proxy is believed without restarting Themia.

- **[PX-05] Fail-open to DIRECT, never to an error.** Any failure to resolve -
  no user profile, `WinHttpOpen` failed, dead PAC URL, garbage in the registry -
  means direct connection. Failing a request because a *setting* could not be
  read would be a regression against the behavior every user had before this
  crate existed. Non-http(s) schemes never reach the OS at all.

- **[PX-06] RAII on every handle and OS string.** The WinHTTP session handle,
  the three strings from `WinHttpGetIEProxyConfigForCurrentUser`, and the two
  from `WinHttpGetProxyForUrl` are all owned by types that free on every path.
  The session is process-lifetime **by design** (WinHTTP caches the PAC script
  on it; a fresh handle per lookup would re-download the script every time) -
  that is an intended lifetime, not a leak.

- **[PX-08] Raw TCP services are tunnelled with HTTP `CONNECT`.** reqwest and
  ureq negotiate their own tunnels; IMAP brings a bare socket, so the handshake
  is ours to perform. `connect_tcp` dials the proxy, sends `CONNECT host:port`,
  and hands back the socket only on a `200` - after which the tunnel is
  transparent and the caller layers TLS exactly as it would on a direct
  connection. The CONNECT response is read one byte at a time to the header
  terminator: chunked reads could swallow bytes the origin already sent through
  the established tunnel and corrupt the TLS handshake that follows.

  Windows has no "imap proxy" notion, so the service is presented to resolution
  as `https://host:port`. That is the accurate question, not a fudge: a proxied
  IMAP connection *is* a CONNECT tunnel, which is precisely what `https` denotes
  to a proxy - so PAC scripts get a real host to branch on, and a per-protocol
  manual config contributes its `https=` proxy, the one that can CONNECT.

  The timeout bounds the dial and the handshake only, and is cleared before the
  socket is handed over. IMAP sockets previously had no read timeout; imposing
  one here would silently turn a slow fetch of a large mailbox into a failure,
  and bounding the session is a separate decision from bounding the handshake.

  A `407` is reported as "requires authentication" in those words rather than a
  generic connection failure - it is the limit users will actually hit, and a
  vague error would send them debugging their mail server instead of their
  proxy. SOCKS proxies are rejected explicitly for this path rather than
  silently connecting direct.

- **[PX-09] The updater is not exempt.** `tauri-plugin-updater` builds its HTTP
  client *inside* `Updater::check` and `Update::download`, from a reqwest it
  declares with `default-features = false` and only `json` + `stream` - so
  `system-proxy` is off and the updater honored **no** proxy at all, not even
  the manual box. Nothing in the plugin's `Builder` can inject a client, so the
  app owns the two IPC commands (`updater_check`, `updater_install`) and feeds
  the resolver's answer into `UpdaterBuilder::proxy`/`no_proxy` and the matching
  fields on the resulting `Update`. `no_proxy()` is set explicitly on a DIRECT
  verdict rather than left unset: reqwest would otherwise be free to sniff a
  proxy of its own, which is a second resolver disagreeing with [PX-01]. The
  plugin's own IPC commands are ungranted in `capabilities/default.json`, so
  there is one path from the frontend, not two.

  The manifest and the package can live on different hosts - GitHub serves
  release downloads from a separate domain - so the question is asked twice,
  once per URL actually fetched, instead of applying the manifest's answer to
  the download. A redirect *within* one of those fetches still rides the proxy
  resolved for the URL that started it: the plugin's `Proxy::all` has no
  per-host opinion, and reaching further would mean handing reqwest a closure
  through a seam the plugin does not expose.

- **[PX-07] The proxy address is never SSRF-checked.** Corporate proxies live on
  RFC1918 addresses essentially always. `netguard` guards the *destination* of a
  user-supplied URL; the proxy is infrastructure, not a destination, and running
  it through the guard would block the overwhelmingly common case (`10.x` proxy)
  for every user with the default-secure policy on.

- **[PX-10] One User-Agent for every outbound request.** `user_agent()` returns
  a single honest, contactable identity (`Themia/<version> (+<releases URL>)`),
  stamped once at startup by `set_app_version`. Every reqwest caller uses it, so
  one URL cannot behave as two different clients depending on which code path
  touched it - the #81 failure mode, where adding an RSS feed sent `themia-app`
  and refreshing that same feed sent a truncated `Mozilla/5.0 … AppleWebKit/537.36`
  with no `Chrome/`/`Safari/` token. A forged browser string is what bot filters
  block; an honest one is what feed hosts allow-list and rate-limit against.

## Test coverage

`cargo test -p widget-netproxy`.

[PX-02] is covered **for real**, not mocked: `winhttp::tests` serves an actual
PAC script from a loopback socket and passes its URL to WinHTTP as an explicit
`lpszAutoConfigUrl`. Windows fetches it, runs the JavaScript, and returns a
verdict - proving the previously-missing path end-to-end on a developer machine
with no proxy and no corporate network, and without writing a single registry
key or disturbing the developer's real proxy settings.

## Manual / live verification (not reachable by CI)

The one thing a dev machine cannot establish is behavior against a *real*
configured proxy, because proving it would mean repointing the developer's own
system proxy. Specifically still unverified:

1. **Manual box, live.** Set Settings > Proxy > Manual, confirm widgets, mail
   sync, license check and update check all traverse it. The updater's two
   phases are separate requests ([PX-09]) and separately unverified: the
   manifest fetch is exercised by any "Check for updates" click, but the
   package download only by actually installing a proxied update.
2. **Automatic box via registry.** [PX-02] is proven with an explicit PAC URL;
   the path where the URL comes from `AutoConfigURL` rather than an argument is
   the same call with the same flag, but has not been observed live.
3. **WPAD auto-detect** (`WINHTTP_AUTOPROXY_AUTO_DETECT`) needs a network
   serving `wpad.dat` via DHCP/DNS. Untested.
4. **Proxy requiring authentication.** Known **not** supported on the Rust
   paths - see below.

## Known limits (deliberate, documented, do not re-litigate)

- **Proxy authentication is not supported on the Rust paths.** Windows stores no
  credentials for a proxy, so a 407 has no answer available - for HTTP and for
  the [PX-08] IMAP tunnel alike. WebView2 windows handle their own auth prompt
  and are unaffected. Closing this needs a credential prompt + DPAPI storage;
  deferred until a user reports a 407.
- **A proxy may refuse `CONNECT` to port 993.** Many proxies allow CONNECT only
  to 443. That is the proxy's policy, not something Themia can route around; the
  refusal is surfaced with the proxy's own status line ([PX-08]) so it is
  diagnosable rather than mysterious.
- **NTLM / Kerberos proxy auth is out of reach for reqwest**, which has no SSPI
  integration. It would require routing every request through WinHTTP as the
  transport, which is a rewrite rather than a feature.
- **No proxy failover.** A PAC returning several proxies yields the first that
  parses; reqwest has no "try the next proxy" hook, and a silent second path
  would violate one-path.

//! Proxy resolution for every outbound request Themia makes. See SPEC.md.
//!
//! Themia has no proxy of its own and must never invent one ([PX-01]): the only
//! source of truth is the Windows proxy configuration for the current user, the
//! same thing Edge and every WinHTTP app reads. This crate's whole job is to
//! answer one question - "which proxy, if any, applies to this URL?" - and to
//! answer it the way Windows would.
//!
//! It lives in its OWN crate for the same reason `widget-netguard` does: every
//! outbound caller (`src-tauri`'s command wrappers, the email fetch paths, the
//! `ureq` one-shots) must share ONE resolver and ONE cache, and a single crate
//! linked once into the final binary is what guarantees that.

pub mod parse;
#[cfg(target_os = "windows")]
mod winhttp;

pub use parse::ProxyTarget;

use std::collections::HashMap;
use std::sync::{OnceLock, RwLock};
use url::Url;

/// Cache key: the parts of a URL a proxy decision can legitimately turn on.
///
/// A PAC script is handed the full URL and *could* branch on the path, but in
/// practice they branch on host (and occasionally scheme); caching per path
/// would mean a blocking WinHTTP call per distinct URL, which for a widget
/// polling a feed is every tick. Scheme+host is the trade-off, and it is the
/// same one WinINET's own cache makes.
type CacheKey = (String, String);

fn cache() -> &'static RwLock<HashMap<CacheKey, ProxyTarget>> {
    static CACHE: OnceLock<RwLock<HashMap<CacheKey, ProxyTarget>>> = OnceLock::new();
    CACHE.get_or_init(|| RwLock::new(HashMap::new()))
}

fn key_for(url: &Url) -> CacheKey {
    (
        url.scheme().to_ascii_lowercase(),
        url.host_str().unwrap_or_default().to_ascii_lowercase(),
    )
}

/// Which proxy applies to `url`, per the Windows proxy settings ([PX-01]).
///
/// Cached per scheme+host ([PX-04]). The first lookup for a host is blocking:
/// resolving the "Automatic proxy setup" box can mean fetching a PAC script and
/// running its JavaScript. WinHTTP caches the script on our session, so that
/// cost lands once per process, not once per request - but callers on an async
/// runtime should still be aware this is not a pure function on a cache miss.
///
/// Never returns an error. A proxy we cannot resolve is DIRECT ([PX-05]):
/// failing a request outright because we could not read a setting would be
/// worse than trying the connection the user would have got before this crate
/// existed.
pub fn resolve(url: &Url) -> ProxyTarget {
    // Only http(s) targets have a proxy notion here.
    if !matches!(url.scheme(), "http" | "https") {
        return ProxyTarget::Direct;
    }
    let key = key_for(url);

    if let Ok(map) = cache().read() {
        if let Some(hit) = map.get(&key) {
            return hit.clone();
        }
    }

    let resolved = resolve_uncached(url);

    // A poisoned cache lock is not a reason to fail a request: the value is
    // recomputable and the map holds nothing a panic could corrupt
    // meaningfully, so recover the guard rather than propagate.
    match cache().write() {
        Ok(mut map) => {
            map.insert(key, resolved.clone());
        }
        Err(poisoned) => {
            poisoned.into_inner().insert(key, resolved.clone());
        }
    }
    resolved
}

#[cfg(target_os = "windows")]
fn resolve_uncached(url: &Url) -> ProxyTarget {
    winhttp::resolve_uncached(url)
}

/// Themia ships on Windows only; this keeps the crate buildable elsewhere
/// (and `cargo test` honest on a non-Windows CI image) without pretending.
#[cfg(not(target_os = "windows"))]
fn resolve_uncached(_url: &Url) -> ProxyTarget {
    ProxyTarget::Direct
}

/// Drop every cached decision ([PX-04]).
///
/// Call when Windows reports the proxy configuration changed; otherwise a user
/// who fixes their proxy would have to restart Themia to be believed.
pub fn invalidate() {
    match cache().write() {
        Ok(mut map) => map.clear(),
        Err(poisoned) => poisoned.into_inner().clear(),
    }
    log::info!("netproxy: proxy cache invalidated");
}

/// Seed a decision directly, bypassing the OS. Test-only.
///
/// Proving that a client actually honors [PX-01] needs `resolve` to yield a
/// proxy, and the only other way to arrange that is to repoint the developer's
/// real system proxy. This seam is `cfg(test)`, so it cannot exist in a shipped
/// binary and is not a second resolution path.
#[cfg(test)]
fn seed_for_test(url: &Url, target: ProxyTarget) {
    match cache().write() {
        Ok(mut map) => {
            map.insert(key_for(url), target);
        }
        Err(poisoned) => {
            poisoned.into_inner().insert(key_for(url), target);
        }
    }
}

/// A `reqwest::Proxy` that defers every decision to [PX-01].
///
/// Attaching this also switches reqwest's own `auto_sys_proxy` OFF, which is
/// the point: its Windows branch reads `ProxyServer` and ignores the automatic
/// box, so leaving both active would be two resolvers disagreeing. One path.
///
/// The closure is called by reqwest's connector, on the runtime, and blocks on
/// a cache miss ([PX-04]) - a PAC lookup can mean an HTTP fetch. WinHTTP caches
/// the script on our session, so that lands once per host per process.
#[cfg(feature = "reqwest-client")]
pub fn reqwest_proxy() -> reqwest::Proxy {
    reqwest::Proxy::custom(|url| match resolve(url) {
        ProxyTarget::Direct => None,
        ProxyTarget::Proxy(p) => Some(p),
    })
}

/// The one User-Agent every Themia outbound HTTP request carries ([PX-10]).
///
/// One identity, one place. The failure mode this replaces is #81: adding an
/// RSS feed used `themia-app` while refreshing that same feed used a TRUNCATED
/// browser string (`Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36`
/// — no `Chrome/` or `Safari/` token, a textbook bot signature). Adding the feed
/// worked and every refresh afterwards could be challenged by the host's bot
/// filter, so one URL behaved as two different clients depending on which code
/// path touched it. An honest, contactable identity is also what feed hosts
/// allow-list and rate-limit against; a forged browser string is what they block.
static USER_AGENT: OnceLock<String> = OnceLock::new();

/// UA used before [`set_app_version`] runs. Only reachable from unit tests and
/// from a request issued before Tauri setup, which nothing currently does.
const USER_AGENT_FALLBACK: &str = "Themia (+https://github.com/crazy-rabbit-software/themia)";

/// Stamp the running app version into the shared User-Agent. Called once from
/// `src-tauri`'s setup, before any window (and therefore any request) exists.
/// Idempotent: a second call is ignored rather than racing the first.
pub fn set_app_version(version: &str) {
    let _ = USER_AGENT.set(format!(
        "Themia/{version} (+https://github.com/crazy-rabbit-software/themia)"
    ));
}

/// The shared User-Agent. `&'static str` so callers can hand it straight to
/// `reqwest::ClientBuilder::user_agent`.
pub fn user_agent() -> &'static str {
    USER_AGENT
        .get()
        .map(String::as_str)
        .unwrap_or(USER_AGENT_FALLBACK)
}

/// A `reqwest::ClientBuilder` that already honors the Windows proxy ([PX-01]).
///
/// Every reqwest caller in the app builds from this, so proxy support cannot be
/// forgotten at a new call site - the failure mode this replaces is exactly
/// that: `tauri-plugin-updater` sets `default-features = false` on reqwest and
/// never asks for `system-proxy`, so the updater honored no proxy whatsoever
/// until the app took ownership of its commands ([PX-09]). Feature unification
/// does not rescue it either - the plugin is on reqwest 0.13 while every crate
/// here is on 0.12, so they are two different crates as far as cargo is
/// concerned. That version split is also why [PX-09] wires the decision in as a
/// `Url` rather than by handing the plugin a `reqwest::Proxy` from here.
///
/// `timeout` is mandatory rather than optional: several clients shipped without
/// one, so a hung endpoint had no bound at all.
#[cfg(feature = "reqwest-client")]
pub fn client_builder(timeout: std::time::Duration) -> reqwest::ClientBuilder {
    reqwest::Client::builder()
        .timeout(timeout)
        .proxy(reqwest_proxy())
}

/// A blocking `ureq::Agent` that honors the Windows proxy for `url` ([PX-01]).
///
/// ureq resolves its proxy per-agent, not per-request, so the agent is built
/// for one target. Both callers (public-IP lookup, drag-drop download) are
/// one-shot, so that grain is the right one.
#[cfg(feature = "ureq-agent")]
pub fn ureq_agent(url: &Url, timeout: std::time::Duration) -> ureq::Agent {
    let mut b = ureq::AgentBuilder::new().timeout(timeout);
    if let ProxyTarget::Proxy(p) = resolve(url) {
        match ureq::Proxy::new(p.as_str()) {
            Ok(proxy) => b = b.proxy(proxy),
            Err(e) => log::warn!("netproxy: ureq rejected proxy {p}: {e}; going direct"),
        }
    }
    b.build()
}

/// Which proxy applies to a raw TCP service such as IMAP ([PX-08]).
///
/// Windows has no notion of an "imap proxy": `WinHttpGetProxyForUrl` wants a
/// URL, and `ProxyServer`'s per-protocol form has no `imap=` key. So the
/// service is presented as `https://host:port`, which is not a fudge but the
/// accurate question to ask - a proxied IMAP connection *is* an HTTP `CONNECT`
/// tunnel, exactly the mechanism `https` denotes to a proxy. PAC scripts see a
/// real host to branch on, and a per-protocol manual config contributes its
/// `https=` proxy, which is the one that can CONNECT.
pub fn resolve_for_tcp(host: &str, port: u16) -> ProxyTarget {
    match Url::parse(&format!("https://{host}:{port}")) {
        Ok(u) => resolve(&u),
        Err(e) => {
            log::debug!("netproxy: cannot form a URL for {host}:{port} ({e}); direct");
            ProxyTarget::Direct
        }
    }
}

/// Open a TCP stream to `host:port`, tunneling through the Windows proxy when
/// one applies ([PX-08]).
///
/// This is what lets IMAP be proxied at all. reqwest and ureq negotiate their
/// own tunnels; IMAP brings a bare socket, so the `CONNECT` handshake is ours
/// to perform. The returned stream is a transparent pipe to the origin, so the
/// caller layers TLS over it exactly as it would over a direct connection.
pub fn connect_tcp(
    host: &str,
    port: u16,
    timeout: std::time::Duration,
) -> Result<std::net::TcpStream, String> {
    let sock = match resolve_for_tcp(host, port) {
        ProxyTarget::Direct => connect_direct(host, port, timeout),
        ProxyTarget::Proxy(p) => connect_via_tunnel(&p, host, port, timeout),
    }?;
    // `timeout` bounds the dial and the CONNECT handshake only. Clear it before
    // handing the socket over: callers (IMAP) previously got a stream with no
    // read timeout, and imposing one here would silently turn a slow fetch of a
    // large mailbox into a failure. Bounding the session is a separate decision
    // from bounding the handshake, and not one this change is entitled to make.
    let _ = sock.set_read_timeout(None);
    let _ = sock.set_write_timeout(None);
    Ok(sock)
}

fn dial(
    addr_host: &str,
    addr_port: u16,
    timeout: std::time::Duration,
) -> Result<std::net::TcpStream, String> {
    use std::net::ToSocketAddrs;
    let mut last = String::from("no addresses");
    let addrs = (addr_host, addr_port)
        .to_socket_addrs()
        .map_err(|e| format!("Cannot resolve '{addr_host}': {e}"))?;
    for sa in addrs {
        match std::net::TcpStream::connect_timeout(&sa, timeout) {
            Ok(s) => {
                // Bound the CONNECT handshake too: a proxy that accepts the
                // socket and then says nothing must not hang the caller.
                let _ = s.set_read_timeout(Some(timeout));
                let _ = s.set_write_timeout(Some(timeout));
                return Ok(s);
            }
            Err(e) => last = e.to_string(),
        }
    }
    Err(format!("Cannot connect to {addr_host}:{addr_port}: {last}"))
}

fn connect_direct(
    host: &str,
    port: u16,
    timeout: std::time::Duration,
) -> Result<std::net::TcpStream, String> {
    dial(host, port, timeout)
}

/// Perform an HTTP `CONNECT` handshake and hand back the tunneled socket.
fn connect_via_tunnel(
    proxy: &Url,
    host: &str,
    port: u16,
    timeout: std::time::Duration,
) -> Result<std::net::TcpStream, String> {
    use std::io::{Read, Write};

    if proxy.scheme() != "http" {
        // SOCKS would need its own handshake; we do not speak it, and silently
        // connecting direct would be worse than saying so.
        return Err(format!(
            "Proxy {proxy} uses '{}', which is not supported for IMAP (only HTTP CONNECT)",
            proxy.scheme()
        ));
    }
    let proxy_host = proxy
        .host_str()
        .ok_or_else(|| format!("Proxy {proxy} has no host"))?;
    let proxy_port = proxy.port().unwrap_or(80);

    let mut sock = dial(proxy_host, proxy_port, timeout)?;

    let target = format!("{host}:{port}");
    let req = format!(
        "CONNECT {target} HTTP/1.1\r\nHost: {target}\r\nProxy-Connection: Keep-Alive\r\n\r\n"
    );
    sock.write_all(req.as_bytes())
        .map_err(|e| format!("Proxy CONNECT write failed: {e}"))?;
    sock.flush()
        .map_err(|e| format!("Proxy CONNECT flush failed: {e}"))?;

    // Read the response one byte at a time until the header terminator. Reading
    // in chunks could swallow bytes the ORIGIN has already sent through the
    // established tunnel, which would corrupt the TLS handshake the caller is
    // about to start. Headers are small; the syscalls are worth the safety.
    let mut head = Vec::with_capacity(128);
    let mut byte = [0u8; 1];
    loop {
        match sock.read(&mut byte) {
            Ok(0) => return Err("Proxy closed the connection during CONNECT".to_string()),
            Ok(_) => {
                head.push(byte[0]);
                if head.ends_with(b"\r\n\r\n") {
                    break;
                }
                if head.len() > 8192 {
                    return Err("Proxy sent an oversized CONNECT response".to_string());
                }
            }
            Err(e) => return Err(format!("Proxy CONNECT read failed: {e}")),
        }
    }

    let head = String::from_utf8_lossy(&head);
    let status = head.lines().next().unwrap_or("");
    let code = status.split_whitespace().nth(1).unwrap_or("");
    match code {
        "200" => Ok(sock),
        // Called out by name: this is the one limit users will actually hit,
        // and "connection failed" would send them hunting in the wrong place.
        "407" => Err(format!(
            "Proxy {proxy} requires authentication (407), which Themia does not \
             support for mail. See netproxy SPEC.md."
        )),
        _ => Err(format!(
            "Proxy {proxy} refused CONNECT to {target}: {status}"
        )),
    }
}

/// Human-readable summary of what Windows currently reports, for `diag_proxy`.
///
/// This is the one thing that cannot be established from a developer machine:
/// whether a given user's box has the manual or the automatic box filled in.
#[cfg(target_os = "windows")]
pub fn describe() -> String {
    match winhttp::ie_config() {
        None => "no per-user proxy configuration (direct)".to_string(),
        Some(c) => format!(
            "auto_detect={} auto_config_url={} manual_proxy={} manual_bypass={}",
            c.auto_detect,
            c.auto_config_url.as_deref().unwrap_or("<none>"),
            c.manual_proxy.as_deref().unwrap_or("<none>"),
            c.manual_bypass.as_deref().unwrap_or("<none>"),
        ),
    }
}

#[cfg(not(target_os = "windows"))]
pub fn describe() -> String {
    "not windows".to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    // [PX-05] Non-http schemes have no proxy notion and must not reach the OS.
    #[test]
    fn px05_non_http_scheme_is_direct() {
        let u = Url::parse("ftp://files.example.com/x").expect("parse");
        assert_eq!(resolve(&u), ProxyTarget::Direct);
    }

    // [PX-04] Cache key folds scheme and host case, so `EXAMPLE.com` and
    // `example.com` cannot resolve to two different answers.
    #[test]
    fn px04_cache_key_is_case_folded() {
        let a = Url::parse("https://EXAMPLE.com/a").expect("parse");
        let b = Url::parse("https://example.com/b").expect("parse");
        assert_eq!(key_for(&a), key_for(&b));
    }

    // [PX-04] Path is deliberately NOT part of the key.
    #[test]
    fn px04_cache_key_ignores_path() {
        let a = Url::parse("https://example.com/one").expect("parse");
        let b = Url::parse("https://example.com/two?q=1").expect("parse");
        assert_eq!(key_for(&a), key_for(&b));
    }

    // [PX-04] Scheme IS part of the key: a per-protocol manual config can send
    // http and https to different proxies.
    #[test]
    fn px04_cache_key_separates_schemes() {
        let a = Url::parse("http://example.com/").expect("parse");
        let b = Url::parse("https://example.com/").expect("parse");
        assert_ne!(key_for(&a), key_for(&b));
    }

    // [PX-04] Invalidation must not panic on a cold cache.
    #[test]
    fn px04_invalidate_on_empty_cache_is_safe() {
        invalidate();
    }

    /// A fake proxy: accept one connection, report the request-line it saw.
    ///
    /// The request-line is the whole proof. A direct request sends the origin
    /// form (`GET /thing`); only a proxied one sends the absolute form
    /// (`GET http://host/thing`). Seeing the latter arrive on this socket means
    /// the client really rewrote the request and really dialled the proxy.
    #[cfg(feature = "reqwest-client")]
    fn fake_proxy() -> (u16, std::thread::JoinHandle<String>) {
        use std::io::{Read, Write};
        use std::net::TcpListener;
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind fake proxy");
        let port = listener.local_addr().expect("addr").port();
        let handle = std::thread::spawn(move || {
            let Ok((mut sock, _)) = listener.accept() else {
                return String::new();
            };
            let mut buf = [0u8; 1024];
            let n = sock.read(&mut buf).unwrap_or(0);
            let req = String::from_utf8_lossy(&buf[..n]).to_string();
            let _ = sock.write_all(b"HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi");
            req.lines().next().unwrap_or("").to_string()
        });
        (port, handle)
    }

    // [PX-01] A client from `client_builder()` sends its traffic to whatever
    // proxy resolution decided - end to end, through real reqwest, over a real
    // socket. This is the wiring that makes the resolver matter.
    #[cfg(feature = "reqwest-client")]
    #[tokio::test]
    async fn px01_client_builder_routes_through_resolved_proxy() {
        let (port, handle) = fake_proxy();
        let target = Url::parse("http://px01-proxied.example/thing").expect("parse");
        seed_for_test(
            &target,
            ProxyTarget::Proxy(Url::parse(&format!("http://127.0.0.1:{port}")).expect("parse")),
        );

        let client = client_builder(std::time::Duration::from_secs(5))
            .build()
            .expect("build client");
        let resp = client.get(target.as_str()).send().await;
        assert!(resp.is_ok(), "request through fake proxy failed: {resp:?}");

        let request_line = handle.join().unwrap_or_default();
        assert_eq!(
            request_line, "GET http://px01-proxied.example/thing HTTP/1.1",
            "absolute-form request-line proves the client proxied rather than \
             dialling the origin directly"
        );
    }

    /// A fake CONNECT proxy. Replies `reply` to the CONNECT, then (on 200)
    /// acts as the origin by echoing whatever the client sends through the
    /// tunnel. Returns the CONNECT request-line it saw.
    fn fake_connect_proxy(reply: &'static str) -> (u16, std::thread::JoinHandle<String>) {
        use std::io::{Read, Write};
        use std::net::TcpListener;
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind connect proxy");
        let port = listener.local_addr().expect("addr").port();
        let handle = std::thread::spawn(move || {
            let Ok((mut sock, _)) = listener.accept() else {
                return String::new();
            };
            let mut head = Vec::new();
            let mut b = [0u8; 1];
            while sock.read(&mut b).unwrap_or(0) == 1 {
                head.push(b[0]);
                if head.ends_with(b"\r\n\r\n") {
                    break;
                }
            }
            let seen = String::from_utf8_lossy(&head)
                .lines()
                .next()
                .unwrap_or("")
                .to_string();
            let _ = sock.write_all(reply.as_bytes());
            let _ = sock.flush();
            if reply.contains("200") {
                // Tunnel established: echo one payload back, standing in for
                // the origin server on the far side.
                let mut buf = [0u8; 64];
                if let Ok(n) = sock.read(&mut buf) {
                    let _ = sock.write_all(&buf[..n]);
                    let _ = sock.flush();
                }
            }
            seen
        });
        (port, handle)
    }

    // [PX-08] The tunnel: IMAP's socket really traverses the proxy, and the
    // CONNECT names the ORIGIN host and port - not the proxy's.
    #[test]
    fn px08_connect_tcp_tunnels_through_proxy() {
        use std::io::{Read, Write};
        let (port, handle) = fake_connect_proxy("HTTP/1.1 200 Connection established\r\n\r\n");
        let target = Url::parse("https://imap.px08-test.example:993").expect("parse");
        seed_for_test(
            &target,
            ProxyTarget::Proxy(Url::parse(&format!("http://127.0.0.1:{port}")).expect("parse")),
        );

        let mut stream = connect_tcp(
            "imap.px08-test.example",
            993,
            std::time::Duration::from_secs(5),
        )
        .expect("tunnel should establish");

        // The stream must be a transparent pipe: whatever we write arrives at
        // the far side and comes back, with no CONNECT bytes left in the way.
        // A greeting-shaped payload stands in for the TLS ClientHello.
        stream
            .write_all(b"a1 CAPABILITY\r\n")
            .expect("write through tunnel");
        let mut buf = [0u8; 32];
        let n = stream.read(&mut buf).expect("read through tunnel");
        assert_eq!(
            &buf[..n],
            b"a1 CAPABILITY\r\n",
            "tunnel must carry bytes verbatim; leftover CONNECT header bytes here \
             would corrupt the TLS handshake"
        );

        let request_line = handle.join().unwrap_or_default();
        assert_eq!(
            request_line, "CONNECT imap.px08-test.example:993 HTTP/1.1",
            "CONNECT must name the origin, not the proxy"
        );
    }

    // [PX-08] A proxy demanding auth must say so in those words. This is the
    // known limit users will actually hit, and a generic connection error would
    // send them debugging their mail server instead of their proxy.
    #[test]
    fn px08_connect_407_reports_auth_not_a_generic_failure() {
        let (port, _h) = fake_connect_proxy("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");
        let target = Url::parse("https://imap.px08-auth.example:993").expect("parse");
        seed_for_test(
            &target,
            ProxyTarget::Proxy(Url::parse(&format!("http://127.0.0.1:{port}")).expect("parse")),
        );
        let err = connect_tcp(
            "imap.px08-auth.example",
            993,
            std::time::Duration::from_secs(5),
        )
        .expect_err("407 must be an error");
        assert!(err.contains("requires authentication"), "got: {err}");
        assert!(err.contains("407"), "got: {err}");
    }

    // [PX-08] Any other refusal surfaces the proxy's own status line rather
    // than being flattened into "connection failed".
    #[test]
    fn px08_connect_refusal_surfaces_proxy_status() {
        let (port, _h) = fake_connect_proxy("HTTP/1.1 403 Forbidden\r\n\r\n");
        let target = Url::parse("https://imap.px08-deny.example:993").expect("parse");
        seed_for_test(
            &target,
            ProxyTarget::Proxy(Url::parse(&format!("http://127.0.0.1:{port}")).expect("parse")),
        );
        let err = connect_tcp(
            "imap.px08-deny.example",
            993,
            std::time::Duration::from_secs(5),
        )
        .expect_err("403 must be an error");
        assert!(err.contains("403 Forbidden"), "got: {err}");
    }

    // [PX-08] With no proxy, connect_tcp is an ordinary direct dial - no
    // CONNECT preamble is sent to the origin.
    #[test]
    fn px08_connect_tcp_dials_direct_when_unproxied() {
        use std::io::{Read, Write};
        use std::net::TcpListener;
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind origin");
        let port = listener.local_addr().expect("addr").port();
        let h = std::thread::spawn(move || {
            let Ok((mut sock, _)) = listener.accept() else {
                return String::new();
            };
            let mut buf = [0u8; 64];
            let n = sock.read(&mut buf).unwrap_or(0);
            let _ = sock.write_all(b"ok");
            String::from_utf8_lossy(&buf[..n]).to_string()
        });

        let target = Url::parse(&format!("https://127.0.0.1:{port}")).expect("parse");
        seed_for_test(&target, ProxyTarget::Direct);
        let mut stream = connect_tcp("127.0.0.1", port, std::time::Duration::from_secs(5))
            .expect("direct dial should work");
        stream.write_all(b"hello").expect("write");

        let seen = h.join().unwrap_or_default();
        assert_eq!(
            seen, "hello",
            "a direct dial must not send a CONNECT preamble"
        );
    }

    // [PX-05] A DIRECT decision must leave the client alone: it dials the
    // origin, and nothing lands on the proxy socket.
    #[cfg(feature = "reqwest-client")]
    #[tokio::test]
    async fn px05_direct_decision_does_not_route_through_a_proxy() {
        let (port, handle) = fake_proxy();
        // Point at the fake proxy's port as if it were the ORIGIN, and resolve
        // DIRECT. A direct dial must produce an origin-form request-line.
        let target = Url::parse(&format!("http://127.0.0.1:{port}/direct")).expect("parse");
        seed_for_test(&target, ProxyTarget::Direct);

        let client = client_builder(std::time::Duration::from_secs(5))
            .build()
            .expect("build client");
        let _ = client.get(target.as_str()).send().await;

        let request_line = handle.join().unwrap_or_default();
        assert_eq!(
            request_line, "GET /direct HTTP/1.1",
            "origin-form request-line proves no proxy rewrite happened"
        );
    }
}

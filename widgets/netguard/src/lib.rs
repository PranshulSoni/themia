//! SSRF guard for outbound requests built from user-supplied URLs/hosts.
//!
//! Widgets let users point at arbitrary URLs and hosts (RSS, CalDAV/CardDAV,
//! self-hosted GitHub, IMAP, file downloads). Without a guard, a hostile feed
//! URL or a tricked user could make the app fetch `http://127.0.0.1/...`,
//! `http://169.254.169.254/...` (cloud metadata), or other internal hosts, a
//! classic Server-Side Request Forgery vector.
//!
//! Policy: by default, block requests that resolve to loopback / private /
//! link-local / reserved addresses. Because many users legitimately run
//! self-hosted CalDAV / RSS / GitHub Enterprise / IMAP on their LAN, there is a
//! single global bypass (`set_bypass`) the user can opt into from Settings.
//!
//! This lives in its OWN crate so every outbound caller (`src-tauri` command
//! wrappers AND the `widget-email` fetch paths) shares one guard and one bypass
//! flag: the `BYPASS` static is linked once into the final binary, so the
//! `set_ssrf_bypass` command and the email crate read the same value.
//!
//! The flag is owned by the frontend (persisted in the design store) and pushed
//! in via the `set_ssrf_bypass` command on startup and whenever it changes. The
//! runtime default is the SECURE one (block), so there is never a window where
//! the policy is off before the frontend has spoken.

use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, ToSocketAddrs};
use std::sync::atomic::{AtomicBool, Ordering};

/// When true, the SSRF policy is disabled and every host is allowed.
static BYPASS: AtomicBool = AtomicBool::new(false);

/// Set the global bypass. `true` disables the policy (allow private hosts).
pub fn set_bypass(on: bool) {
    BYPASS.store(on, Ordering::Relaxed);
}

/// Whether the policy is currently bypassed.
pub fn bypass() -> bool {
    BYPASS.load(Ordering::Relaxed)
}

/// Validate a full URL (scheme must be http/https). Resolves the host and
/// rejects if it points at a private/reserved address. Blocking (does DNS), so
/// call from a blocking context or via `spawn_blocking`.
pub fn check_url(raw: &str) -> Result<(), String> {
    if bypass() {
        return Ok(());
    }
    let url = url::Url::parse(raw).map_err(|e| format!("Invalid URL: {e}"))?;
    match url.scheme() {
        "http" | "https" => {}
        s => return Err(format!("Blocked URL scheme '{s}'")),
    }
    let host = url
        .host_str()
        .ok_or_else(|| "URL has no host".to_string())?;
    let port = url.port_or_known_default().unwrap_or(0);

    // [NG-07] If this request will travel through a proxy, Themia never
    // resolves the host - the proxy does - so a local resolution failure says
    // nothing about the target and must not block it. See SPEC.md for why this
    // relaxation is bounded (a proxy config that returns DIRECT for internal
    // hosts, which is essentially all of them, re-arms the full guard).
    let proxied = matches!(
        widget_netproxy::resolve(&url),
        widget_netproxy::ProxyTarget::Proxy(_)
    );
    check_host_port_inner(host, port, proxied)
}

/// Validate a bare host (+ port, used for resolution), for protocols that take
/// a host directly (IMAP). Blocking (does DNS).
///
/// Relaxes on a resolution failure under exactly the same rule as [`check_url`]
/// ([NG-07]): IMAP now reaches its server through an HTTP CONNECT tunnel when
/// the Windows proxy says so ([PX-08]), and in that case the proxy resolves the
/// host, not Themia. This function previously could not relax, because IMAP
/// always dialled its own socket - that is no longer true.
pub fn check_host_port(host: &str, port: u16) -> Result<(), String> {
    // Short-circuit before resolving: when the policy is off there is nothing
    // to decide, and asking Windows for a proxy would be a blocking WinHTTP
    // call (possibly a PAC fetch) whose answer we would then throw away.
    if bypass() {
        return Ok(());
    }
    let proxied = matches!(
        widget_netproxy::resolve_for_tcp(host, port),
        widget_netproxy::ProxyTarget::Proxy(_)
    );
    check_host_port_inner(host, port, proxied)
}

/// Shared core. `allow_unresolvable` is [NG-07]: it says "something other than
/// us will resolve this host, so our inability to is not evidence about it".
///
/// It only ever loosens the *resolution failure* path. An IP literal is still
/// checked (it needs no DNS), and a host that does resolve is still checked
/// against every address it maps to - being proxied is not a bypass.
fn check_host_port_inner(host: &str, port: u16, allow_unresolvable: bool) -> Result<(), String> {
    if bypass() {
        return Ok(());
    }
    // IP literal (with or without IPv6 brackets) → check directly, no DNS.
    let stripped = host.trim_start_matches('[').trim_end_matches(']');
    if let Ok(ip) = stripped.parse::<IpAddr>() {
        return guard_ip(ip);
    }
    // Hostname → resolve and check EVERY address it maps to.
    let addrs = match (host, port).to_socket_addrs() {
        Ok(a) => a,
        Err(e) => {
            if allow_unresolvable {
                log::debug!(
                    "netguard: cannot resolve '{host}' locally ({e}), but a proxy \
                     will resolve it; allowing [NG-07]"
                );
                return Ok(());
            }
            return Err(format!("Cannot resolve host '{host}': {e}"));
        }
    };
    let mut any = false;
    for sa in addrs {
        any = true;
        guard_ip(sa.ip())?;
    }
    if !any {
        if allow_unresolvable {
            return Ok(());
        }
        return Err(format!("Host '{host}' resolved to no addresses"));
    }
    Ok(())
}

fn guard_ip(ip: IpAddr) -> Result<(), String> {
    let blocked = match ip {
        IpAddr::V4(v4) => is_blocked_v4(v4),
        IpAddr::V6(v6) => is_blocked_v6(v6),
    };
    if blocked {
        Err(format!(
            "Blocked request to private/reserved address {ip}. Enable \
             'Allow private network access' in Settings to permit self-hosted services."
        ))
    } else {
        Ok(())
    }
}

fn is_blocked_v4(ip: Ipv4Addr) -> bool {
    ip.is_loopback()                 // 127.0.0.0/8
        || ip.is_private()           // 10/8, 172.16/12, 192.168/16
        || ip.is_link_local()        // 169.254/16 (incl. 169.254.169.254 metadata)
        || ip.is_unspecified()       // 0.0.0.0
        || ip.is_broadcast()         // 255.255.255.255
        || ip.is_documentation()     // 192.0.2/24, 198.51.100/24, 203.0.113/24
        || is_shared_v4(ip)          // 100.64.0.0/10 (CGNAT)
        || ip.octets()[0] == 0       // 0.0.0.0/8 "this network"
        || (ip.octets()[0] & 0xf0) == 0xf0 // 240.0.0.0/4 reserved
}

fn is_shared_v4(ip: Ipv4Addr) -> bool {
    let o = ip.octets();
    o[0] == 100 && (o[1] & 0xc0) == 0x40 // 100.64.0.0/10
}

fn is_blocked_v6(ip: Ipv6Addr) -> bool {
    if ip.is_loopback() || ip.is_unspecified() {
        return true;
    }
    // IPv4-mapped (::ffff:a.b.c.d): classify the embedded v4.
    if let Some(v4) = ip.to_ipv4_mapped() {
        return is_blocked_v4(v4);
    }
    let seg = ip.segments();
    (seg[0] & 0xffc0) == 0xfe80          // link-local fe80::/10
        || (seg[0] & 0xfe00) == 0xfc00   // unique-local fc00::/7
        || (seg[0] == 0x2001 && seg[1] == 0x0db8) // documentation 2001:db8::/32
}

#[cfg(test)]
mod tests {
    use super::*;

    fn blocked(raw: &str) -> bool {
        // Tests run with bypass OFF (the secure default).
        set_bypass(false);
        check_url(raw).is_err()
    }

    #[test]
    fn blocks_loopback_and_metadata_and_private() {
        assert!(blocked("http://127.0.0.1/"));
        assert!(blocked("http://localhost/")); // resolves to loopback
        assert!(blocked("http://[::1]/"));
        assert!(blocked("http://169.254.169.254/latest/meta-data/")); // cloud metadata
        assert!(blocked("http://10.0.0.5/"));
        assert!(blocked("http://192.168.1.1/"));
        assert!(blocked("http://172.16.0.1/"));
        assert!(blocked("http://0.0.0.0/"));
        assert!(blocked("http://100.64.0.1/")); // CGNAT
    }

    #[test]
    fn blocks_non_http_schemes() {
        assert!(blocked("file:///etc/passwd"));
        assert!(blocked("gopher://127.0.0.1/"));
        assert!(blocked("ftp://10.0.0.1/"));
    }

    #[test]
    fn allows_public_addresses() {
        set_bypass(false);
        assert!(check_url("http://1.1.1.1/").is_ok());
        assert!(check_url("http://8.8.8.8/").is_ok());
        assert!(check_url("https://93.184.216.34/").is_ok()); // example.com literal
    }

    #[test]
    fn bypass_allows_private() {
        set_bypass(true);
        assert!(check_url("http://127.0.0.1/").is_ok());
        assert!(check_host_port("192.168.1.50", 993).is_ok());
        set_bypass(false); // restore for other tests
    }

    #[test]
    fn host_port_checks_ip_literals() {
        set_bypass(false);
        assert!(check_host_port("127.0.0.1", 993).is_err());
        assert!(check_host_port("[::1]", 993).is_err());
        assert!(check_host_port("169.254.169.254", 80).is_err());
        assert!(check_host_port("1.1.1.1", 993).is_ok());
    }

    // [NG-07] Characterization: with no proxy in play, a host that cannot be
    // resolved is blocked. The connection would fail regardless, and the guard
    // gives the clearer message. `.invalid` is reserved by RFC 2606 precisely
    // so it can never resolve, on any network.
    #[test]
    fn ng07_unresolvable_host_is_blocked_when_direct() {
        set_bypass(false);
        let res = check_url("http://themia-netguard-test.invalid/feed.xml");
        assert!(
            res.is_err(),
            "an unresolvable host must not be allowed through on a direct path"
        );
        let msg = res.unwrap_err();
        assert!(
            msg.contains("Cannot resolve host"),
            "expected a resolution error, got: {msg}"
        );
    }

    // [NG-07] The fix: when something else will resolve the host, our failure
    // to resolve it is not evidence, and must not block. This is the corporate
    // split-DNS case, where every user-supplied URL used to fail with a
    // misleading "Cannot resolve host".
    #[test]
    fn ng07_unresolvable_host_is_allowed_when_proxied() {
        set_bypass(false);
        assert!(
            check_host_port_inner("themia-netguard-test.invalid", 80, true).is_ok(),
            "behind a proxy, a host we cannot resolve must be left to the proxy"
        );
    }

    // [NG-07] ...but being proxied is NOT a bypass. An IP literal needs no DNS,
    // so it is still checked - a hostile URL cannot reach the metadata endpoint
    // just because a proxy happens to be configured.
    #[test]
    fn ng07_proxied_does_not_bypass_ip_literal_checks() {
        set_bypass(false);
        assert!(check_host_port_inner("169.254.169.254", 80, true).is_err());
        assert!(check_host_port_inner("127.0.0.1", 80, true).is_err());
        assert!(check_host_port_inner("10.0.0.5", 80, true).is_err());
        assert!(check_host_port_inner("[::1]", 80, true).is_err());
    }

    // [NG-07] ...and a host that DOES resolve is still fully checked when
    // proxied. localhost resolves everywhere, so the guard must still fire.
    #[test]
    fn ng07_proxied_still_checks_hosts_that_do_resolve() {
        set_bypass(false);
        assert!(
            check_host_port_inner("localhost", 80, true).is_err(),
            "a resolvable private host must stay blocked even when proxied"
        );
    }

    // [NG-07] IMAP's entry point follows the same rule as check_url: with no
    // proxy in play (as here), an unresolvable host is still blocked.
    #[test]
    fn ng07_check_host_port_blocks_unresolvable_when_direct() {
        set_bypass(false);
        assert!(check_host_port("themia-netguard-test.invalid", 993).is_err());
    }

    #[test]
    fn v6_unique_local_and_link_local_blocked() {
        assert!(is_blocked_v6("fc00::1".parse().unwrap()));
        assert!(is_blocked_v6("fe80::1".parse().unwrap()));
        assert!(is_blocked_v6("::ffff:127.0.0.1".parse().unwrap())); // mapped loopback
        assert!(!is_blocked_v6("2606:4700:4700::1111".parse().unwrap())); // public
    }
}

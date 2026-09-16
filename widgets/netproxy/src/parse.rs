//! Pure parsing/matching logic for Windows proxy configuration strings.
//!
//! Split out from the Win32 calls in `winhttp.rs` so the string formats (which
//! are where the bugs live) are unit-testable without a registry, a PAC server,
//! or a proxy. Nothing here touches the OS.

use url::Url;

/// The proxy decision for a single URL. `Direct` means "connect straight out",
/// which is also the fail-open answer whenever Windows cannot tell us better
/// ([PX-05]).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ProxyTarget {
    Direct,
    Proxy(Url),
}

/// Pick the proxy for `scheme` out of a WinINET `ProxyServer` string ([PX-03]).
///
/// Two shapes are legal, and both appear in the wild:
///   * `"proxy.corp:8080"`                     - one proxy for every protocol
///   * `"http=a:1;https=b:2;ftp=c:3"`          - per-protocol
///
/// Entries may be separated by `;` or whitespace. A per-protocol string that
/// lists no entry for `scheme` means DIRECT for that scheme, not "fall back to
/// some other protocol's proxy" - sending HTTPS through the FTP proxy because
/// it happened to be listed would be worse than not proxying at all.
pub fn select_proxy_for_scheme(list: &str, scheme: &str) -> Option<String> {
    let entries: Vec<&str> = list
        .split([';', ' ', '\t'])
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .collect();
    if entries.is_empty() {
        return None;
    }

    // Shape 1: a bare "host:port" with no `=` anywhere applies to all schemes.
    if !entries.iter().any(|e| e.contains('=')) {
        return entries.first().map(|s| (*s).to_string());
    }

    // Shape 2: per-protocol. Match `scheme=` case-insensitively.
    for e in entries {
        if let Some((proto, addr)) = e.split_once('=') {
            if proto.trim().eq_ignore_ascii_case(scheme) {
                let addr = addr.trim();
                if !addr.is_empty() {
                    return Some(addr.to_string());
                }
            }
        }
    }
    None
}

/// Whether `host` is exempt from proxying per a WinINET `ProxyOverride` string
/// ([PX-03]).
///
/// Format: `;`-separated patterns, `*` as a wildcard, plus the special token
/// `<local>` meaning "any hostname with no dots in it" (the classic intranet
/// shorthand). Matching is case-insensitive; hosts are matched whole, so
/// `corp.com` does not match `evilcorp.com`.
pub fn bypass_matches(bypass: &str, host: &str) -> bool {
    let host = host.trim().trim_start_matches('[').trim_end_matches(']');
    for raw in bypass.split([';', ' ']).map(str::trim) {
        if raw.is_empty() {
            continue;
        }
        if raw.eq_ignore_ascii_case("<local>") {
            // "<local>" = no dot in the hostname. Loopback is always local.
            if !host.contains('.') || host.eq_ignore_ascii_case("localhost") {
                return true;
            }
            continue;
        }
        if wildcard_matches(raw, host) {
            return true;
        }
    }
    false
}

/// Case-insensitive glob where `*` matches any run of characters. Anchored at
/// both ends: a pattern must consume the whole host.
fn wildcard_matches(pattern: &str, host: &str) -> bool {
    let p: Vec<char> = pattern.to_ascii_lowercase().chars().collect();
    let h: Vec<char> = host.to_ascii_lowercase().chars().collect();
    // Classic two-pointer glob with backtracking on the last `*`.
    let (mut pi, mut hi) = (0usize, 0usize);
    let (mut star, mut backtrack) = (usize::MAX, 0usize);
    while hi < h.len() {
        if pi < p.len() && (p[pi] == h[hi]) {
            pi += 1;
            hi += 1;
        } else if pi < p.len() && p[pi] == '*' {
            star = pi;
            backtrack = hi;
            pi += 1;
        } else if star != usize::MAX {
            pi = star + 1;
            backtrack += 1;
            hi = backtrack;
        } else {
            return false;
        }
    }
    while pi < p.len() && p[pi] == '*' {
        pi += 1;
    }
    pi == p.len()
}

/// Normalize one proxy address into a URL reqwest/ureq can consume ([PX-03]).
///
/// Accepts what Windows hands back: a bare `host:port`, an already-schemed
/// `http://host:port`, or a WinINET protocol prefix (`socks=host:1080`). A bare
/// address means an HTTP proxy - that is WinINET's own default reading, and it
/// is what every corporate CONNECT proxy is.
pub fn normalize_proxy(raw: &str) -> Option<Url> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    // WinINET may prefix the scheme with `proto=`; strip it, but remember a
    // `socks=` prefix, which changes the proxy protocol rather than the target.
    let (prefix, addr) = match raw.split_once('=') {
        Some((p, a)) => (Some(p.trim().to_ascii_lowercase()), a.trim()),
        None => (None, raw),
    };
    if addr.is_empty() {
        return None;
    }

    let candidate = if addr.contains("://") {
        addr.to_string()
    } else if prefix.as_deref() == Some("socks") {
        format!("socks5://{addr}")
    } else {
        format!("http://{addr}")
    };

    let url = Url::parse(&candidate).ok()?;
    // A proxy with no host is meaningless; reject rather than hand reqwest a
    // URL it will fail on later at request time.
    if url.host_str().is_none_or(str::is_empty) {
        return None;
    }
    Some(url)
}

/// Take the first usable proxy out of a WinHTTP proxy list ([PX-02]).
///
/// `WinHttpGetProxyForUrl` flattens a PAC script's `return` value into
/// `lpszProxy`, which may hold several entries separated by `;` or whitespace
/// (`"10.0.0.1:8080; 10.0.0.2:8080"`). We take the first that parses. Failover
/// across the rest is deliberately not implemented: reqwest has no hook for
/// "try the next proxy", and a silent second proxy would be a second code path.
pub fn first_proxy(list: &str) -> Option<Url> {
    list.split([';', ' ', '\t'])
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .find_map(normalize_proxy)
}

#[cfg(test)]
mod tests {
    use super::*;

    // [PX-03] Manual proxy: the single-proxy-for-everything shape.
    #[test]
    fn px03_bare_address_applies_to_every_scheme() {
        assert_eq!(
            select_proxy_for_scheme("proxy.corp:8080", "https"),
            Some("proxy.corp:8080".to_string())
        );
        assert_eq!(
            select_proxy_for_scheme("proxy.corp:8080", "http"),
            Some("proxy.corp:8080".to_string())
        );
    }

    // [PX-03] Manual proxy: the per-protocol shape.
    #[test]
    fn px03_per_protocol_selects_matching_scheme() {
        let list = "http=a.corp:1;https=b.corp:2;ftp=c.corp:3";
        assert_eq!(
            select_proxy_for_scheme(list, "https"),
            Some("b.corp:2".to_string())
        );
        assert_eq!(
            select_proxy_for_scheme(list, "http"),
            Some("a.corp:1".to_string())
        );
    }

    // A scheme absent from a per-protocol list means DIRECT, never another
    // protocol's proxy.
    #[test]
    fn px03_absent_scheme_is_direct_not_a_fallback() {
        assert_eq!(select_proxy_for_scheme("http=a.corp:1", "https"), None);
        assert_eq!(select_proxy_for_scheme("ftp=c.corp:3", "https"), None);
    }

    #[test]
    fn px03_scheme_match_is_case_insensitive() {
        assert_eq!(
            select_proxy_for_scheme("HTTPS=b.corp:2", "https"),
            Some("b.corp:2".to_string())
        );
    }

    #[test]
    fn px03_whitespace_separated_entries_parse() {
        let list = "http=a.corp:1 https=b.corp:2";
        assert_eq!(
            select_proxy_for_scheme(list, "https"),
            Some("b.corp:2".to_string())
        );
    }

    #[test]
    fn px03_empty_list_is_direct() {
        assert_eq!(select_proxy_for_scheme("", "https"), None);
        assert_eq!(select_proxy_for_scheme("   ", "https"), None);
    }

    // [PX-03] Bypass list.
    #[test]
    fn px03_bypass_exact_host() {
        assert!(bypass_matches("intranet.corp.com", "intranet.corp.com"));
        assert!(!bypass_matches("intranet.corp.com", "other.corp.com"));
    }

    #[test]
    fn px03_bypass_wildcard_subdomain() {
        assert!(bypass_matches("*.corp.com", "a.corp.com"));
        assert!(bypass_matches("*.corp.com", "deep.a.corp.com"));
        assert!(!bypass_matches("*.corp.com", "corp.com.evil.net"));
    }

    // A bypass entry must match the WHOLE host: a suffix pattern must not let
    // `evilcorp.com` ride on a `corp.com` exemption.
    #[test]
    fn px03_bypass_is_anchored_both_ends() {
        assert!(!bypass_matches("corp.com", "evilcorp.com"));
        assert!(!bypass_matches("corp.com", "corp.com.attacker.net"));
    }

    #[test]
    fn px03_bypass_local_token_matches_dotless_hosts() {
        assert!(bypass_matches("<local>", "intranet"));
        assert!(bypass_matches("<local>", "localhost"));
        assert!(!bypass_matches("<local>", "www.example.com"));
    }

    #[test]
    fn px03_bypass_multiple_entries() {
        let b = "<local>;*.corp.com;10.*";
        assert!(bypass_matches(b, "build"));
        assert!(bypass_matches(b, "git.corp.com"));
        assert!(bypass_matches(b, "10.1.2.3"));
        assert!(!bypass_matches(b, "graph.microsoft.com"));
    }

    #[test]
    fn px03_bypass_is_case_insensitive() {
        assert!(bypass_matches("*.CORP.com", "git.corp.COM"));
    }

    #[test]
    fn px03_empty_bypass_matches_nothing() {
        assert!(!bypass_matches("", "example.com"));
        assert!(!bypass_matches("  ", "example.com"));
    }

    // Normalization.
    #[test]
    fn px03_bare_address_becomes_http_proxy() {
        assert_eq!(
            normalize_proxy("10.0.0.1:8080"),
            Url::parse("http://10.0.0.1:8080").ok()
        );
    }

    #[test]
    fn px03_schemed_address_is_preserved() {
        assert_eq!(
            normalize_proxy("http://10.0.0.1:8080"),
            Url::parse("http://10.0.0.1:8080").ok()
        );
    }

    #[test]
    fn px03_socks_prefix_becomes_socks5() {
        assert_eq!(
            normalize_proxy("socks=10.0.0.1:1080"),
            Url::parse("socks5://10.0.0.1:1080").ok()
        );
    }

    #[test]
    fn px03_garbage_is_rejected_not_guessed() {
        assert_eq!(normalize_proxy(""), None);
        assert_eq!(normalize_proxy("   "), None);
        assert_eq!(normalize_proxy("http="), None);
    }

    // [PX-02] PAC output: WinHTTP flattens the script's answer into a list.
    #[test]
    fn px02_first_proxy_of_a_pac_list_wins() {
        assert_eq!(
            first_proxy("10.0.0.1:8080; 10.0.0.2:8080"),
            Url::parse("http://10.0.0.1:8080").ok()
        );
    }

    #[test]
    fn px02_first_proxy_skips_unparseable_leading_entries() {
        assert_eq!(
            first_proxy("   ; 10.0.0.2:8080"),
            Url::parse("http://10.0.0.2:8080").ok()
        );
    }

    #[test]
    fn px02_empty_pac_list_is_direct() {
        assert_eq!(first_proxy(""), None);
    }
}

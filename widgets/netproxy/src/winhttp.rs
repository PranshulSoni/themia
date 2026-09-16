//! Win32 WinHTTP proxy resolution ([PX-01], [PX-02]).
//!
//! Themia brings its own HTTP stack (reqwest / ureq / a raw IMAP socket), so
//! unlike WebView2 it gets nothing from Windows for free: an HTTP proxy is a
//! rewrite of the request itself, not something the OS can apply to a socket
//! underneath us. The Windows proxy page is only a settings store; every app
//! must read it and opt in.
//!
//! We do NOT reimplement that reading. The "Automatic proxy setup" box (WPAD
//! auto-detect and/or a PAC setup script) requires fetching a script and
//! executing its JavaScript per URL - `WinHttpGetProxyForUrl` already does
//! exactly that, including the script cache. So this module is a thin, honest
//! shim: ask Windows what proxy applies to a URL, hand the answer upward.
//!
//! Everything here is `unsafe` FFI, so every handle and every OS-allocated
//! string is owned by an RAII type that frees on all paths.

#![cfg(target_os = "windows")]

use crate::parse::{
    bypass_matches, first_proxy, normalize_proxy, select_proxy_for_scheme, ProxyTarget,
};
use std::sync::OnceLock;
use url::Url;
use windows::core::PCWSTR;
use windows::Win32::Foundation::{GlobalFree, HGLOBAL};
use windows::Win32::Networking::WinHttp::{
    WinHttpCloseHandle, WinHttpGetIEProxyConfigForCurrentUser, WinHttpGetProxyForUrl, WinHttpOpen,
    WINHTTP_ACCESS_TYPE_NAMED_PROXY, WINHTTP_ACCESS_TYPE_NO_PROXY, WINHTTP_AUTOPROXY_AUTO_DETECT,
    WINHTTP_AUTOPROXY_CONFIG_URL, WINHTTP_AUTOPROXY_OPTIONS, WINHTTP_AUTO_DETECT_TYPE_DHCP,
    WINHTTP_AUTO_DETECT_TYPE_DNS_A, WINHTTP_CURRENT_USER_IE_PROXY_CONFIG, WINHTTP_PROXY_INFO,
};

/// UTF-16, NUL-terminated. Kept alive by the caller for the whole FFI call.
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Read an OS-allocated wide string. Returns `None` for NULL / empty.
///
/// # Safety
/// `p` must be NULL or a valid NUL-terminated wide string.
unsafe fn pwstr_to_string(p: *const u16) -> Option<String> {
    if p.is_null() {
        return None;
    }
    let mut len = 0usize;
    while unsafe { *p.add(len) } != 0 {
        len += 1;
    }
    if len == 0 {
        return None;
    }
    let slice = unsafe { std::slice::from_raw_parts(p, len) };
    Some(String::from_utf16_lossy(slice))
}

/// Free a string WinHTTP allocated for us with `GlobalAlloc`.
///
/// # Safety
/// `p` must be NULL or a pointer WinHTTP produced; it must not be used after.
unsafe fn global_free(p: *mut u16) {
    if p.is_null() {
        return;
    }
    // ignore: GlobalFree returns NULL on SUCCESS, so the windows-rs wrapper -
    // which treats a NULL return as invalid - reports the success path as Err.
    // There is no failure we could act on here regardless: the pointer came
    // from WinHTTP and is dropped either way.
    let _ = unsafe { GlobalFree(Some(HGLOBAL(p as *mut core::ffi::c_void))) };
}

/// RAII owner of the WinHTTP session handle ([PX-06]).
struct Session(*mut core::ffi::c_void);

// The handle is only ever used by `WinHttpGetProxyForUrl`, which is documented
// thread-safe for a session handle; we never mutate the pointer after open.
unsafe impl Send for Session {}
unsafe impl Sync for Session {}

impl Drop for Session {
    fn drop(&mut self) {
        if !self.0.is_null() {
            // ignore: closing on teardown; nothing to recover if it fails.
            let _ = unsafe { WinHttpCloseHandle(self.0) };
        }
    }
}

/// One process-lifetime session ([PX-04]).
///
/// Deliberately long-lived rather than per-call: WinHTTP caches the fetched PAC
/// script and its auto-detect result *on the session*, so a fresh handle per
/// lookup would re-download and re-parse the script every time. It is dropped
/// only at process exit, which is the intended lifetime, not a leak.
fn session() -> Option<&'static Session> {
    static SESSION: OnceLock<Option<Session>> = OnceLock::new();
    SESSION
        .get_or_init(|| {
            let agent = wide("Themia");
            let h = unsafe {
                WinHttpOpen(
                    PCWSTR(agent.as_ptr()),
                    WINHTTP_ACCESS_TYPE_NO_PROXY,
                    PCWSTR::null(),
                    PCWSTR::null(),
                    0,
                )
            };
            if h.is_null() {
                log::warn!("netproxy: WinHttpOpen failed; proxy resolution disabled");
                None
            } else {
                Some(Session(h))
            }
        })
        .as_ref()
}

/// RAII owner of the three strings `WinHttpGetIEProxyConfigForCurrentUser`
/// allocates ([PX-06]).
struct IeConfig(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG);

impl Drop for IeConfig {
    fn drop(&mut self) {
        unsafe {
            global_free(self.0.lpszAutoConfigUrl.0);
            global_free(self.0.lpszProxy.0);
            global_free(self.0.lpszProxyBypass.0);
        }
    }
}

/// What the user actually has in Settings > Network & Internet > Proxy.
pub(crate) struct WindowsProxyConfig {
    /// "Automatically detect settings" (WPAD).
    pub auto_detect: bool,
    /// "Use setup script" address (PAC).
    pub auto_config_url: Option<String>,
    /// "Manual proxy setup" address:port (possibly per-protocol).
    pub manual_proxy: Option<String>,
    /// Manual bypass list.
    pub manual_bypass: Option<String>,
}

/// Read the current user's proxy configuration.
///
/// Returns `None` when Windows has no per-user config to give (running with no
/// user profile). That is "we don't know", which resolves to DIRECT ([PX-05]).
pub(crate) fn ie_config() -> Option<WindowsProxyConfig> {
    let mut raw = WINHTTP_CURRENT_USER_IE_PROXY_CONFIG::default();
    // SAFETY: `raw` is a valid, zeroed, correctly-sized out-param. On success
    // WinHTTP fills it with GlobalAlloc'd strings, which `IeConfig` then owns.
    let ok = unsafe { WinHttpGetIEProxyConfigForCurrentUser(&mut raw) };
    if let Err(e) = ok {
        log::debug!("netproxy: no per-user proxy config ({e}); treating as direct");
        return None;
    }
    let owned = IeConfig(raw);
    // SAFETY: the pointers are NULL or valid NUL-terminated WinHTTP strings,
    // still alive - `owned` frees them when this function returns.
    unsafe {
        Some(WindowsProxyConfig {
            auto_detect: owned.0.fAutoDetect.as_bool(),
            auto_config_url: pwstr_to_string(owned.0.lpszAutoConfigUrl.0),
            manual_proxy: pwstr_to_string(owned.0.lpszProxy.0),
            manual_bypass: pwstr_to_string(owned.0.lpszProxyBypass.0),
        })
    }
}

/// RAII owner of the two strings `WinHttpGetProxyForUrl` allocates ([PX-06]).
struct ProxyInfo(WINHTTP_PROXY_INFO);

impl Drop for ProxyInfo {
    fn drop(&mut self) {
        unsafe {
            global_free(self.0.lpszProxy.0);
            global_free(self.0.lpszProxyBypass.0);
        }
    }
}

/// Resolve via the "Automatic proxy setup" box: WPAD and/or a PAC script
/// ([PX-02]). This is the box Themia used to ignore entirely.
///
/// `None` means auto-config produced no answer (no WPAD server, unreachable or
/// broken script), which is different from "the script said DIRECT" - the
/// caller falls back to the manual box on `None`, but honors `Some(Direct)`.
pub(crate) fn resolve_auto(
    url: &Url,
    auto_detect: bool,
    config_url: Option<&str>,
) -> Option<ProxyTarget> {
    let session = session()?;

    let mut flags = 0u32;
    if auto_detect {
        flags |= WINHTTP_AUTOPROXY_AUTO_DETECT;
    }
    let cfg_wide = config_url.map(wide);
    if cfg_wide.is_some() {
        flags |= WINHTTP_AUTOPROXY_CONFIG_URL;
    }
    if flags == 0 {
        return None;
    }

    let url_wide = wide(url.as_str());

    // MSDN: fAutoLogonIfChallenged=TRUE makes every call slow, so try FALSE
    // first and only retry with TRUE if the script fetch was challenged.
    for auto_logon in [false, true] {
        let mut opts = WINHTTP_AUTOPROXY_OPTIONS {
            dwFlags: flags,
            dwAutoDetectFlags: if auto_detect {
                WINHTTP_AUTO_DETECT_TYPE_DHCP | WINHTTP_AUTO_DETECT_TYPE_DNS_A
            } else {
                0
            },
            lpszAutoConfigUrl: cfg_wide
                .as_ref()
                .map_or(PCWSTR::null(), |w| PCWSTR(w.as_ptr())),
            lpvReserved: std::ptr::null_mut(),
            dwReserved: 0,
            fAutoLogonIfChallenged: auto_logon.into(),
        };
        let mut info = WINHTTP_PROXY_INFO::default();
        // SAFETY: session handle is live for the process; `url_wide` and
        // `cfg_wide` outlive the call; `info` is a valid out-param whose
        // strings `ProxyInfo` takes ownership of on success.
        let res = unsafe {
            WinHttpGetProxyForUrl(session.0, PCWSTR(url_wide.as_ptr()), &mut opts, &mut info)
        };
        match res {
            Ok(()) => {
                let owned = ProxyInfo(info);
                if owned.0.dwAccessType == WINHTTP_ACCESS_TYPE_NO_PROXY {
                    // The script explicitly said DIRECT for this URL.
                    return Some(ProxyTarget::Direct);
                }
                if owned.0.dwAccessType == WINHTTP_ACCESS_TYPE_NAMED_PROXY {
                    // SAFETY: valid WinHTTP string, alive until `owned` drops.
                    let list = unsafe { pwstr_to_string(owned.0.lpszProxy.0) };
                    if let Some(u) = list.as_deref().and_then(first_proxy) {
                        return Some(ProxyTarget::Proxy(u));
                    }
                }
                return Some(ProxyTarget::Direct);
            }
            Err(e) => {
                log::debug!(
                    "netproxy: auto-config lookup failed for {} (auto_logon={auto_logon}): {e}",
                    url.host_str().unwrap_or("?")
                );
                // Retry once with auto-logon; otherwise give up on the auto box.
            }
        }
    }
    None
}

/// Resolve via the "Manual proxy setup" box ([PX-03]).
fn resolve_manual(url: &Url, cfg: &WindowsProxyConfig) -> ProxyTarget {
    let Some(proxy) = cfg.manual_proxy.as_deref() else {
        return ProxyTarget::Direct;
    };
    let host = url.host_str().unwrap_or_default();
    if let Some(bypass) = cfg.manual_bypass.as_deref() {
        if bypass_matches(bypass, host) {
            return ProxyTarget::Direct;
        }
    }
    match select_proxy_for_scheme(proxy, url.scheme())
        .as_deref()
        .and_then(normalize_proxy)
    {
        Some(u) => ProxyTarget::Proxy(u),
        None => ProxyTarget::Direct,
    }
}

/// Ask Windows which proxy (if any) applies to `url` ([PX-01]).
///
/// Order matches WinINET's own: the automatic box wins when it produces an
/// answer, the manual box is the fallback, and "no answer anywhere" is DIRECT
/// ([PX-05]). Blocking - see `crate::resolve` for the caching that keeps this
/// off the hot path.
pub(crate) fn resolve_uncached(url: &Url) -> ProxyTarget {
    let Some(cfg) = ie_config() else {
        return ProxyTarget::Direct;
    };
    if cfg.auto_detect || cfg.auto_config_url.is_some() {
        if let Some(t) = resolve_auto(url, cfg.auto_detect, cfg.auto_config_url.as_deref()) {
            return t;
        }
    }
    resolve_manual(url, &cfg)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Read, Write};
    use std::net::TcpListener;

    /// Serve a PAC script from loopback for as many requests as WinHTTP makes.
    ///
    /// This is what lets [PX-02] be tested for real on a machine with no proxy
    /// and no corporate network: `WinHttpGetProxyForUrl` takes an explicit
    /// `lpszAutoConfigUrl`, so we can hand it our own script and observe that
    /// Windows fetched it, ran its JavaScript, and returned the verdict -
    /// without writing a single registry key or changing the developer's real
    /// proxy settings.
    fn serve_pac(script: &'static str) -> String {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind pac server");
        let port = listener.local_addr().expect("addr").port();
        std::thread::spawn(move || {
            for stream in listener.incoming().take(8) {
                let Ok(mut sock) = stream else { continue };
                let mut buf = [0u8; 2048];
                let _ = sock.read(&mut buf);
                let body = script.as_bytes();
                let head = format!(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
                    body.len()
                );
                let _ = sock.write_all(head.as_bytes());
                let _ = sock.write_all(body);
                let _ = sock.flush();
            }
        });
        format!("http://127.0.0.1:{port}/proxy.pac")
    }

    // [PX-02] The "Use setup script" box: Windows fetches the PAC, runs its JS,
    // and we honor the PROXY verdict. This is the case Themia used to ignore
    // entirely and go direct.
    #[test]
    fn px02_pac_script_proxy_verdict_is_honored() {
        let pac =
            serve_pac("function FindProxyForURL(url, host) { return \"PROXY 10.0.0.1:8080\"; }");
        let url = Url::parse("https://graph.microsoft.com/v1.0/me").expect("parse");
        let got = resolve_auto(&url, false, Some(&pac));
        assert_eq!(
            got,
            Some(ProxyTarget::Proxy(
                Url::parse("http://10.0.0.1:8080").expect("parse")
            )),
            "WinHTTP should have run the PAC and returned its proxy"
        );
    }

    // [PX-02] A PAC that says DIRECT for a host must mean DIRECT, and must be
    // distinguishable from "auto-config produced no answer".
    #[test]
    fn px02_pac_script_direct_verdict_is_honored() {
        let pac = serve_pac("function FindProxyForURL(url, host) { return \"DIRECT\"; }");
        let url = Url::parse("https://example.com/").expect("parse");
        assert_eq!(
            resolve_auto(&url, false, Some(&pac)),
            Some(ProxyTarget::Direct)
        );
    }

    // [PX-02] PAC scripts routinely branch on host; the per-URL call must pass
    // the real host through, not a cached answer for a different one.
    #[test]
    fn px02_pac_script_branches_on_host() {
        let pac = serve_pac(
            "function FindProxyForURL(url, host) { \
               if (host == \"intranet.corp\") return \"DIRECT\"; \
               return \"PROXY 10.9.9.9:3128\"; }",
        );
        let internal = Url::parse("https://intranet.corp/app").expect("parse");
        let external = Url::parse("https://api.github.com/user").expect("parse");
        assert_eq!(
            resolve_auto(&internal, false, Some(&pac)),
            Some(ProxyTarget::Direct)
        );
        assert_eq!(
            resolve_auto(&external, false, Some(&pac)),
            Some(ProxyTarget::Proxy(
                Url::parse("http://10.9.9.9:3128").expect("parse")
            ))
        );
    }

    // [PX-05] An unreachable setup script must not fail the request: it means
    // "no answer from the automatic box", so the caller falls back to manual /
    // direct rather than erroring out.
    #[test]
    fn px05_unreachable_pac_url_yields_no_answer() {
        // Port 1 on loopback: nothing listens, connection refused immediately.
        let url = Url::parse("https://example.com/").expect("parse");
        let got = resolve_auto(&url, false, Some("http://127.0.0.1:1/nope.pac"));
        assert_eq!(got, None, "a dead PAC URL is 'no answer', not a proxy");
    }
}

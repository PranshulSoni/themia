use serde::{Deserialize, Serialize};

#[derive(Deserialize, Clone)]
pub struct PingHost {
    pub label: String,
    pub address: String,
}

#[derive(Serialize, Clone)]
pub struct PingResult {
    pub label: String,
    pub address: String,
    pub ok: bool,
    pub latency_ms: Option<u32>,
}

pub fn ping_hosts(hosts: Vec<PingHost>) -> Vec<PingResult> {
    use std::sync::{Arc, Mutex};
    use std::thread;

    let results: Arc<Mutex<Vec<(usize, PingResult)>>> = Arc::new(Mutex::new(Vec::new()));

    let handles: Vec<_> = hosts
        .into_iter()
        .enumerate()
        .map(|(i, host)| {
            let results = Arc::clone(&results);
            thread::spawn(move || {
                let latency = do_ping(&host.address);
                let r = PingResult {
                    label: host.label,
                    address: host.address,
                    ok: latency.is_some(),
                    latency_ms: latency,
                };
                results
                    .lock()
                    .unwrap_or_else(|e| e.into_inner())
                    .push((i, r));
            })
        })
        .collect();

    for h in handles {
        let _ = h.join();
    }

    let mut pairs = results
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .drain(..)
        .collect::<Vec<_>>();
    pairs.sort_by_key(|(i, _)| *i);
    pairs.into_iter().map(|(_, r)| r).collect()
}

/// Validate a host string before passing it to `ping.exe`.
///
/// `ping.exe` treats arguments beginning with `/` or `-` as flags (e.g. `/t`
/// makes it ping forever, hanging the worker thread). To prevent argument
/// injection / DoS we accept only plausible hostnames and IP addresses:
/// non-empty, no leading `/` or `-`, no whitespace, and every char restricted
/// to `[A-Za-z0-9.:_-]` (`:` for IPv6, `.`/`-` for hostnames).
fn is_valid_ping_host(host: &str) -> bool {
    if host.is_empty() {
        return false;
    }
    if host.starts_with('/') || host.starts_with('-') {
        return false;
    }
    host.chars().all(|c| {
        c.is_ascii_alphanumeric() || matches!(c, '.' | ':' | '_' | '-')
    })
}

#[cfg(target_os = "windows")]
fn do_ping(host: &str) -> Option<u32> {
    if !is_valid_ping_host(host) {
        return None;
    }
    let output = {
        use std::os::windows::process::CommandExt;
        let mut cmd = std::process::Command::new("ping");
        cmd.args(["/n", "1", "/w", "2000", host]);
        cmd.creation_flags(0x08000000); // CREATE_NO_WINDOW
        cmd.output().ok()?
    };

    let stdout = String::from_utf8_lossy(&output.stdout);

    for line in stdout.lines() {
        // English: "Average = Xms"
        if let Some(pos) = line.find("Average = ") {
            let rest = &line[pos + 10..];
            let ms_str: String = rest.chars().take_while(|c| c.is_ascii_digit()).collect();
            if let Ok(ms) = ms_str.parse::<u32>() {
                return Some(ms);
            }
        }
        // Fallback for other Windows locales: look for a number before "ms"
        if line.contains("ms") && (line.contains("=") || line.contains("<")) {
            let digits: String = line
                .split_whitespace()
                .find(|w| w.ends_with("ms") && w[..w.len() - 2].parse::<u32>().is_ok())
                .map(|w| w[..w.len() - 2].to_string())
                .unwrap_or_default();
            if let Ok(ms) = digits.parse::<u32>() {
                return Some(ms);
            }
        }
    }
    None
}

#[cfg(not(target_os = "windows"))]
fn do_ping(host: &str) -> Option<u32> {
    if !is_valid_ping_host(host) {
        return None;
    }
    None
}

#[cfg(test)]
mod tests {
    use super::is_valid_ping_host;

    #[test]
    fn rejects_ping_flag_injection_and_garbage() {
        // Leading slash/dash become ping.exe flags (e.g. /t pings forever).
        assert!(!is_valid_ping_host("/t"));
        assert!(!is_valid_ping_host("-t"));
        // Whitespace / multiple args / shell metacharacters.
        assert!(!is_valid_ping_host(" "));
        assert!(!is_valid_ping_host("a b"));
        assert!(!is_valid_ping_host("8.8.8.8;evil"));
        // Empty.
        assert!(!is_valid_ping_host(""));
    }

    #[test]
    fn accepts_plausible_hosts() {
        assert!(is_valid_ping_host("8.8.8.8"));
        assert!(is_valid_ping_host("example.com"));
        assert!(is_valid_ping_host("fe80::1"));
    }
}

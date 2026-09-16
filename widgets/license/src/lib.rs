use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

const GRACE_PERIOD_SECS: u64 = 7 * 24 * 60 * 60; // 7 days

/// Lemon Squeezy calls are slow-path and few; 30s matches the email crate.
const LICENSE_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(30);

/// Shared client honoring the Windows proxy (netproxy SPEC [PX-01]).
///
/// Every call here previously built a bare `reqwest::Client::new()`, which had
/// no timeout at all - and `validate_on_launch` runs on every start, so a hung
/// Lemon Squeezy socket was unbounded. One pooled client, one timeout, one
/// proxy decision.
fn http_client() -> reqwest::Client {
    use std::sync::OnceLock;
    static CLIENT: OnceLock<reqwest::Client> = OnceLock::new();
    CLIENT
        .get_or_init(|| {
            widget_netproxy::client_builder(LICENSE_TIMEOUT)
                .build()
                // A builder failure here means the TLS backend did not
                // initialize; a bare client would fail the same request anyway,
                // so fall back rather than panic in a #[tauri::command] path.
                .unwrap_or_else(|e| {
                    log::error!("license: HTTP client build failed ({e}); no proxy support");
                    reqwest::Client::new()
                })
        })
        .clone()
}

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

#[derive(Serialize, Deserialize, Clone, Debug)]
pub struct LicenseStatus {
    pub tier: String,
    pub activated: bool,
    pub key: Option<String>,
    pub instance_id: Option<String>,
    pub valid_until: Option<String>,
    pub error: Option<String>,
}

impl LicenseStatus {
    pub fn free() -> Self {
        Self {
            tier: "free".into(),
            activated: false,
            key: None,
            instance_id: None,
            valid_until: None,
            error: None,
        }
    }

    pub fn free_with_error(err: String) -> Self {
        Self {
            tier: "free".into(),
            activated: false,
            key: None,
            instance_id: None,
            valid_until: None,
            error: Some(err),
        }
    }

    pub fn pro(key: &str, instance_id: &str) -> Self {
        Self {
            tier: "pro".into(),
            activated: true,
            key: Some(mask_key(key)),
            instance_id: Some(instance_id.to_string()),
            valid_until: None,
            error: None,
        }
    }
}

fn mask_key(key: &str) -> String {
    if key.len() <= 8 {
        return key.to_string();
    }
    // Take the last 4 chars (not bytes): byte-slicing `&key[key.len()-4..]`
    // panics if the boundary falls inside a multi-byte UTF-8 sequence. For the
    // normal ASCII key case this is identical to the previous behavior.
    let suffix: String = {
        let chars: Vec<char> = key.chars().collect();
        let start = chars.len().saturating_sub(4);
        chars[start..].iter().collect()
    };
    format!("****-****-****-{}", suffix)
}

// ---------------------------------------------------------------------------
// Cached license (persisted to disk, encrypted)
// ---------------------------------------------------------------------------

#[derive(Serialize, Deserialize)]
struct CachedLicense {
    key: String,
    instance_id: String,
    machine_id: String,
    last_validated: u64,
}

// ---------------------------------------------------------------------------
// Machine fingerprint
// ---------------------------------------------------------------------------

#[cfg(target_os = "windows")]
pub fn machine_fingerprint() -> Result<String, String> {
    use std::ffi::OsString;
    use std::os::windows::ffi::OsStringExt;
    use winapi::um::winnt::{KEY_READ, REG_SZ};
    use winapi::um::winreg::{RegOpenKeyExW, RegQueryValueExW, HKEY_LOCAL_MACHINE};

    unsafe {
        let subkey: Vec<u16> = "SOFTWARE\\Microsoft\\Cryptography\0"
            .encode_utf16()
            .collect();
        let value_name: Vec<u16> = "MachineGuid\0".encode_utf16().collect();

        let mut hkey = std::ptr::null_mut();
        let res = RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey.as_ptr(), 0, KEY_READ, &mut hkey);
        if res != 0 {
            return Err("Failed to open registry key for MachineGuid".into());
        }

        let mut buf: [u16; 256] = [0; 256];
        let mut buf_len: u32 = (buf.len() * 2) as u32;
        let mut reg_type: u32 = 0;
        let res = RegQueryValueExW(
            hkey,
            value_name.as_ptr(),
            std::ptr::null_mut(),
            &mut reg_type,
            buf.as_mut_ptr() as *mut u8,
            &mut buf_len,
        );
        winapi::um::winreg::RegCloseKey(hkey);

        if res != 0 || reg_type != REG_SZ {
            return Err("Failed to read MachineGuid from registry".into());
        }

        let len = (buf_len as usize / 2).saturating_sub(1); // strip null terminator
        let guid = OsString::from_wide(&buf[..len])
            .to_string_lossy()
            .to_string();

        let username = std::env::var("USERNAME").unwrap_or_default();
        let input = format!("{}:{}", guid, username);
        let hash = Sha256::digest(input.as_bytes());
        Ok(hex::encode(hash))
    }
}

#[cfg(not(target_os = "windows"))]
pub fn machine_fingerprint() -> Result<String, String> {
    let hostname = std::env::var("HOSTNAME").unwrap_or_else(|_| "unknown".into());
    let username = std::env::var("USER").unwrap_or_default();
    let input = format!("{}:{}", hostname, username);
    let hash = Sha256::digest(input.as_bytes());
    Ok(hex::encode(hash))
}

// ---------------------------------------------------------------------------
// Cache path & encryption
// ---------------------------------------------------------------------------

fn cache_dir() -> PathBuf {
    let appdata = std::env::var("APPDATA").unwrap_or_else(|_| ".".into());
    PathBuf::from(appdata).join("com.themia.desktop")
}

fn cache_path() -> PathBuf {
    cache_dir().join("license.enc")
}

fn derive_key() -> Result<[u8; 32], String> {
    let fp = machine_fingerprint()?;
    let hash = Sha256::digest(fp.as_bytes());
    let mut key = [0u8; 32];
    key.copy_from_slice(&hash);
    Ok(key)
}

fn save_cache(data: &CachedLicense) -> Result<(), String> {
    use aes_gcm::{aead::Aead, Aes256Gcm, KeyInit, Nonce};
    use rand::RngCore;

    let key_bytes = derive_key()?;
    let cipher = Aes256Gcm::new_from_slice(&key_bytes).map_err(|e| e.to_string())?;

    let mut nonce_bytes = [0u8; 12];
    rand::thread_rng().fill_bytes(&mut nonce_bytes);
    let nonce = Nonce::from_slice(&nonce_bytes);

    let plaintext = serde_json::to_vec(data).map_err(|e| e.to_string())?;
    let ciphertext = cipher
        .encrypt(nonce, plaintext.as_ref())
        .map_err(|e| e.to_string())?;

    // Format: nonce (12 bytes) || ciphertext
    let mut out = Vec::with_capacity(12 + ciphertext.len());
    out.extend_from_slice(&nonce_bytes);
    out.extend_from_slice(&ciphertext);

    let dir = cache_dir();
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    std::fs::write(cache_path(), &out).map_err(|e| e.to_string())?;
    Ok(())
}

fn load_cache() -> Option<CachedLicense> {
    use aes_gcm::{aead::Aead, Aes256Gcm, KeyInit, Nonce};

    let data = std::fs::read(cache_path()).ok()?;
    if data.len() < 13 {
        return None;
    }

    let key_bytes = derive_key().ok()?;
    let cipher = Aes256Gcm::new_from_slice(&key_bytes).ok()?;
    let nonce = Nonce::from_slice(&data[..12]);
    let plaintext = cipher.decrypt(nonce, &data[12..]).ok()?;
    serde_json::from_slice(&plaintext).ok()
}

fn delete_cache() -> Result<(), String> {
    let path = cache_path();
    if path.exists() {
        std::fs::remove_file(&path).map_err(|e| e.to_string())?;
    }
    Ok(())
}

fn now_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs()
}

// ---------------------------------------------------------------------------
// LemonSqueezy API
// ---------------------------------------------------------------------------

#[derive(Deserialize)]
#[allow(dead_code)]
struct LsActivateResponse {
    activated: bool,
    instance: Option<LsInstance>,
    error: Option<String>,
    license_key: Option<LsLicenseKey>,
}

#[derive(Deserialize)]
#[allow(dead_code)]
struct LsValidateResponse {
    valid: bool,
    error: Option<String>,
    license_key: Option<LsLicenseKey>,
}

#[derive(Deserialize)]
#[allow(dead_code)]
struct LsDeactivateResponse {
    deactivated: bool,
    error: Option<String>,
}

#[derive(Deserialize)]
struct LsInstance {
    id: String,
}

#[derive(Deserialize)]
#[allow(dead_code)]
struct LsLicenseKey {
    status: String,
}

// Dev bypass key: remove before release
const DEV_BYPASS_KEY: &str = "THEMIA-DEV-PRO";

pub async fn activate_key(key: &str, machine_id: &str) -> Result<LicenseStatus, String> {
    if key == DEV_BYPASS_KEY {
        let cached = CachedLicense {
            key: key.to_string(),
            instance_id: "dev-bypass".to_string(),
            machine_id: machine_id.to_string(),
            last_validated: now_secs(),
        };
        save_cache(&cached)?;
        return Ok(LicenseStatus::pro(key, "dev-bypass"));
    }

    let client = http_client();
    let resp = client
        .post("https://api.lemonsqueezy.com/v1/licenses/activate")
        .json(&serde_json::json!({
            "license_key": key,
            "instance_name": machine_id,
        }))
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;

    let body: LsActivateResponse = resp
        .json()
        .await
        .map_err(|e| format!("Parse error: {}", e))?;

    if !body.activated {
        return Err(body.error.unwrap_or_else(|| "Activation failed".into()));
    }

    let instance_id = body.instance.ok_or("No instance returned")?.id;

    let cached = CachedLicense {
        key: key.to_string(),
        instance_id: instance_id.clone(),
        machine_id: machine_id.to_string(),
        last_validated: now_secs(),
    };
    save_cache(&cached)?;

    Ok(LicenseStatus::pro(key, &instance_id))
}

pub async fn validate_key(key: &str, instance_id: &str) -> Result<bool, String> {
    if key == DEV_BYPASS_KEY {
        return Ok(true);
    }

    let client = http_client();
    let resp = client
        .post("https://api.lemonsqueezy.com/v1/licenses/validate")
        .json(&serde_json::json!({
            "license_key": key,
            "instance_id": instance_id,
        }))
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;

    let body: LsValidateResponse = resp
        .json()
        .await
        .map_err(|e| format!("Parse error: {}", e))?;
    Ok(body.valid)
}

pub async fn deactivate_key(key: &str, instance_id: &str) -> Result<(), String> {
    if key == DEV_BYPASS_KEY {
        delete_cache()?;
        return Ok(());
    }

    let client = http_client();
    let resp = client
        .post("https://api.lemonsqueezy.com/v1/licenses/deactivate")
        .json(&serde_json::json!({
            "license_key": key,
            "instance_id": instance_id,
        }))
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;

    let body: LsDeactivateResponse = resp
        .json()
        .await
        .map_err(|e| format!("Parse error: {}", e))?;

    if !body.deactivated {
        return Err(body.error.unwrap_or_else(|| "Deactivation failed".into()));
    }

    delete_cache()?;
    Ok(())
}

// ---------------------------------------------------------------------------
// Public API for Tauri commands
// ---------------------------------------------------------------------------

/// Synchronous read of cached license state (no network).
pub fn get_status() -> LicenseStatus {
    let cached = match load_cache() {
        Some(c) => c,
        None => return LicenseStatus::free(),
    };

    let fp = match machine_fingerprint() {
        Ok(fp) => fp,
        Err(_) => return LicenseStatus::free(),
    };

    if cached.machine_id != fp {
        return LicenseStatus::free();
    }

    LicenseStatus::pro(&cached.key, &cached.instance_id)
}

/// Full launch validation: try network validate, fall back to offline grace.
pub async fn validate_on_launch() -> LicenseStatus {
    let cached = match load_cache() {
        Some(c) => c,
        None => return LicenseStatus::free(),
    };

    let fp = match machine_fingerprint() {
        Ok(fp) => fp,
        Err(_) => return LicenseStatus::free(),
    };

    if cached.machine_id != fp {
        return LicenseStatus::free();
    }

    // Try network validation
    match validate_key(&cached.key, &cached.instance_id).await {
        Ok(true) => {
            // Update last_validated timestamp
            let updated = CachedLicense {
                last_validated: now_secs(),
                ..cached
            };
            let _ = save_cache(&updated);
            LicenseStatus::pro(&updated.key, &updated.instance_id)
        }
        Ok(false) => {
            // Key is no longer valid (expired, refunded, etc.)
            let _ = delete_cache();
            LicenseStatus::free_with_error("License is no longer valid".into())
        }
        Err(_) => {
            // Network error: check offline grace period
            let elapsed = now_secs().saturating_sub(cached.last_validated);
            if elapsed <= GRACE_PERIOD_SECS {
                LicenseStatus::pro(&cached.key, &cached.instance_id)
            } else {
                LicenseStatus::free_with_error(
                    "License validation failed: offline grace period expired".into(),
                )
            }
        }
    }
}

/// Deactivate the currently cached license.
pub async fn deactivate_current() -> Result<(), String> {
    let cached = load_cache().ok_or("No active license")?;
    deactivate_key(&cached.key, &cached.instance_id).await
}

/// Return the raw (unmasked) cached license key, if the cache is valid for
/// this machine. The regular LicenseStatus masks the key so it never appears
/// in logs; this command is only used when the user explicitly asks to
/// reveal or copy the full key from the Settings UI.
pub fn get_raw_key() -> Option<String> {
    let cached = load_cache()?;
    let fp = machine_fingerprint().ok()?;
    if cached.machine_id != fp {
        return None;
    }
    Some(cached.key)
}

// ---------------------------------------------------------------------------
// Newsletter signup (Lemon Squeezy public email-subscribe endpoint)
// ---------------------------------------------------------------------------
//
// Same endpoint the themia.app website footer form uses. Public, no API key
// required. Form-urlencoded body with `email` (and optional `name`).

const NEWSLETTER_ENDPOINT: &str = "https://themia.lemonsqueezy.com/email-subscribe/external";

pub async fn subscribe_newsletter(email: &str) -> Result<(), String> {
    let client = http_client();
    let resp = client
        .post(NEWSLETTER_ENDPOINT)
        .form(&[("email", email)])
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;

    if resp.status().is_success() {
        Ok(())
    } else {
        Err(format!("Subscription failed ({})", resp.status()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mask_key_short_returns_verbatim() {
        assert_eq!(mask_key("ABCD"), "ABCD");
        assert_eq!(mask_key("12345678"), "12345678"); // len == 8 boundary
    }

    #[test]
    fn mask_key_ascii_masks_last_four() {
        assert_eq!(mask_key("ABCDEFGHIJKL"), "****-****-****-IJKL");
    }

    #[test]
    fn mask_key_multibyte_does_not_panic() {
        // A multi-byte UTF-8 suffix would make naive byte-slicing
        // `&key[key.len()-4..]` panic on a non-char boundary. This must not
        // panic and must yield a sensible (last-4-chars) mask.
        let key = "key-with-émojis-🔑🔑🔑";
        let masked = mask_key(key); // must not panic
        assert!(masked.starts_with("****-****-****-"));
        assert!(masked.ends_with("🔑🔑🔑"));
    }
}

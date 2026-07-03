// Native HMAC attestation for app-only video playback.
//
// The backend issues a short-lived nonce challenge; this command signs
// "{nonce}|{videoId}|{userId}|tauri|v1" with HMAC-SHA256 and returns the
// signature as lowercase hex. The secret is embedded at COMPILE time from the
// OET_DESKTOP_ATTEST_SECRET env var (a CI secret) via `option_env!`, so it
// lives only in the native binary — it never crosses the IPC boundary and is
// not readable from JS. Granting this command to the remote origin is safe by
// design: the page can only obtain signatures over server-issued nonces
// (single-use, server-verified), never the key itself.

use std::collections::VecDeque;
use std::fmt::Write as _;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use hmac::{Hmac, Mac};
use serde_json::{json, Value};
use sha2::Sha256;

type HmacSha256 = Hmac<Sha256>;

const PLATFORM: &str = "tauri";
const KEY_ID: &str = "v1";
const MAX_INPUT_CHARS: usize = 512;
const DEV_FALLBACK_SECRET: &str = "dev-attestation-secret-not-for-production";

// Resolved at compile time; release builds without the CI secret refuse to sign
// (see resolve_secret) instead of silently falling back to the dev constant.
const EMBEDDED_SECRET: Option<&str> = option_env!("OET_DESKTOP_ATTEST_SECRET");

// ── per-process rate guard ──────────────────────────────────────────

const RATE_MAX_CALLS: usize = 30;
const RATE_WINDOW: Duration = Duration::from_secs(60);

static RECENT_CALLS: Mutex<VecDeque<Instant>> = Mutex::new(VecDeque::new());

// Sliding-window limiter over an explicit queue so the policy is unit-testable
// without touching the process-global state.
fn rate_check(calls: &mut VecDeque<Instant>, now: Instant) -> Result<(), String> {
    while calls
        .front()
        .is_some_and(|t| now.duration_since(*t) >= RATE_WINDOW)
    {
        calls.pop_front();
    }
    if calls.len() >= RATE_MAX_CALLS {
        return Err("Attestation rate limit exceeded; try again shortly.".into());
    }
    calls.push_back(now);
    Ok(())
}

// ── signing ─────────────────────────────────────────────────────────

fn resolve_secret() -> Result<&'static str, String> {
    match EMBEDDED_SECRET {
        Some(secret) if !secret.trim().is_empty() => Ok(secret),
        // Dev fallback ONLY for debug builds; a release build without the CI
        // secret must fail loudly rather than sign with a public constant.
        _ if cfg!(debug_assertions) => Ok(DEV_FALLBACK_SECRET),
        _ => Err("attestation secret not embedded".into()),
    }
}

fn validate_input(label: &str, value: &str) -> Result<(), String> {
    if value.trim().is_empty() {
        return Err(format!("A non-empty {label} is required."));
    }
    if value.chars().count() >= MAX_INPUT_CHARS {
        return Err(format!("{label} exceeds the maximum length."));
    }
    Ok(())
}

// Message grammar is a pinned cross-platform contract (backend + web FE are
// built against it): "{nonce}|{videoId}|{userId}|{platform}|{keyId}".
fn build_message(nonce: &str, video_id: &str, user_id: &str) -> String {
    format!("{nonce}|{video_id}|{user_id}|{PLATFORM}|{KEY_ID}")
}

fn compute_signature_hex(secret: &str, message: &str) -> String {
    let mut mac =
        HmacSha256::new_from_slice(secret.as_bytes()).expect("HMAC accepts keys of any length");
    mac.update(message.as_bytes());
    let bytes = mac.finalize().into_bytes();
    let mut out = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        let _ = write!(out, "{byte:02x}");
    }
    out
}

#[tauri::command]
pub fn sign_video_challenge(
    nonce: String,
    video_id: String,
    user_id: String,
) -> Result<Value, String> {
    validate_input("nonce", &nonce)?;
    validate_input("videoId", &video_id)?;
    validate_input("userId", &user_id)?;

    {
        let mut calls = RECENT_CALLS
            .lock()
            .map_err(|_| "attestation rate guard unavailable".to_string())?;
        rate_check(&mut calls, Instant::now())?;
    }

    let secret = resolve_secret()?;
    let signature = compute_signature_hex(secret, &build_message(&nonce, &video_id, &user_id));

    Ok(json!({
        "signature": signature,
        "platform": PLATFORM,
        "keyId": KEY_ID,
        "appVersion": env!("CARGO_PKG_VERSION"),
    }))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_hmac_vector_lowercase_hex() {
        // Independently computed (node: crypto.createHmac('sha256',
        // 'test-secret-key').update('nonce-123|video-456|user-789|tauri|v1'))
        // — pins the message grammar, platform/keyId constants, and the
        // lowercase-hex encoding all at once.
        let message = build_message("nonce-123", "video-456", "user-789");
        assert_eq!(message, "nonce-123|video-456|user-789|tauri|v1");
        assert_eq!(
            compute_signature_hex("test-secret-key", &message),
            "3b81eee90b0d328cb4bf2ea826f88dc6e928ce7af343d7c3e8d837bcba98746c"
        );
    }

    #[test]
    fn dev_fallback_secret_vector() {
        assert_eq!(
            compute_signature_hex(DEV_FALLBACK_SECRET, &build_message("n", "v", "u")),
            "d6cfd041f1d9af83c5e8531948b0684818781a93369b5388476f2768698f4a6e"
        );
    }

    #[test]
    fn rejects_empty_and_oversized_inputs() {
        assert!(sign_video_challenge("".into(), "v".into(), "u".into()).is_err());
        assert!(sign_video_challenge("   ".into(), "v".into(), "u".into()).is_err());
        assert!(sign_video_challenge("n".into(), "".into(), "u".into()).is_err());
        assert!(sign_video_challenge("n".into(), "v".into(), "".into()).is_err());
        let long = "a".repeat(MAX_INPUT_CHARS);
        assert!(sign_video_challenge(long.clone(), "v".into(), "u".into()).is_err());
        assert!(sign_video_challenge("n".into(), long.clone(), "u".into()).is_err());
        assert!(sign_video_challenge("n".into(), "v".into(), long).is_err());
    }

    #[test]
    fn signs_and_returns_contract_shape() {
        // Test builds are debug builds, so resolve_secret succeeds (embedded CI
        // secret or dev fallback); assert the pinned response shape either way.
        let value = sign_video_challenge("n1".into(), "v1".into(), "u1".into()).expect("signs");
        assert_eq!(value["platform"], "tauri");
        assert_eq!(value["keyId"], "v1");
        assert_eq!(value["appVersion"], env!("CARGO_PKG_VERSION"));
        let signature = value["signature"].as_str().expect("signature string");
        assert_eq!(signature.len(), 64);
        assert!(signature
            .chars()
            .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase()));
    }

    #[test]
    fn rate_guard_blocks_flood_and_recovers() {
        let mut calls = VecDeque::new();
        let start = Instant::now();
        for _ in 0..RATE_MAX_CALLS {
            assert!(rate_check(&mut calls, start).is_ok());
        }
        assert!(rate_check(&mut calls, start).is_err());
        // Once the window passes, old entries expire and calls succeed again.
        let later = start + RATE_WINDOW + Duration::from_secs(1);
        assert!(rate_check(&mut calls, later).is_ok());
    }
}

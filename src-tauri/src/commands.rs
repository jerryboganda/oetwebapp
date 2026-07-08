// Tauri command implementations for the `window.desktopBridge` contract
// (types/desktop.d.ts). Response shapes match the contract byte-for-byte so the
// frontend consumers need zero changes. In the remote-only shell these are
// granted narrowly (see capabilities/) — only `runtime_info` is exposed to the
// remote origin; the rest stay registered for localhost/dev and future use.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Mutex;

use base64::Engine;
use serde::Serialize;
use serde_json::{json, Value};
use tauri::{AppHandle, Manager, State};
use tauri_plugin_notification::NotificationExt;

use crate::runtime::RuntimeState;

// Sanitizes a cache key / session id: alphanumerics plus `.`, `_`, `-`; every
// other char (including path separators) becomes `_`, then capped to max_len.
fn sanitize_component(value: &str, max_len: usize) -> Result<String, String> {
    if value.trim().is_empty() {
        return Err("A valid key is required.".into());
    }
    let cleaned: String = value
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '.' || c == '_' || c == '-' {
                c
            } else {
                '_'
            }
        })
        .collect();
    Ok(cleaned.chars().take(max_len).collect())
}

fn user_data_dir(app: &AppHandle) -> PathBuf {
    app.path().app_data_dir().expect("app_data_dir unavailable")
}

// ── runtime ─────────────────────────────────────────────────────────

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct WindowStateSnapshot {
    pub is_focused: bool,
    pub is_visible: bool,
    pub is_minimized: bool,
    pub is_maximized: bool,
    pub is_full_screen: bool,
}

pub fn window_state_snapshot(app: &AppHandle) -> WindowStateSnapshot {
    match app.get_webview_window("main") {
        Some(win) => WindowStateSnapshot {
            is_focused: win.is_focused().unwrap_or(false),
            is_visible: win.is_visible().unwrap_or(false),
            is_minimized: win.is_minimized().unwrap_or(false),
            is_maximized: win.is_maximized().unwrap_or(false),
            is_full_screen: win.is_fullscreen().unwrap_or(false),
        },
        None => WindowStateSnapshot {
            is_focused: false,
            is_visible: false,
            is_minimized: false,
            is_maximized: false,
            is_full_screen: false,
        },
    }
}

#[tauri::command]
pub fn runtime_info(app: AppHandle, state: State<'_, RuntimeState>) -> Value {
    json!({
        "isPackaged": !tauri::is_dev(),
        "activeBackendUrl": *state.active_backend_url.lock().unwrap(),
        "ignoredPackagedLoopbackApiTarget": Value::Null,
        // Surfaced so the web app can read the installed shell version and drive
        // the forced-update gate (client-version.ts) without a privileged call.
        "appVersion": env!("CARGO_PKG_VERSION"),
        "windowState": window_state_snapshot(&app),
    })
}

// ── updater + hard reload ───────────────────────────────────────────

// Pushes an updater lifecycle event into the page as a CustomEvent (same
// pattern as emit_window_state) so the remote origin needs no extra capability.
// `detail` carries { phase, progress?, version?, currentVersion?, notes?, error? }.
fn emit_update_event(app: &AppHandle, detail: &Value) {
    if let Some(win) = app.get_webview_window("main") {
        if let Ok(detail_str) = serde_json::to_string(detail) {
            let _ = win.eval(format!(
                "window.dispatchEvent(new CustomEvent('desktop:update-progress', {{ detail: {detail_str} }}))"
            ));
        }
    }
}

fn build_updater(app: &AppHandle) -> Result<tauri_plugin_updater::Updater, String> {
    use tauri_plugin_updater::UpdaterExt;
    // Mirror the startup check: OET_UPDATER_URL overrides the endpoint for tests.
    let builder = match std::env::var("OET_UPDATER_URL") {
        Ok(url) => {
            let parsed = url
                .parse()
                .map_err(|e| format!("invalid OET_UPDATER_URL: {e}"))?;
            app.updater_builder()
                .endpoints(vec![parsed])
                .map_err(|e| e.to_string())?
        }
        Err(_) => app.updater_builder(),
    };
    builder.build().map_err(|e| e.to_string())
}

/// Checks for an available update WITHOUT installing. Returns
/// `{ available, version?, currentVersion, notes? }`.
#[tauri::command]
pub async fn updater_check(app: AppHandle) -> Result<Value, String> {
    let updater = build_updater(&app)?;
    match updater.check().await {
        Ok(Some(update)) => Ok(json!({
            "available": true,
            "version": update.version,
            "currentVersion": update.current_version,
            "notes": update.body,
        })),
        Ok(None) => Ok(json!({
            "available": false,
            "currentVersion": env!("CARGO_PKG_VERSION"),
        })),
        Err(e) => Err(e.to_string()),
    }
}

/// Downloads, verifies (minisign), and stages the update. Emits
/// `desktop:update-progress` events (downloading → installing → ready|error).
/// Does NOT relaunch — the page calls `app_relaunch` afterward.
#[tauri::command]
pub async fn updater_install(app: AppHandle) -> Result<Value, String> {
    let updater = build_updater(&app)?;
    let update = match updater.check().await {
        Ok(Some(update)) => update,
        Ok(None) => return Ok(json!({ "ok": false, "error": "No update available." })),
        Err(e) => return Err(e.to_string()),
    };

    let downloaded = std::sync::Arc::new(std::sync::atomic::AtomicU64::new(0));
    let app_progress = app.clone();
    let app_finish = app.clone();
    let dl = downloaded.clone();

    let result = update
        .download_and_install(
            move |chunk_len, content_len| {
                let received = dl.fetch_add(chunk_len as u64, std::sync::atomic::Ordering::Relaxed)
                    + chunk_len as u64;
                let progress = match content_len {
                    Some(total) if total > 0 => {
                        ((received as f64 / total as f64) * 100.0).min(100.0) as u64
                    }
                    _ => 0,
                };
                emit_update_event(
                    &app_progress,
                    &json!({ "phase": "downloading", "progress": progress }),
                );
            },
            move || {
                emit_update_event(&app_finish, &json!({ "phase": "installing" }));
            },
        )
        .await;

    match result {
        Ok(()) => {
            emit_update_event(&app, &json!({ "phase": "ready" }));
            Ok(json!({ "ok": true }))
        }
        Err(e) => {
            emit_update_event(&app, &json!({ "phase": "error", "error": e.to_string() }));
            Err(e.to_string())
        }
    }
}

/// Relaunches the app into the freshly installed version. Split from
/// `updater_install` so the page controls the restart moment.
#[tauri::command]
pub fn app_relaunch(app: AppHandle) {
    app.restart();
}

/// Ctrl+F5 equivalent: clears the WebView's browsing data (cache/storage) then
/// re-navigates to the trusted remote origin, so everything is re-fetched fresh.
#[tauri::command]
pub fn hard_reload(app: AppHandle, state: State<'_, RuntimeState>) -> Result<(), String> {
    let win = app
        .get_webview_window("main")
        .ok_or_else(|| "main window unavailable".to_string())?;
    win.clear_all_browsing_data().map_err(|e| e.to_string())?;
    let base = state
        .renderer_url
        .lock()
        .unwrap()
        .clone()
        .ok_or_else(|| "remote url unavailable".to_string())?;
    let url = base
        .parse::<tauri::Url>()
        .map_err(|e| format!("invalid remote url: {e}"))?;
    win.navigate(url).map_err(|e| e.to_string())?;
    Ok(())
}

// ── open external ───────────────────────────────────────────────────

// Validates an external URL is a non-empty http(s) URL and returns the trimmed
// value. Extracted so the scheme/empty checks are unit-testable without actually
// opening a browser.
fn validate_external_url(url: &str) -> Result<&str, String> {
    let trimmed = url.trim();
    if trimmed.is_empty() {
        return Err("A valid URL is required.".into());
    }
    if !(trimmed.starts_with("http://") || trimmed.starts_with("https://")) {
        return Err("Only http and https URLs can be opened externally.".into());
    }
    Ok(trimmed)
}

#[tauri::command]
pub fn open_external(_app: AppHandle, url: String) -> Result<bool, String> {
    let trimmed = validate_external_url(&url)?;
    tauri_plugin_opener::open_url(trimmed, None::<&str>).map_err(|e| e.to_string())?;
    Ok(true)
}

// ── secure secrets (keyring: Credential Manager / Keychain) ─────────

fn keyring_entry(namespace: &str, key: &str) -> Result<keyring::Entry, String> {
    let ns = sanitize_component(namespace, 64)?;
    let k = sanitize_component(key, 128)?;
    keyring::Entry::new(&format!("com.oetprep.desktop/{ns}"), &k).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn secret_get(namespace: Option<String>, key: String) -> Result<Option<String>, String> {
    let entry = keyring_entry(&namespace.unwrap_or_else(|| "default".into()), &key)?;
    match entry.get_password() {
        Ok(v) => Ok(Some(v)),
        Err(keyring::Error::NoEntry) => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

#[tauri::command]
pub fn secret_set(namespace: Option<String>, key: String, value: String) -> Result<bool, String> {
    let entry = keyring_entry(&namespace.unwrap_or_else(|| "default".into()), &key)?;
    entry.set_password(&value).map_err(|e| e.to_string())?;
    Ok(true)
}

#[tauri::command]
pub fn secret_delete(namespace: Option<String>, key: String) -> Result<bool, String> {
    let entry = keyring_entry(&namespace.unwrap_or_else(|| "default".into()), &key)?;
    match entry.delete_credential() {
        Ok(()) => Ok(true),
        Err(keyring::Error::NoEntry) => Ok(true),
        Err(e) => Err(e.to_string()),
    }
}

#[tauri::command]
pub fn secret_status() -> Value {
    // keyring talks to the native store directly — no vault file exists, so
    // the weak-backend flags are always false and vaultPath is empty.
    let backend = if cfg!(windows) {
        "windows-credential-manager"
    } else if cfg!(target_os = "macos") {
        "macos-keychain"
    } else {
        "secret-service"
    };
    json!({
        "available": true,
        "backend": backend,
        "usingWeakBackend": false,
        "allowWeakBackend": false,
        "ready": true,
        "vaultPath": "",
    })
}

// ── offline cache (JSON files under userData/offline-content) ──

fn offline_cache_dir(app: &AppHandle) -> PathBuf {
    user_data_dir(app).join("offline-content")
}

#[tauri::command]
pub fn offline_cache_store(app: AppHandle, key: String, data: Value) -> Result<Value, String> {
    if data.is_null() {
        return Err("Data is required for cache storage.".into());
    }
    let dir = offline_cache_dir(&app);
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let key = sanitize_component(&key, 256)?;
    let now_ms = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_err(|e| e.to_string())?
        .as_millis() as u64;
    let payload = json!({ "cachedAt": now_ms, "data": data });
    std::fs::write(
        dir.join(format!("{key}.json")),
        serde_json::to_string(&payload).unwrap(),
    )
    .map_err(|e| e.to_string())?;
    Ok(json!({ "success": true, "key": key }))
}

#[tauri::command]
pub fn offline_cache_get(app: AppHandle, key: String) -> Result<Value, String> {
    let key = sanitize_component(&key, 256)?;
    let path = offline_cache_dir(&app).join(format!("{key}.json"));
    match std::fs::read_to_string(path) {
        Ok(raw) => serde_json::from_str(&raw).map_err(|e| e.to_string()),
        Err(_) => Ok(Value::Null),
    }
}

#[tauri::command]
pub fn offline_cache_delete(app: AppHandle, key: String) -> Result<Value, String> {
    let key = sanitize_component(&key, 256)?;
    let path = offline_cache_dir(&app).join(format!("{key}.json"));
    if path.exists() {
        std::fs::remove_file(&path).map_err(|e| e.to_string())?;
    }
    Ok(json!({ "success": true, "key": key }))
}

#[tauri::command]
pub fn offline_cache_list(app: AppHandle) -> Result<Vec<Value>, String> {
    let dir = offline_cache_dir(&app);
    let mut out = Vec::new();
    let Ok(entries) = std::fs::read_dir(&dir) else {
        return Ok(out);
    };
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().to_string();
        if let Some(key) = name.strip_suffix(".json") {
            if let Ok(meta) = entry.metadata() {
                let modified_ms = meta
                    .modified()
                    .ok()
                    .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                    .map(|d| d.as_millis() as u64)
                    .unwrap_or(0);
                out.push(json!({ "key": key, "sizeBytes": meta.len(), "modifiedAt": modified_ms }));
            }
        }
    }
    Ok(out)
}

#[tauri::command]
pub fn offline_cache_clear(app: AppHandle) -> Result<Value, String> {
    let dir = offline_cache_dir(&app);
    let mut cleared = 0u64;
    if let Ok(entries) = std::fs::read_dir(&dir) {
        for entry in entries.flatten() {
            if entry.file_name().to_string_lossy().ends_with(".json")
                && std::fs::remove_file(entry.path()).is_ok()
            {
                cleared += 1;
            }
        }
    }
    Ok(json!({ "success": true, "cleared": cleared }))
}

// ── notifications ───────────────────────────────────────────────────

#[tauri::command]
pub fn show_notification(
    app: AppHandle,
    title: String,
    body: String,
    route: Option<String>,
) -> Value {
    // Click-to-route parity is best-effort: notification click activation is
    // not uniformly delivered by OS notification centers; the in-app
    // notification center remains the primary surface.
    let _ = route;
    let result = app
        .notification()
        .builder()
        .title(&title)
        .body(&body)
        .show();
    json!({ "ok": result.is_ok() })
}

// ── dropped file info ───────────────────────────────────────────────

#[tauri::command]
pub fn get_dropped_file_info(file_path: String) -> Value {
    let path = PathBuf::from(&file_path);
    match std::fs::metadata(&path) {
        Ok(meta) if meta.is_file() => {
            let modified_ms = meta
                .modified()
                .ok()
                .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|d| d.as_millis() as u64)
                .unwrap_or(0);
            json!({
                "ok": true,
                "name": path.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default(),
                "size": meta.len(),
                "path": path.to_string_lossy(),
                "lastModified": modified_ms,
            })
        }
        Ok(_) => json!({ "ok": false, "error": "NOT_A_FILE" }),
        Err(_) => json!({ "ok": false, "error": "FILE_NOT_FOUND" }),
    }
}

// ── speaking audio (temp-dir blob persistence, port of main.cjs:1141-1248) ──

pub struct SpeakingSession {
    pub mime_type: String,
    pub started_at: u64,
    pub finalized: bool,
    pub file_path: Option<PathBuf>,
}

#[derive(Default)]
pub struct SpeakingAudioState(pub Mutex<HashMap<String, SpeakingSession>>);

fn speaking_audio_dir() -> PathBuf {
    std::env::temp_dir().join("oet-prep-speaking-audio")
}

fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

// Decodes and concatenates the renderer's base64 audio chunks into one buffer.
// Extracted from speaking_audio_stop so the decode/join is unit-testable.
fn decode_base64_chunks(chunks: &[String]) -> Result<Vec<u8>, String> {
    let mut buffer: Vec<u8> = Vec::new();
    for chunk in chunks {
        let decoded = base64::engine::general_purpose::STANDARD
            .decode(chunk.as_bytes())
            .map_err(|e| e.to_string())?;
        buffer.extend_from_slice(&decoded);
    }
    Ok(buffer)
}

#[tauri::command]
pub fn speaking_audio_start(
    state: State<'_, SpeakingAudioState>,
    session_id: String,
    mime_type: Option<String>,
) -> Result<Value, String> {
    std::fs::create_dir_all(speaking_audio_dir()).map_err(|e| e.to_string())?;
    let session_id = sanitize_component(&session_id, 64)?;
    let mime = mime_type
        .filter(|m| !m.is_empty())
        .unwrap_or_else(|| "audio/webm".into());
    state.0.lock().unwrap().insert(
        session_id.clone(),
        SpeakingSession {
            mime_type: mime.clone(),
            started_at: now_ms(),
            finalized: false,
            file_path: None,
        },
    );
    Ok(json!({ "ok": true, "sessionId": session_id, "mimeType": mime, "mode": "renderer-capture" }))
}

#[tauri::command]
pub fn speaking_audio_stop(
    state: State<'_, SpeakingAudioState>,
    session_id: String,
    chunks_base64: Vec<String>,
) -> Result<Value, String> {
    let session_id = sanitize_component(&session_id, 64)?;
    let mut sessions = state.0.lock().unwrap();
    let Some(session) = sessions.get_mut(&session_id) else {
        return Ok(json!({ "ok": false, "error": "SESSION_NOT_FOUND" }));
    };

    let dir = speaking_audio_dir();
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let buffer = decode_base64_chunks(&chunks_base64)?;

    let file_path = dir.join(format!("{}-{}.bin", session_id, session.started_at));
    std::fs::write(&file_path, &buffer).map_err(|e| e.to_string())?;

    let stopped_at = now_ms();
    session.finalized = true;
    session.file_path = Some(file_path.clone());

    Ok(json!({
        "ok": true,
        "sessionId": session_id,
        "sizeBytes": buffer.len(),
        "mimeType": session.mime_type,
        "filePath": file_path.to_string_lossy(),
        "durationMs": stopped_at.saturating_sub(session.started_at),
    }))
}

#[tauri::command]
pub fn speaking_audio_get_blob(
    state: State<'_, SpeakingAudioState>,
    session_id: String,
) -> Result<Value, String> {
    let session_id = sanitize_component(&session_id, 64)?;
    let sessions = state.0.lock().unwrap();
    let Some(session) = sessions.get(&session_id) else {
        return Ok(json!({ "ok": false, "error": "SESSION_NOT_AVAILABLE" }));
    };
    if !session.finalized {
        return Ok(json!({ "ok": false, "error": "SESSION_NOT_AVAILABLE" }));
    }
    let Some(path) = &session.file_path else {
        return Ok(json!({ "ok": false, "error": "SESSION_NOT_AVAILABLE" }));
    };
    match std::fs::read(path) {
        Ok(data) => Ok(json!({
            "ok": true,
            "sessionId": session_id,
            "mimeType": session.mime_type,
            "sizeBytes": data.len(),
            "dataBase64": base64::engine::general_purpose::STANDARD.encode(&data),
        })),
        Err(e) => Ok(json!({ "ok": false, "error": e.to_string() })),
    }
}

#[tauri::command]
pub fn speaking_audio_discard(
    state: State<'_, SpeakingAudioState>,
    session_id: String,
) -> Result<Value, String> {
    let session_id = sanitize_component(&session_id, 64)?;
    let mut sessions = state.0.lock().unwrap();
    if let Some(session) = sessions.remove(&session_id) {
        if let Some(path) = session.file_path {
            let _ = std::fs::remove_file(path);
        }
    }
    Ok(json!({ "ok": true, "sessionId": session_id }))
}

#[cfg(test)]
mod tests {
    use super::{decode_base64_chunks, sanitize_component, validate_external_url};

    #[test]
    fn sanitize_rejects_empty_and_whitespace() {
        assert!(sanitize_component("", 64).is_err());
        assert!(sanitize_component("   ", 64).is_err());
    }

    #[test]
    fn sanitize_neutralizes_path_traversal_and_separators() {
        // `..` survives but every separator becomes `_`, so no traversal escapes.
        assert_eq!(
            sanitize_component("../etc/passwd", 64).unwrap(),
            ".._etc_passwd"
        );
        assert_eq!(sanitize_component("a/b\\c", 64).unwrap(), "a_b_c");
        assert_eq!(sanitize_component("a:b*c?", 64).unwrap(), "a_b_c_");
    }

    #[test]
    fn sanitize_preserves_allowed_chars() {
        assert_eq!(
            sanitize_component("Key.Name_1-2", 64).unwrap(),
            "Key.Name_1-2"
        );
    }

    #[test]
    fn sanitize_caps_length() {
        let long = "a".repeat(500);
        assert_eq!(sanitize_component(&long, 10).unwrap().len(), 10);
    }

    #[test]
    fn validate_external_url_accepts_http_and_https_and_trims() {
        assert_eq!(
            validate_external_url("https://example.com").unwrap(),
            "https://example.com"
        );
        assert_eq!(
            validate_external_url("  http://x.io/y  ").unwrap(),
            "http://x.io/y"
        );
    }

    #[test]
    fn validate_external_url_rejects_empty_and_other_schemes() {
        assert!(validate_external_url("").is_err());
        assert!(validate_external_url("   ").is_err());
        assert!(validate_external_url("file:///etc/passwd").is_err());
        assert!(validate_external_url("javascript:alert(1)").is_err());
        assert!(validate_external_url("ftp://host/x").is_err());
    }

    #[test]
    fn decode_base64_chunks_joins_in_order() {
        // "QUI=" => b"AB", "Q0Q=" => b"CD"
        let chunks = vec!["QUI=".to_string(), "Q0Q=".to_string()];
        assert_eq!(decode_base64_chunks(&chunks).unwrap(), b"ABCD");
    }

    #[test]
    fn decode_base64_chunks_empty_is_empty() {
        assert_eq!(decode_base64_chunks(&[]).unwrap(), Vec::<u8>::new());
    }

    #[test]
    fn decode_base64_chunks_rejects_invalid() {
        let bad = vec!["!!!not-base64!!!".to_string()];
        assert!(decode_base64_chunks(&bad).is_err());
    }
}

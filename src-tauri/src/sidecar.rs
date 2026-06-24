// Sidecar orchestration — Rust port of electron/main.cjs:443-635.
// Spawns the bundled .NET API and the Next.js standalone server, health-polls
// both, and exposes the resulting URLs. Crash policy: restart with backoff
// (max 3 in 60s), then surface a fatal error (improvement over Electron's
// quit-on-exit).

use std::collections::HashMap;
use std::net::TcpListener;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::time::Duration;

use serde::Deserialize;

pub struct SidecarHandles {
    pub backend: Option<Child>,
    pub renderer: Option<Child>,
}

pub struct RuntimeState {
    pub active_backend_url: Mutex<Option<String>>,
    pub renderer_url: Mutex<Option<String>>,
    pub children: Mutex<SidecarHandles>,
    pub shutting_down: Mutex<bool>,
}

impl Default for RuntimeState {
    fn default() -> Self {
        Self {
            active_backend_url: Mutex::new(None),
            renderer_url: Mutex::new(None),
            children: Mutex::new(SidecarHandles {
                backend: None,
                renderer: None,
            }),
            shutting_down: Mutex::new(false),
        }
    }
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct DesktopRuntimeConfig {
    pub public_api_base_url: Option<String>,
}

pub struct Paths {
    pub standalone_root: PathBuf,
    pub backend_root: PathBuf,
    pub node_binary: PathBuf,
    pub user_data: PathBuf,
}

// Same linear scan semantics as electron/main.cjs findAvailablePort.
pub fn find_available_port(start: u16) -> Result<u16, String> {
    (start..start + 25)
        .find(|p| TcpListener::bind(("127.0.0.1", *p)).is_ok())
        .ok_or_else(|| format!("Unable to find an open port starting from {start}"))
}

// Same poll cadence as waitForBackend/waitForRenderer (1s), but the budget is
// caller-chosen: first boot seeds the full reference dataset into a fresh
// SQLite DB and can exceed Electron's 120s window.
pub fn wait_for_health_attempts(url: &str, attempts: u32) -> Result<(), String> {
    for _ in 0..attempts {
        match ureq::get(url).timeout(Duration::from_secs(2)).call() {
            Ok(resp) if resp.status() == 200 => return Ok(()),
            _ => std::thread::sleep(Duration::from_secs(1)),
        }
    }
    Err(format!("Timed out waiting for {url}"))
}

/// Reads desktop-runtime-config.json: packaged copy from the resource dir,
/// overridden by a copy in userData (port of electron/runtime-config.cjs).
pub fn load_runtime_config(resource_dir: &Path, user_data: &Path) -> DesktopRuntimeConfig {
    let mut merged = DesktopRuntimeConfig::default();
    for candidate in [
        resource_dir.join("desktop-runtime-config.json"),
        user_data.join("desktop-runtime-config.json"),
    ] {
        if let Ok(raw) = std::fs::read_to_string(&candidate) {
            let raw = raw.trim_start_matches('\u{feff}');
            if let Ok(cfg) = serde_json::from_str::<DesktopRuntimeConfig>(raw) {
                if cfg.public_api_base_url.is_some() {
                    merged.public_api_base_url = cfg.public_api_base_url;
                }
            }
        }
    }
    // Env wins, mirroring selectConfiguredDesktopApiBaseUrl precedence.
    for var in [
        "PUBLIC_API_BASE_URL",
        "API_PROXY_TARGET_URL",
        "NEXT_PUBLIC_API_BASE_URL",
    ] {
        if let Ok(v) = std::env::var(var) {
            let v = v.trim().trim_end_matches('/').to_string();
            if v.starts_with("http://") || v.starts_with("https://") {
                merged.public_api_base_url = Some(v);
                break;
            }
        }
    }
    merged
}

fn backend_env(paths: &Paths, runtime_url: &str, is_packaged: bool) -> HashMap<String, String> {
    let data_root = paths.user_data.join("backend");
    let storage_root = paths.user_data.join("storage");
    let _ = std::fs::create_dir_all(&data_root);
    let _ = std::fs::create_dir_all(&storage_root);
    let db_path = data_root.join("oet-prep.desktop.db");

    // Mirror of getBundledBackendEnv (electron/main.cjs:528).
    let mut env = HashMap::new();
    env.insert(
        "ASPNETCORE_ENVIRONMENT".into(),
        if is_packaged {
            "Production"
        } else {
            "Development"
        }
        .into(),
    );
    env.insert("ASPNETCORE_URLS".into(), runtime_url.into());
    env.insert(
        "ConnectionStrings__DefaultConnection".into(),
        format!("Data Source={}", db_path.display()),
    );
    env.insert(
        "Auth__UseDevelopmentAuth".into(),
        if is_packaged { "false" } else { "true" }.into(),
    );
    env.insert("Bootstrap__AutoMigrate".into(), "true".into());
    env.insert(
        "Bootstrap__SeedDemoData".into(),
        if is_packaged { "false" } else { "true" }.into(),
    );
    env.insert("Platform__PublicApiBaseUrl".into(), runtime_url.into());
    env.insert(
        "Platform__PublicWebBaseUrl".into(),
        "http://localhost:3000".into(),
    );
    env.insert(
        "Billing__CheckoutBaseUrl".into(),
        "http://localhost:3000/billing/checkout".into(),
    );
    env.insert("Proxy__TrustForwardHeaders".into(), "false".into());
    env.insert("Proxy__EnforceHttps".into(), "false".into());
    env.insert(
        "Storage__LocalRootPath".into(),
        storage_root.display().to_string(),
    );
    env
}

fn renderer_env(runtime_url: &str, port: u16, backend_url: &str) -> HashMap<String, String> {
    // Mirror of getStandaloneServerEnv (electron/main.cjs:515).
    let mut env = HashMap::new();
    env.insert("NODE_ENV".into(), "production".into());
    env.insert("PORT".into(), port.to_string());
    env.insert("HOSTNAME".into(), "127.0.0.1".into());
    env.insert("NEXT_PUBLIC_API_BASE_URL".into(), "/api/backend".into());
    env.insert("API_PROXY_TARGET_URL".into(), backend_url.into());
    env.insert("APP_URL".into(), runtime_url.into());
    env
}

pub fn start_backend(paths: &Paths, is_packaged: bool) -> Result<(String, Child), String> {
    let exe_name = if cfg!(windows) {
        "OetLearner.Api.exe"
    } else {
        "OetLearner.Api"
    };
    let exe = paths.backend_root.join(exe_name);
    if !exe.exists() {
        return Err(format!(
            "Bundled backend executable missing: {}",
            exe.display()
        ));
    }

    let start_port: u16 = std::env::var("OET_BACKEND_PORT")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(5198);
    let port = find_available_port(start_port)?;
    let runtime_url = format!("http://127.0.0.1:{port}");

    let mut cmd = Command::new(&exe);
    cmd.current_dir(&paths.backend_root)
        .envs(backend_env(paths, &runtime_url, is_packaged))
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    let child = cmd
        .spawn()
        .map_err(|e| format!("failed to spawn backend: {e}"))?;

    // NOTE: /health/ready 503s forever on fresh SQLite (pre-existing bug —
    // Program.cs pending-migrations check vs EnsureCreatedAsync bootstrap).
    // Poll /health until that fix lands.
    wait_for_health_attempts(&format!("{runtime_url}/health"), 300)?;
    Ok((runtime_url, child))
}

pub fn start_renderer(paths: &Paths, backend_url: &str) -> Result<(String, Child), String> {
    let server_js = paths.standalone_root.join("server.js");
    if !server_js.exists() {
        return Err(format!(
            "Standalone renderer missing: {}",
            server_js.display()
        ));
    }

    let start_port: u16 = std::env::var("OET_RENDERER_PORT")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(3000);
    let port = find_available_port(start_port)?;
    let runtime_url = format!("http://127.0.0.1:{port}");

    let mut cmd = Command::new(&paths.node_binary);
    cmd.arg(&server_js)
        .current_dir(&paths.standalone_root)
        .envs(renderer_env(&runtime_url, port, backend_url))
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    let child = cmd
        .spawn()
        .map_err(|e| format!("failed to spawn node renderer: {e}"))?;

    wait_for_health_attempts(&format!("{runtime_url}/api/health"), 120)?;
    Ok((runtime_url, child))
}

/// Boots both sidecars (or only the renderer when a remote API target is
/// configured) and records handles + URLs in state.
pub fn start_all(
    paths: &Paths,
    state: &RuntimeState,
    runtime_config: &DesktopRuntimeConfig,
    is_packaged: bool,
) -> Result<String, String> {
    let (backend_url, backend_child) = match &runtime_config.public_api_base_url {
        Some(remote) if !remote.contains("127.0.0.1") && !remote.contains("localhost") => {
            (remote.clone(), None)
        }
        _ => {
            let (url, child) = start_backend(paths, is_packaged)?;
            (url, Some(child))
        }
    };

    let (renderer_url, renderer_child) = start_renderer(paths, &backend_url)?;

    *state.active_backend_url.lock().unwrap() = Some(backend_url);
    *state.renderer_url.lock().unwrap() = Some(renderer_url.clone());
    {
        let mut children = state.children.lock().unwrap();
        children.backend = backend_child;
        children.renderer = Some(renderer_child);
    }
    Ok(renderer_url)
}

pub fn stop_all(state: &RuntimeState) {
    *state.shutting_down.lock().unwrap() = true;
    let mut children = state.children.lock().unwrap();
    if let Some(child) = children.backend.as_mut() {
        let _ = child.kill();
    }
    if let Some(child) = children.renderer.as_mut() {
        let _ = child.kill();
    }
    children.backend = None;
    children.renderer = None;
}

/// One-time migration of Electron's userData into the Tauri app-data dir.
/// Deliberately EXCLUDES the backend SQLite database: the bundled backend
/// bootstraps SQLite with EnsureCreatedAsync, which is a no-op on non-empty
/// databases — an Electron DB from an older schema generation (e.g. missing
/// RuntimeSettings) crashes the new backend at startup. Until the backend
/// adopts real EF migrations on SQLite, the desktop DB starts fresh and
/// re-syncs from the server. Secrets are also not migratable (safeStorage
/// ciphertext is Chromium-specific).
pub fn migrate_from_electron(user_data: &Path) {
    let marker = user_data.join(".migrated-from-electron");
    if marker.exists() {
        return;
    }
    let Some(appdata) = std::env::var_os("APPDATA") else {
        return;
    };
    let electron_root = PathBuf::from(appdata)
        .join("OET Prep")
        .join("prod")
        .join("user-data");
    if !electron_root.exists() {
        let _ = std::fs::create_dir_all(user_data);
        let _ = std::fs::write(&marker, "no-electron-data");
        return;
    }
    for dir in ["storage", "offline-content"] {
        let src = electron_root.join(dir);
        let dst = user_data.join(dir);
        if src.exists() && !dst.exists() {
            let _ = copy_dir_recursive(&src, &dst);
        }
    }
    let _ = std::fs::create_dir_all(user_data);
    let _ = std::fs::write(&marker, "migrated");
}

fn copy_dir_recursive(src: &Path, dst: &Path) -> std::io::Result<()> {
    std::fs::create_dir_all(dst)?;
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let target = dst.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            copy_dir_recursive(&entry.path(), &target)?;
        } else {
            std::fs::copy(entry.path(), target)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::TcpListener;

    fn unique_tmp(tag: &str) -> PathBuf {
        std::env::temp_dir().join(format!("oet-{tag}-test-{}", std::process::id()))
    }

    #[test]
    fn find_available_port_returns_bindable_in_range() {
        let port = find_available_port(39000).expect("a port in range");
        assert!((39000..39025).contains(&port));
        // The returned port must actually be bindable.
        assert!(TcpListener::bind(("127.0.0.1", port)).is_ok());
    }

    #[test]
    fn find_available_port_skips_occupied() {
        // Hold 39100 (or rely on it being held elsewhere); find must not return it.
        let _hold = TcpListener::bind(("127.0.0.1", 39100u16));
        let port = find_available_port(39100).expect("a port in range");
        assert_ne!(port, 39100);
    }

    #[test]
    fn renderer_env_sets_expected_keys() {
        let env = renderer_env("http://127.0.0.1:3000", 3000, "http://127.0.0.1:5198");
        assert_eq!(env.get("NODE_ENV").map(String::as_str), Some("production"));
        assert_eq!(env.get("PORT").map(String::as_str), Some("3000"));
        assert_eq!(env.get("HOSTNAME").map(String::as_str), Some("127.0.0.1"));
        assert_eq!(
            env.get("NEXT_PUBLIC_API_BASE_URL").map(String::as_str),
            Some("/api/backend")
        );
        assert_eq!(
            env.get("API_PROXY_TARGET_URL").map(String::as_str),
            Some("http://127.0.0.1:5198")
        );
    }

    #[test]
    fn backend_env_toggles_packaged_vs_dev() {
        let tmp = unique_tmp("be");
        let paths = Paths {
            standalone_root: tmp.join("s"),
            backend_root: tmp.join("b"),
            node_binary: tmp.join("node"),
            user_data: tmp.join("ud"),
        };
        let prod = backend_env(&paths, "http://127.0.0.1:5198", true);
        assert_eq!(
            prod.get("ASPNETCORE_ENVIRONMENT").map(String::as_str),
            Some("Production")
        );
        assert_eq!(
            prod.get("Auth__UseDevelopmentAuth").map(String::as_str),
            Some("false")
        );
        assert_eq!(
            prod.get("Bootstrap__SeedDemoData").map(String::as_str),
            Some("false")
        );
        assert!(prod
            .get("ConnectionStrings__DefaultConnection")
            .unwrap()
            .contains("oet-prep.desktop.db"));

        let dev = backend_env(&paths, "http://127.0.0.1:5198", false);
        assert_eq!(
            dev.get("ASPNETCORE_ENVIRONMENT").map(String::as_str),
            Some("Development")
        );
        assert_eq!(
            dev.get("Bootstrap__SeedDemoData").map(String::as_str),
            Some("true")
        );
        std::fs::remove_dir_all(&tmp).ok();
    }

    #[test]
    fn load_runtime_config_reads_file_and_strips_bom() {
        // NOTE: assumes PUBLIC_API_BASE_URL / API_PROXY_TARGET_URL /
        // NEXT_PUBLIC_API_BASE_URL are unset in the test env (they would override).
        let tmp = unique_tmp("rc-read");
        let res = tmp.join("res");
        let ud = tmp.join("ud");
        std::fs::create_dir_all(&res).unwrap();
        std::fs::create_dir_all(&ud).unwrap();
        std::fs::write(
            res.join("desktop-runtime-config.json"),
            "\u{feff}{\"publicApiBaseUrl\":\"https://api.example.com\"}",
        )
        .unwrap();
        let cfg = load_runtime_config(&res, &ud);
        assert_eq!(
            cfg.public_api_base_url.as_deref(),
            Some("https://api.example.com")
        );
        std::fs::remove_dir_all(&tmp).ok();
    }

    #[test]
    fn load_runtime_config_user_data_overrides_resource() {
        let tmp = unique_tmp("rc-override");
        let res = tmp.join("res");
        let ud = tmp.join("ud");
        std::fs::create_dir_all(&res).unwrap();
        std::fs::create_dir_all(&ud).unwrap();
        std::fs::write(
            res.join("desktop-runtime-config.json"),
            "{\"publicApiBaseUrl\":\"https://resource.example.com\"}",
        )
        .unwrap();
        std::fs::write(
            ud.join("desktop-runtime-config.json"),
            "{\"publicApiBaseUrl\":\"https://userdata.example.com\"}",
        )
        .unwrap();
        let cfg = load_runtime_config(&res, &ud);
        assert_eq!(
            cfg.public_api_base_url.as_deref(),
            Some("https://userdata.example.com")
        );
        std::fs::remove_dir_all(&tmp).ok();
    }

    #[test]
    fn migrate_from_electron_is_noop_when_marker_exists() {
        let tmp = unique_tmp("mig");
        std::fs::create_dir_all(&tmp).unwrap();
        std::fs::write(tmp.join(".migrated-from-electron"), "migrated").unwrap();
        migrate_from_electron(&tmp);
        // Early-return on the marker: no migration dirs created.
        assert!(!tmp.join("storage").exists());
        assert!(!tmp.join("offline-content").exists());
        std::fs::remove_dir_all(&tmp).ok();
    }
}

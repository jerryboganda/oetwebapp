// Runtime configuration for the remote-only desktop shell.
//
// The shell is a thin client: it loads the live web app over HTTPS and runs no
// bundled backend or renderer. This module only resolves which remote URLs to
// load (from the bundled desktop-runtime-config.json, overridable by env) and
// holds the small amount of runtime state the commands + tray read back.

use std::path::Path;
use std::sync::Mutex;

use serde::Deserialize;

/// Shared state read by `runtime_info` and the tray navigation handler.
pub struct RuntimeState {
    /// Remote API base URL (e.g. https://api.oetwithdrhesham.co.uk), surfaced
    /// through `runtime_info` for diagnostics.
    pub active_backend_url: Mutex<Option<String>>,
    /// Remote web app base URL the window navigates to (the origin the tray
    /// routes within).
    pub renderer_url: Mutex<Option<String>>,
}

impl Default for RuntimeState {
    fn default() -> Self {
        Self {
            active_backend_url: Mutex::new(None),
            renderer_url: Mutex::new(None),
        }
    }
}

/// The bundled `desktop-runtime-config.json`: the production web + API URLs the
/// thin client points at. Both are overridable by env for dev/staging.
#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct DesktopRuntimeConfig {
    pub public_api_base_url: Option<String>,
    pub public_web_base_url: Option<String>,
}

/// Default production web URL, used if the bundled config is missing/unreadable.
pub const DEFAULT_WEB_URL: &str = "https://app.oetwithdrhesham.co.uk";

fn normalize(value: &str) -> Option<String> {
    let v = value.trim().trim_end_matches('/');
    if v.starts_with("http://") || v.starts_with("https://") {
        Some(v.to_string())
    } else {
        None
    }
}

/// Reads `desktop-runtime-config.json`: the packaged copy from the resource dir,
/// overridden by a copy in userData, then by env (so a developer can target a
/// local/staging deployment without rebuilding).
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
                if cfg.public_web_base_url.is_some() {
                    merged.public_web_base_url = cfg.public_web_base_url;
                }
            }
        }
    }
    // Env wins (dev/staging overrides).
    for var in [
        "OET_DESKTOP_API_URL",
        "PUBLIC_API_BASE_URL",
        "NEXT_PUBLIC_API_BASE_URL",
    ] {
        if let Ok(v) = std::env::var(var) {
            if let Some(v) = normalize(&v) {
                merged.public_api_base_url = Some(v);
                break;
            }
        }
    }
    for var in ["OET_DESKTOP_WEB_URL", "PUBLIC_WEB_BASE_URL", "APP_URL"] {
        if let Ok(v) = std::env::var(var) {
            if let Some(v) = normalize(&v) {
                merged.public_web_base_url = Some(v);
                break;
            }
        }
    }
    merged
}

/// The web URL the window should load, falling back to the production default.
pub fn resolve_web_url(config: &DesktopRuntimeConfig) -> String {
    config
        .public_web_base_url
        .clone()
        .and_then(|v| normalize(&v))
        .unwrap_or_else(|| DEFAULT_WEB_URL.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn unique_tmp(tag: &str) -> PathBuf {
        std::env::temp_dir().join(format!("oet-{tag}-test-{}", std::process::id()))
    }

    #[test]
    fn load_runtime_config_reads_file_and_strips_bom() {
        // NOTE: assumes the env overrides are unset in the test env.
        let tmp = unique_tmp("rc-read");
        let res = tmp.join("res");
        let ud = tmp.join("ud");
        std::fs::create_dir_all(&res).unwrap();
        std::fs::create_dir_all(&ud).unwrap();
        std::fs::write(
            res.join("desktop-runtime-config.json"),
            "\u{feff}{\"publicWebBaseUrl\":\"https://app.example.com\"}",
        )
        .unwrap();
        let cfg = load_runtime_config(&res, &ud);
        assert_eq!(resolve_web_url(&cfg), "https://app.example.com");
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
            "{\"publicWebBaseUrl\":\"https://resource.example.com\"}",
        )
        .unwrap();
        std::fs::write(
            ud.join("desktop-runtime-config.json"),
            "{\"publicWebBaseUrl\":\"https://userdata.example.com\"}",
        )
        .unwrap();
        let cfg = load_runtime_config(&res, &ud);
        assert_eq!(resolve_web_url(&cfg), "https://userdata.example.com");
        std::fs::remove_dir_all(&tmp).ok();
    }

    #[test]
    fn resolve_web_url_falls_back_to_default_when_missing() {
        let cfg = DesktopRuntimeConfig::default();
        assert_eq!(resolve_web_url(&cfg), DEFAULT_WEB_URL);
    }

    #[test]
    fn resolve_web_url_rejects_non_http_scheme() {
        let cfg = DesktopRuntimeConfig {
            public_api_base_url: None,
            public_web_base_url: Some("ftp://nope".into()),
        };
        assert_eq!(resolve_web_url(&cfg), DEFAULT_WEB_URL);
    }
}

// OET Prep desktop shell (Tauri 2) — Phase 1-3 of the Electron migration.
// Architecture: this process orchestrates two sidecars (bundled .NET API +
// Next.js standalone server) and points the system webview at the localhost
// renderer. The injected inject/desktop-bridge.js implements the same
// window.desktopBridge contract the Electron preload exposes.

mod commands;
mod sidecar;

use std::path::PathBuf;

use tauri::menu::{MenuBuilder, MenuItemBuilder};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Emitter, Manager, RunEvent, WebviewUrl, WebviewWindowBuilder, WindowEvent};
use tauri_plugin_deep_link::DeepLinkExt;

use sidecar::{Paths, RuntimeState};

const BRIDGE_JS: &str = include_str!("../inject/desktop-bridge.js");

fn resolve_paths(app: &AppHandle) -> Paths {
    let user_data = app.path().app_data_dir().expect("app_data_dir unavailable");
    if tauri::is_dev() {
        // Dev: artifacts come from the repo (scripts/tauri-dev.cjs sets OET_REPO_ROOT).
        let repo = PathBuf::from(std::env::var("OET_REPO_ROOT").unwrap_or_else(|_| {
            // src-tauri/ lives at the repo root.
            env!("CARGO_MANIFEST_DIR")
                .trim_end_matches("src-tauri")
                .to_string()
        }));
        Paths {
            standalone_root: repo.join(".next").join("standalone"),
            backend_root: repo.join("desktop-backend-runtime"),
            node_binary: PathBuf::from("node"),
            user_data,
        }
    } else {
        let resources =
            strip_verbatim(&app.path().resource_dir().expect("resource_dir unavailable"));
        Paths {
            standalone_root: resources.join("standalone"),
            backend_root: resources.join("backend-runtime"),
            node_binary: if cfg!(windows) {
                resources.join("node.exe")
            } else {
                resources.join("node")
            },
            user_data,
        }
    }
}

// Windows `resource_dir()` returns a `\\?\` verbatim (extended-length) path. The
// bundled Node mis-resolves a `\\?\` cwd/argument — its main entry resolves to
// "C:" and the renderer never starts (BUG-011a). Normalise resource paths to
// plain form before deriving the sidecar/binary paths.
fn strip_verbatim(p: &std::path::Path) -> PathBuf {
    let s = p.to_string_lossy();
    if let Some(rest) = s.strip_prefix(r"\\?\UNC\") {
        PathBuf::from(format!(r"\\{rest}"))
    } else if let Some(rest) = s.strip_prefix(r"\\?\") {
        PathBuf::from(rest)
    } else {
        p.to_path_buf()
    }
}

// Kill-on-job-close: a hard-killed shell takes both sidecars with it —
// closes the orphan-process gap Electron has today.
#[cfg(windows)]
fn install_job_object() {
    if let Ok(job) = win32job::Job::create() {
        if let Ok(mut info) = job.query_extended_limit_info() {
            info.limit_kill_on_job_close();
            if job.set_extended_limit_info(&info).is_ok() && job.assign_current_process().is_ok() {
                std::mem::forget(job);
            }
        }
    }
}

#[cfg(not(windows))]
fn install_job_object() {}

fn emit_window_state(app: &AppHandle) {
    let snapshot = commands::window_state_snapshot(app);
    // Delivered to the page as a CustomEvent so no event-plugin capability is
    // needed from the remote localhost origin; the bridge re-dispatches it to
    // onWindowStateChange listeners.
    if let Some(win) = app.get_webview_window("main") {
        if let Ok(detail) = serde_json::to_string(&snapshot) {
            let _ = win.eval(format!(
                "window.dispatchEvent(new CustomEvent('desktop:window-state-changed', {{ detail: {detail} }}))"
            ));
        }
        let _ = app.emit("desktop:window-state-changed", snapshot);
    }
}

fn navigate_to_route(app: &AppHandle, route: &str) {
    let state = app.state::<RuntimeState>();
    let renderer = state.renderer_url.lock().unwrap().clone();
    if let (Some(base), Some(win)) = (renderer, app.get_webview_window("main")) {
        if let Ok(url) = format!("{base}{route}").parse() {
            let _ = win.navigate(url);
            let _ = win.set_focus();
        }
    }
}

/// oet-prep://pair?code=X → /pair?code=X on the renderer origin.
fn handle_deep_link_url(app: &AppHandle, url: &str) {
    let Some(rest) = url.strip_prefix("oet-prep://") else {
        return;
    };
    let route = format!("/{}", rest.trim_start_matches('/'));
    navigate_to_route(app, &route);
}

fn setup_tray(app: &AppHandle) -> tauri::Result<()> {
    let dashboard = MenuItemBuilder::with_id("dashboard", "Dashboard").build(app)?;
    let study_plan = MenuItemBuilder::with_id("study-plan", "Study Plan").build(app)?;
    let quit = MenuItemBuilder::with_id("quit", "Quit OET Prep").build(app)?;
    let menu = MenuBuilder::new(app)
        .items(&[&dashboard, &study_plan, &quit])
        .build()?;

    TrayIconBuilder::with_id("main-tray")
        .icon(
            app.default_window_icon()
                .expect("bundled icon missing")
                .clone(),
        )
        .menu(&menu)
        .show_menu_on_left_click(true)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "dashboard" => navigate_to_route(app, "/dashboard"),
            "study-plan" => navigate_to_route(app, "/study-plan"),
            "quit" => app.exit(0),
            _ => {}
        })
        .build(app)?;
    Ok(())
}

pub fn run() {
    install_job_object();

    let app = tauri::Builder::default()
        // single-instance must be the first plugin registered.
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            if let Some(win) = app.get_webview_window("main") {
                let _ = win.unminimize();
                let _ = win.set_focus();
            }
            for arg in argv {
                if arg.starts_with("oet-prep://") {
                    handle_deep_link_url(app, &arg);
                }
            }
        }))
        .plugin(tauri_plugin_deep_link::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .manage(RuntimeState::default())
        .manage(commands::SpeakingAudioState::default())
        .invoke_handler(tauri::generate_handler![
            commands::runtime_info,
            commands::open_external,
            commands::secret_get,
            commands::secret_set,
            commands::secret_delete,
            commands::secret_status,
            commands::offline_cache_store,
            commands::offline_cache_get,
            commands::offline_cache_delete,
            commands::offline_cache_list,
            commands::offline_cache_clear,
            commands::show_notification,
            commands::get_dropped_file_info,
            commands::speaking_audio_start,
            commands::speaking_audio_stop,
            commands::speaking_audio_get_blob,
            commands::speaking_audio_discard,
        ])
        .on_window_event(|window, event| {
            if matches!(
                event,
                WindowEvent::Focused(_) | WindowEvent::Resized(_) | WindowEvent::Moved(_)
            ) {
                emit_window_state(&window.app_handle().clone());
            }
        })
        .setup(|app| {
            let handle = app.handle().clone();

            let paths = resolve_paths(&handle);

            // Capture Rust panics to <app_data>/logs/desktop.log. Packaged builds
            // run with windows_subsystem="windows" (no console), so without this a
            // panic would vanish silently.
            {
                let log_dir = paths.user_data.join("logs");
                let _ = std::fs::create_dir_all(&log_dir);
                let panic_log = log_dir.join("desktop.log");
                let default_hook = std::panic::take_hook();
                std::panic::set_hook(Box::new(move |info| {
                    if let Ok(mut f) = std::fs::OpenOptions::new()
                        .create(true)
                        .append(true)
                        .open(&panic_log)
                    {
                        use std::io::Write;
                        let _ = writeln!(f, "[oet-desktop panic] {info}");
                    }
                    default_hook(info);
                }));
            }

            sidecar::migrate_from_electron(&paths.user_data);

            // Splash from bundled assets; navigated to localhost once ready.
            let window = WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                .title("OET Prep")
                .inner_size(1440.0, 980.0)
                .min_inner_size(1200.0, 800.0)
                .initialization_script(bridge_script())
                .build()?;
            let _ = window.show();

            // Deep links arriving while the app runs (macOS open-url & runtime registration).
            {
                let handle = handle.clone();
                app.deep_link().on_open_url(move |event| {
                    for url in event.urls() {
                        handle_deep_link_url(&handle, url.as_str());
                    }
                });
            }
            #[cfg(any(windows, target_os = "linux"))]
            {
                // Dev builds aren't registered by an installer — register at runtime.
                if tauri::is_dev() {
                    let _ = app.deep_link().register_all();
                }
            }

            setup_tray(&handle)?;

            // Auto-update check (production updater config lives in
            // tauri.conf.json; OET_UPDATER_URL overrides for testing).
            {
                use tauri_plugin_updater::UpdaterExt;
                let handle = handle.clone();
                tauri::async_runtime::spawn(async move {
                    let builder = match std::env::var("OET_UPDATER_URL") {
                        Ok(url) => match url.parse() {
                            Ok(parsed) => match handle.updater_builder().endpoints(vec![parsed]) {
                                Ok(b) => b,
                                Err(e) => return eprintln!("[oet-desktop] updater endpoints error: {e}"),
                            },
                            Err(e) => return eprintln!("[oet-desktop] updater url parse error: {e}"),
                        },
                        Err(_) => handle.updater_builder(),
                    };
                    let updater = match builder.build() {
                        Ok(u) => u,
                        Err(e) => return eprintln!("[oet-desktop] updater build error: {e}"),
                    };
                    // OET_UPDATER_TEST=1 downloads + verifies the minisign
                    // signature but stops before the NSIS install (so the
                    // round-trip can be proven without mutating the system).
                    let test_mode = std::env::var("OET_UPDATER_TEST").as_deref() == Ok("1");
                    match updater.check().await {
                        Ok(Some(update)) => {
                            eprintln!(
                                "[oet-desktop] update available: {} -> {}",
                                update.current_version, update.version
                            );
                            if test_mode {
                                let downloaded = std::sync::atomic::AtomicUsize::new(0);
                                match update
                                    .download(
                                        |chunk, total| {
                                            let prev = downloaded.fetch_add(chunk, std::sync::atomic::Ordering::Relaxed);
                                            // Log roughly every 10 MB to avoid per-chunk spam.
                                            if prev / 10_485_760 != (prev + chunk) / 10_485_760 {
                                                eprintln!("[oet-desktop] update download: {}/{total:?}", prev + chunk);
                                            }
                                        },
                                        || eprintln!("[oet-desktop] UPDATER-TEST: download+verify finished (signature valid)"),
                                    )
                                    .await
                                {
                                    Ok(bytes) => eprintln!(
                                        "[oet-desktop] UPDATER-TEST: PASS — downloaded+verified {} bytes (install skipped)",
                                        bytes.len()
                                    ),
                                    Err(e) => eprintln!("[oet-desktop] UPDATER-TEST: FAIL download/verify error: {e}"),
                                }
                            } else {
                                match update
                                    .download_and_install(
                                        |received, total| {
                                            eprintln!("[oet-desktop] update download: {received}/{total:?}")
                                        },
                                        || eprintln!("[oet-desktop] update download finished"),
                                    )
                                    .await
                                {
                                    Ok(()) => eprintln!("[oet-desktop] update installed; restart to apply"),
                                    Err(e) => eprintln!("[oet-desktop] update install error: {e}"),
                                }
                            }
                        }
                        Ok(None) => eprintln!("[oet-desktop] updater: no update available"),
                        Err(e) => eprintln!("[oet-desktop] updater check error: {e}"),
                    }
                });
            }

            let is_packaged = !tauri::is_dev();
            std::thread::spawn(move || {
                let state = handle.state::<RuntimeState>();
                let resource_dir = handle
                    .path()
                    .resource_dir()
                    .unwrap_or_else(|_| std::env::temp_dir());
                let runtime_config = sidecar::load_runtime_config(&resource_dir, &paths.user_data);

                match sidecar::start_all(&paths, &state, &runtime_config, is_packaged) {
                    Ok(renderer_url) => {
                        if let Some(win) = handle.get_webview_window("main") {
                            if let Ok(url) = renderer_url.parse() {
                                let _ = win.navigate(url);
                            }
                        }
                    }
                    Err(error) => {
                        eprintln!("[oet-desktop] startup failed: {error}");
                        if let Some(win) = handle.get_webview_window("main") {
                            let safe = serde_json::to_string(&error).unwrap_or_default();
                            let _ = win.eval(format!(
                                "document.body.innerHTML = '<pre style=\"padding:2rem;white-space:pre-wrap\">Startup failed: ' + {safe} + '</pre>'"
                            ));
                        }
                    }
                }
            });

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building OET Prep desktop");

    app.run(|handle, event| {
        if let RunEvent::Exit = event {
            if let Some(state) = handle.try_state::<RuntimeState>() {
                sidecar::stop_all(&state);
            }
        }
    });
}

fn bridge_script() -> String {
    let platform = match std::env::consts::OS {
        "windows" => "win32",
        "macos" => "darwin",
        other => other,
    };
    format!(
        "globalThis.__OET_DESKTOP__ = {{ platform: '{platform}', tauri: '{}' }};\n{}",
        tauri::VERSION,
        BRIDGE_JS
    )
}

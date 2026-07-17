// OET with Dr Hesham desktop shell (Tauri 2) — remote-only thin client.
//
// The window loads a tiny bundled splash, which probes reachability and then
// navigates to the live web app over HTTPS. No frontend source, .NET backend, or
// Node renderer is bundled. The injected inject/desktop-bridge.js implements the
// same window.desktopBridge contract the renderer already expects, so the live
// app runs unchanged. A navigation guard locks the window to the trusted origin
// and routes any other link to the system browser.

mod attestation;
mod commands;
mod runtime;

use tauri::menu::{MenuBuilder, MenuItemBuilder};
use tauri::tray::TrayIconBuilder;
use tauri::{AppHandle, Emitter, Manager, Url, WebviewUrl, WebviewWindowBuilder, WindowEvent};
use tauri_plugin_deep_link::DeepLinkExt;

use runtime::RuntimeState;

const BRIDGE_JS: &str = include_str!("../inject/desktop-bridge.js");

fn emit_window_state(app: &AppHandle) {
    let snapshot = commands::window_state_snapshot(app);
    // Delivered to the page as a CustomEvent so no event-plugin capability is
    // needed from the remote origin; the bridge re-dispatches it to
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

/// oet-with-dr-hesham://pair?code=X → /pair?code=X on the renderer origin.
fn handle_deep_link_url(app: &AppHandle, url: &str) {
    let Some(rest) = url.strip_prefix("oet-with-dr-hesham://") else {
        return;
    };
    let route = format!("/{}", rest.trim_start_matches('/'));
    navigate_to_route(app, &route);
}

/// HTTPS-only origin lock. Allows the bundled splash, the trusted remote origin
/// (and its sub-paths — same-origin SPA routing), and the local dev server in
/// dev builds. Everything else is blocked; external http(s) links open in the
/// system browser instead.
fn is_allowed_origin(url: &Url, remote_base: &str) -> bool {
    match url.scheme() {
        // Bundled splash / Tauri internal asset origin.
        "tauri" => return true,
        "http" | "https" => {}
        _ => return false,
    }
    let Some(host) = url.host_str() else {
        return false;
    };
    // Tauri serves bundled assets from tauri.localhost on some platforms.
    if host == "tauri.localhost" {
        return true;
    }
    // The trusted remote origin (scheme + host + port must match).
    if let Ok(remote) = Url::parse(remote_base) {
        if url.scheme() == remote.scheme()
            && url.host_str() == remote.host_str()
            && url.port_or_known_default() == remote.port_or_known_default()
        {
            return true;
        }
    }
    // Dev only: the local Next.js dev server.
    if tauri::is_dev() && (host == "localhost" || host == "127.0.0.1") {
        return true;
    }
    false
}

fn setup_tray(app: &AppHandle) -> tauri::Result<()> {
    let dashboard = MenuItemBuilder::with_id("dashboard", "Dashboard").build(app)?;
    let study_plan = MenuItemBuilder::with_id("study-plan", "Study Plan").build(app)?;
    let quit = MenuItemBuilder::with_id("quit", "Quit OET with Dr Hesham").build(app)?;
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
    let app = tauri::Builder::default()
        // single-instance must be the first plugin registered.
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            if let Some(win) = app.get_webview_window("main") {
                let _ = win.unminimize();
                let _ = win.set_focus();
            }
            for arg in argv {
                if arg.starts_with("oet-with-dr-hesham://") {
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
            attestation::sign_video_challenge,
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
            commands::updater_check,
            commands::updater_install,
            commands::app_relaunch,
            commands::hard_reload,
            commands::set_capture_protection,
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

            let user_data = app
                .path()
                .app_data_dir()
                .unwrap_or_else(|_| std::env::temp_dir());

            // Capture Rust panics to <app_data>/logs/desktop.log. Packaged builds
            // run with windows_subsystem="windows" (no console), so without this a
            // panic would vanish silently.
            {
                let log_dir = user_data.join("logs");
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

            // Resolve the remote URLs (bundled config, overridable by env).
            let resource_dir = app
                .path()
                .resource_dir()
                .unwrap_or_else(|_| std::env::temp_dir());
            let config = runtime::load_runtime_config(&resource_dir, &user_data);
            let remote_url = runtime::resolve_web_url(&config);
            {
                let state = handle.state::<RuntimeState>();
                *state.renderer_url.lock().unwrap() = Some(remote_url.clone());
                *state.active_backend_url.lock().unwrap() = config.public_api_base_url.clone();
            }

            // The window starts on the bundled splash (index.html), which probes
            // the remote and navigates to it (or shows an offline/retry screen).
            let guard_remote = remote_url.clone();
            #[cfg_attr(not(windows), allow(unused_mut))]
            let mut builder =
                WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                    .title("OET with Dr Hesham")
                    .inner_size(1440.0, 980.0)
                    .min_inner_size(1200.0, 800.0)
                    .initialization_script(bridge_script(&remote_url))
                    .on_navigation(move |url| {
                        if is_allowed_origin(url, &guard_remote) {
                            return true;
                        }
                        // External http(s) link → open in the system browser, block
                        // the in-app navigation (treat the remote page as untrusted).
                        if matches!(url.scheme(), "http" | "https") {
                            let _ = tauri_plugin_opener::open_url(url.as_str(), None::<&str>);
                        }
                        false
                    });

            // WebView2 (Windows) tuning. NOTE: additional_browser_args REPLACES
            // Tauri's defaults, so the default --disable-features set is re-specified
            // here before appending ours.
            //
            //  • --use-fake-ui-for-media-stream: Speaking-module mic capture. WebView2
            //    denies getUserMedia by default unless the host grants it. The window
            //    is already locked to the trusted origin + bundled splash (see
            //    is_allowed_origin / on_navigation), so auto-accepting the media prompt
            //    for this first-party app is safe.
            //
            //  • --disable-gpu-compositing + --disable-direct-composition-video-overlays:
            //    CRITICAL for video playback under screen-capture protection. By default
            //    Chromium promotes hardware-decoded video onto a SEPARATE DirectComposition
            //    overlay/swapchain plane (MPO) that DWM scans out INDEPENDENTLY of the
            //    window's composited surface. Our SetWindowDisplayAffinity(
            //    WDA_EXCLUDEFROMCAPTURE) protection (commands::set_capture_protection)
            //    applies to the window surface, so that separate plane is dropped from the
            //    protected path — the user sees a BLACK rectangle where the video should be
            //    (stream loads, duration/controls/watermark render, only the frame is black).
            //    --disable-gpu-compositing removes the GPU compositor so there is NO
            //    independent video plane: the frame composites INTO the window surface that
            //    WDA protects — the user sees the video AND capture stays black. It keeps
            //    hardware DECODE (unlike --disable-gpu, which would also risk the native-HLS
            //    black-screen regression). --disable-direct-composition-video-overlays is
            //    kept as belt-and-suspenders. NOTE the overlay switch ALONE is NOT enough on
            //    WebView2/Edge (MicrosoftEdge/WebView2Feedback#5574: video still routes
            //    through a DComp swapchain; --disable-gpu-compositing is the lever proven to
            //    fix it there). Do NOT add --disable-gpu or --disable-direct-composition
            //    (WebView2 blank-screen / perf risk). A bare
            //    --disable-features=DirectCompositionVideoOverlays is a NO-OP (no such
            //    base::Feature). macOS uses NSWindow.sharingType and is unaffected.
            #[cfg(windows)]
            {
                builder = builder.additional_browser_args(
                    "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection --disable-gpu-compositing --disable-direct-composition-video-overlays --use-fake-ui-for-media-stream",
                );
            }

            let window = builder.build()?;
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
                                // Prompt-first (no silent install): signal the page that an
                                // update exists. The in-app UI (Check-for-updates dialog and,
                                // when the server gate forces it, the forced-update overlay)
                                // drives the actual download+install via the updater_* commands.
                                if let Some(win) = handle.get_webview_window("main") {
                                    let detail = serde_json::json!({
                                        "phase": "available",
                                        "version": update.version,
                                        "currentVersion": update.current_version,
                                        "notes": update.body,
                                    });
                                    if let Ok(detail_str) = serde_json::to_string(&detail) {
                                        let _ = win.eval(format!(
                                            "window.dispatchEvent(new CustomEvent('desktop:update-available', {{ detail: {detail_str} }}))"
                                        ));
                                    }
                                }
                            }
                        }
                        Ok(None) => eprintln!("[oet-desktop] updater: no update available"),
                        Err(e) => eprintln!("[oet-desktop] updater check error: {e}"),
                    }
                });
            }

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building OET with Dr Hesham desktop");

    app.run(|_handle, _event| {});
}

fn bridge_script(remote_url: &str) -> String {
    let platform = match std::env::consts::OS {
        "windows" => "win32",
        "macos" => "darwin",
        other => other,
    };
    // serde_json::to_string yields a safely-quoted JS string literal for the URL.
    let remote_literal = serde_json::to_string(remote_url).unwrap_or_else(|_| "\"\"".into());
    format!(
        "globalThis.__OET_DESKTOP__ = {{ platform: '{platform}', tauri: '{}' }};\nglobalThis.__OET_REMOTE__ = {remote_literal};\n{}",
        tauri::VERSION,
        BRIDGE_JS
    )
}

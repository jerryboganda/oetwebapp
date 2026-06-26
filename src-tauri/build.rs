fn main() {
    // Declaring the app commands generates `allow-<command>` permissions. In the
    // remote-only shell, capabilities/app-remote.json grants ONLY `allow-runtime-info`
    // to the remote origin; the rest stay declared for localhost/dev and future use.
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(
        tauri_build::AppManifest::new().commands(&[
            "runtime_info",
            "open_external",
            "secret_get",
            "secret_set",
            "secret_delete",
            "secret_status",
            "offline_cache_store",
            "offline_cache_get",
            "offline_cache_delete",
            "offline_cache_list",
            "offline_cache_clear",
            "show_notification",
            "get_dropped_file_info",
            "speaking_audio_start",
            "speaking_audio_stop",
            "speaking_audio_get_blob",
            "speaking_audio_discard",
        ]),
    ))
    .expect("failed to run tauri-build")
}

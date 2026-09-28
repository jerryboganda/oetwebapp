# Desktop Native Capabilities Plan (Tauri shell)

> **Status:** plan, awaiting owner sign-off on the open questions in section 12. No code in this PR.
> **Written:** 2026-09-28, against `main` at `f1d63b3d2`.
> **Owner decision being implemented:** the privileged Tauri commands that are registered in the desktop shell
> but not granted to the web app should be put to proper use, not left as dead code.
> **Scope:** the Tauri 2 desktop shell (`src-tauri/`), the injected bridge (`src-tauri/inject/desktop-bridge.js`),
> the bridge contract (`types/desktop.d.ts`) and the web code that would call it. Android/iOS (Capacitor) and the
> plain browser appear only as fallbacks.

Every statement about current behaviour below cites the file it comes from. Items marked **verify** are
platform behaviours this plan could not confirm from the code alone. They must be checked on real Windows and
macOS hardware before the phase that depends on them ships.

---

## 1. Summary

The shell registers **23** IPC commands (`src-tauri/build.rs`, `tauri::generate_handler!` in
`src-tauri/src/lib.rs`). The live web origin gets **7** of them (`src-tauri/capabilities/app-remote.json`):
`runtime_info`, `sign_video_challenge`, `updater_check`, `updater_install`, `app_relaunch`, `hard_reload` and
`set_capture_protection`. On macOS it also gets `core:window:allow-set-fullscreen`
(`src-tauri/capabilities/app-remote-macos.json`).

That leaves **16** commands, not 15, that are registered and granted to nobody. No capability grants them,
`dev-localhost.json` included, so they cannot run in any build. No web code calls them either. The last
consumer, `lib/desktop/speaking-audio-bridge.ts`, was deleted as dead code in PR #269, and `lib/desktop/` no
longer exists.

| # | Command(s) | Recommendation | Product feature it should power | Phase |
|---|---|---|---|---|
| 1 | `open_external` | **Harden, then grant** | Checkout, Zoom join, app-store and help links open reliably in the system browser | 1 |
| 2 | `show_notification` | **Harden, then grant** | OS toasts for push-channel notifications (grading done, session reminders, tutor replies) while the app is in the background | 1 |
| 3–6 | `secret_get` / `secret_set` / `secret_delete` / `secret_status` | **Narrow, then grant** | A stable desktop device identity (`X-OET-Device-Id`) in the OS keyring. Refresh-token storage only if the owner approves (Q5) | 2 |
| 7–10 | `speaking_audio_start` / `_stop` / `_get_blob` / `_discard` | **Redesign, then grant** | A crash-safe local copy of the Speaking recorder-fallback audio until the server confirms the upload | 3 |
| 11–15 | `offline_cache_store` / `_get` / `_delete` / `_list` / `_clear` | **Harden, then grant (optional)** | A durable, encrypted outbox for queued offline Listening/Reading answers | 4 |
| 16 | `get_dropped_file_info` | **Retire** (owner decision, Q8) | None that is safe. HTML5 drag-and-drop gives the same result without a filesystem oracle | 5 |

Four findings come out of reading the code. They block or shape the phases, so they are grouped as Phase 0
(section 5):

- **`hard_reload` wipes all WebView data.** It calls `clear_all_browsing_data()` (`commands.rs::hard_reload`).
  That signs the learner out, generates a new device ID, and deletes queued offline answers and IndexedDB
  recordings. The web-side hard reload only clears Service Worker caches (`lib/shell/hard-reload.ts`).
- **The native drag-drop handler is on.** Tauri's handler is enabled by default and `lib.rs` never turns it
  off. Tauri documents that turning it off is required for HTML5 drag-and-drop on Windows (**verify**). File
  drops on `components/billing/proof-dropzone.tsx` and the admin media page are probably dead in the Windows
  app.
- **The web app cannot tell which commands it may call.** `types/desktop.d.ts` types every privileged
  namespace as always present, and the bridge defines them all. The only way to find out whether a command is
  granted is to call it and catch the rejection.
- **Some commands can crash the app.** `user_data_dir()` calls `.expect()`, and every command state uses
  `Mutex::lock().unwrap()`. Release builds set `panic = "abort"` (`src-tauri/Cargo.toml`), so any of these
  panics kills the app.

---

## 2. Current state (from the code)

### 2.1 Shell shape

- **The shell is a remote-only thin client.** A bundled splash (`src-tauri/splash/`) checks that the server is
  reachable, then navigates to `https://app.oetwithdrhesham.co.uk` (`lib.rs` header,
  `runtime.rs::DEFAULT_WEB_URL`). Packaged builds ignore every URL override
  (`runtime.rs::load_runtime_config_with`).
- **A navigation guard locks the window to the app origin.** `lib.rs::is_allowed_origin` allows only the splash,
  the trusted HTTPS origin, the Bunny embed hosts and `about:blank`/`about:srcdoc`. `on_navigation` sends any
  other http(s) URL to the system browser through `tauri_plugin_opener::open_url` and cancels the in-app
  navigation. This path does not go through `open_external`.
- **The bridge is injected before the page runs.** `src-tauri/inject/desktop-bridge.js` is installed with
  `initialization_script`, so `window.desktopBridge` exists before any app code runs. Each method calls
  `window.__TAURI_INTERNALS__.invoke`. A command without a grant rejects inside Tauri's ACL.
- **Microphone access is granted automatically on Windows.** WebView2 runs with
  `--use-fake-ui-for-media-stream` (`lib.rs`), so `getUserMedia` works without a prompt. The reasoning in the
  code is that the window is locked to the trusted origin.

### 2.2 Why the 16 commands are withheld

The reason is written in `src-tauri/capabilities/app-remote.json`:

> "The remote page is treated as semi-trusted (assume MITM/compromise risk) ... The privileged commands (OS
> keyring secrets, offline cache, speaking-audio temp files, dropped-file probing, notifications,
> open-external) are deliberately NOT granted to the remote origin ... they stay registered in lib.rs only for
> future native re-enablement."

Most of these commands came across from the old Electron shell (`commands.rs`: "port of main.cjs:1141-1248").
In Electron they ran in a privileged preload. In the Tauri shell they would be callable by **any script
running in the top-level document of the remote origin**. Tauri capabilities are scoped by URL, not by script,
and that URL's `script-src` (`proxy.ts::buildCsp`) admits third-party code:

- Zoom (`https://*.zoom.us`, `source.zoom.us`)
- PayPal (`https://*.paypal.com`, `*.paypalobjects.com`)
- Google reCAPTCHA (`www.google.com`, `www.gstatic.com`, `apis.google.com`, `*.firebaseapp.com`)
- Whop (`cdn.whop.com`)
- Google Pay and Apple Pay (`pay.google.com`, `applepay.cdn-apple.com`)

A compromise of any of those scripts, an XSS bug in the Next.js app, or a malicious dependency would inherit
every command granted to the origin. That is the real threat model. TLS-level MITM is a lesser concern
because the shell only loads HTTPS (`is_allowed_origin` rejects `http://app...`).

### 2.3 What each withheld command does today

| Command | Behaviour today (`src-tauri/src/commands.rs`) | Problems if it were granted as-is |
|---|---|---|
| `open_external(url)` | `validate_external_url` accepts any non-empty `http://` or `https://` URL. It then calls `tauri_plugin_opener::open_url` | Any host, plain http, no length limit and no rate limit. A compromised page could open phishing pages, or flood the user with browser tabs |
| `secret_get/set/delete(namespace, key[, value])` | Reads and writes keyring entries named `com.oetprep.desktop/<ns>` / `<key>`. Namespace and key pass through `sanitize_component` (64 and 128 characters) | Any namespace, any key and any value size. The page can read every entry, or write unlimited entries to the keyring |
| `secret_status()` | Returns a fixed JSON object naming the keyring backend | Harmless |
| `offline_cache_store(key, data)` | Writes `{cachedAt, data}` as plain JSON to `<app_data>/offline-content/<key>.json` | No size or count limit, so the page could fill the disk. Not encrypted |
| `offline_cache_get/delete/list/clear` | Read, delete, list or wipe the files in that directory | `list` shows every key. `user_data_dir` panics if the app-data directory cannot be resolved |
| `show_notification(title, body, route)` | Shows an OS toast through `tauri-plugin-notification`. `route` is ignored (`let _ = route;`) | No length or rate limit, and clicking a toast does nothing |
| `get_dropped_file_info(file_path)` | Returns the name, size, full path and modified time of **any path the user can access** | Lets a page check whether any local file exists and read its size and date. Leaks the Windows user name in paths |
| `speaking_audio_start(session_id, mime)` | Records the session in an in-memory `HashMap` | Unbounded sessions, and the MIME type is not checked |
| `speaking_audio_stop(session_id, chunks_base64)` | Decodes **the whole recording in one IPC call** while holding the mutex. Writes `<temp>/oet-prep-speaking-audio/<id>-<ts>.bin` and returns `filePath` | Not crash-safe: nothing reaches disk until stop. The file sits unencrypted in a shared temp directory, is never cleaned up, and its path is leaked to the page |
| `speaking_audio_get_blob(session_id)` | Reads the file back only if the in-memory session exists | After a restart the in-memory map is empty. The file stays on disk but can never be recovered |
| `speaking_audio_discard(session_id)` | Removes the session and deletes the file | Fine |

### 2.4 How the web side covers these jobs today

| Job | Browser / desktop today | Android / iOS today |
|---|---|---|
| Open an external URL | `window.open(..., '_blank')` or `location.assign` (`lib/mobile/web-checkout.ts::openCheckoutUrl`, `lib/native/billing-bridge.ts::openInPlatformBrowser`, `app/classes/[id]/sessions/[sessionId]/join/page.tsx`). On desktop, `location.assign` to a foreign host goes through the navigation guard to the browser. How WebView2 and WKWebView handle `window.open` with no new-window handler is **verify** | `@capacitor/browser` |
| Device identity | `localStorage` plus a first-party cookie (`lib/device-id.ts`) | Keychain / Keystore through `lib/mobile/secure-storage.ts` (`device_id`) |
| Session persistence | httpOnly `oet_rt` refresh cookie. Tokens are never written to web storage in production (`lib/auth-storage.ts`, IAM-05) | Keychain / Keystore (`storeAuthTokens`) |
| Notifications | SignalR `/v1/notifications/hub`, with the `notification` event handled in `contexts/notification-center-context.tsx::handleRealtimeEnvelope` (in-app toast only when the page is visible). Web Push is **website-only**: `ensureNotificationWorkerRegistered` returns early unless the runtime is `web` | Capacitor push (`lib/mobile/push-notifications.ts`) |
| Speaking fallback recording | `hooks/useSpeakingSessionRecorder.ts` keeps the recording **in memory only**. The code says: "ponytail: in-memory only — a tab close before a successful upload loses the audio" | Native recorder (`lib/mobile/speaking-recorder.ts`) |
| Offline answers | Encrypted IndexedDB outbox (`lib/mobile/offline-sync.ts::queueOfflineAttempt`, AES-GCM through `lib/mobile/offline-crypto.ts`) | Same |
| File drop | HTML5 `DataTransfer` (`components/billing/proof-dropzone.tsx`, `app/admin/content/media/page.tsx`, `app/admin/users/import/page.tsx`) | Picker only |
| Listening audio | Kept in memory as Blob URLs (`lib/listening/audio-prebuffer.ts`, 2-hour TTL). Never written to disk | Same |

---

## 3. Design principles for granting anything to the remote origin

These rules apply to every phase. A grant that breaks one of them does not ship.

1. **One capability file per feature.** Add a file such as `capabilities/app-remote-notifications.json` rather
   than widening `app-remote.json`. Each file carries its own `description` (the pattern `app-remote.json`
   already follows) and `"platforms": ["windows", "macOS"]`. Linux has no release pipeline (Q10), so it gets
   no grants.
2. **Pin each grant to the URL paths that need it.** `remote.urls` takes URLPattern strings. Grant speaking
   audio only on `https://app.oetwithdrhesham.co.uk/speaking/*`, for example, never on the whole origin.
   - This is defence-in-depth, not a boundary. The app is a single-page app: a third-party script loaded on
     `/checkout` stays in memory after client-side navigation to `/speaking`, and Tauri checks the webview's
     current URL.
   - It still reduces exposure, and it stops accidental calls from unrelated pages.
   - **Verify** that Tauri 2.11 matches path patterns on remote URLs as expected, and how it treats a bare
     origin (today's grants rely on a bare origin matching every path).
3. **Validate every argument in Rust.** Treat every argument as hostile. Use allowlists over sanitising,
   explicit length and size caps, strict formats (UUIDs, relative routes), and reject everything else with a
   stable error code (`{ ok: false, error: "HOST_NOT_ALLOWED" }`). `attestation.rs` is the in-repo model:
   `validate_input` plus a unit-tested sliding-window `rate_check`.
4. **Rate-limit per process.** Reuse the `attestation.rs` pattern (`VecDeque<Instant>`, with an injected
   `now` so tests can drive it). Each command family gets its own budget.
5. **Never return what the page does not need.** No absolute file paths, no directory listings outside the
   command's own store, no secrets the page did not store itself. The capture-protection comment in
   `app-remote.json` shows the standard: "reads/writes no data".
6. **No secret exfiltration.** Build-time secrets such as `OET_DESKTOP_ATTEST_SECRET` never cross IPC
   (`attestation.rs`). Keyring access is limited to named keys the web app owns (Phase 2).
7. **Never panic in a command.** Release builds abort on panic. Replace `.expect()` and `.unwrap()` in the
   command paths with `Result` errors.
8. **CSP stays the first line of defence.** Each new grant raises the cost of a `script-src` compromise.
   Before Phase 3 (the first grant that handles personal data), review whether the pages that get grants
   really need the third-party script origins in `proxy.ts::buildCsp`. Tauri's `app.security.csp`
   (`tauri.conf.json`) covers only the bundled splash, not the remote page.
9. **The web app detects features from what the shell reports.** Add a `nativeCapabilities` array to
   `runtime_info` (for example `["open-external", "notifications"]`), listing only what this shell version
   actually grants. The web app calls a privileged method only when the capability is listed **and** its
   server feature flag is on. `types/desktop.d.ts` marks the privileged namespaces optional, as it already
   does for `updater`, `reload`, `captureProtection`, `window` and `attestation`.
10. **Grants ship inside the binary.** A capability is compiled into the shell and reaches users only through
    a desktop release (`tauri-desktop-release.yml`, `docs/app-release-playbook.md`, platform-scoped "cut a
    desktop release"). Rollout and revocation therefore work at three levels:
    - The server flag (`FeatureFlag.Enabled` / `RolloutPercentage`, `Domain/AdminEntities.cs`, read through
      `hooks/use-feature-flag-map.ts`) switches **use** on or off instantly.
    - Removing a grant needs a new shell release.
    - Forcing users off a shell with a bad grant uses the server-side version gate:
      `LaunchReadinessSettings.DesktopMinSupportedVersion` / `DesktopForceUpdate`, enforced by
      `ClientVersionGateMiddleware`, with the prompt in `components/shell/ForcedUpdateOverlay.tsx`.

---

## 4. Data protection

- **Learner voice recordings are personal data.** Speaking audio identifies a person and records their
  performance.
  - The server is the system of record, with its own retention (`SpeakingComplianceOptions.AudioRetentionDays`,
    default 365, `Configuration/SpeakingComplianceOptions.cs`; swept by `SpeakingAudioRetentionWorker`;
    `docs/speaking-module-runbook.md` section 7).
  - Any desktop copy is a **temporary safety buffer**, never a second archive. It is encrypted at rest and
    deleted as soon as the server confirms the upload.
  - Any buffer older than the TTL (Q6) is deleted at shell startup and at sign-out.
- **Offline answers are attempt data.** They are already encrypted before storage and "fail closed rather than
  ever putting an unencrypted answer into IndexedDB" (`queueOfflineAttempt`). The native store receives
  ciphertext only.
- **Keyring versus WebView storage:**

  | Storage | Protects against | Does not protect against | Survives `hard_reload` today |
  |---|---|---|---|
  | WebView cookies / localStorage / IndexedDB | Other OS users (profile directory permissions) | Malware running as the same user; script on the page (except httpOnly cookies) | **No** (`clear_all_browsing_data`) |
  | Windows Credential Manager (`keyring` crate) | Other OS users; offline disk theft (DPAPI) | Any process running as the same user (no per-app ACL) | Yes |
  | macOS Keychain | Other users; other apps (the ACL is tied to the app's code signature) | Page script if the grant exposes the value | Yes |

  Two consequences follow:
  - On Windows the keyring is about **surviving WebView data loss**. It does not add confidentiality. The
    Windows generic-credential blob limit (2,560 bytes, `CRED_MAX_CREDENTIAL_BLOB_SIZE`) caps what can be
    stored. The shell writes with keyring 3.6.3 `set_password` (`commands.rs`), whose Windows backend stores the
    value as UTF-16, so the practical limit is about 1,280 characters (**verify**).
  - On macOS an **unsigned or ad-hoc-signed** build may show a Keychain access prompt after every update,
    because the ACL follows the code signature (**verify**). Installers are unsigned unless the `APPLE_*`
    secrets are set (`docs/tauri-desktop-shell.md`), so Phase 2 on macOS depends on signing (Q9).
- **Notification previews** show on the lock screen and in the OS notification centre. Toast bodies must not
  contain scores, payment details or clinical case content (Q4).
- **Exam and video content never goes to disk.**
  - Listening exam audio stays in the in-memory prebuffer (`lib/listening/audio-prebuffer.ts`).
  - Bunny video is protected content: signed URLs, capture protection, attestation.
  - No command in this plan may persist either. `open_external` must refuse `mediadelivery.net` hosts: the
    0.7.8 P0, where a signed player URL was handed to Safari, is recorded in `lib.rs`.

---

## 5. Phase 0: prerequisites (no new grants)

| ID | Change | Why | Where |
|---|---|---|---|
| P0-1 | **Limit `hard_reload` to HTTP and cache data.** Keep cookies, localStorage and IndexedDB. Use the platform APIs through `with_webview`: WebView2 `ClearBrowsingData` with cache kinds only, and WKWebView `WKWebsiteDataStore.removeData(ofTypes:)` with the disk and memory cache types | `clear_all_browsing_data()` removes the `oet_rt` cookie (sign-out), `oet_device_id` (the next sign-in counts as a new device, see `lib/device-id.ts`) and the IndexedDB `oet-offline` outbox and `oet-speaking-recordings` store (possible loss of queued answers). A browser Ctrl+F5 clears none of these, and the web path in `lib/shell/hard-reload.ts` only clears Service Worker caches | `commands.rs::hard_reload`. Q2 |
| P0-2 | **Turn off the native drag-drop handler.** Call `.disable_drag_drop_handler()` on the main `WebviewWindowBuilder` | Tauri documents that disabling it is required for HTML5 drag-and-drop on Windows. The shell does not disable it, so file drops on the billing proof dropzone and admin uploads are probably swallowed (**verify**) | `lib.rs` window builder |
| P0-3 | **Remove panics from command paths.** `user_data_dir` returns `Result`, and mutex poisoning is handled | `panic = "abort"` in release | `commands.rs` |
| P0-4 | **Add `nativeCapabilities` to `runtime_info`** and to its type | Lets the web app detect features without trial-and-error IPC | `commands.rs::runtime_info`, `types/desktop.d.ts` |
| P0-5 | **Make the privileged namespaces optional** in `types/desktop.d.ts` (`secureSecrets?`, `offlineCache?`, `notifications?`, `fileInfo?`, `speakingAudio?`, `openExternal?`) and have the bridge define each only when it is granted | Today TypeScript promises methods that always reject | `types/desktop.d.ts`, `src-tauri/inject/desktop-bridge.js`, conformance test |
| P0-6 | **Add a capability lint** (a vitest in `src-tauri/__tests__/`). It parses `capabilities/*.json` and asserts the exact set of grants per file and per URL pattern, and that the `build.rs` command list matches `generate_handler!` | An accidental extra grant fails CI instead of shipping | `tauri-ci.yml` → `conformance` job |
| P0-7 | **Audit `core:default` on the remote origin.** Replace it with only the core permissions the bridge uses | `core:default` pulls in the default sets of several core plugins (window, webview, event, path and others). Their contents for 2.11.3 need a **verify** pass: the path plugin, for example, can reveal home-directory paths | `capabilities/app-remote.json` |
| P0-8 | **Decide IPC transport under the web CSP.** `proxy.ts` `connect-src` does not list Tauri's IPC endpoints (`ipc:` / `http://ipc.localhost`), so Tauri falls back to postMessage IPC on the remote origin (**verify**). Either add them to `connect-src` or confirm the fallback's throughput is acceptable for Phase 3 chunk uploads | Speaking chunks cross IPC every second | `proxy.ts::buildCsp` |
| P0-9 | **Fix stale text.** `commands.rs` line 4 ("only `runtime_info` is exposed") and the ACL paragraph of `docs/tauri-desktop-shell.md` (already fixed in this PR) | Both documents disagreed with `app-remote.json` | — |

**Acceptance:**
- **P0-1:** after pressing Reload on the desktop, the learner is still signed in, the same `X-OET-Device-Id`
  is sent, and a queued offline answer is still pending.
- **P0-2:** dropping a PDF on `/billing/manual-payment` (proof dropzone) in the Windows app fills the upload.
- **P0-6:** the lint fails when an `allow-secret-get` is added to any capability file.

**Effort:** about 4 engineer-days. **Risk:** low to medium. P0-1 changes what the in-app "Reload" button
does; the owner must agree (Q2).

---

## 6. Per-command plan

### 6.1 `open_external` (Phase 1)

**Feature: reliable "open in browser" on desktop.**
- Checkout pages: Whop, Stripe, PayPal and Fawaterak hosted pages.
- Zoom join links when the in-app SDK is not used (`app/classes/[id]/sessions/[sessionId]/join/page.tsx`).
- App-store and help links.
- Links rendered from notifications.

`on_navigation` already reroutes top-level navigations. The command's value is a deterministic, testable path
for **new-window** opens, whose behaviour is undefined without a new-window handler (**verify**).

**Web integration.** Create `lib/shell/open-external.ts`, next to `lib/shell/hard-reload.ts`, with one ordered
fallback:
1. Desktop with the `open-external` capability: `desktopBridge.openExternal(url)`.
2. Capacitor: `@capacitor/browser`.
3. Web: `window.open(..., 'noopener,noreferrer')`, then `location.assign` if the popup is blocked.

Replace the duplicated logic in `lib/mobile/web-checkout.ts::openCheckoutUrl` and
`lib/native/billing-bridge.ts::openInPlatformBrowser`. Point the Zoom join page and `app/writing/stats/page.tsx`
at the helper.

**Rust hardening** (`validate_external_url`):
- HTTPS only.
- No userinfo (`user:pass@`) in the URL.
- At most 2,048 characters.
- Host must match a compiled allowlist of registrable domains:
  - `oetwithdrhesham.co.uk` and its subdomains
  - `whop.com`, `whop.io`
  - `checkout.stripe.com`, `billing.stripe.com`
  - `paypal.com`
  - `fawaterk.com`
  - `zoom.us`, `zoom.com`
  - `apps.apple.com`
  - `play.google.com`
- Explicitly deny `iframe.mediadelivery.net` and `player.mediadelivery.net`.
- At most 5 calls per minute.

Unknown hosts return `HOST_NOT_ALLOWED`, and the web helper shows the URL with a copy button (or a native
confirm, Q3). The allowlist is compiled in, so adding a host needs a shell release. A host list passed in from
the page would be worthless as a control.

**Capability.** New file `app-remote-open-external.json`, remote URL `https://app.oetwithdrhesham.co.uk/*`.
Links can appear on any page, and the host allowlist is the real control.

**Fallback.** When the capability is missing (older shell or flag off), use the existing `window.open` path.

**Telemetry.** `shell_open_external` with `{ host, outcome }`. Log the host only, never the path or query:
checkout URLs carry session tokens.

**Tests.**
- Rust unit tests for every accept and reject rule, including IDN and punycode hosts, `evil.com?x=whop.com`,
  `whop.com.evil.com`, and uppercase schemes.
- Vitest for `lib/shell/open-external.ts` across the three runtimes.
- Update the conformance test.

### 6.2 `show_notification` (Phase 1)

**Feature.** When the desktop window is not focused or is minimised, show an OS toast for any real-time
notification whose `channels` include `push`: grading complete, Speaking session reminders, tutor replies,
class starting.
- The server has already applied preferences, quiet hours (`NotificationScheduling`) and consent, so the client
  adds no preference logic of its own.
- The app has no background process. Closing the last window exits the app (there is no `CloseRequested`
  handler in `lib.rs`), so toasts only arrive while the app is running. Desktop does not replace email or
  mobile push.

**Web integration.** In `contexts/notification-center-context.tsx::handleRealtimeEnvelope`, when all of these
hold, call `desktopBridge.notifications.show(title, body, actionUrl)`:
- the runtime is `desktop`;
- the capability is present and the flag is on;
- the item is new;
- `envelope.notification.channels` includes `'push'`;
- the window is not focused (`runtime.onWindowStateChange` / `document.visibilityState`).

The in-app toast path stays as it is for the visible case.

**Rust hardening:**
- Title: at most 64 characters. Body: at most 240. Strip control characters.
- `route` must be a relative path that starts with `/`, with no `//`, no scheme, and at most 256 characters.
- At most 6 calls per minute.
- Use the route: store it, and on activation call `lib.rs::navigate_to_route`. Click delivery on desktop is not
  uniform (`commands.rs` comment), so this is best effort and the in-app notification centre stays the primary
  surface.
- The tray menu (`lib.rs::setup_tray`) can add "Open notifications".

**Capability.** New file `app-remote-notifications.json` on `https://app.oetwithdrhesham.co.uk/*`. The
notification hub is mounted app-wide.

**Data protection.** Categories with sensitive bodies (billing, results) send a generic body, such as "Your
Writing result is ready". Q4 settles the list.

**Fallback.** On older shells, only the in-app toast. Unchanged in browsers and on mobile.

**Platform checks (verify).**
- Windows toasts need the AppUserModelID that the NSIS install registers. Dev runs may show the wrong app name.
- macOS delivery for unsigned builds.

**Telemetry.** `shell_notification_shown` with `{ category, outcome }`. No title or body.

**Tests.** Rust validators and the rate limiter. A vitest for the `handleRealtimeEnvelope` decision table
(focused, unfocused, `push` in channels or not, no capability).

### 6.3 `secret_get` / `secret_set` / `secret_delete` / `secret_status` (Phase 2)

**Feature, step 2a (recommended): stable desktop device identity.**
- `lib/device-id.ts` already uses the keychain on native mobile (`initNativeDeviceId` → `getSecureItem('device_id')`).
- On desktop it falls back to `localStorage` plus a cookie, which any WebView data reset destroys. That forces
  re-verification and uses up one of the learner's device slots.
- The change: add a desktop branch that mirrors `initNativeDeviceId`, using
  `desktopBridge.secureSecrets.get('device', 'device_id')` and `set(...)`.

**Feature, step 2b (owner decision, Q5): persistent refresh token.**
- The backend already returns the refresh token in the response body to `X-OET-Client-Platform: desktop`
  (`AuthService.Sessions.cs::ShouldExposeRefreshTokenInResponse`, `lib/auth-client.ts::canSendBodyRefreshToken`).
  Today desktop keeps it in memory only.
- Storing it in the keyring would keep the learner signed in through WebView data loss.
- Once P0-1 stops `hard_reload` deleting cookies, the httpOnly `oet_rt` cookie probably covers this. **Do 2b
  only if support tickets show cookie-jar loss after P0-1.**
- If 2b goes ahead, extend `lib/auth-storage.ts::saveStoredSession` and the restore path to call the desktop
  keyring where they call `storeAuthTokens` / `getStoredAuthTokens` on mobile. Keep the production rule that
  tokens are never stored in web storage (IAM-05).
- Independently, consider whether desktop still needs the body-exposed refresh token at all (Q5).

**Rust hardening.** Replace the free-form namespace and key with a compiled allowlist:
- `device/device_id`, value a UUID;
- `auth/refresh_token` (only if 2b is approved), value at most 1,280 characters (keyring's Windows backend
  stores `set_password` values as UTF-16, so 2,560 blob bytes hold about 1,280 characters; **verify**).

Anything else returns `KEY_NOT_ALLOWED`. `secret_get` is limited to 30 calls per minute. `secret_status` stays
as it is.

**Capability.** New file `app-remote-secrets.json` on `https://app.oetwithdrhesham.co.uk/*`: device identity
is read on every authenticated request.

**Fallback.** When the call fails, keep today's web storage path. An error must never block sign-in: the header
is simply omitted, as `initNativeDeviceId` already does.

**Migration.** On first run with the capability, if the keyring has no `device_id` and `localStorage` has one,
**adopt the existing web ID** so the learner does not count as a new device.

**Telemetry.** `shell_keyring` with `{ op, outcome }`. Never keys or values.

**Tests.**
- Rust allowlist tests.
- A vitest for the `lib/device-id.ts` desktop branch: adopt the existing ID, fall back on error.
- Manual check that a macOS signed-build update does not show a Keychain prompt (Q9).

### 6.4 `speaking_audio_*` (Phase 3)

**Feature.** Do not lose a candidate's Speaking recording on desktop.
- The recorder fallback (`hooks/useSpeakingSessionRecorder.ts`, used by
  `components/domain/speaking/ExamConversationPanel.tsx`) holds the recording in memory until
  `uploadSpeakingSessionRecording` (`lib/api/speaking-sessions.ts`) succeeds. A crash, a force-quit or a
  mistaken close loses up to 5 minutes of exam audio.
- On desktop the shell becomes the durable buffer. (IndexedDB could do the same on web; that is a separate
  web change.)

**Redesign before granting.** The current start / stop-with-all-chunks shape gives no crash safety.

| New command (replaces) | Contract |
|---|---|
| `speaking_audio_start(sessionId, mimeType)` | `sessionId` must be a UUID, the server's Speaking session ID. `mimeType` must be one of `audio/webm`, `audio/webm;codecs=opus`, `audio/mp4` or `audio/ogg;codecs=opus`. At most 2 open sessions. Creates `<app_data>/speaking-audio/<sessionId>/` with user-only permissions |
| `speaking_audio_append(sessionId, seq, chunkBase64)` (new) | Appends one `MediaRecorder` timeslice (`recorder.start(1000)` already emits 1-second chunks). Each chunk is at most 512 KB. `seq` must increase by one each call. At most 60 MB per session. The chunk is encrypted (AES-GCM, key in the keyring under `speaking/audio_key`, generated once) and written with fsync before the call returns `ok` |
| `speaking_audio_stop(sessionId)` | Marks the session final. Returns `{ ok, sizeBytes, durationMs, mimeType }`, with **no `filePath`** |
| `speaking_audio_get_blob(sessionId)` | Decrypts and returns the audio. It works **after a restart** by reading from disk, not from the in-memory map. At most 60 MB |
| `speaking_audio_discard(sessionId)` | Deletes the session directory |
| `speaking_audio_list_pending()` (new, optional) | Returns the session IDs of finalised or orphaned buffers, so the page can offer "Recover recording" |

Housekeeping: on shell startup, and when the page reports sign-out, delete every buffer older than the TTL
(Q6). The shared `std::env::temp_dir()` location is no longer used.

**Web integration.** Put a small adapter in `hooks/useSpeakingSessionRecorder.ts`:
- `ondataavailable` → `append` (fire and forget, errors logged). The in-memory `chunksRef` stays the primary
  copy.
- `stop()` → `speaking_audio_stop`.
- After `uploadSpeakingSessionRecording` resolves → `discard`.
- On mount, if `list_pending` contains this `sessionId` and the server shows no recording, offer "Recover and
  upload".

`hooks/useSpeakingDualTrackRecorder.ts` has no consumers. Leave it alone; removing it is a separate cleanup.

**Capability.** New file `app-remote-speaking-audio.json`, pinned to `https://app.oetwithdrhesham.co.uk/speaking/*`
(Q11: add `/conversation/*` only if role-play recording starts using the fallback).

**Data protection.** See section 4: ciphertext at rest, deleted on upload confirmation, TTL sweep, never sent
anywhere by the shell itself. Consent text on the Speaking pre-check must mention the temporary local copy on
desktop (Q6).

**Fallback.** On browsers, mobile and older shells, the current in-memory behaviour.

**Telemetry.** `speaking_local_buffer` with `{ event: started|appended_fail|recovered|discarded|ttl_swept, sizeBucket }`.
No audio, no transcript.

**Tests.**
- Rust: append ordering and gap rejection, size caps, encrypt/decrypt round trip, recovery after a simulated
  restart (a new state object reading the same directory), TTL sweep with an injected clock, and path
  traversal through `sessionId`.
- Vitest: the adapter's fallback when the capability is missing or `append` rejects.
- Manual on hardware: kill the process mid-recording and recover after relaunch, on Windows and macOS.

### 6.5 `offline_cache_*` (Phase 4, optional)

**Feature.** A durable outbox for queued offline answers.
- `lib/mobile/offline-sync.ts::queueOfflineAttempt` already encrypts every Listening and Reading answer
  queued while offline, and `syncPendingAttempts` / `enableAutoSync` replay them.
- On desktop, write each attempt's ciphertext through to `offline_cache_store('outbox.<attemptId>', ...)`.
- Mirror the offline-crypto per-device salt from the IndexedDB meta store to `outbox.__salt`. If IndexedDB is
  lost, the outbox can then be rebuilt from the native copy (the key still comes from the session).
- `markAttemptSynced` deletes both copies.
- `offline-crypto.ts` was written for this: "This applies to both the desktop offline cache (JSON) and
  Capacitor IndexedDB storage."

Once P0-1 stops `hard_reload` wiping IndexedDB, the extra protection is limited to WebView profile corruption
or reset. **This phase is optional (Q7).**

**Rust hardening:**
- Keys must match `^(outbox\.[A-Za-z0-9_-]{1,80}|outbox\.__salt)$`.
- At most 256 KB per entry, 500 entries and 50 MB in total. Stores are rejected past these limits.
- `list` returns only `outbox.*` keys and sizes.
- The directory moves to `<app_data>/offline-outbox/`.
- `clear` runs on sign-out of a different account. The existing rule "another account cannot read them"
  (`clearOfflineEncryptionKey`) holds because payloads are encrypted under the session-derived key.
- Never store plaintext: reject any value missing `_encrypted: true`.

**Not in scope.** Exam audio, video, papers and reading passages are never cached offline (section 4). A
"download practice pack" feature would be a separate, owner-approved product decision with its own content
licensing review.

**Capability.** `app-remote-offline-outbox.json`, pinned to `/listening/*` and `/reading/*`, where
`queueOfflineAttempt` is called (`app/listening/player/[id]/page.tsx`, `app/listening/paper/[paperId]/page.tsx`,
`app/reading/paper/[paperId]/page.tsx`).

**Fallback.** IndexedDB only, as today.

**Telemetry.** `offline_outbox` with `{ event: mirrored|restored|synced|quota_rejected }`.

**Tests.** Rust quota, key-grammar and plaintext-rejection tests. A vitest for the offline-sync desktop
mirror, including "IndexedDB empty but native outbox present → restore".

### 6.6 `get_dropped_file_info` (Phase 5: retire)

**Why not grant it.**
- It is a filesystem oracle: any path in, existence, size, modified time and full path out.
- The remote page never receives native drop events, because no event capability is granted and the shell does
  not forward them.
- It returns metadata only, so it cannot power an upload by itself. Doing that would need a file-read command,
  which is a much larger grant.

**Replacement.** P0-2 turns off the native drag-drop handler, so HTML5 `DataTransfer.files` gives the page
proper `File` objects. The existing upload paths then work unchanged: chunked admin upload
(`lib/content-upload-api.ts`), Bunny TUS (`lib/video/bunny-tus-upload.ts`) and the billing proof dropzone.

**Removal.**
- Delete the command from `build.rs`, `lib.rs` and `commands.rs`.
- Delete `fileInfo` from the bridge, `types/desktop.d.ts` and the conformance test's `EXPECTED_SHAPE`.
- Delete `src-tauri/permissions/autogenerated/get_dropped_file_info.toml` (it regenerates).

If the owner wants to keep it (Q8), the only acceptable design passes an opaque, single-use token from a
native drop event (forwarded by `win.eval`, as `desktop:update-progress` is), never a raw path.

---

## 7. Failure and fallback matrix

| Runtime | Capability present | Flag on | Behaviour |
|---|---|---|---|
| Desktop, new shell | yes | yes | Native path. On any rejection, fall back silently to the web path and emit `outcome: "fallback"` |
| Desktop, new shell | yes | no | Web path (kill switch) |
| Desktop, older shell | no (`nativeCapabilities` missing) | any | Web path. Do not attempt IPC |
| Browser | n/a | any | Web path: `window.open`, web push, in-memory recorder, IndexedDB |
| Android / iOS | n/a | any | Capacitor path: Browser, push, native recorder, secure storage |

**No native failure may block an exam action.** The Speaking and offline paths add durability; they never gate
submission.

---

## 8. Telemetry

- **Web side.** Add the event names above to `TRACKED_EVENTS` in `lib/analytics.ts` (events must be listed to be
  tracked). Every event carries `shellVersion` (from `runtime_info().appVersion`) and `outcome`
  (`ok|rejected|fallback|error`). Nothing identifying: no URLs beyond the host, no titles or bodies, no keys or
  values, no audio. Errors go through the existing Sentry setup (`lib/observability/sentry-shared.ts`).
- **Shell side.** The shell makes **no network calls of its own**, keeping it a thin client. Validation
  rejections and I/O errors are appended to `<app_data>/logs/desktop.log`, where the panic hook in `lib.rs`
  already writes. Size-capped, no personal data.
- **Per-phase dashboards** (grants actually exercised, rejections by code, fallbacks) feed the go/no-go at each
  phase's acceptance gate.

---

## 9. Testing strategy and CI

| Layer | What | Where it runs |
|---|---|---|
| Rust unit | Every validator, allowlist, cap and rate limiter, using injected clocks as in `attestation.rs::rate_check`. Crypto round trips. Crash recovery with a new state reading an old directory | `tauri-ci.yml` → "Rust gate" (`cargo test --all-features`, Windows) |
| Rust lint | `cargo fmt --check` and `cargo clippy -D warnings` | same job |
| Capability lint | P0-6: the exact grant set per capability file and URL pattern, and the `build.rs` list against `generate_handler!` | `tauri-ci.yml` → `conformance` (Ubuntu, vitest) |
| Bridge conformance | `src-tauri/__tests__/desktop-bridge-conformance.test.ts`, updated for the optional namespaces, `nativeCapabilities` and the new `speaking_audio_append` argument names | `tauri-ci.yml` → `conformance` |
| Web unit | `lib/shell/open-external.ts`, the notification decision table, the `lib/device-id.ts` desktop branch, the recorder adapter and the offline-sync mirror, all with a mocked `window.desktopBridge` | `qa-smoke.yml` → `frontend-unit` |
| Web E2E | The fallback paths (no bridge) are covered by the existing Playwright smoke suites. No desktop-specific Playwright | `qa-smoke.yml` → `e2e-smoke` |
| Desktop E2E | `tauri-driver` (WebDriver; Windows through msedgedriver; macOS is not supported by tauri-driver). Needs an `e2e` Cargo feature that compiles a **test-only** loopback capability granting the new commands to a CI-served build of the web app. The feature must never be enabled in `tauri-desktop-release.yml`, and the P0-6 lint asserts that | New `tauri-e2e` job in `tauri-ci.yml`, `windows-latest` |
| Launch smoke | Existing Windows and macOS launch smokes | `tauri-ci.yml` |
| Manual hardware matrix | Windows 10 and 11; macOS 12–15, Intel and Apple Silicon. Toasts, keyring prompts, recovery after kill, drag-drop | Owner or QA, per phase |

Following the repo compute rule, every automated check runs on GitHub Actions only.

---

## 10. Rollout phases

Each phase ships the Rust hardening and the grant in a desktop release, while the web code stays dark behind a
server flag. The phase then goes canary (`RolloutPercentage`) → 100% → flag removal. Flag names are proposals.

| Phase | Commands | Flag | Acceptance criteria | Effort | Risk |
|---|---|---|---|---|---|
| **0** | none (fixes) | none | Section 5 acceptance. CI capability lint green. No change to the grant set | ~4 days + desktop release | Low to medium (Reload semantics) |
| **1** | `open_external`, `show_notification` | `desktop_open_external`, `desktop_os_notifications` | Checkout, Zoom and store links open in the default browser on Windows and macOS. A non-allowlisted host is refused. A grading-complete notification shows a toast while minimised and none while focused; clicking it (where the OS delivers clicks) opens the linked route; quiet hours respected (server-side) | ~4 days + release | Low |
| **2a** | `secret_*` (device key only) | `desktop_keyring_device_id` | The device ID survives Reload, cache clear and reinstall (the keyring persists); the existing web ID is adopted on first run; the device-slot count does not rise after upgrading; a keyring error never blocks sign-in | ~2 days + release | Medium (auth-adjacent) |
| **2b** | `secret_*` (refresh token) | `desktop_keyring_session` | Only if Q5 is approved: the learner stays signed in after a WebView profile reset; sign-out deletes the entry; nothing is written to web storage | ~2 days + release | Medium to high |
| **3** | `speaking_audio_*` (redesigned) | `desktop_speaking_local_buffer` | Killing the app 3 minutes into a recorder-fallback session and relaunching offers "Recover and upload" and uploads the full audio; after a confirmed upload no buffer remains on disk; buffers past the TTL are swept at startup; no plaintext audio on disk (checked in a test) | ~6 days + release | Medium to high (exam audio, personal data) |
| **4** | `offline_cache_*` (outbox) | `desktop_offline_outbox` | Only if Q7 is approved: a queued offline answer survives a WebView profile reset and syncs; quotas enforced; another account cannot read it | ~4 days + release | Medium |
| **5** | retire `get_dropped_file_info` | none | Command, bridge member and type removed; conformance and capability lint updated | ~0.5 day (in the next desktop release) | Low |

**Order: 0 → 1 → 2a → 3 → 5, with 2b and 4 only on owner approval.**

**Rollback:**
- Flag off: instant.
- Grant removal: the next desktop release.
- Forced move off a bad shell: `DesktopMinSupportedVersion` / `DesktopForceUpdate`.

---

## 11. Effort and risk summary

About **17–21 engineer-days** for phases 0, 1, 2a, 3 and 5, plus about 6 days if 2b and 4 are approved. Each
phase also needs a platform-scoped desktop release and a hardware pass. The main risks:

1. **A third-party script compromise inherits the grants** (section 2.2). This is mitigated by allowlists,
   caps, path pinning and never returning data the page did not provide. The residual risk is highest for
   Phase 3 (audio) and Phase 2b (session token), which is why those carry the most validation and are
   approval-gated.
2. **Platform behaviour we cannot see from the code.** Everything marked **verify**: `window.open`
   handling, drag-drop, toast identity, Keychain prompts, URL pattern matching and IPC transport under CSP.
   Each is checked before the phase that relies on it.
3. **Changing what Reload does** (P0-1) is visible to learners and support.

---

## 12. Open questions for the owner

1. **Count.** The shell has **16** registered-but-ungranted commands, not 15 (list in section 2.3). Confirm this
   plan should cover all 16.
2. **Reload.** Should the desktop "Reload" button clear only caches (like a browser Ctrl+F5, keeping the learner
   signed in with pending work intact), instead of wiping all app data as it does today?
3. **External links.** Approve the host allowlist in 6.1. For any other host, choose between refusing and
   showing the link to copy, or a native confirm dialog. The dialog needs `tauri-plugin-dialog`, which would
   be a new dependency: `src-tauri/Cargo.toml` does not include it. (`docs/tauri-desktop-shell.md` used to
   list it; this PR corrects that.)
4. **Desktop notifications.** Which categories should show OS toasts? Should toast bodies be generic for
   results and billing? Should desktop toasts be on by default, or opt-in in notification settings?
5. **Session storage.** Should desktop keep the refresh token in the OS keyring (2b)? Separately, should the
   backend stop returning the refresh token in the response body to `X-OET-Client-Platform: desktop` once the
   cookie path is reliable?
6. **Speaking audio.** What is the TTL for a local buffer that never uploaded (proposed: 7 days)? Approve the
   consent wording and the "Recover recording" flow.
7. **Offline outbox.** Build Phase 4, or rely on IndexedDB once P0-1 lands?
8. **Dropped-file probe.** Approve retiring `get_dropped_file_info` in favour of HTML5 drag-and-drop.
9. **macOS signing.** Keyring (Phase 2) and toasts (Phase 1) on macOS depend on a signed app for a smooth
   experience. Will the `APPLE_*` secrets be provisioned before those phases, or should they ship Windows-first?
10. **Linux.** The capability files list `linux`, but no Linux build is released. Remove `linux` from the new
    grants?
11. **Conversation / role-play.** Should the local audio buffer also cover `/conversation/*` sessions, or only
    the Speaking recorder fallback?

---

## Appendix: files this plan is grounded in

- **Shell:**
  - `src-tauri/src/commands.rs`, `src-tauri/src/lib.rs`, `src-tauri/src/runtime.rs`, `src-tauri/src/attestation.rs`
  - `src-tauri/build.rs`, `src-tauri/Cargo.toml`, `src-tauri/tauri.conf.json`
  - `src-tauri/capabilities/{app-remote,app-remote-macos,default,dev-localhost}.json`, `src-tauri/permissions/autogenerated/`
  - `src-tauri/inject/desktop-bridge.js`, `src-tauri/splash/`
  - `src-tauri/__tests__/desktop-bridge-conformance.test.ts`
- **Contract:** `types/desktop.d.ts`
- **Web:**
  - Shell and runtime: `lib/shell/hard-reload.ts`, `lib/shell/update-controller.ts`, `lib/runtime-signals.ts`,
    `lib/client-version.ts`, `proxy.ts`
  - Auth and identity: `lib/auth-client.ts`, `lib/auth-storage.ts`, `lib/device-id.ts`,
    `lib/mobile/secure-storage.ts`
  - Offline: `lib/mobile/offline-sync.ts`, `lib/mobile/offline-crypto.ts`, `lib/listening/audio-prebuffer.ts`
  - Speaking: `hooks/useSpeakingSessionRecorder.ts`, `hooks/useSpeakingDualTrackRecorder.ts`,
    `lib/speaking/dual-track-recorder.ts`, `lib/api/speaking-sessions.ts`
  - External links and checkout: `lib/mobile/web-checkout.ts`, `lib/native/billing-bridge.ts`,
    `app/classes/[id]/sessions/[sessionId]/join/page.tsx`
  - Notifications: `contexts/notification-center-context.tsx`, `lib/types/notifications.ts`
  - Uploads: `components/billing/proof-dropzone.tsx`
  - Flags and telemetry: `lib/analytics.ts`, `hooks/use-feature-flag-map.ts`
- **Backend:** `Services/AuthService.Sessions.cs`, `Security/CookieBackedAuthCsrfGuard.cs`,
  `Domain/AdminEntities.cs` (`FeatureFlag`), `Domain/LaunchReadinessSettings.cs`,
  `Middleware/ClientVersionGateMiddleware.cs`
- **CI and docs:** `.github/workflows/tauri-ci.yml`, `.github/workflows/tauri-desktop-release.yml`,
  `docs/tauri-desktop-shell.md`, `docs/app-release-playbook.md`, `docs/speaking-module-runbook.md`

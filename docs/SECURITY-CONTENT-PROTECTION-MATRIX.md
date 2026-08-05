# Course Platform Security — Platform Matrix & Implementation Notes

Companion to `OET_Course_Platform_Security_Requirements.md` (25 Jul 2026). This
document is the developer deliverable required by that spec's §5: a
platform-by-platform matrix, an explanation of how each control actually
works, and an honest list of technical limitations with their compensating
controls. It reflects the implementation state as of the dates noted per
section — some rows are shipped and live in production, others are designed
and partially built. Each row says which.

## 1. Final confirmation table (spec §5)

| Requirement area | Status | Notes |
|---|---|---|
| Screenshot protection | **Implemented** (Win/macOS/Android) / **Compensated limitation** (iOS/web) | Windows/macOS/Android block capture at OS level. iOS detects a still capture, immediately pauses/mutes playback, logs it, and revokes the playback session. Web has no capture API, so it uses the moving identifying watermark, short token, session enforcement, and audit controls. |
| Screen-recording protection | **Implemented** (Win/macOS/Android/iOS) | All four platforms blank the app during active recording/mirroring. See §3. |
| Dynamic watermark | **Implemented** | Shipped and live — full name, masked email, live clock, session reference; moves on a randomized interval; tiled low-opacity forensic layer; integrity watchdog. See §2. |
| DRM and encrypted streaming | **Application ready; provider activation unverified** | Clients receive only a 5-minute token-authenticated Bunny embed; raw HLS/MP4 URLs are no longer exposed. That player is the required MediaCage surface. Basic or Enterprise DRM must still be enabled in the Bunny account and proven with a real library/video. See §4. |
| One active session only | **Implemented** | Shipped and live — signing in anywhere revokes every other session within seconds via a SignalR push, with a family-liveness check as the hard backstop. See §5. |
| Device binding and reset | **Implemented and enforced** | Email-OTP challenge on a new device, old-device session revocation, self-service flow, admin reset, and change cooldown. The production profile enables `SecurityTrustedDeviceRequired`; completed production trust-device OTPs were observed before activation. |
| IP and risk monitoring | **Implemented and enforced; provider token pending** | Country-change, impossible-travel, device-churn, step-up, and high-risk blocking run in `enforce` mode. IPinfo Core/Plus/Max can now be enabled with an encrypted runtime token; production provider activation remains an account action. See §7. |
| Audit logs and admin controls | **Implemented** | `SecurityEvents` telemetry, a full `/admin/security` console (events feed, filters, computed alerts), and per-account session/device management (targeted revoke, device reset, block playback) are all live. See §8. |
| Root/jailbreak/emulator detection | **Implemented (heuristics)** | Android + iOS native plugins report best-effort signals on every playback-session request; `VideoProtectionBlockRootedDevices`/`BlockEmulators` (default: on) reject a new session with 403 `device_integrity` when present. See §10. |

---

## 2. Capture protection — platform matrix

| Platform | Screenshot | Screen recording / mirroring | Mechanism | Status |
|---|---|---|---|---|
| **Windows (Tauri)** | Blocked | Blocked | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the app window (`src-tauri/src/commands.rs`). Combined with `--disable-gpu-compositing` and `--disable-direct-composition-video-overlays` on the WebView2 instance (`src-tauri/src/lib.rs`) — without those flags, Chromium promotes hardware-decoded video onto a separate DirectComposition overlay plane that DWM scans out independently of the protected window surface, so the *user's own view* would go black too. Disabling GPU compositing forces the frame to composite into the protected surface instead. | **Shipped** |
| **macOS (Tauri)** | Blocked | Blocked | `NSWindow.sharingType = .none` (`src-tauri/src/commands.rs`). | **Shipped** |
| **Linux (Tauri)** | Not blocked | Not blocked | No equivalent API wired; `apply_capture_protection` returns `false` (no-op). | **Not implemented** — documented limitation, no compensating control beyond the watermark. Not currently a supported desktop target. |
| **Android (Capacitor)** | Blocked | Blocked | `WindowManager.LayoutParams.FLAG_SECURE` (`PlaybackAttestationPlugin.java`). The OS itself renders both stills and recordings as black — there is nothing to *detect*, the block is unconditional. | **Shipped** |
| **iOS (Capacitor)** | **Not blockable** | Blocked | Screen recording/mirroring: observes `UIScreen.capturedDidChangeNotification` and overlays an opaque black view for the duration (`PlaybackAttestationPlugin.swift`, `enableCaptureBlackout`). Still screenshots: iOS provides no API to make an arbitrary WKWebView render black in a screenshot — only DRM/AVPlayer-protected content gets that treatment from the OS. | Recording: **shipped**. Screenshot: **limitation** — see §3. |
| **Web (browser)** | Not blockable | Not blockable | The course remains functional on web as required. Playback uses a 5-minute signed secure embed, server-side entitlement/session checks, a moving identifying watermark, single-session enforcement, and immediate audit/revocation for signals the browser can provide. | **Implemented with compensating controls** — browsers expose no reliable screenshot/recording-block API. |

**OBS / third-party capture tools, remote desktop, HDMI capture, camera-at-screen:** OBS and generic screen-recording tools ride the same OS capture APIs as the built-in recorder on Windows/macOS/Android, so they are blocked identically. Remote-desktop protocols that use a different scan-out path than the standard capture API (some RDP shadowing modes) are a known theoretical gap — not separately tested. A physical camera pointed at a screen, or an HDMI capture card between the GPU and a monitor, defeats **any** software control on **any** platform; the moving, identifying watermark is the sole compensating control for that class of attack, on every platform, by design.

## 3. Why iOS still-screenshot blocking is not attempted

The one documented technique that can make an arbitrary view black out during
a screenshot — reparenting content under a `UITextField` with
`isSecureTextEntry = true` and exploiting how iOS renders secure text layers
— is an undocumented, unsupported side effect of a private rendering
behavior. It has changed across iOS point releases before, would need to
wrap the *entire* app UI (the video renders inside the WebView, there is no
separable native video layer the way a real AVPlayer would have), carries a
real risk of blanking the whole app for every user on an OS update, and
sits in a grey area for App Store review. The engineering judgment made here
is: do not ship it. Apps that truly block still-screenshots of premium video
(Netflix, etc.) do it via **FairPlay DRM**, which decodes into a
hardware-protected surface the OS itself refuses to capture — see §4 for why
that is a deliberate non-goal for now.

The compensating controls actually shipped: (1) the watermark is visible and
identifying at the exact moment a screenshot is taken, so a leaked still
still traces back to the account; (2) iOS screenshot **detection** — via
`UIApplication.userDidTakeScreenshotNotification` (Phase 8, shipped) —
reports a `screenshot_detected` protection event to the server, immediately
pauses and mutes playback, and shows the learner a brief notice. With
`VideoProtectionRevokeOnCaptureDetected` enabled, the server revokes that
playback session. This reacts after capture rather than preventing the
captured frame, which is the honest limit of non-DRM iOS content.

## 4. DRM and encrypted streaming

The application path is now MediaCage-ready. `BunnyStreamClient` signs the
Bunny iframe with `SHA256(libraryApiKey + videoId + expires)`, the API clamps
that view token to 5–15 minutes (the production profile sets 5 minutes), and
the frontend renders the provider player inside the first-party fullscreen
container so the forensic watermark remains above it. Raw HLS and MP4 URLs
are not returned to the client. The iframe is origin-checked, sandboxed, and
controlled with a minimal audited `player.js` postMessage adapter.

The remaining step is account-level provider activation and live evidence.
Bunny MediaCage Basic supplies dynamic clear-key encryption/download
resistance through this embed. Enterprise DRM adds Widevine and FairPlay,
including the stronger protected-surface behavior needed for iOS
still-screenshot blocking; it requires a Bunny enterprise contract and
Apple FairPlay certificate material. Neither tier can be asserted active
from source code alone, so this row remains explicitly unverified until the
Bunny library setting and a real playback/network test are recorded.

## 5. Single active session (spec §3.1) — shipped

- **Mechanism:** every access token now carries a `sfam` claim — the
  refresh-token *family* id, which is the identity that survives token
  rotation (unlike the per-token `sid`). Signing in anywhere with
  `SecuritySingleActiveSessionEnabled` on (default: on) revokes every other
  family for the account via a new `SessionRevocationService`, which is the
  single choke point every revocation path (self-serve, sign-in-elsewhere,
  admin, suspend, password reset) now goes through.
- **Immediacy:** the revoked device receives a SignalR `session_revoked`
  push within the same request that revoked it (if its notification socket
  is connected) and is force-signed-out with a "signed in on another device"
  banner. A video player open on that device pauses on the same push. If the
  push is missed (socket briefly disconnected), the hard backstop is a
  per-request family-liveness check added to the JWT validation pipeline —
  the very next API call from the revoked device fails, and the existing
  401-handling flow signs it out.
- **Copied tokens/cookies:** a copied access token dies at its very next use
  once the family is revoked (family-liveness check). A copied refresh
  cookie was already defended by pre-existing single-use rotation +
  reuse-detection (presenting an already-rotated token burns the whole
  family) — that mechanism is unchanged and still the second line of
  defense.
- **Relationship to device binding:** single active session does not by
  itself require a device to be "known," only that there is exactly one live
  session at a time. Device *binding* (spec §3.2) is a separate, additional
  gate — see §6 — is also enforced.

## 6. Device binding (spec §3.2) — shipped and enforced

A `TrustedDevice` row per approved client identity; a client-generated device id
(`lib/device-id.ts`) sent as the `X-OET-Device-Id` header on every auth
request; the default is one active identity per account; a sign-in from a
*different* identity triggers an email one-time-code challenge (reusing the
existing OTP infrastructure) — the learner completes it at
`app/(auth)/device/verify`, mirroring the MFA-challenge flow exactly.
Approving a new identity at capacity revokes the replaced identity's sessions (via the same
`SessionRevocationService` from §5) and logs the change; a cooldown blocks
more than a handful of device changes in a rolling week. An admin can also
reset an account's trusted device from `/admin/security` or the user detail
page, which revokes its sessions the same way.

An admin may set a positive per-learner approved-identity override from 1
through 5. It retains more approved identities but does not disable the
global single-active-session rule. Browser profiles, app installations, and
same-hardware browser/app pairs are counted exactly as documented in
`docs/SECURITY-DEVICE-POLICY.md`.

`SecurityTrustedDeviceRequired` defaults on in the mandatory production
profile. Before activation, the production OTP ledger showed successful
`trust_device` challenge completions. The activation migration revokes
legacy active refresh tokens that have no device id so they cannot bypass
the binding rule. Web/desktop device ids initialize synchronously; native
auth requests await keychain/keystore initialization. A fresh enforced
sign-in with no device id is rejected with `device_id_required`; malformed
identities use `device_id_invalid`, and both are recorded as device rejections.

## 7. IP / location risk signals (spec §3.3) — shipped and enforced

A rule-based `SignInRiskService` evaluates every fresh sign-in against the
account's own history (no external IP-intelligence lookup yet):

- **Country changed** since the last recorded sign-in (CF-IPCountry header)
  → medium risk.
- **Impossible travel** — a country change within 2 hours of the previous
  sign-in → high risk.
- **Device/family churn** — 5 or more distinct sessions created in a rolling
  7 days → medium risk.

Every signal is recorded as a `SecurityEvent` regardless of mode. In the
production `enforce` mode, a Medium-risk sign-in must
pass an email-OTP step-up (the same challenge transport as device
verification; sign-ins that already carried a second factor skip it) and a
High-risk sign-in is rejected outright with `403 sign_in_blocked_risk`,
raising an admin notification and a security-alert email to the account
owner. The risk check runs *before* any existing session is touched, so a
false-positive block never costs the account its legitimate session.

A **country allow-list** is available as an independent fence
(`security.countryAllowList` + `security.countryAllowListMode`, default
off): sign-ins from outside the listed ISO codes are either challenged
(`step_up`) or rejected (`block`); unknown-country sign-ins always pass.
Datacenter/VPN/Tor detection uses the production-configurable
`IpinfoIpIntelligenceService`. It sends only parsed public addresses over
HTTPS using bearer authentication, maps IPinfo anonymity/hosting flags,
caches results for one hour, and fails open on provider timeout/error. The
token is stored encrypted through runtime settings and never returned
unmasked. Until a paid Core/Plus/Max token is stored and provider `ipinfo`
selected, lookups remain disabled; static IP-range lists are intentionally
not used.

## 8. Audit logging & admin controls (spec §4.4) — shipped

A partitioned `SecurityEvents` table records sign-in success/failure,
sign-out, MFA failure, refresh-token reuse, session created/revoked,
playback session start, risk signals, and admin-initiated security actions —
each with account, IP, country, user agent, platform, and session-family id
where applicable. Behind a `security:read` / `security:write` admin
permission pair:

- `GET /v1/admin/security/events`, `GET /v1/admin/security/video-protection-events`
  — the raw feed, with a dedicated `/admin/security` console (filters by
  kind/severity/account, event-detail drawer) plus computed alerts
  (impossible travel, refresh-token reuse, device-change cooldown hits,
  capture/tamper events) surfaced there and folded into `/admin/alerts`.
- `GET/POST /v1/admin/users/{userId}/security/sessions` (list + targeted
  revoke by family id), `GET/POST /v1/admin/users/{userId}/security/devices/reset`,
  `POST /v1/admin/users/{userId}/security/block-playback` — all routed
  through `ISessionRevocationService`/`ITrustedDeviceService` so the same
  playback-kill/push/audit guarantees apply as any other revoke. Surfaced on
  the admin user detail page's "Sessions & Devices" section.

The pre-existing `AuditEvent` trail (admin actions) is unchanged and still
the system of record for admin-initiated changes; every mutation above is
dual-logged (a `SecurityEvent` from the account's perspective, an
`AuditEvent` from the admin's).

## 9. Capture-protection telemetry (spec §2, cross-cutting)

A `VideoProtectionEvents` pipeline (`POST
/v1/video-library/protection-events`) ingests: OS capture-protection
engaged/unavailable, watermark tampering, focus/visibility loss (risk
signals only, never a gate), iOS screenshot/capture detection (Phase 8,
shipped), and device-integrity signals (rooted/jailbroken/emulator — Phase 8,
shipped, persisted server-side directly from the playback-session request
rather than the client batch endpoint). High-severity kinds also write to
the admin audit trail, and a capture/screenshot-detected event can — subject
to `VideoProtectionRevokeOnCaptureDetected` (default: on) — immediately
revoke that playback session.

## 10. Root / jailbreak / emulator / debugger detection — shipped

Android/iOS native-plugin heuristics (test-keys build tag, `su` binary
probing, emulator fingerprint matching, `Debug.isDebuggerConnected()` on
Android; jailbreak file-path probing, a Cydia URL-scheme probe, a
sandbox-escape write test, `DYLD_INSERT_LIBRARIES` inspection on iOS —
deliberately no `fork()` test, to stay inside App Store review guidelines,
and no private-API usage) are reported on every playback-session request
(`lib/mobile/playback-attestation.ts getDeviceIntegrity()`), independent of
the HMAC attestation payload. `VideoProtectionBlockRootedDevices` /
`VideoProtectionBlockEmulators` (both default: on, owner directive) reject
the session outright with 403 `device_integrity` when the corresponding
signal category is present; every signal is persisted as an
`integrity_signal` event regardless of the block decision. A client that
sends no integrity field at all (desktop, web, or an old mobile shell that
predates this) always fails open — nothing here can regress an existing
build. These are heuristics, not hardware attestation (Play Integrity / App
Attest) — evadable by sufficiently determined tooling (Magisk hides most of
the Android checks), which is why the spec's "where reasonably detectable"
qualifier is the right bar for a first version.

**Release state:** the signed Android v1.4.1 APK/AAB contains the Phase 8
plugin and is published; signed Windows/macOS desktop 0.7.0 is also
published. The iOS source is implemented, but no signed IPA
has been produced because the Apple team id, distribution certificate,
provisioning profile, and valid associated-domain file are not available to
the release workflow. Desktop
(Tauri) integrity heuristics are explicitly **out of scope for v1** — the
capture blackout (§2) already blanks the screen regardless of device
integrity, so the marginal security value is lower there; documented as a
residual gap, not silently skipped.

## 11. Direct-file closure and provider verification

The learner API no longer emits directory-scoped CDN tokens, HLS manifests,
or MP4 URLs. It emits only a short-lived token-authenticated embed URL, so
the former token-reuse path to `play_720p.mp4` is closed in application
code. MediaCage must still be enabled in the Bunny library and verified
against a real video; Basic DRM disables direct downloads through the embed,
while Enterprise DRM provides Widevine/FairPlay license enforcement.

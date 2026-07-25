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
| Screenshot protection | **Implemented** (Win/macOS/Android) / **Partial** (iOS) | Windows/macOS/Android block the capture at the OS level. iOS cannot block a still screenshot of an arbitrary WKWebView — see §3.3. Detection-after-the-fact is designed (Phase 8), not yet shipped. |
| Screen-recording protection | **Implemented** (Win/macOS/Android/iOS) | All four platforms blank the app during active recording/mirroring. See §3. |
| Dynamic watermark | **Implemented** | Shipped and live — full name, masked email, live clock, session reference; moves on a randomized interval; tiled low-opacity forensic layer; integrity watchdog. See §2. |
| DRM and encrypted streaming | **Not implemented** (documented upgrade path) | Native-only playback + token-signed HLS + OS capture blocking is the current model. No DRM/EME. See §4. |
| One active session only | **Implemented** | Shipped and live — signing in anywhere revokes every other session within seconds via a SignalR push, with a family-liveness check as the hard backstop. See §5. |
| Device binding and reset | **Designed, not yet shipped** | See §6 — next planned increment. |
| IP and risk monitoring | **Implemented (log-only)** | Shipped and live in log-only mode — country-change and impossible-travel detection, device-churn detection. Enforcement mode (blocking) is built and ready but held off pending a review week. See §7. |
| Audit logs and admin controls | **Implemented (backend); admin UI partial** | `SecurityEvents` telemetry + a read-only admin API are live. A full admin console (per-account session/device management, computed alerts) is designed but not yet built. See §8. |

---

## 2. Capture protection — platform matrix

| Platform | Screenshot | Screen recording / mirroring | Mechanism | Status |
|---|---|---|---|---|
| **Windows (Tauri)** | Blocked | Blocked | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` on the app window (`src-tauri/src/commands.rs`). Combined with `--disable-gpu-compositing` and `--disable-direct-composition-video-overlays` on the WebView2 instance (`src-tauri/src/lib.rs`) — without those flags, Chromium promotes hardware-decoded video onto a separate DirectComposition overlay plane that DWM scans out independently of the protected window surface, so the *user's own view* would go black too. Disabling GPU compositing forces the frame to composite into the protected surface instead. | **Shipped** |
| **macOS (Tauri)** | Blocked | Blocked | `NSWindow.sharingType = .none` (`src-tauri/src/commands.rs`). | **Shipped** |
| **Linux (Tauri)** | Not blocked | Not blocked | No equivalent API wired; `apply_capture_protection` returns `false` (no-op). | **Not implemented** — documented limitation, no compensating control beyond the watermark. Not currently a supported desktop target. |
| **Android (Capacitor)** | Blocked | Blocked | `WindowManager.LayoutParams.FLAG_SECURE` (`PlaybackAttestationPlugin.java`). The OS itself renders both stills and recordings as black — there is nothing to *detect*, the block is unconditional. | **Shipped** |
| **iOS (Capacitor)** | **Not blockable** | Blocked | Screen recording/mirroring: observes `UIScreen.capturedDidChangeNotification` and overlays an opaque black view for the duration (`PlaybackAttestationPlugin.swift`, `enableCaptureBlackout`). Still screenshots: iOS provides no API to make an arbitrary WKWebView render black in a screenshot — only DRM/AVPlayer-protected content gets that treatment from the OS. | Recording: **shipped**. Screenshot: **limitation** — see §3. |
| **Web (browser)** | Not applicable | Not applicable | Browsers never receive playback at all — `getAppRuntimeKind()` routes web sessions to a "download the app" screen, and the server independently refuses to issue a playback session to a non-attested client regardless of what the client claims. | **Shipped** — the spec explicitly blesses restricting sensitive video to a native app when a browser can't provide reliable capture blocking (§2.2). |

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
`UIApplication.userDidTakeScreenshotNotification` — is designed (Phase 8) to
report the event to the server and, at the owner's option, revoke the
session; this catches it after the fact rather than preventing it, which is
the honest limit of what iOS allows for non-DRM content.

## 4. DRM and encrypted streaming

**Not implemented; a documented upgrade path, not a gap being silently
carried.** The current model is: native-app-only playback (§2 above),
HMAC-attested clients only, short-lived signed HLS URLs (see §5 of the spec
requirements and the `BunnyStreamClient` token scheme), and OS-level capture
blocking. There is no EME/Widevine/FairPlay/ClearKey anywhere in the stack,
and Bunny Stream's delivery is unencrypted-HLS-with-signed-URLs, not
encrypted-at-rest video.

**Upgrade path:** Bunny Stream offers a paid "MediaCage" DRM tier. Adopting
it would mean replacing the current `hls.js` pipeline with the browser's
Encrypted Media Extensions API, re-implementing the token-renewal and
`TokenPropagatingLoader` logic against a DRM license server instead of a
signed-URL scheme, and re-validating the WebView2 capture-protection fix
(the `--disable-gpu-compositing` flag interacts with how video frames are
composited — a DRM-decoded surface changes that path and would need
re-testing on every platform). This is a real project, not a toggle. Given
that native-only playback + OS capture blocking + the identifying watermark
already cover the realistic leak paths, and DRM's marginal win is
essentially "iOS still-screenshot blocking" (which needs FairPlay
specifically) plus wire-level encryption, this is recorded as an owner
decision to revisit, not something silently skipped. (`docs/CONTENT-UPLOAD-PLAN.md`
independently recorded a "no DRM needed" decision earlier in the project's
life — this is the same conclusion, restated with the reasoning.)

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
- **What is deferred:** device *binding* (spec §3.2 — requiring email
  verification of a genuinely new device, with a cooldown on frequent
  changes) is designed but not yet built; single active session does not by
  itself require a device to be "known," only that there is exactly one live
  session at a time.

## 6. Device binding (spec §3.2) — designed, not yet shipped

Planned design: a `TrustedDevice` row per account; a client-generated device
id sent as a header on every auth request; first sign-in from a device
auto-trusts silently; a sign-in from a *different* device triggers an email
one-time-code challenge (reusing the existing OTP infrastructure); approving
it revokes the previous device's sessions (via the same
`SessionRevocationService` from §5) and logs the change; a cooldown blocks
more than a handful of device changes in a rolling week. This is the next
planned increment — see the admin runbook for the toggle that will gate it.

## 7. IP / location risk signals (spec §3.3) — shipped, log-only

A rule-based `SignInRiskService` evaluates every fresh sign-in against the
account's own history (no external IP-intelligence lookup yet):

- **Country changed** since the last recorded sign-in (CF-IPCountry header)
  → medium risk.
- **Impossible travel** — a country change within 2 hours of the previous
  sign-in → high risk.
- **Device/family churn** — 5 or more distinct sessions created in a rolling
  7 days → medium risk.

Every signal is recorded as a `SecurityEvent` regardless of mode. In
`enforce` mode (not yet enabled — see runbook), a High-risk sign-in is
rejected outright with `403 sign_in_blocked_risk`; the risk check runs
*before* any existing session is touched, so a false-positive block never
costs the account its legitimate session. Datacenter/VPN/Tor detection and a
country allow-list are **not implemented** — reliable detection needs a paid
IP-intelligence feed (ipinfo/MaxMind), and static IP-range lists are not
trustworthy enough to act on; this is a clean, documented later integration
point (`IIpIntelligenceService` interface exists with a no-op default).

## 8. Audit logging & admin controls (spec §4.4)

**Shipped:** a new partitioned `SecurityEvents` table records sign-in
success/failure, sign-out, MFA failure, refresh-token reuse, session
created/revoked, playback session start, and risk signals — each with
account, IP, country, user agent, platform, and session-family id where
applicable. A read-only admin API (`GET /v1/admin/security/events`, `GET
/v1/admin/security/video-protection-events`) exists behind a new
`security:read` / `security:write` admin permission pair. The pre-existing
`AuditEvent` trail (admin actions) is unchanged and still the system of
record for admin-initiated changes.

**Not yet shipped:** a dedicated admin UI page for browsing this feed,
per-account session/device management (targeted revoke, device reset, block
playback) beyond the existing blunt "revoke all sessions" action, and
computed alerts (repeated device switching, simultaneous-use clusters,
abnormal geography) surfaced on the admin dashboard. This is the next
planned increment after device binding.

## 9. Capture-protection telemetry (spec §2, cross-cutting)

A `VideoProtectionEvents` pipeline (`POST
/v1/video-library/protection-events`) ingests: OS capture-protection
engaged/unavailable, watermark tampering, focus/visibility loss (risk
signals only, never a gate), and reserved slots for iOS screenshot/capture
detection and device-integrity signals (rooted/jailbroken/emulator —
Phase 8). High-severity kinds also write to the admin audit trail, and a
capture/screenshot-detected event can — subject to
`VideoProtectionRevokeOnCaptureDetected` (default: on) — immediately revoke
that playback session.

## 10. Root / jailbreak / emulator / debugger detection

**Not yet shipped.** Planned as Android/iOS native-plugin heuristics
(test-keys build tag, `su` binary probing, emulator fingerprint matching,
`Debug.isDebuggerConnected()` on Android; jailbreak file-path probing,
sandbox write tests, `DYLD_INSERT_LIBRARIES` inspection on iOS) reported
alongside the existing HMAC attestation payload. These are heuristics, not
hardware attestation (Play Integrity / App Attest) — evadable by
sufficiently determined tooling, which is why the spec's "where reasonably
detectable" qualifier is the right bar for a first version. Requires a
native mobile release to ship (not deployable via the web/API path alone).

## 11. MP4-fallback closure (owner action, not yet executed)

The Bunny Stream directory token that authorizes `/{videoId}/playlist.m3u8`
mathematically **also** authorizes `/{videoId}/play_720p.mp4` if MP4
Fallback is left on in the Bunny console — a single-request full-file pull
that bypasses the whole attestation/renewal dance. No client-side or
token-scoping fix closes this; it requires disabling MP4 Fallback in the
Bunny Stream library settings. See the admin runbook for the exact
checklist and the verification step.

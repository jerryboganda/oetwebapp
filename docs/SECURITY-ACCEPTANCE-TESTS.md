# Course Platform Security — Acceptance Test Report

Template + current results for the acceptance tests in
`OET_Course_Platform_Security_Requirements.md` §2.4 and §3.4. Columns to fill
in as each platform is actually tested against production: **Result**
(Pass / Partial / Fail / Not Applicable) and **Evidence** (screenshot,
log excerpt, or event-feed query link).

## §2.4 Capture-protection acceptance tests

| ID | Test | Windows | macOS | Android | iOS | Web |
|---|---|---|---|---|---|---|
| T1 | A standard screenshot attempt produces a black/protected result, or the video is hidden immediately. | Expected: Pass (`WDA_EXCLUDEFROMCAPTURE`) | Expected: Pass (`NSWindow.sharingType=.none`) | Expected: Pass (`FLAG_SECURE`) | **Expected: Fail to block, Pass to react** — iOS reports the still capture, then the app pauses/mutes and the server revokes the playback session. A signed iOS build is still unavailable for hardware proof. | **Platform limitation** — browsers expose no screenshot-block API; verify the identifying watermark is present in the captured frame. |
| T2 | Starting a built-in or third-party screen recorder pauses/blanks the video and stops audio. | Expected: Pass | Expected: Pass | Expected: Pass (recording is also blocked by FLAG_SECURE, produces black) | Expected: Pass (`enableCaptureBlackout` on `UIScreen.isCaptured`) | **Platform limitation** — no browser capture-state API; watermark/session/audit controls compensate. |
| T3 | OBS, screen mirroring, remote desktop, and common capture tools tested on each desktop platform; limitations reported with compensating control. | OBS: rides the same capture API, expected blocked. Remote desktop: **not separately tested** — some RDP shadowing modes use a different scan-out path; flag as unverified until tested. | OBS: expected blocked (same API). Screen mirroring (AirPlay/QuickTime): **not separately tested**. | — | — | Browser-level capture is not blockable; watermark evidence is required. |
| T4 | No permanent or directly reusable video URL is visible or functional outside the authorised session. | **Application Pass / provider proof pending** — the API returns only a 5-minute token-authenticated Bunny embed and never exposes HLS/MP4. Enable MediaCage in Bunny, load a real protected video, then verify a copied/expired embed token is denied and no direct media URL is reusable. | Same as Windows | Same | Same | Same |
| T5 | The dynamic watermark remains visible, moves during playback, and uniquely identifies the active account and session. | **Pass** — full name, masked email, live clock, session reference; randomized 20–45s position hop; visible above controls; survives fullscreen (container-level fullscreen). | Pass (same web overlay, same shell architecture) | Pass | Pass | Pass |

**Outstanding before T3/T4 can be marked production-proven:** (1) enable
MediaCage in the Bunny library and record a real embed/license/network test;
(2) run actual OBS/mirroring/remote-desktop attempts against signed desktop
and mobile builds and record the evidence here.

## §3.4 Account-sharing acceptance tests

| ID | Test | Result | Evidence |
|---|---|---|---|
| T1 | Log in on web, then on mobile with the same account. The web session must be terminated immediately. | **Expected: Pass** (shipped) — sign-in anywhere revokes every other session's refresh-token family; a connected device receives a live `session_revoked` push and is redirected to sign-in within the same request cycle; a disconnected device is caught by the family-liveness check on its very next API call. | Query `GET /v1/admin/security/events?kind=session.revoked` for the account immediately after the second sign-in; confirm a `session.created` immediately followed by a `session.revoked` (reason `new_sign_in`) for the first family. |
| T2 | Log in on mobile, then on desktop. The mobile session must be terminated. | **Expected: Pass** — same mechanism as T1, platform-agnostic. | Same query pattern as T1. |
| T3 | Attempt simultaneous playback from two devices using the same account. Only the latest authorised session may continue. | **Expected: Pass** — the earlier device's playback session is revoked as part of the same `SessionRevocationService` call that revokes its auth session; its next renew call gets `403 session_expired`, and if its notification socket is connected the player pauses immediately on the `session_revoked` push. | `GET /v1/admin/security/video-protection-events` / playback session records for the two `sessionId`s involved; confirm the first was revoked at the same timestamp as the auth session. |
| T4 | Copy the active session token or browser storage to another device. The copied session must fail. | **Expected: Pass** — a copied access token dies at its next request once the family is revoked. A copied refresh token was already defended pre-existing single-use rotation + reuse detection (presenting an already-rotated token burns the whole family) — unchanged by this work. | Attempt a request with a copied token after the legitimate session has rotated or been revoked; expect `401`. |
| T5 | Approve a new trusted device. The previously trusted device must be revoked and the change appear in the admin audit log. | **Enabled; production OTP transport proven; two-device acceptance still pending** — `security.trustedDeviceRequired` is part of the mandatory profile and successful `trust_device` OTP completions exist in production history. | Sign in from device A, then device B with the same test account; approve the email OTP; confirm device A is revoked and `device.trusted` plus session-revocation events appear in `/admin/security`. |

## Final confirmation table (spec §5, cross-reference)

See `docs/SECURITY-CONTENT-PROTECTION-MATRIX.md` §1 for the authoritative,
current-as-of-last-update version of this table (Implemented / Partial /
Not implemented per requirement area, with reasoning). Do not duplicate
maintenance of that table here — update it there and this document only
tracks per-platform test execution.

## How to (re-)run this report

Evidence for every test below now lives in the **`/admin/security`** console
(Phase 6) — sign in as an admin and use it directly instead of raw API
queries: filter by event kind/severity, filter by the test account's auth-
account id, and open a row for the full detail (IP, country, device, raw
JSON). It also surfaces computed alerts (impossible travel, refresh-token
reuse, device-change cooldown, capture/tamper) so a real problem during
testing shows up there without you having to know the exact event kind to
filter for.

1. **§2.4 T4 (secure delivery)** — enable MediaCage in the Bunny library.
   During real playback, confirm the application response contains only the
   signed `iframe.mediadelivery.net/embed/...` URL, no `.m3u8` or `.mp4`;
   confirm an expired or copied embed token is denied.
2. **§2.4 T1–T3 (capture protection)** — for each desktop platform, install a
   signed release on a clean machine and manually attempt a screenshot, a
   built-in/third-party screen recording, and (where applicable) OBS /
   screen mirroring / remote desktop while a video plays. Record Pass/Fail
   and a screenshot or short clip as evidence. For iOS specifically: take a
   screenshot during playback and confirm the on-screen "Screenshot
   detected" notice appears and a `screenshot_detected` event lands in
   `/admin/security`. Android v1.4.1 is available; the iOS check remains
   blocked until Apple signing assets produce an installable IPA.
3. **§3.4 T1–T4 (session sharing)** — use two real devices/browsers signed
   into the same test account and drive each test by hand; use
   `/admin/security` (filter by the test account) as evidence instead of a
   raw query.
4. **§3.4 T5 (device binding)** — the production security profile keeps
   `security.trustedDeviceRequired` ON. Run the two-device sequence with a
   dedicated test account and preserve the OTP, revocation, and audit
   evidence.
5. **§3.3 (VPN/proxy/Tor/datacenter signal)** — configure an IPinfo
   Core/Plus/Max token under Admin → Settings → Security, select `ipinfo`,
   then sign in from a known provider test address. Confirm the
   `high_risk_network` reason is recorded and the enforce-mode step-up is
   required. Also confirm an IPinfo outage does not prevent an otherwise
   low-risk learner sign-in.
6. Re-run this whole report whenever a deferred item's status changes.

## Device identity acceptance tests

The exact counting rule is documented in `docs/SECURITY-DEVICE-POLICY.md`.

| ID | Test | Expected result | Evidence |
|---|---|---|---|
| D1 | Use one browser profile with multiple tabs and windows. | One stable client identity and one approved-device slot. | `/admin/security` device events and the learner Sessions screen. |
| D2 | Use a second browser profile and an official app on the same physical hardware. | Each profile/install is a separate identity; each new identity follows OTP and device-slot policy. | Device IDs/platforms in admin security detail, with masked values in the UI. |
| D3 | Set an admin override from 1 to 2, approve two identities, then sign in on both sequentially. | Both identities may remain approved, but the earlier live session is still revoked by the global single-active-session rule. | `admin.device_limit_override`, `device.trusted`, and `session.revoked` events plus Audit Logs. |
| D4 | Reduce the override while two identities are active. | The oldest identities over the new limit are revoked, associated sessions are signed out, and the client receives the reduction message. | `device.revoked`, `session.revoked`, and AuditEvent records. |

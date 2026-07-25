# Course Platform Security — Acceptance Test Report

Template + current results for the acceptance tests in
`OET_Course_Platform_Security_Requirements.md` §2.4 and §3.4. Columns to fill
in as each platform is actually tested against production: **Result**
(Pass / Partial / Fail / Not Applicable) and **Evidence** (screenshot,
log excerpt, or event-feed query link).

## §2.4 Capture-protection acceptance tests

| ID | Test | Windows | macOS | Android | iOS | Web |
|---|---|---|---|---|---|---|
| T1 | A standard screenshot attempt produces a black/protected result, or the video is hidden immediately. | Expected: Pass (`WDA_EXCLUDEFROMCAPTURE`) | Expected: Pass (`NSWindow.sharingType=.none`) | Expected: Pass (`FLAG_SECURE`) | **Expected: Fail — documented limitation** (no OS API to black an arbitrary WKWebView on still capture; see content-protection matrix §3) | N/A — web never receives playback |
| T2 | Starting a built-in or third-party screen recorder pauses/blanks the video and stops audio. | Expected: Pass | Expected: Pass | Expected: Pass (recording is also blocked by FLAG_SECURE, produces black) | Expected: Pass (`enableCaptureBlackout` on `UIScreen.isCaptured`) | N/A |
| T3 | OBS, screen mirroring, remote desktop, and common capture tools tested on each desktop platform; limitations reported with compensating control. | OBS: rides the same capture API, expected blocked. Remote desktop: **not separately tested** — some RDP shadowing modes use a different scan-out path; flag as unverified until tested. | OBS: expected blocked (same API). Screen mirroring (AirPlay/QuickTime): **not separately tested**. | — | — | — |
| T4 | No permanent or directly reusable video URL is visible or functional outside the authorised session. | **Blocked on Bunny console change** — see admin runbook §4. Until MP4 Fallback is disabled, a captured directory token also authorizes a direct `.mp4` pull. Signed HLS URLs themselves already expire and are session-bound. | Same as Windows | Same | Same | N/A |
| T5 | The dynamic watermark remains visible, moves during playback, and uniquely identifies the active account and session. | **Pass** — full name, masked email, live clock, session reference; randomized 20–45s position hop; visible above controls; survives fullscreen (container-level fullscreen). | Pass (same web overlay, same shell architecture) | Pass | Pass | N/A |

**Outstanding before T3/T4 can be marked Pass:** (1) owner executes the Bunny
console MP4-Fallback change and the token-reuse verification step (admin
runbook §4); (2) a tester runs an actual OBS/mirroring/remote-desktop session
against a signed desktop build and records the result here.

## §3.4 Account-sharing acceptance tests

| ID | Test | Result | Evidence |
|---|---|---|---|
| T1 | Log in on web, then on mobile with the same account. The web session must be terminated immediately. | **Expected: Pass** (shipped) — sign-in anywhere revokes every other session's refresh-token family; a connected device receives a live `session_revoked` push and is redirected to sign-in within the same request cycle; a disconnected device is caught by the family-liveness check on its very next API call. | Query `GET /v1/admin/security/events?kind=session.revoked` for the account immediately after the second sign-in; confirm a `session.created` immediately followed by a `session.revoked` (reason `new_sign_in`) for the first family. |
| T2 | Log in on mobile, then on desktop. The mobile session must be terminated. | **Expected: Pass** — same mechanism as T1, platform-agnostic. | Same query pattern as T1. |
| T3 | Attempt simultaneous playback from two devices using the same account. Only the latest authorised session may continue. | **Expected: Pass** — the earlier device's playback session is revoked as part of the same `SessionRevocationService` call that revokes its auth session; its next renew call gets `403 session_expired`, and if its notification socket is connected the player pauses immediately on the `session_revoked` push. | `GET /v1/admin/security/video-protection-events` / playback session records for the two `sessionId`s involved; confirm the first was revoked at the same timestamp as the auth session. |
| T4 | Copy the active session token or browser storage to another device. The copied session must fail. | **Expected: Pass** — a copied access token dies at its next request once the family is revoked. A copied refresh token was already defended pre-existing single-use rotation + reuse detection (presenting an already-rotated token burns the whole family) — unchanged by this work. | Attempt a request with a copied token after the legitimate session has rotated or been revoked; expect `401`. |
| T5 | Approve a new trusted device. The previously trusted device must be revoked and the change appear in the admin audit log. | **Not yet implemented** — device binding (spec §3.2) is designed but not built. This test cannot pass until that ships. | — |

## Final confirmation table (spec §5, cross-reference)

See `docs/SECURITY-CONTENT-PROTECTION-MATRIX.md` §1 for the authoritative,
current-as-of-last-update version of this table (Implemented / Partial /
Not implemented per requirement area, with reasoning). Do not duplicate
maintenance of that table here — update it there and this document only
tracks per-platform test execution.

## How to (re-)run this report

1. Complete the Bunny console change (admin runbook §4) before attempting T4.
2. For each desktop platform, build a signed release, install it on a clean
   machine, and manually attempt T1–T3 with a real screen recorder / OBS /
   remote-desktop session. Record Pass/Fail and a screenshot or short clip
   as evidence.
3. For §3.4, use two real devices/browsers signed into the same test
   account; drive T1–T4 by hand and pull the corresponding
   `GET /v1/admin/security/events` query as evidence.
4. Re-run this whole report after each of the deferred items ships (device
   binding for T5; iOS screenshot detection before re-scoring §2.4 T1's iOS
   column from "documented limitation" to "detected, compensating control
   active").

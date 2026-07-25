# Course Platform Security — Admin Runbook

Operator instructions for the controls shipped under the Course Platform
Security Requirements spec (25 Jul 2026). Companion to
`docs/SECURITY-CONTENT-PROTECTION-MATRIX.md` (what's implemented and why)
and `docs/SECURITY-ACCEPTANCE-TESTS.md` (how to verify it).

## 1. Runtime Settings toggles

All toggles live in the `RuntimeSettings` row and are readable/writable via
`GET` / `PUT /v1/admin/runtime-settings` (an admin UI section is planned;
until it ships, use the API directly or a REST client with an admin
session). Every toggle is DB-backed — flipping one takes effect on the next
cache refresh (≈30s), no redeploy required.

| Field (JSON path) | Default | What it does | When to touch it |
|---|---|---|---|
| `security.singleActiveSessionEnabled` | `true` | Signing in anywhere revokes every other session immediately. | Flip to `false` only if this produces unexpected lockouts you need to investigate — it is the P0 control from spec §3.1 and should stay on. |
| `security.riskMode` | `"log_only"` | `"off"` \| `"log_only"` \| `"enforce"`. In `log_only`, risk signals (country change, impossible travel, device churn) are recorded but never block a sign-in. In `enforce`, a High-risk sign-in gets `403 sign_in_blocked_risk`. | After a week of `log_only` data, review `GET /v1/admin/security/events?kind=risk.impossible_travel` (and `risk.country_changed`) for false positives (e.g. legitimate VPN users, staff traveling) before flipping to `enforce`. |
| `videoProtection.revokeOnCaptureDetected` | `true` | A `capture_detected` / `screenshot_detected` protection event immediately revokes that playback session. | Currently only matters once iOS screenshot/capture detection ships (Phase 8) — Android/Windows/macOS block capture at the OS level and never emit these events in the first place. |
| `dataRetention.securityEventsDays` | `180` | How long `SecurityEvents` rows are kept. | Shorten if storage becomes a concern; this table is far higher volume than `AuditEvents` (every sign-in, refresh, playback start). |

To change a toggle:

```bash
curl -X PUT https://api.oetwithdrhesham.co.uk/v1/admin/runtime-settings \
  -H "Authorization: Bearer <admin access token>" \
  -H "Content-Type: application/json" \
  -d '{"security": {"riskMode": "enforce"}}'
```

## 2. Reading the security event feed

`GET /v1/admin/security/events` (requires the `security:read` admin
permission) — filterable by `accountId`, `kind`, `severity`, `from`, `to`,
paginated via `page`/`pageSize`. Kinds you'll see:

- `auth.sign_in_succeeded` / `auth.sign_in_failed` / `auth.sign_out` /
  `auth.mfa_failed`
- `auth.refresh_reuse_detected` — a revoked refresh token was presented
  again; treat as a strong compromise signal, the whole session family was
  auto-revoked.
- `session.created` / `session.revoked` / `session.revoked_all`
- `playback.session_started`
- `risk.country_changed` / `risk.impossible_travel` — see §1 for
  enforcement.
- `admin.session_revoked` / `admin.account_suspended` — admin-initiated
  actions, dual-logged here and in the existing `AuditEvent` trail.

`GET /v1/admin/security/video-protection-events` — same shape, for capture
protection / watermark tamper telemetry from the video player. Filter by
`userId`, `videoId`, `kind`.

## 3. Revoking a session or blocking a user right now

Today (until the dedicated per-session admin UI ships):

- **Revoke every session for an account:** `POST
  /v1/admin/users/{userId}/sessions/revoke` (existing endpoint, unchanged).
  This now also revokes the account's video playback sessions and pushes a
  live sign-out to any connected device, via the same
  `SessionRevocationService` every other revocation path uses.
- **Suspend an account:** `PUT /v1/admin/users/{userId}/status` with
  `{"status": "suspended"}` — already revokes all sessions as part of
  suspension (this was true before this security work and remains true).
- **Reset a device / targeted single-session revoke:** not yet available as
  a distinct action — device binding (spec §3.2) is the feature that
  introduces a "trusted device" concept to reset. Until it ships, "revoke
  every session" is the closest equivalent.

## 4. Bunny Stream console checklist (owner action — one-time)

This closes the single highest-leverage remaining content-protection gap:
the directory-scoped playback token also authorizes a direct MP4 pull.

1. **Stream Library → Encoding → MP4 Fallback: turn OFF.** This removes the
   progressive-download rendition entirely. If Bunny requires re-encoding
   existing videos to fully remove it, do that per the console's guidance.
2. **Token Authentication: leave ON** (already set).
3. **Block Direct Play: leave OFF.** Native WebViews (Tauri, Capacitor) send
   no `Referer` header — turning this on breaks playback on every shell.
4. **Allowed Referrers / domains: leave EMPTY**, same reason as #3.
   Cross-domain browser embedding is already prevented on our side
   (`frame-ancestors 'self'`, `X-Frame-Options: DENY`, and the
   attestation gate that refuses non-native clients regardless).
5. **Disable public embed / iframe view** if the library setting exists.
6. **Verification (do this after the change):** capture a token from a live
   playback session (browser devtools on a desktop build, or intercept the
   API response), then request
   `https://{cdn-hostname}/{videoId}/play_720p.mp4?token=...&expires=...`
   directly. Expect `403` or `404`. Keep the before/after evidence for the
   acceptance report (`docs/SECURITY-ACCEPTANCE-TESTS.md`, spec §2.4 T4).

## 5. Key rotation

- **Bunny CDN token-auth key** (`BunnyStreamTokenAuthKeyEncrypted`): rotate
  via the runtime-settings admin API; takes effect immediately (no
  redeploy), but invalidates every currently-signed URL — active playback
  sessions will fail their next renewal and need to re-attest.
- **Video attestation HMAC keys** (`VideoAttestationKeysEncrypted`, JSON map
  `"{platform}:{keyId}"` → hex secret): rotating requires coordinating with
  a new native build for the affected platform, since the secret is
  compiled into the Tauri/Android/iOS binaries. Add the new `keyId` while
  keeping the old one valid, ship the new builds, then remove the old
  `keyId` once adoption is confirmed via the `keyId` distribution in
  attestation-failure security events.
- **Auth token signing key** (`AuthTokenAccessTokenSigningKey`, env-only —
  not DB-overridable, a trust anchor): rotating invalidates every access
  token instantly; every signed-in user is forced to refresh. Only do this
  in response to an actual key-compromise incident.

## 6. Forced client-update gate (426)

`ClientVersionGateMiddleware` can force old desktop/mobile builds to update
before any of this native work (Phase 8: iOS screenshot detection, device
integrity heuristics) reaches users. Do not flip the minimum-version gate
until the corresponding signed release is live in the relevant store /
distribution channel — flipping it early locks out users on the currently
"latest" build.

## 7. What is NOT yet operational (do not assume these work)

- Device binding / trusted-device email-OTP challenge (spec §3.2) —
  designed, not built.
- Per-account "current active session/device" admin dashboard and computed
  alerts (repeated device switching, simultaneous-use, abnormal geography)
  — designed, not built. Use the raw event feed (§2) in the meantime.
- Root/jailbreak/emulator/debugger detection (spec §2, "where reasonably
  detectable") — designed, not built; requires a native mobile release.
- iOS screenshot detection (`userDidTakeScreenshotNotification`) — designed,
  not built; requires a native iOS release.
- Country allow-list and any external IP-intelligence (VPN/datacenter/Tor)
  detection — not implemented; see the content-protection matrix §7 for why.
- DRM / encrypted streaming — not implemented; documented upgrade path, see
  the content-protection matrix §4.

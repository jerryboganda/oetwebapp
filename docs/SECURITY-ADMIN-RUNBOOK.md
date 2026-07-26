# Course Platform Security — Admin Runbook

Operator instructions for the controls shipped under the Course Platform
Security Requirements spec (25 Jul 2026). Companion to
`docs/SECURITY-CONTENT-PROTECTION-MATRIX.md` (what's implemented and why)
and `docs/SECURITY-ACCEPTANCE-TESTS.md` (how to verify it).

## 1. Runtime Settings toggles

All toggles live in the `RuntimeSettings` row, editable in **Admin →
Settings** (Security / Video Protection sections) or via
`GET` / `PUT /v1/admin/runtime-settings`. Every toggle is DB-backed —
flipping one takes effect on the next cache refresh (≈30s), no redeploy
required.

| Field (JSON path) | Default | What it does | When to touch it |
|---|---|---|---|
| `security.singleActiveSessionEnabled` | `true` | Signing in anywhere revokes every other session immediately. | Flip to `false` only if this produces unexpected lockouts you need to investigate — it is the P0 control from spec §3.1 and should stay on. |
| `security.riskMode` | `"log_only"` | `"off"` \| `"log_only"` \| `"enforce"`. In `log_only`, risk signals (country change, impossible travel, device churn) are recorded but never block a sign-in. In `enforce`, a Medium-risk sign-in must pass an email-OTP step-up and a High-risk sign-in gets `403 sign_in_blocked_risk` (plus an admin notification and a security-alert email to the account owner). | After a week of `log_only` data, review `GET /v1/admin/security/events?kind=risk.impossible_travel` (and `risk.country_changed`) for false positives (e.g. legitimate VPN users, staff traveling) before flipping to `enforce`. |
| `security.trustedDeviceRequired` | `false` | Spec §3.2 device binding: a sign-in from a device other than the account's trusted one requires an email-OTP challenge; approving the new device revokes the old one. | Flip to `true` only after a real end-to-end OTP round-trip has been observed on prod (email delivered, challenge completed). |
| `security.deviceChangeWindowDays` / `security.deviceChangeMaxPerWindow` | `7` / `3` | Device-change cooldown: more than N trusts inside the window blocks further changes (`device_change_cooldown`). | Raise if support sees legitimate multi-device users hitting the cooldown. |
| `security.inactiveSessionTimeoutDays` | `30` | Sessions with no refresh activity for this long are revoked by the retention sweep (spec §4.2). | Rarely. |
| `security.requireVerifiedEmailForLearners` | `false` | Spec §4.2 hard gate: when ON, unverified learners get `403 email_verification_required` on every learner endpoint and the app routes them to the verify screen. | Flip ON once the email-verification banner has drained the unverified backlog. The banner itself always shows for unverified learners regardless of this toggle. |
| `security.countryAllowList` + `security.countryAllowListMode` | empty / `"off"` | Spec §3.3 country fence: comma-separated ISO codes; mode `step_up` challenges sign-ins from outside the list with an email OTP, `block` rejects them. Unknown-country sign-ins always pass. | Only if the owner decides to geographically restrict accounts. Set the list BEFORE setting the mode. |
| `videoProtection.revokeOnCaptureDetected` | `true` | A `capture_detected` / `screenshot_detected` protection event immediately revokes that playback session. | Matters on iOS (detection-only platform) once the Phase 8 native build is adopted — Android/Windows/macOS block capture at the OS level and never emit these events in the first place. |
| `videoProtection.blockRootedDevices` / `videoProtection.blockEmulators` | `true` / `true` | Reject new playback sessions from clients reporting root/jailbreak or emulator integrity signals (`403 DEVICE_BLOCKED`). Clients that send no signal (old shells, desktop, web) fail open and are logged. | Flip off temporarily if the heuristics produce false positives on some handset population. |
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
- `device.trust_requested` / `device.trusted` / `device.revoked` /
  `device.change_blocked_cooldown` / `device.admin_reset` — spec §3.2
  trusted-device lifecycle.
- `auth.refresh_device_mismatch` — a refresh token was presented from a
  device other than the one it was minted on; the family was revoked.
- `playback.session_started`
- `risk.country_changed` / `risk.impossible_travel` /
  `risk.step_up_required` / `risk.sign_in_blocked` — see §1 for
  enforcement.
- `admin.session_revoked` / `admin.device_reset` /
  `admin.account_suspended` / `admin.playback_blocked` — admin-initiated
  actions, dual-logged here and in the existing `AuditEvent` trail.

`GET /v1/admin/security/video-protection-events` — same shape, for capture
protection / watermark tamper telemetry from the video player. Filter by
`userId`, `videoId`, `kind`.

## 3. Revoking a session or blocking a user right now

The **Admin → Security** console (`/admin/security`, `security:read`
permission) shows the live event feed and alerts; each user's admin detail
page has a Security card with their sessions, trusted devices, and event
timeline. Actions (all require `security:write`, all dual-logged to
`AuditEvent` + `SecurityEvent`):

- **Revoke one session:** `DELETE
  /v1/admin/users/{userId}/security/sessions/{familyId}` — targeted
  single-family revoke; pushes a live sign-out to that device.
- **Revoke every session for an account:** `POST
  /v1/admin/users/{userId}/sessions/revoke` (existing endpoint, unchanged).
  This also revokes the account's video playback sessions and pushes a
  live sign-out to any connected device, via the same
  `SessionRevocationService` every other revocation path uses.
- **Reset the trusted device:** `POST
  /v1/admin/users/{userId}/security/devices/reset` — clears device trust
  AND revokes all sessions (a cleared device is a security-boundary
  reset); the next sign-in bootstraps a new trusted device silently.
- **Block playback immediately:** `POST
  /v1/admin/users/{userId}/security/block-playback` — kills every active
  video playback session for the account.
- **Suspend an account:** `PUT /v1/admin/users/{userId}/status` with
  `{"status": "suspended"}` — revokes all sessions as part of suspension
  and blocks sign-in for learner, expert, and admin accounts alike.

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

Everything in §§1–3 is live code. The honest residual list:

- **Device binding enforcement** (spec §3.2) — fully built (service,
  endpoints, sign-in challenge UI, this runbook's reset action), but the
  `security.trustedDeviceRequired` toggle ships **OFF** until a real
  end-to-end OTP round-trip is observed on prod. Until the owner flips it,
  no sign-in is challenged.
- **Native Phase 8 features on user devices** — iOS screenshot detection
  and root/jailbreak/emulator heuristics are in the repo and the backend
  enforces on their signals, but they only reach users once the signed
  Android+iOS release is cut (`mobile-release.yml`), passes store review,
  and is adopted. Old shells send no signal and fail open (by design).
- **iOS still-screenshot BLOCKING** — platform limitation; iOS only allows
  detection, not prevention. Compensating controls: detection event +
  watermark + `videoProtection.revokeOnCaptureDetected`. See the matrix §5.
- **External IP-intelligence (VPN/datacenter/Tor) detection** — the code
  seam exists (`IIpIntelligenceService`), but the registered implementation
  is a no-op until a paid provider (ipinfo/MaxMind) is configured; until
  then `high_risk_network` never fires. See the matrix §7 for why static
  lists were rejected.
- **Sign-in risk enforcement** — code-complete, but `security.riskMode`
  ships `log_only`; flip to `enforce` after the log-only review week (§1).
- **Learner verified-email hard gate** — code-complete behind
  `security.requireVerifiedEmailForLearners`, ships OFF (banner-only).
- **DRM / encrypted streaming** — not implemented; documented upgrade path,
  see the content-protection matrix §4.

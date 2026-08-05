# Agent State - Course platform security production completion

Last updated: 2026-08-05

## Goal

Implement and production-verify the controls in
`OET_Course_Platform_Security_Requirements (1).pdf`, preserving explicit
evidence for platform limitations and external provider blockers.

## Implemented

- Implemented the reference anti-sharing contract end to end: one approved
  client identity by default, bounded admin override (1-5), OTP approval for
  new identities, automatic replacement/revocation, family-wide sign-out,
  refresh/access-token liveness enforcement, playback termination, clear
  user-facing sign-out reasons, and SecurityEvent + AuditEvent evidence.
- Defined the exact cross-platform count: browser profile tabs/windows count
  once; each Android/iOS/Windows/macOS official app installation counts once;
  a browser profile and app on the same hardware count separately because the
  server cannot prove hardware equivalence. The exact policy is documented in
  `docs/SECURITY-DEVICE-POLICY.md` and surfaced in admin and learner UI.
- Added the learner limit migration and the SecurityEvents platform-width
  migration required for canonical `capacitor-android`/`capacitor-ios` values.
- Routed self-service family revocation through the central revocation service
  and made device/session security decisions resilient to request disconnects.

- Replaced learner-visible direct HLS URLs with 5-minute token-authenticated
  Bunny embed URLs, ready for MediaCage and never exposing the library key.
- Enabled the protected player on web using an authenticated, user-bound,
  single-use nonce; native clients retain shell-held HMAC attestation.
- Playback sessions persist the request device id and revoke if renewal is
  attempted from a different device.
- Auth and API requests await native secure-storage device-id initialization;
  enforced fresh sign-ins reject a missing device id instead of failing open.
- Added a sandboxed, origin-checked embed controller while keeping the moving
  forensic watermark above the provider player, including parent fullscreen.
- Native shells now fail closed when OS capture protection cannot engage.
  Web remains functional under watermark, token, session, and audit controls.
- Screenshot/capture/tamper signals flush immediately and pause/mute both
  direct and embedded playback before server-side revocation.
- The production migration enables single-session, risk enforcement,
  trusted-device OTP binding, verified-email gating, rooted/emulator blocks,
  capture revocation, and a 300-second playback token TTL. Legacy device-unbound
  refresh tokens and active playback sessions are revoked.
- Added encrypted runtime configuration and a fail-open IPinfo
  Core/Plus/Max integration for VPN/proxy/Tor/hosting sign-in signals.
- Updated the security matrix, acceptance report, and admin runbook with the
  actual implementation state and remaining external/manual evidence.

## Validation

- Focused Vitest (attestation + embed-origin controller): 11/11 passed.
- Focused ESLint across every touched frontend/test file: passed.
- `pnpm exec tsc --noEmit --pretty false`: first run reached only four
  pre-existing unrelated errors in `lib/__tests__/api.test.ts` (lines 458,
  459, 480, 481); the post-change rerun hit the host timeout.
- Focused .NET test/build attempts hit the repository's known MSBuild host
  stall and were terminated; the GitHub production build is the compile gate.
- A focused ESLint retry also hit the host command timeout; no lint result was
  claimed.
- Final targeted ESLint checks completed with 0 errors (existing React hook
  warnings only) for the admin security/user pages, sessions page, auth and
  notification paths, device-id client, and API type helpers.
- `git diff --check` passed. The bounded API build emitted package/vulnerability
  warnings and then produced no compiler output for over two minutes, so it
  was stopped; no green backend build is claimed from this host.

## External Blockers / Residual Evidence

- Bunny MediaCage Basic or Enterprise DRM must be enabled and verified in the
  authenticated Bunny account; provider dashboard access is unavailable here.
- A signed iOS IPA requires Apple team id, distribution certificate,
  provisioning profile, and a non-placeholder associated-domain file.
- Android 1.4.1 and Windows/macOS desktop 0.7.0 are signed and published, but
  both tags predate the final security commits. The installed Windows 0.7.0
  app remained capturable in a foreground `CopyFromScreen` test, so corrected
  releases must be published from the post-deploy `main` SHA.
- IPinfo code is complete; production activation still needs a paid provider
  token entered through Admin → Settings → Security.
- Hardware OBS/RDP/mirroring/screenshot tests remain manual acceptance work.

## Next Step

Stage only the explicit implementation paths (preserving `.codex/config.toml`
and `.superpowers/`), commit, push `main`, and verify the blue/green production
workflow. The owner still needs real-device/manual acceptance for same-hardware
browser/app counting and cross-platform sign-out behavior.

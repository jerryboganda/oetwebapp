# Speaking Module — Pre-Launch Security Checklist

> **Current status (2026-05-29)**: pre-launch sign-off checklist. Unchecked items are security gates, not proof that the whole Speaking module is unimplemented. Cross-check active release ownership in [`../../STATUS/REMAINING-WORK.md`](../../STATUS/REMAINING-WORK.md).

Sign off every box before the `Features__SpeakingV2` flag is enabled for production cohorts.

## Authentication + authorization

- [ ] Every Speaking endpoint maps to one of `LearnerOnly`, `ExpertOnly`, `AdminOnly` (or granular admin permission).
- [ ] No anonymous endpoints other than the LiveKit webhook receiver (HMAC-verified).
- [ ] Owner check enforced on all per-session routes — 404 (not 403) on non-owner access.
- [ ] Profession IDOR check on role-play card read — non-matching profession returns 404.

## Data protection

- [ ] `InterlocutorScript` never serialized in any learner response (integration test asserts).
- [ ] All recordings stored with SHA-256 hash + manifest; mid-flight tamper triggers reject.
- [ ] Pre-signed URLs for recording playback short-TTL (≤ 5 min) and bound to caller id.
- [ ] Consent versioning stamps every recording and live-tutor session.

## Audit logging

- [ ] `AuditEvent` row written for: session lifecycle transitions, recording deletion, admin recording access, retention purge, tutor assessment submit, calibration sample creation, role-play card publish.
- [ ] Audit log retained for `AuditLogRetentionDays` (default 2555 = 7y).

## Webhook security

- [ ] LiveKit webhook signature verified with constant-time HMAC compare.
- [ ] Webhook source IP allow-list configured (LiveKit Cloud egress IPs).
- [ ] `WebhookEventsJson` append-only per `SpeakingLiveRoom`.

## Provider hardening

- [ ] AI provider keys stored in env (not committed) — see `docs/env/speaking.md`.
- [ ] Provider keys rotated within 90 days (see `key-rotation.md`).
- [ ] `IAiGatewayService` redacts secrets from logs (validated by `docs/security/ai-provider-secret-redaction.md`).
- [ ] LiveKit `WebhookSigningSecret` rotatable without downtime.

## Input validation

- [ ] All Speaking endpoint DTOs use Zod / DataAnnotation validation.
- [ ] Card text passed through prompt-injection sanitizer (TODO — tracked in README).
- [ ] Audio uploads capped at 8 MiB chunks + 200 MiB total per session.

## Rate limiting

- [ ] `POST /v1/speaking/sessions` rate-limited per user.
- [ ] `POST /v1/admin/speaking/cards/ai-draft` rate-limited per admin.
- [ ] LiveKit room provisioning rate-limited per booking.

## Network

- [ ] TLS required for every external surface.
- [ ] Content Security Policy header set on Next.js responses including Speaking pages.
- [ ] S3 bucket policy denies anonymous + denies public ACL.

## Compliance

- [ ] GDPR DPIA completed for voice-biometric storage.
- [ ] Cookie + consent banner covers AI processing.
- [ ] Erasure pre-flight returns the full Speaking inventory for the caller.
- [ ] DSR (Data Subject Request) runbook references this module.

## Monitoring + alerting

- [ ] Grafana dashboards imported (`ops/dashboards/speaking-*.json`).
- [ ] Alert routes configured (PagerDuty / Slack) for: AI provider 5xx, LiveKit webhook failure, recording loss, drift spike, S3 access denied.

## Known open gaps (must be either closed or risk-accepted before launch)

- [ ] Originality watermark on learner-visible card text (abuse case #2).
- [ ] Prompt-injection classifier on user-supplied card text (abuse case #3).
- [ ] MFA enforcement for `ExpertOnly` policy (abuse case #5).

## Sign-off

- [ ] Speaking lead
- [ ] Security lead
- [ ] Compliance lead
- [ ] Backend lead

# Notification Module PRD

## Goal

Make notifications a production-ready, administrator-configurable communication system across learner, expert, admin, and sponsor journeys. The module must support in-app, email, push, SMS, and WhatsApp policies without silently sending messages outside user consent, quiet hours, frequency caps, or protected-event rules.

## Current foundation

- Backend domain already models notification events, inbox items, preferences, push subscriptions/tokens, consents, suppressions, delivery attempts, event catalog entries, channel policies, and admin health.
- User APIs expose feed, read state, preferences, push subscriptions, mobile push token registration, and consent updates under `/v1/notifications`.
- Admin APIs expose catalog, policy overrides, delivery health, delivery attempts, consents, suppressions, test email, and proof trigger under `/v1/admin/notifications`.
- UI coverage includes `/admin/notifications`, notification center/preferences, and `/settings/reminders` backed by the shared notification preferences panel.

## Launch requirements

1. Given a notification event is created, when fan-out runs, then in-app, email, and push delivery must respect per-event policy, global preferences, quiet hours, frequency caps, consent, and suppression records.
2. Given SMS or WhatsApp is enabled by an admin, when a lifecycle event targets those channels, then the provider dispatcher must require explicit consent, record delivery attempts, and fail closed when provider credentials or templates are missing.
3. Given an admin edits notification policy, when the setting is saved, then the change must be audit-visible and take effect without deployment for safe channel enablement, caps, digest mode, and provider toggles.
4. Given a message is security, billing, password, verification, or refund related, when a user disables non-critical notifications, then protected-event rules must still prevent unsafe suppression where legally or operationally required.
5. Given a campaign or lifecycle automation is scheduled, when the audience is resolved, then segments, approvals, exclusions, cost estimates, and dry-run counts must be visible before send.
6. Given delivery failures rise, when an admin opens notification health, then failed provider, channel, event key, recipient-safe error code, and retry/suppression state must be visible without exposing secrets or raw PII.
7. Given schools, sponsors, or minors are targeted, when a notification is sent, then guardian/school/sponsor consent and local privacy policy gates must be enforced before any external-channel dispatch.

## Remaining implementation tracks

| Track | Required outcome |
| --- | --- |
| SMS dispatcher | Provider abstraction, runtime settings, consent gate, delivery attempts, webhook status mapping, test-send proof. |
| WhatsApp dispatcher | Provider abstraction, template approval model, consent gate, webhook status mapping, cost tracking, test-send proof. |
| Provider webhooks | Signed webhook endpoints for email, push-adjacent provider callbacks where applicable, SMS, and WhatsApp with idempotent delivery status updates. |
| Campaigns and segments | Admin segment builder, dry run, approval workflow, scheduled send, cancellation, exclusions, and audit trail. |
| Template manager | Admin-managed templates per event/channel/audience with validation, placeholders, versioning, preview, and provider template IDs. |
| Cost and quota dashboard | Per-provider spend estimates, sent/failed/suppressed counts, channel caps, and alert thresholds configurable from Admin UI. |
| Full lifecycle automation | OET study, mock, speaking, writing, billing, inactivity, win-back, and admin alert events wired to deterministic triggers. |
| Evidence | Unit/integration coverage for policy decisions plus browser proof for admin policy, user preferences, proof trigger, and failed-provider health. |

## Non-negotiables

- No provider secrets in frontend code, Docker web containers, logs, or test artifacts.
- No external notification without consent where consent is required.
- No SMS, WhatsApp, or campaign send without admin-visible channel enablement and budget/cap controls.
- No silent fallback from failed external channel to success-shaped state; every attempt records sent, queued, suppressed, failed, delivered, opened, clicked, bounced, or unsubscribed.
- No direct file or temp-log leakage of notification proof failures.


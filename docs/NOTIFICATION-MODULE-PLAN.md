# Notification Module Implementation Plan

**Status:** active implementation plan.
**Scope:** in-app, email, push, SMS, WhatsApp, templates, preferences, consent, suppression, delivery logs, campaign automation, admin governance, and OET-specific notification flows.
**Current baseline:** the platform already has `NotificationService`, `NotificationCatalog`, notification events, inbox items, user preferences, admin policy overrides, delivery attempts, web push subscriptions, mobile push tokens, SignalR realtime delivery, and an admin notification governance page.

---

## 1. Product Goals

The notification module is a student-success system, not only a message sender. It must answer:

| Question | System responsibility |
| --- | --- |
| Who needs to know? | Resolve learner, expert, admin, sponsor, affiliate, and future parent recipients from domain events. |
| What should they know? | Select a safe template with role, category, severity, channel, locale, and deep link. |
| When should they be told? | Apply schedules, quiet hours, timezones, urgency, reminders, and expiry. |
| Which channel is best? | Route through in-app, email, push, SMS, WhatsApp, and future internal tools by priority and consent. |
| Did it arrive? | Record queue, sent, delivered, opened, clicked, failed, bounced, suppressed, unsubscribed, and expired states. |
| Did it help? | Track analytics for feedback views, no-show reduction, trial conversion, study return, and billing recovery. |
| Are we annoying users? | Enforce consent, preferences, frequency caps, suppression lists, quiet hours, and digest modes. |

---

## 2. Non-Negotiable Invariants

1. In-app is the canonical record for every user-facing notification, even when other channels fail.
2. Security, password reset, email verification, and payment receipts cannot be disabled by marketing preferences.
3. Marketing is separate from transactional, educational, billing, and security categories.
4. SMS and WhatsApp require explicit consent for non-essential messages and must support opt-out evidence.
5. Push/SMS/WhatsApp copy must avoid sensitive lock-screen details such as exact scores, health-like scenario details, or shaming language.
6. Every send attempt creates an auditable delivery record.
7. Domain flows enqueue notifications through the notification service or a domain event bridge, never by sending directly inside user-facing request latency.
8. Admin mass campaigns require draft, preview, compliance review, test send, approval, schedule, monitoring, and emergency pause before broad release.
9. Provider secrets stay in configuration, never in templates, logs, or notification payloads.
10. Deep links must route to the relevant page, not a generic dashboard.

---

## 3. Target Architecture

```text
Domain event
  -> notification event/outbox
  -> rule and policy resolver
  -> preference + consent + suppression + frequency-cap checks
  -> template renderer + safe-payload scrubber
  -> channel queue items
  -> provider dispatchers
  -> delivery event ingest
  -> analytics + admin health dashboards
```

### Core components

| Component | Responsibility |
| --- | --- |
| NotificationCatalog | Canonical event keys, default severity, category, channels, title/body/deep-link fallback. |
| NotificationService | Event creation, preference resolution, fan-out, digest dispatch, admin health, proof triggers. |
| Channel dispatchers | Email, web push, mobile push, SMS, WhatsApp, and future Slack/internal dispatch behind interfaces. |
| Consent service | Stores explicit consent and withdrawal evidence per user/channel/category. |
| Suppression service | Prevents sends to bounced, unsubscribed, opted-out, invalid, or blocked destinations. |
| Template service | Versioned, channel-specific templates with variables, status, language, and approval state. |
| Rule engine | Admin-configurable event-to-channel policies, fallback rules, quiet hours, frequency caps, and expiry. |
| Campaign service | Segments, schedules, approvals, A/B variants, recipients, and conversion metrics. |
| Provider webhook ingest | Normalizes delivery/open/click/bounce/read/failure events from email, push, SMS, and WhatsApp providers. |

---

## 4. Step-by-Step Phases

### Phase 0 - Discovery and Baseline Lock

**Goal:** prevent duplicate implementation and define the exact extension points.

Deliverables:

1. Map existing backend entities, migrations, endpoints, and frontend notification center.
2. Confirm current dispatch behavior for in-app, email, browser push, mobile token registration, digest, and admin proof sends.
3. Create this implementation plan.
4. Add focused tests before broad behavioral changes.

Acceptance criteria:

1. Plan is checked into `docs/`.
2. First implementation phase extends existing `NotificationService` and related entities rather than creating a parallel module.

### Phase 1 - Channel, Consent, Suppression, and Template Foundation

**Goal:** make the current module structurally capable of the uploaded brief.

Backend tasks:

1. Extend `NotificationChannel` with `Sms` and `WhatsApp`.
2. Extend delivery status with `Created`, `Queued`, `Delivered`, `Opened`, `Clicked`, `Bounced`, and `Unsubscribed` while preserving existing statuses.
3. Add consent entities for channel/category opt-in, opt-out, source, wording, and audit metadata.
4. Add suppression records for email, phone, push token, WhatsApp number, and category-specific stops.
5. Enrich notification templates with name, language, status, version, owner, variables, CTA, approval metadata, and message category.
6. Add API contracts and endpoints for users to view/update consent and admins to inspect consent/suppression records.
7. Add indexes and migration coverage.

Frontend tasks:

1. Add SMS and WhatsApp to shared notification types.
2. Add consent summary and channel opt-in copy to the notification preferences surface.
3. Add admin read-only consent/suppression cards before full editors.

Acceptance criteria:

1. Users can retrieve and change channel consent for optional categories.
2. Admins can audit consent and suppression state.
3. Existing in-app/email/push behavior remains compatible.

### Phase 2 - OET Event Catalog Expansion

**Goal:** cover the full OET academy lifecycle.

Event families:

1. Transactional: account created, email verification, password reset, class booking confirmed, mock submitted, invoice generated.
2. Educational: daily study reminder, missed lesson, weak-skill reminder, homework due, new lesson unlocked, streak reminder.
3. Mock lifecycle: scheduled, 24h/2h/30m reminders, technical check, started, incomplete, auto-score ready, feedback ready, retake recommended.
4. Speaking: booked, 24h/30m reminders, tutor joined, student late, recording ready, feedback ready, reschedule needed, teacher assignment/no-show/overdue.
5. Writing: assigned, deadline reminder, submitted, teacher assigned, feedback ready, rewrite requested, rewrite overdue, teacher overdue/escalation.
6. Billing: trial started/ending/expired, payment success/failed, renewal coming, cancelled, coupon applied, credits low/expiring, refund processed.
7. Marketing and engagement: trial conversion, abandoned checkout, inactive, exam urgency, win-back, course launch.
8. Admin/security: suspicious activity, failed payment spike, provider outage, overdue feedback spike, coupon abuse, content flagged.

Acceptance criteria:

1. Admin catalog shows all event keys grouped by audience and category.
2. Every event has safe title/body/deep-link fallback.
3. Critical/security events are marked non-optional at the policy layer.

### Phase 3 - Rule Engine, Routing, Quiet Hours, and Frequency Caps

**Goal:** send the right message at the right time.

Tasks:

1. Add configurable rule rows for event key, audience, channels, priority, delay, expiry, fallback, and required consent category.
2. Add frequency cap records for per-user/channel/category windows.
3. Apply user timezone and quiet hours, with urgent events bypassing quiet hours.
4. Route fallback: push -> email, email bounce -> in-app + consented WhatsApp, WhatsApp failed -> consented SMS, teacher no-action -> admin escalation.
5. Add digest batching for study, teacher, admin, sponsor, and weekly learner progress summaries.

Acceptance criteria:

1. A suppressed notification records why it was not sent.
2. Reminder events respect quiet hours unless priority is urgent/critical.
3. Admin can configure channel policies without code changes.

Implementation progress:

1. Frequency caps are now part of admin notification policy overrides and apply per account/channel/category/event window for outbound email and push.
2. Protected critical, security, verification, and payment receipt notifications bypass frequency caps and reject event-specific cap overrides.
3. Cap suppression is recorded as a delivery attempt with reason code `frequency_cap_exceeded`.

### Phase 4 - Provider Dispatchers and Webhooks

**Goal:** turn queue records into reliable multi-channel delivery.

Tasks:

1. Define `ISmsDispatcher`, `IWhatsAppDispatcher`, and mobile push dispatch abstractions.
2. Implement provider adapters behind configuration with mock adapters for tests/development.
3. Add provider webhook endpoints for SendGrid/Brevo/Mailgun-style email events, FCM/APNs delivery feedback where available, Twilio SMS/WhatsApp status callbacks, and manual provider event ingest.
4. Normalize provider payloads into delivery attempts, provider events, bounces, unsubscribes, invalid token cleanup, and suppression rows.

Acceptance criteria:

1. Provider failures do not fail user-facing domain actions.
2. Hard bounces and invalid tokens suppress or deactivate future sends.
3. Provider webhook payloads are validated and safely logged without secrets.

### Phase 5 - User Preference Center

**Goal:** give learners, teachers, admins, sponsors, and future affiliates clear control.

Tasks:

1. Expand preferences by category and channel: security, account, educational, mock, speaking, writing, billing, marketing, newsletter, admin operations.
2. Add opt-out warnings for class/mock/speaking reminders.
3. Add timezone, quiet hours, language, digest mode, and preferred channel.
4. Record every consent and withdrawal event.

Acceptance criteria:

1. Marketing opt-out does not disable transactional notifications.
2. Consent changes are auditable and visible to admins.
3. The preferences UI remains usable on mobile.

### Phase 6 - Admin Template and Campaign Studio

**Goal:** let admins operate notifications safely without developer help.

Tasks:

1. Template manager with variables, preview, test send, language, status, approval state, and version history.
2. Segment builder using profile, behavior, score, billing, engagement, and consent filters.
3. Campaign builder with audience, channel, template, schedule, condition, CTA, coupon, cap, goal, and status.
4. Approval workflow: draft -> preview -> compliance review -> test send -> approval -> schedule -> monitor -> pause.
5. Emergency pause for all marketing/campaign traffic.

Acceptance criteria:

1. No mass campaign can go live without approval.
2. Test send previews rendered variables and deep links.
3. Paused campaigns stop scheduling new recipients.

### Phase 7 - Domain Flow Wiring

**Goal:** connect core OET product events to the notification engine.

Tasks:

1. Writing: assigned, submitted, teacher assigned, feedback ready, rewrite due/overdue, escalation.
2. Speaking/private speaking: booking, reminders, late/no-show, tutor joined, recording/feedback ready, reschedule.
3. Mocks: scheduled, reminders, technical check, incomplete, score/report/teacher feedback ready, retake recommended.
4. Billing: trial, failed payment, renewal, credits, coupon, cancellation, refund.
5. Learning: weak-skill nudges, new lesson unlocked, study plan, exam countdown, inactivity recovery.
6. Admin/security: suspicious login, provider failures, overdue feedback spikes, failed payment spikes, content flags.

Acceptance criteria:

1. Each domain flow has tests proving event creation and dedupe.
2. User-facing requests enqueue work rather than synchronously sending provider messages.
3. Deep links route to the correct learner/expert/admin page.

### Phase 8 - Analytics, Reporting, and Cost Controls

**Goal:** make notification performance measurable and affordable.

Tasks:

1. Add campaign, provider, and student-level reporting views.
2. Track sent, delivered, opened, clicked, failed, bounced, suppressed, opt-out, conversion, no-show reduction, and recovery metrics.
3. Add provider cost snapshots by channel.
4. Add alerting for provider failure rate, bounce spike, opt-out spike, and campaign complaints.

Acceptance criteria:

1. Admin dashboard shows delivery health and campaign effectiveness.
2. SMS/WhatsApp spend can be capped and reviewed.
3. Reporting can trace from notification to conversion where applicable.

### Phase 9 - Compliance, Privacy, and Production Readiness

**Goal:** harden the module for real students and regulated markets.

Tasks:

1. Add compliance checks for commercial email footer/unsubscribe/address requirements.
2. Add SMS STOP opt-out handling and WhatsApp opt-out handling.
3. Add lock-screen-safe copy checks for push/SMS/WhatsApp templates.
4. Add retention policies for provider payloads and personal contact data.
5. Add operational runbooks for provider outage, bounce spike, campaign pause, token invalidation, and accidental send.

Acceptance criteria:

1. Marketing and transactional communications are clearly separated.
2. Consent withdrawal is fast, simple, and auditable.
3. Production launch checklist passes before SMS/WhatsApp/campaign features are enabled.

---

## 5. MVP Launch Scope

Must ship before launch:

1. In-app notification bell and feed.
2. Email notifications.
3. Browser/mobile push registration and safe deep links.
4. Basic WhatsApp reminder foundation and consent records.
5. Student preferences and consent audit trail.
6. Templates, policy overrides, delivery logs, and admin health.
7. Writing feedback, speaking booking/reminder, mock report, billing/trial reminders.
8. Suppression list and unsubscribe/opt-out handling.

Later:

1. Full A/B testing.
2. Advanced segmentation and AI-personalized nudges.
3. Multi-language template localization.
4. Provider failover and cost optimization dashboard.
5. Parent/sponsor digests and affiliate commission alerts.

---

## 6. Verification Strategy

Backend checks:

1. Unit tests for consent, suppression, template rendering, policy resolution, quiet hours, caps, and fallback.
2. Integration tests for notification endpoints, admin policies, provider webhooks, and domain flow event creation.
3. `dotnet build backend/OetLearner.sln` after each backend slice.
4. Targeted `dotnet test` filters for notification tests before broad suites.

Frontend checks:

1. Vitest coverage for notification center, preferences, admin governance, and campaign/template forms.
2. TypeScript checks for shared notification types and API client contracts.
3. Manual/browser smoke for notification bell, preferences save, push permission, and admin notification page.

Operational checks:

1. Provider sandbox test sends.
2. Webhook signature validation.
3. Retry/fallback simulation.
4. Hard bounce and STOP/opt-out suppression simulation.
5. Emergency campaign pause drill.

---

## 7. Current Implementation Start

The first implementation slice is Phase 1 foundation:

1. Extend channel/status enums without breaking existing clients.
2. Add consent and suppression persistence.
3. Enrich template metadata.
4. Add API contracts and endpoints for consent visibility and updates.
5. Add admin visibility for consent/suppression records.

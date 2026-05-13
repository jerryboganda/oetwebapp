# Admin Dashboard and CMS Manual

This manual documents the admin-facing control plane as implemented in the current codebase.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Sponsor Portal Manual](./sponsor-portal-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)
- [Route, API, and Domain Surface Index](./route-api-domain-surface-index.md)
- [Reference Appendix](./reference-appendix.md)

Status labels used in this document:

- `implemented`
- `partial`
- `unclear`

`implemented` means the current UI and API surface exist. `partial` means the surface exists but is transitional, policy-dependent, heuristic, or not fully data-driven. `unclear` means the route or API is referenced but was not confirmed strongly enough to document as complete behavior.

These labels follow the canonical vocabulary in [README](./README.md) and do not by themselves mean end-to-end release validation.

## Admin Route Inventory

The admin surface currently contains 105 page routes. The older sections below remain useful for the original dashboard/CMS path, but the current control plane must be read by workstream.

Overview and audit:

- `/admin`
- `/admin/alerts`
- `/admin/audit-logs`
- `/admin/sla-health`
- `/admin/business-intelligence`

Analytics:

- `/admin/analytics/quality`
- `/admin/analytics/reading`
- `/admin/analytics/listening`
- `/admin/analytics/listening/question/[paperId]/[number]`
- `/admin/analytics/content-effectiveness`
- `/admin/analytics/subscription-health`
- `/admin/analytics/expert-efficiency`
- `/admin/analytics/cohort`

Content hub and assets:

- `/admin/content`
- `/admin/content/new`
- `/admin/content/[id]`
- `/admin/content/[id]/revisions`
- `/admin/content/library`
- `/admin/content/papers`
- `/admin/content/papers/[paperId]`
- `/admin/content/papers/import`
- `/admin/content/import`
- `/admin/content/media`
- `/admin/content/hierarchy`
- `/admin/content/dedup`
- `/admin/content/generation`
- `/admin/content/quality`
- `/admin/content/analytics`
- `/admin/content/publish-requests`

Module authoring:

- `/admin/content/writing`
- `/admin/content/writing/ai-draft`
- `/admin/writing/options`
- `/admin/writing/ai-draft`
- `/admin/content/listening`
- `/admin/content/mocks`
- `/admin/content/mocks/wizard`
- `/admin/content/mocks/wizard/[bundleId]/bundle`
- `/admin/content/mocks/wizard/[bundleId]/reading`
- `/admin/content/mocks/wizard/[bundleId]/listening`
- `/admin/content/mocks/wizard/[bundleId]/writing`
- `/admin/content/mocks/wizard/[bundleId]/speaking`
- `/admin/content/mocks/wizard/[bundleId]/review`
- `/admin/content/mocks/operations`
- `/admin/content/mocks/item-analysis`
- `/admin/content/mocks/[bundleId]/item-analysis`
- `/admin/content/mocks/leak-reports`
- `/admin/content/speaking/mock-sets`
- `/admin/content/grammar`
- `/admin/content/grammar/topics`
- `/admin/content/grammar/lessons/new`
- `/admin/content/grammar/lessons/[lessonId]`
- `/admin/content/grammar/ai-draft`
- `/admin/content/pronunciation`
- `/admin/content/pronunciation/new`
- `/admin/content/pronunciation/[drillId]`
- `/admin/content/pronunciation/ai-draft`
- `/admin/content/conversation`
- `/admin/content/conversation/new`
- `/admin/content/conversation/[id]`
- `/admin/content/conversation/settings`
- `/admin/content/conversation/sessions`
- `/admin/content/conversation/sessions/[id]`
- `/admin/content/vocabulary`
- `/admin/content/vocabulary/new`
- `/admin/content/vocabulary/[id]`
- `/admin/content/vocabulary/import`
- `/admin/content/vocabulary/ai-draft`
- `/admin/content/strategies`
- `/admin/content/strategies/new`
- `/admin/content/strategies/[guideId]`
- `/admin/recalls/bulk-upload`

Governance, AI, and review operations:

- `/admin/signup-catalog`
- `/admin/taxonomy`
- `/admin/criteria`
- `/admin/rulebooks`
- `/admin/rulebooks/[id]`
- `/admin/ai-config`
- `/admin/ai-providers`
- `/admin/ai-usage`
- `/admin/ai-usage/users/[userId]`
- `/admin/review-ops`
- `/admin/escalations`
- `/admin/marketplace-review`
- `/admin/private-speaking`
- `/admin/private-speaking/calibration`
- `/admin/score-guarantee-claims`

People, access, sponsor/enterprise, billing, and communications:

- `/admin/users`
- `/admin/users/import`
- `/admin/users/[id]`
- `/admin/experts`
- `/admin/community`
- `/admin/roles`
- `/admin/permissions`
- `/admin/institutions`
- `/admin/enterprise`
- `/admin/billing`
- `/admin/billing/wallet-tiers`
- `/admin/credit-lifecycle`
- `/admin/free-tier`
- `/admin/freeze`
- `/admin/webhooks`
- `/admin/flags`
- `/admin/notifications`
- `/admin/playbook`
- `/admin/bulk-operations`

`/admin/experts` is a legacy deep-link redirect to User Operations > Tutors. Expert/tutor administration is maintained under `/admin/users?tab=tutors`, not as a standalone admin workspace.

## Current Coverage Addendum

- Signup Catalog is now the primary profession/catalog governance surface; taxonomy remains a legacy mirrored route.
- Content operations include the canonical content-paper pipeline: content papers, typed asset roles, source provenance, chunked imports, publish gates, audit events, media, hierarchy, deduplication, and quality/staleness tooling.
- Admin module authoring covers Writing, Reading authoring/policy, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, and Strategies.
- Admin Writing analytics includes rule-violation summary and attempt drill-down endpoints under `/v1/admin/writing/analytics`.
- AI operations include config, providers, usage, user drill-down, writing options/drafts, BYOK/platform-only policy, quotas, kill-switch behavior, and tool/escalation support. These must stay consistent with the grounded gateway and usage-recording policy.
- Review and quality operations include review ops, escalations, SLA health, marketplace review, private speaking, score-guarantee claims, expert efficiency, and quality analytics.
- Sponsor and enterprise operations are represented through institutions, enterprise, user, billing, sponsor portal, and audit-adjacent workflows.
- Billing governance includes plans, wallet tiers, credit lifecycle, free tier, freezes, webhooks, score guarantee, marketplace/private-speaking payment implications, and learner entitlement effects.
- Upload and storage governance includes per-role upload limits, 8 MB chunking, 24-hour staging cleanup, ZIP entry/count/uncompressed/compression-ratio limits, scanner selection, quarantine, and production fail-fast when NoOp scanning is configured.
- Notification governance includes user feed/read/preferences, push subscriptions/tokens/consent, admin catalog/policy/health/delivery/consent/suppression/test-email/proof-trigger APIs, and account-scoped SignalR delivery.
- Billing operations include payment transactions, refunds, disputes, provider lifecycle signals, webhook monitoring, summaries, and retry/support actions.

## Additional Admin Workstream Reference

| Workstream | Status | Primary surfaces | Notes |
| --- | --- | --- | --- |
| Content papers and assets | `implemented` | content papers, import, media, hierarchy, dedup, generation | canonical publish gates require typed assets and source provenance |
| Reading authoring and policy | `implemented` | backend Reading authoring/policy endpoints, reading analytics | mission-critical exact-match and safe DTO rules apply |
| Module authoring | `implemented` | Writing, Speaking mock sets, Listening, Mocks, Grammar, Pronunciation, Conversation, Vocabulary, Recalls, Strategies | AI drafting must remain grounded where applicable |
| AI operations | `implemented` | config, providers, usage, user drill-down, writing AI options/drafts | covers provider registry, quotas, BYOK/platform-only policy, and kill-switches |
| Review/private-speaking quality | `implemented` | review ops, escalations, private speaking, marketplace review, SLA health | governs expert throughput and paid service quality |
| Sponsor and enterprise | `implemented` | institutions, enterprise, users, billing, sponsor portal adjacency | sponsor portal is separate from admin but governed here operationally |
| Billing and lifecycle | `implemented` | billing, wallet tiers, credit lifecycle, free tier, freezes, webhooks | affects learner entitlement and commercial workflows |
| Bulk and playbook operations | `implemented` | bulk operations, playbook, audit logs | used for operator-scale actions and runbook support |
| Upload and storage security | `implemented` | content imports, media, chunked uploads, scanner/quarantine path | production must not run NoOp scanner |
| Notification delivery governance | `implemented` | notifications admin, feed APIs, push consent, SignalR hub | account-scoped delivery and suppression policies must be tested |
| Provider lifecycle and refunds | `implemented` | billing, webhooks, provider lifecycle signals, refunds/disputes | payment-provider semantics require integration-level release testing |

## 1. Operations Dashboard

- Status: `implemented`
- Purpose: Provide an operational overview of platform health across content, review load, billing risk, and quality.
- Business logic served: Gives admins a central control point for the current state of the platform.
- Location: `/admin`
- Who uses it: Admins and operations leads
- Main inputs:
  - admin dashboard summary data
- Main outputs:
  - published content count
  - review backlog and overdue state
  - billing risk
  - agreement rate
  - quality and flag summaries
- Main actions:
  - inspect operational shortcuts
  - move into content, review ops, or quality surfaces
- Cross-dashboard impact:
  - this page summarizes the health of learner delivery and expert operations rather than editing them directly

## 2. Content Library

- Status: `implemented`
- Purpose: Manage the catalog of learner-facing content.
- Business logic served: Controls what practice material exists and how it is filtered by type, profession, and publication state.
- Location: `/admin/content`
- Who uses it: Content managers and admins
- Main inputs:
  - content filters
  - content rows
- Main outputs:
  - filtered content inventory
- Main actions:
  - search
  - filter by type, profession, and status
  - open item
  - open revisions
- Step-by-step workflow:
  1. Admin opens the content library.
  2. Admin filters to the target content set.
  3. Admin opens an item for editing or revision review.
- Dependencies:
  - content editor
  - content revisions
- Cross-dashboard impact:
  - controls the practice material learners can access

## 3. Content Editor

- Status: `implemented`
- Purpose: Create and maintain structured learning and assessment content.
- Business logic served: Encodes task definition, profession mapping, difficulty, time, prompts, criteria focus, and model-answer support.
- Location:
  - `/admin/content/new`
  - `/admin/content/[id]`
- Who uses it: Content managers and admins
- Main inputs:
  - title
  - content type
  - sub-test code
  - profession
  - difficulty
  - estimated duration
  - description
  - prompt or case notes
  - model answer
  - criteria focus
- Main outputs:
  - created or updated content item
  - publication state changes
- Main actions:
  - create content
  - edit metadata
  - edit prompt or case notes
  - map criteria
  - publish content
- Step-by-step workflow:
  1. Admin opens the editor in create or edit mode.
  2. Admin completes metadata and content fields.
  3. Admin maps criteria and optional model-answer support.
  4. Admin reviews impact information.
  5. Admin saves or publishes.
- Dependencies:
  - taxonomy
  - criteria
  - content impact data
- Cross-dashboard impact:
  - changes learner-visible content and can affect study-plan references and evaluation logic

## 4. Content Revisions

- Status: `implemented`
- Purpose: Preserve edit history and allow controlled rollback.
- Business logic served: Supports safe content operations and auditability.
- Location: `/admin/content/[id]/revisions`
- Who uses it: Content managers and admins
- Main inputs:
  - revision history
- Main outputs:
  - revision restore action
- Main actions:
  - inspect revisions
  - restore a prior version
- Step-by-step workflow:
  1. Admin opens a content item's revision history.
  2. Admin inspects prior versions.
  3. Admin restores a revision if required.
- Dependencies:
  - content editor
  - audit logs
- Cross-dashboard impact:
  - restored content can alter what learners receive and what experts review

## 5. Professions

- Status: `implemented`
- Purpose: Manage profession and classification structures used across the platform.
- Business logic served: Keeps OET content, learner goals, and operational filters aligned to profession-specific context.
- Location: `/admin/taxonomy`
- Who uses it: Admins and content operators
- Main inputs:
  - taxonomy nodes
  - archive impact data
- Main outputs:
  - created, updated, or archived taxonomy entries
- Main actions:
  - create taxonomy entry
  - edit taxonomy entry
  - inspect impact
  - archive taxonomy entry
- Dependencies:
  - content mapping
  - learner profession profiles
  - expert queue filtering
- Cross-dashboard impact:
  - influences learner goals, content assignment, and operational filtering

## 6. Rubrics and Criteria

- Status: `implemented`
- Purpose: Define evaluation criteria by sub-test.
- Business logic served: Makes criterion-based feedback configurable and governable.
- Location: `/admin/criteria`
- Who uses it: Admins and assessment leads
- Main inputs:
  - criterion name
  - description
  - weight
  - status
  - sub-test grouping
- Main outputs:
  - criterion definitions by Writing, Speaking, Reading, and Listening
- Main actions:
  - create criterion
  - update criterion
  - switch between sub-test tabs
- Dependencies:
  - content criteria mapping
  - evaluation output
  - expert review rubric structure
- Cross-dashboard impact:
  - affects learner-facing feedback language and expert scoring structures

## 7. AI Evaluation Configuration

- Status: `implemented`
- Purpose: Manage AI provider registry, feature routing, quotas, BYOK/platform-only policy, kill-switches, and usage visibility.
- Business logic served: Separates operational AI control from learner, expert, and admin authoring surfaces while preserving grounded prompt and usage-recording rules.
- Location: `/admin/ai-config`
- Who uses it: Admins and AI operations leads
- Main inputs:
  - provider
  - model
  - task type
  - confidence threshold
  - routing rule
  - experiment flag
  - prompt label
- Main outputs:
  - created, updated, and activated AI configurations
- Main actions:
  - create config
  - update config
  - activate config
- Dependencies:
  - learner evaluation flows
  - review ops
  - quality analytics
- Cross-dashboard impact:
  - changes how evaluation behavior is routed or interpreted across learner and expert workflows

## 8. Review Operations

- Status: `implemented`
- Purpose: Monitor and intervene in the human review pipeline.
- Business logic served: Maintains service continuity for Writing and Speaking expert review.
- Location: `/admin/review-ops`
- Who uses it: Admin ops leads
- Main inputs:
  - review ops summary
  - queue rows
  - failure and stuck-work data
  - expert roster data
- Main outputs:
  - queue assignment changes
  - cancelled or reopened work
- Main actions:
  - assign review
  - cancel review
  - reopen review
  - inspect failures and stuck work
- Step-by-step workflow:
  1. Admin inspects queue pressure and failure indicators.
  2. Admin assigns or reassigns review work.
  3. Admin cancels or reopens work as needed.
  4. Admin investigates stuck or failed items.
- Dependencies:
  - learner review requests
  - expert queue
  - users
- Cross-dashboard impact:
  - directly changes what work appears in the expert console and when learners receive review outcomes
- What to test:
  - assign, cancel, and reopen actions
  - failed review visibility
  - stuck-work visibility

## 9. Notification Governance

- Status: `implemented`
- Purpose: Control communication policy across user audiences and events.
- Business logic served: Keeps operational messaging under admin control rather than hardcoded behavior.
- Location: `/admin/notifications`
- Who uses it: Admins and operations
- Main inputs:
  - notification catalog
  - policy matrix
  - health data
  - recent deliveries
- Main outputs:
  - updated notification policy
  - test email triggers
- Main actions:
  - edit per-event policy
  - change audience-channel modes
  - send test email
  - inspect delivery and failure health
- Dependencies:
  - learner settings
  - expert notifications
  - notification API and health surfaces
  - push subscription and token registration APIs
  - consent and suppression APIs
  - account-scoped SignalR notification groups
- Cross-dashboard impact:
  - determines how learner, expert, sponsor, and admin users receive operational messaging
- What to test:
  - policy persistence
  - channel toggles
  - test-email behavior
  - push token/subscription registration and consent revocation
  - delivery suppression and proof-trigger behavior
  - real-time group delivery for authenticated accounts

## 9A. Upload, Storage, and Scanner Governance

- Status: `implemented`
- Purpose: Protect content and media ingestion before files become learner-visible or operationally trusted.
- Business logic served: Keeps the content-paper pipeline, media uploads, bulk ZIP imports, and derived artifacts under size, safety, and provenance controls.
- Main constraints:
  - content uploads use per-role size limits for audio, PDF, image, and ZIP assets
  - chunked uploads use 8 MB chunks and 24-hour staging cleanup
  - ZIP imports enforce entry count, per-entry size, total uncompressed size, and compression-ratio limits
  - ClamAV is the production scanner path; NoOp scanning is only acceptable outside production
  - scanner errors fail closed by default
  - scanner failures and unsafe content should be quarantined rather than published
  - canonical learner content uses `ContentPaper -> ContentPaperAsset -> MediaAsset`, source provenance, publish gates, and audit events
- What to test:
  - oversized assets per role
  - incomplete chunk cleanup
  - ZIP bomb and high-ratio rejection
  - production scanner configuration guard
  - quarantine and publish-gate behavior

## 10. Quality Analytics

- Status: `implemented`
- Purpose: Provide quality oversight for evaluation and review operations.
- Business logic served: Makes quality measurable through agreement, appeals, timing, and risk indicators.
- Location: `/admin/analytics/quality`
- Who uses it: Admins, quality leads, AI operations
- Main inputs:
  - time-range filter
  - sub-test filter
  - profession filter
- Main outputs:
  - AI-human agreement
  - appeals rate
  - average review time
  - risk-case counts
  - content and adoption summaries
- Main actions:
  - filter analytics
  - inspect quality and operations trend charts
- Dependencies:
  - review operations
  - AI configuration
  - content governance
- Cross-dashboard impact:
  - informs changes that affect both learner evaluation experience and expert review quality

## 11. User Operations

- Status: `implemented`
- Purpose: Manage learner, expert, sponsor, and admin accounts operationally.
- Business logic served: Centralizes user lifecycle and entitlement adjustments.
- Location:
  - `/admin/users`
  - `/admin/users/[id]`
- Who uses it: Admins
- Main inputs:
  - role, status, and search filters
  - user detail data
- Main outputs:
  - invitations
  - status updates
  - delete/restore actions
  - credit adjustments
  - password reset triggers
- Main actions:
  - invite user
  - update status
  - delete or restore user
  - adjust credits
  - trigger password reset
- Dependencies:
  - billing
  - review ops
  - auth flows
- Cross-dashboard impact:
  - changes who can access learner, expert, sponsor, and admin experiences and with what operational status

## 12. Billing Operations

- Status: `implemented`
- Purpose: Manage commercial structure, entitlements, and financial records.
- Business logic served: Encodes how access and paid review capacity are sold and administered.
- Location: `/admin/billing`
- Who uses it: Admins and billing operators
- Main inputs:
  - plans
  - add-ons
  - coupons
  - subscriptions
  - coupon redemptions
  - invoices
- Main outputs:
  - created and updated commercial objects
- Main actions:
  - create or update plan
  - create or update add-on
  - create or update coupon
  - inspect subscriptions, redemptions, and invoices
- Dependencies:
  - learner billing page
  - Writing and Speaking review-request entitlement logic
- Cross-dashboard impact:
  - affects learner purchase options and review-access economics
- What to test:
  - plan and add-on save
  - coupon rule persistence
  - invoice and subscription visibility

## 13. Feature Flags

- Status: `implemented`
- Purpose: Manage release, experiment, and operational flags.
- Business logic served: Allows controlled change rollout without direct code deployment switches in the UI layer.
- Location: `/admin/flags`
- Who uses it: Admins and product operations
- Main inputs:
  - name
  - key
  - owner
  - type
  - rollout percentage
  - enabled state
- Main outputs:
  - created or updated flag
  - activated or deactivated flag
- Cross-dashboard impact:
  - can change behavior across learner, expert, or admin surfaces depending on flag usage

## 14. Audit Logs

- Status: `implemented`
- Purpose: Track privileged administrative changes.
- Business logic served: Supports auditability, compliance, and operational diagnosis.
- Location: `/admin/audit-logs`
- Who uses it: Admins and compliance-minded operators
- Main inputs:
  - action filters
  - actor filters
  - search
- Main outputs:
  - filtered audit stream
  - detail drawer
  - export file
- Main actions:
  - search logs
  - inspect log details
  - export CSV
- Dependencies:
  - content operations
  - user operations
  - notifications
  - billing and flags
- Cross-dashboard impact:
  - does not change platform behavior directly, but records the changes that do

## Admin QA Focus Areas

- content create, edit, publish, and archive
- revision restore behavior
- taxonomy impact preview before archive
- criterion editing by sub-test
- AI config activation
- review assignment and reopen flows
- notification policy save and test email
- user credit adjustments and lifecycle actions
- billing object save and visibility
- refund/dispute/provider lifecycle monitoring
- upload scanner and chunked-import safety gates
- flag activation and deactivation
- audit-log detail and export

## Observed Gaps and Partial Implementations

- The admin surface is broad and clearly implemented, but some downstream effects depend on wider system integration that should be validated end to end rather than inferred from admin UI alone.
- Billing operations are concrete, but the external payment-provider behavior was not exhaustively traced in this documentation pass.
- Provider lifecycle, refunds, disputes, and webhook retries are implemented surfaces, but provider-side behavior still needs integration-level release testing.
- Feature flags are fully represented as an admin surface; the exact consumer coverage of each flag key should be validated against runtime usage when testing a specific release.
- The legacy `/v1/media/upload` path and canonical content-paper upload pipeline coexist; content/media releases should validate which path is authoritative for the asset type being changed.

Revision source: `ROUTE-SNAPSHOT-2026-05-13`.

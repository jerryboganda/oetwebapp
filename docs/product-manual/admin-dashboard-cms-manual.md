# Admin Dashboard and CMS Manual

This manual documents the admin-facing control plane as implemented in the current codebase.

Related documents:

- [Master Product Manual](./master-product-manual.md)
- [Learner App Manual](./learner-app-manual.md)
- [Expert Console Manual](./expert-console-manual.md)
- [Cross-System Business Logic and Workflows](./cross-system-business-logic-and-workflows.md)

Status labels used in this document:

- `implemented`
- `partial`
- `unclear`

## Admin Route Inventory

- `/admin`
- `/admin/content`
- `/admin/content/new`
- `/admin/content/[id]`
- `/admin/content/[id]/revisions`
- `/admin/taxonomy`
- `/admin/criteria`
- `/admin/ai-config`
- `/admin/review-ops`
- `/admin/notifications`
- `/admin/analytics/quality`
- `/admin/users`
- `/admin/users/[id]`
- `/admin/billing`
- `/admin/flags`
- `/admin/audit-logs`

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

## 5. Profession Taxonomy

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
- Purpose: Manage model and routing configurations for evaluation-related tasks.
- Business logic served: Separates operational AI control from learner and expert surfaces.
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
- Cross-dashboard impact:
  - determines how learner, expert, and admin users receive operational messaging
- What to test:
  - policy persistence
  - channel toggles
  - test-email behavior

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
- Purpose: Manage learner, expert, and admin accounts operationally.
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
  - changes who can access learner, expert, and admin experiences and with what operational status

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
- flag activation and deactivation
- audit-log detail and export

## Observed Gaps and Partial Implementations

- The admin surface is broad and clearly implemented, but some downstream effects depend on wider system integration that should be validated end to end rather than inferred from admin UI alone.
- Billing operations are concrete, but the external payment-provider behavior was not exhaustively traced in this documentation pass.
- Feature flags are fully represented as an admin surface; the exact consumer coverage of each flag key should be validated against runtime usage when testing a specific release.


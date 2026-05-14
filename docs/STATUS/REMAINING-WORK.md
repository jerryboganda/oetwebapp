# Current Remaining Work

Status date: 2026-05-14

Audience: owner, engineering, QA, release, support, and stakeholder review.

This document is the human-readable index for current remaining work. The May 2026 v1 launch closure register remains preserved in `docs/STATUS/remaining-work.yaml`; that YAML is closed for v1 launch evidence and is not the active backlog for new post-closure initiatives.

Current active work is tracked here by workstream and points to the authoritative detailed artifact for each workstream. Update order: source artifact first, this index second, and `PROGRESS.md` third.

## User Decisions Locked On 2026-05-14

| Decision | Selected value | Recommendation status |
| --- | --- | --- |
| Scope | Whole OET platform | Track post-v1/all-audience readiness, not only ElevenLabs. |
| Priority mode | Production safety first | Keep safety gates ahead of feature exposure. |
| First realtime STT audio strategy | Compare all three before choosing | Spike browser PCM, backend transcoding, and verified container streaming before launch. |
| ElevenLabs pilot cap | `$25/month` | Use as the current pilot default until owner changes it. |
| Realtime beta topology | Single API instance | Acceptable for beta only; sticky/distributed remains a scale-out blocker. |
| Protected ElevenLabs smoke | Mandatory | Required before real-provider admin exposure or paid beta. |
| Final audience goal | All audiences eventually | Direct adult first; sponsors, schools, and minors require explicit privacy/legal gates. |

## Source-Of-Truth Map

| Area | Canonical detail | Status |
| --- | --- | --- |
| V1 launch closure | `docs/STATUS/remaining-work.yaml` | Closed for v1 launch evidence. |
| Current remaining-work index | `docs/STATUS/REMAINING-WORK.md` | Current stakeholder index. |
| Progress ledger | `PROGRESS.md` | Append-only narrative. |
| ElevenLabs realtime STT | `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md` | Active rollout backlog. |
| Conversation invariants | `docs/CONVERSATION.md` | Production contract. |
| Deployment gate | `docs/ops/deploy-gate.md` | Production safety contract. |
| UX audit inventory | `docs/ux/UX-AUDIT-ROUTE-INVENTORY.md` | Active route-readiness input. |
| Mobile plan | `docs/capacitor-mobile-app-plan.md` | External credential/store/device readiness input. |
| Desktop plan | `docs/electron-desktop-conversion-plan.md` | Signed artifact/update-flow readiness input. |
| Listening V2 deferrals | `docs/LISTENING.md`, `PRD-LISTENING-V2.md`, `docs/LISTENING-RULEBOOK-CITATIONS.md` | Post-launch module backlog. |
| Grammar GA | `docs/GRAMMAR-MODULE.md` | GA signoff status needs owner decision. |

## Closed V1 Launch Register

RW-001 through RW-022 are closed in `docs/STATUS/remaining-work.yaml`. Do not reopen those IDs for new work. If a closed item spawns new scope, create a new post-v1 item here and link back to the RW item as provenance.

## Active P0 Work

### P0-001 - Canonical Governance And Evidence Scope

- Recommendation: treat `docs/STATUS/remaining-work.yaml` as closed v1 evidence and this document as the current index.
- Remaining work: add owner/evidence/status updates here for post-v1 initiatives instead of changing closed RW IDs.
- Input required: owner approval that web/API v1 closure remains valid while mobile/desktop/all-audience work continues as post-v1 readiness.
- No-go if missing: stakeholders may read old closure language as proof that all platform work is complete.

### P0-002 - Backend Build Health Check

- Evidence: one failure-mode audit reported `npm run backend:build` failing on `ConversationAsrProviderSelector.cs` and `ConversationOptions` resolution.
- Remaining work: rerun `npm run backend:build` in the current worktree and fix any live compile errors before relying on deeper validation.
- Input required: none.
- Recommendation: make this the first verification command after planning edits.

### P0-003 - ElevenLabs Realtime STT Production Authorization

- Canonical source: `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md`.
- Remaining work: complete RTSTT-001 through RTSTT-008 before any real-provider exposure outside protected smoke: audio/device compatibility, protected smoke, spend reservations, circuit breaker, transcript authority, single-instance topology proof, sponsor/school/minor gates, and consent model.
- User defaults: `$25/month` pilot cap, single API instance beta, protected smoke mandatory, compare all audio strategies.
- Input required: rotated ElevenLabs STT key through protected secret channel, vendor/privacy approval, target beta users, region/topology confirmation.
- No-go if missing: no admin real-provider exposure, no paid WSS streams, and no sponsor/school/minor rollout.

### P0-004 - External Launch Readiness Gate

- Evidence: mobile store credentials/assets/privacy approval, signed desktop artifacts, manual assistive-tech signoff, and GitHub-hosted QA observation are still external-evidence items.
- Remaining work: track these as explicit `pending-external` gates instead of hiding them behind closed v1 status.
- Input required: Apple/Google accounts and signing assets, desktop signing method, release channel decisions, manual QA availability.
- Recommendation: launch web/API first if desired; keep mobile stores and signed desktop as beta until evidence is attached.

### P0-005 - Mobile Association Files And Support Surface

- Evidence: `public/.well-known/apple-app-site-association` contains `TEAM_ID.com.oetprep.learner`; `public/.well-known/assetlinks.json` contains `REPLACE_WITH_YOUR_SHA256_CERT_FINGERPRINT`.
- Remaining work: replace placeholders with production Team ID, bundle ID, and SHA-256 cert fingerprints; add static/CI check for placeholder values; create or redirect `/support` because store listing docs reference it.
- Input required: Apple Team ID, final bundle ID, Android signing certificate fingerprint, support contact path.
- Recommendation: create a simple public support page with contact, privacy, delete-account, and response-time expectations.

### P0-006 - Production Release Evidence Handoff

- Evidence: production deploy workflow expects an evidence bundle already present in the production checkout, while the deploy wrapper fails closed if it is missing.
- Remaining work: document and automate how signed CI evidence reaches `/opt/oetwebapp` for a given SHA.
- Input required: choose manual copy to release-evidence or GitHub Actions artifact fetch by SHA.
- Recommendation: automate artifact fetch by SHA and verify with `EXPECTED_GIT_SHA`.

### P0-007 - Accessibility Launch Policy

- Evidence: release readiness requires manual NVDA/VoiceOver signoff, while the old remaining-work register marks broader accessibility as done-v1-scope.
- Remaining work: decide whether manual assistive-tech signoff is a hard launch gate or an accepted post-launch risk with owner and expiry.
- Input required: owner decision on launch gate.
- Recommendation: require manual signoff for auth, dashboard, billing, one immersive learner flow, expert review submit, and admin audit/user-credit flows.

## Active P1 Work

### P1-001 - Staging Deploy Parity

- Remaining work: add production-style evidence, post-deploy, observability, Reading/media, rollback, and provider-safe smoke gates to staging before using it as production rehearsal.
- Input required: staging host/domain/secrets and whether staging may use non-production provider credentials.

### P1-002 - Production Smoke Automation

- Remaining work: create a protected manual workflow for learner plus privileged read-only smoke after deploy approval.
- Input required: least-privilege learner/admin/sponsor/expired smoke accounts as protected secrets.

### P1-003 - Observability And Alert Proof

- Remaining work: attach dashboard IDs, alert-channel proof, route latency/error dashboards, provider-backed flow dashboards, and incident/tabletop evidence.
- Input required: alert provider/channel and dashboard URLs.

### P1-004 - Backup Restore Drill

- Remaining work: test restore into a non-live DB and record evidence.
- Input required: S3/R2 backup target and quarterly drill cadence.

### P1-005 - Mobile Release Readiness

- Remaining work: validate signing secrets, store metadata, privacy manifest, deep links, push config, real-device microphone/keyboard/safe-area/background tests, and signed artifact verification.
- Input required: Apple Developer account, Google Play account, signing certs, provisioning profile, store billing stance.

### P1-006 - Desktop Release Readiness

- Remaining work: prove signed Windows release, update server behavior, OAuth callback, packaged desktop smoke, backend version pinning, and macOS/Linux signing/notarization decisions.
- Input required: Windows signing method, update server URL, macOS/Linux launch scope.

### P1-007 - Sponsor Billing Attribution

- Remaining work: replace active-sponsorship-window heuristics with explicit payer attribution before finance reporting is contractual.
- Input required: choose `SponsorshipId` foreign key or payer-type/reference model and backfill semantics.

### P1-008 - Sponsor Portal Trustworthiness

- Remaining work: remove or label static ROI metrics, replace placeholder invoices with real empty states, and show seat/invite status from real data only.
- Input required: whether sponsor portal is launch-critical or beta.

### P1-009 - Expert Mobile Review Evidence Binding

- Remaining work: hide or redirect `/expert/mobile-review` until candidate evidence, audio/transcript, rubric context, draft state, and rework flow are bound.
- Input required: ship, hide, or redirect decision.
- Recommendation: redirect to the full expert review workspace until complete.

### P1-010 - Mobile Push Token Registration

- Remaining work: route native push-token registration through `apiClient` or a typed helper with CSRF/auth coverage and add a bridge test.
- Input required: none.

### P1-011 - OET Scoring Threshold Audit

- Remaining work: audit direct `>= 350`, `>= 70`, and `>= 4.2` decision points and replace mission-critical pass/fail logic with scoring helpers.
- Input required: none.
- Recommendation: triage only pass/fail or projected-grade decisions; do not blanket-change display thresholds.

### P1-012 - Reading AI Extraction Production Posture

- Remaining work: decide whether Reading AI extraction is intentionally disabled in production or should be implemented through grounded feature-coded provider flow.
- Input required: production product decision.

## Active P2 Work

### P2-001 - UX Route Audit Execution

- Remaining work: execute the T0/T1-first route audit from `docs/ux/UX-AUDIT-ROUTE-INVENTORY.md` with screenshots, content inventory, accessibility checks, journey maps, and scorecards.
- Recommended first routes: auth, onboarding, dashboard, study plan, diagnostic, four skills, mocks, review, billing, conversation, expert queue/review, admin content/import/billing/users, sponsor dashboard/learners/billing.

### P2-002 - E2E Skip/TODO Triage

- Remaining work: split E2E skips into role-gated, credential-gated, fixture-missing, and genuine TODO buckets; prioritize auth, billing, content upload, realtime conversation, and mobile smoke.

### P2-003 - EF InMemory To SQLite Risk Reduction

- Remaining work: convert high-risk tests using `UseInMemoryDatabase` to SQLite for quotas, entitlements, auth refresh/session, content upload, billing idempotency, and realtime turn store.

### P2-004 - Content Upload Browser Retry Proof

- Remaining work: add deterministic browser fixture for chunk upload retry/resume/duplicate chunk behavior.

### P2-005 - Billing Provider External Proof

- Remaining work: prove provider configuration, webhook secret correctness, checkout redirect UX, production env fail-fast, and replay with real provider-shaped payloads.
- Input required: sandbox Stripe/PayPal keys and webhook secrets through approved secret channel.

### P2-006 - Mobile Billing Policy

- Remaining work: record mobile monetization decision: reader-app scope, web checkout link placement, App Review notes, fallback IAP plan, and rejection criteria.
- Recommendation: reader-app/subscriber-access for first mobile release; keep checkout on web.

### P2-007 - Admin/Support Navigation And Public Support

- Remaining work: ensure Settings navigates to settings, Help opens support, and public `/support` exists or store listings point elsewhere.

### P2-008 - Listening V2 Deferred Work

- Remaining work: track full save/submit DTO handoff, all-parts paper final-review parity, unanswered-number banner coverage, and seeded multi-part free-navigation E2E.

### P2-009 - Grammar GA Signoff

- Remaining work: clarify whether Grammar is GA-blocked, operational, or awaiting product signoff.

### P2-010 - Native Speaking And Pronunciation Realtime Expansion

- Remaining work: do not duplicate realtime stacks; use Conversation realtime proof first, then define native Speaking and Pronunciation-specific storage, scoring, consent, retention, and QA gates.

### P2-011 - Markdown/Docs Hygiene Scope

- Remaining work: run markdownlint over release, ops, QA, and mission-critical docs; fix violations or define a launch-doc lint scope that excludes historical docs.

## Inputs Still Required From You

| Input | Best recommendation |
| --- | --- |
| Web/API versus whole-platform public launch order | Launch web/API first; keep mobile stores and signed desktop as beta until external evidence lands. |
| Manual accessibility signoff | Require it for T0/T1 launch flows before public launch. |
| Mobile billing policy | Use reader-app/subscriber-access first; keep checkout on web until store review guidance is confirmed. |
| Production evidence handoff | Automate GitHub artifact fetch by SHA on the VPS/deploy workflow. |
| Support route | Add a public `/support` page with privacy/delete-account/contact details. |
| Sponsor portal launch status | Treat sponsor portal as beta unless real billing, ROI, and invoice evidence is attached. |
| Expert mobile review status | Hide or redirect to full expert review until real evidence binding is complete. |
| ElevenLabs key | Provide only through protected secret/admin channel, never chat. |
| Vendor/privacy approval | Direct adults first; sponsors/schools/minors only after legal/privacy approval and server-side tests. |

## No-Go Criteria

- No real ElevenLabs provider exposure without mandatory protected smoke, `$25/month` cap enforcement, audio/device proof, and rollback path.
- No all-audience speech processing until sponsor/school/minor privacy gates are approved and tested.
- No mobile store submission with placeholder app association files or missing support surface.
- No signed desktop public release without signed artifact validation and update-flow proof.
- No production deploy unless signed evidence for the exact SHA is available and verified.
- No claim that the whole platform is “complete” without separating closed v1 work from active post-v1/all-audience readiness.

## Change Log

- 2026-05-14: Created current remaining-work index from user decisions, code/doc audit, and 8-subagent sweep covering research, architecture, adversarial review, DevOps, UX/accessibility, failure modes, canonical planning, and documentation. Preserved `docs/STATUS/remaining-work.yaml` as the closed v1 launch register.

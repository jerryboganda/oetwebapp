# Current Remaining Work

Status date: 2026-05-29

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
| Launch packaging goal | Web, mobile, and desktop together | Code guardrails are in place; external signing/store/accessibility evidence remains launch-blocking. |
| Sponsor portal | Hidden until fully ready | Keep sponsor routes disabled until payer attribution, real invoices, ROI analytics, and contracts are proven. |
| Reading AI extraction | Launch-critical with human approval | Safety guardrails are in place; full grounded provider extraction/admin review UI remains. |

## Source-Of-Truth Map

| Area | Canonical detail | Status |
| --- | --- | --- |
| V1 launch closure | `docs/STATUS/remaining-work.yaml` | Closed for v1 launch evidence. |
| Current remaining-work index | `docs/STATUS/REMAINING-WORK.md` | Current stakeholder index. |
| Docs completion audit | `docs/STATUS/DOCS-COMPLETION-AUDIT-2026-05-29.md` | Current docs-folder completion verdict. |
| Progress ledger | `PROGRESS.md` | Append-only narrative. |
| ElevenLabs realtime STT | `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md` | Active rollout backlog. |
| Conversation invariants | `docs/CONVERSATION.md` | Production contract. |
| Deployment gate | `docs/ops/deploy-gate.md` | Production safety contract. |
| UX audit inventory | `docs/ux/UX-AUDIT-ROUTE-INVENTORY.md` | Active route-readiness input. |
| Mobile plan | `docs/capacitor-mobile-app-plan.md` | External credential/store/device readiness input. |
| Desktop plan | `docs/electron-desktop-conversion-plan.md` | Signed artifact/update-flow readiness input. |
| Listening V2 deferrals | `docs/LISTENING.md`, `PRD-LISTENING-V2.md`, `docs/LISTENING-RULEBOOK-CITATIONS.md` | Local deferrals closed; content evidence remains separate. |
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

- Evidence: Docker backend API build on 2026-05-29 produced `OetWithDrHesham.Api.dll`; warnings remain but no live compile error was emitted for `ConversationAsrProviderSelector.cs` / `ConversationOptions`.
- Remaining work: keep backend build in the final validation matrix; fix only live compiler errors, not historical stale-assets output.
- Input required: none.
- Recommendation: make this the first verification command after planning edits.

### P0-003 - ElevenLabs Realtime STT Production Authorization

- Canonical source: `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md`.
- Local hardening complete: production startup now fails closed unless the grounded AI provider has an external HTTPS base URL plus API key, and realtime STT has a real provider key, adult-learner authorization, legal/privacy approval, spend/pricing controls, approved region, and an approved topology value (`single-instance`, `single-region-sticky`, or `distributed`).
- Local hardening update 2026-05-29: launch-readiness statuses are server-validated, and admin conversation settings now reject realtime real-provider authorization unless legal, privacy, protected-smoke, spend-cap, topology, and evidence URL gates are approved.
- Remaining work: complete RTSTT-001 through RTSTT-008 before any real-provider exposure outside protected smoke: audio/device compatibility, protected smoke, spend reservations, circuit breaker, transcript authority, topology evidence, sponsor/school/minor gates, and consent model.
- User defaults: `$25/month` pilot cap, single API instance beta, protected smoke mandatory, compare all audio strategies.
- Input required: rotated ElevenLabs STT key through protected secret channel, vendor/privacy approval, target beta users, region/topology confirmation.
- No-go if missing: no admin real-provider exposure, no paid WSS streams, and no sponsor/school/minor rollout.

### P0-004 - External Launch Readiness Gate

- Evidence: mobile store credentials/assets/privacy approval, signed desktop artifacts, manual assistive-tech signoff, and GitHub-hosted QA observation are still external-evidence items.
- Local hardening complete: mobile release validation rejects placeholder association files, invalid app versions/version codes, and malformed Android certificate fingerprints; production Capacitor app URLs now fail closed to HTTPS/non-loopback except explicit local-loopback development mode; mobile app-host deep links reject HTTP; desktop public release builds require a safe remote API target, verify update metadata/checksums, and route packaged smoke through the release wrapper.
- Local hardening update 2026-05-29: desktop Speaking keeps using the proven Chromium recorder path until native Electron capture sends real chunks, preventing zero-byte desktop recordings from the dormant IPC bridge.
- Remaining work: attach real external evidence for these explicit `pending-external` gates instead of hiding them behind closed v1 status.
- Input required: Apple/Google accounts and signing assets, desktop signing method, release channel decisions, manual QA availability.
- Recommendation: launch web/API first if desired; keep mobile stores and signed desktop as beta until evidence is attached.

### P0-005 - Mobile Association Files And Support Surface

- Evidence: `public/.well-known/apple-app-site-association` contains `TEAM_ID.com.oetwithdrhesham.learner`; `public/.well-known/assetlinks.json` contains `REPLACE_WITH_YOUR_SHA256_CERT_FINGERPRINT`.
- Local hardening complete: `/support` exists; `.well-known` mobile association files bypass auth middleware; `scripts/qa/validate-mobile-release-inputs.mjs` blocks placeholder or malformed association files and checks release version inputs before store packaging.
- Remaining work: replace placeholders with production Team ID, bundle ID, and SHA-256 cert fingerprints.
- Input required: Apple Team ID, final bundle ID, Android signing certificate fingerprint, support contact path.
- Recommendation: create a simple public support page with contact, privacy, delete-account, and response-time expectations.

### P0-006 - Production Digest Handoff

- Evidence: production deploy workflow now requires immutable image digest refs for the target SHA, and deploy scripts now consume the protected workflow's digest inputs directly.
- Remaining work: ensure each manual deploy records the source CI run and the four digest refs in release/deploy notes.
- Input required: final CI artifact URL or run ID at deploy time.
- Recommendation: keep deploys exact-SHA only and supply digest refs through the protected GitHub workflow.

### P0-007 - Accessibility Launch Policy

- Evidence: release readiness requires manual NVDA/VoiceOver signoff, while the old remaining-work register marks broader accessibility as done-v1-scope.
- Local hardening complete: accessibility signoff validation now fails closed unless `qa-artifacts/accessibility-signoff.env` records zero critical/serious axe violations plus manual NVDA and VoiceOver pass results for auth, dashboard, billing, one immersive learner flow, expert review submit, and admin audit/user-credit flows.
- Remaining work: execute the manual assistive-technology pass and attach the signoff artifact to release/deploy notes.
- Input required: manual QA operator availability and the final evidence URL/artifact.
- Recommendation: keep this as a hard production launch gate.

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

- Local hardening complete: mobile release CI passes app version/version-code inputs into the validator and stamps Capacitor/iOS versions with structured Node updates instead of fragile shell substitutions. Production Capacitor config now enforces HTTPS/non-loopback URLs unless explicit local-loopback development mode is enabled, and app deep-link handling rejects HTTP.
- Local hardening update 2026-05-29: mobile `/pair?code=...` deep links now dispatch through the existing device-pairing redeem/exchange backend flow instead of being swallowed by the runtime bridge.
- Remaining work: validate signing secrets, store metadata, privacy manifest, deep links, push config, real-device microphone/keyboard/safe-area/background tests, signed artifact verification, native IAP products, receipt validation, entitlement mapping, cancellation/refund handling, and store-review evidence.
- Input required: Apple Developer account, Google Play account, signing certs, provisioning profile, store products, privacy/support/delete-account metadata, and final IAP policy.

### P1-006 - Desktop Release Readiness

- Local hardening complete: desktop release CI has write permission for GitHub Releases, uses the workflow token explicitly, verifies Windows Authenticode signatures when signing is required, uploads `SHA256SUMS.txt`, enforces safe remote API targets for public releases, validates Electron update metadata, registers the `oet-with-dr-hesham` protocol, queues cold-start deep links, and provides a packaged-smoke wrapper.
- Local hardening update 2026-05-29: the desktop Speaking recorder intentionally falls back to the browser recorder until Electron native capture is implemented end-to-end, avoiding empty recordings.
- Remaining work: prove signed Windows release, hosted update server behavior, OAuth callback, packaged desktop smoke, backend API/version compatibility evidence, macOS signing/notarization, and Linux package evidence.
- Input required: Windows signing method, update server URL/CDN, Apple Developer ID/notarization access, Linux package targets, and final release channel.

### P1-007 - Sponsor Billing Attribution

- Local hardening complete: sponsor portal/frontend access, backend sponsor route mapping, admin sponsor/cohort endpoints, sponsor default redirects, and admin enterprise UI are disabled by default behind explicit sponsor feature flags.
- Remaining work: replace active-sponsorship-window heuristics with explicit payer attribution before finance reporting is contractual.
- Input required: choose `SponsorshipId` foreign key or payer-type/reference model and backfill semantics.

### P1-008 - Sponsor Portal Trustworthiness

- Remaining work: keep sponsor portal hidden until static ROI metrics are removed/labeled, placeholder invoices are replaced with real empty states, and seat/invite status comes from real data only.
- Input required: explicit owner approval before enabling the sponsor feature flags.

### P1-009 - Expert Mobile Review Evidence Binding

- Local hardening complete: `/expert/mobile-review` redirects to the full expert review workspace.
- Remaining work: build a dedicated mobile review surface only after candidate evidence, audio/transcript, rubric context, draft state, and rework flow are bound.
- Input required: none for the redirect; future dedicated surface requires evidence-binding requirements.

### P1-010 - Mobile Push Token Registration

- Remaining work: push-token registration already routes through the typed notification helper; add bridge tests for registration retry/resume and notification tap routing.
- Input required: none.

### P1-011 - OET Scoring Threshold Audit

- Local hardening complete: conversation evaluation pass/grade response, conversation results criterion coloring, and Reading pathway milestone pass logic now route through canonical scoring helpers.
- Remaining work: continue auditing direct `>= 350`, `>= 70`, and `>= 4.2` decision points and replace any remaining mission-critical pass/fail logic with scoring helpers.
- Input required: none.
- Recommendation: triage only pass/fail or projected-grade decisions; do not blanket-change display thresholds.

### P1-012 - Reading AI Extraction Production Posture

- Local hardening complete: `admin.reading_draft` is registered as a platform-only known AI feature, Reading AI extraction always requires human approval, auto-approval was removed, raw provider bodies are not retained by default, and provider/internal failure details are not persisted into draft notes or audit details.
- Remaining work: implement full grounded provider extraction and admin review UI/approval evidence for production.
- Input required: provider credentials and owner approval for protected sandbox/provider smoke.

### P1-013 - Reading Learner DTO Contract Reconciliation

- Local hardening complete 2026-05-29: submitted Reading review payloads no longer emit `CorrectAnswer` or `ExplanationMarkdown`; answer-key-only data stays server-side/admin-only per the AGENTS hard ban.
- Evidence: backend projection redaction in `ReadingLearnerEndpoints.cs`, frontend review copy updated to learner-safe outcome language, and focused regression coverage added to `ReadingAuthoringTests.cs`.
- Remaining work: keep this in the final backend validation matrix and do not reintroduce answer-key-only learner fields without explicit owner signoff changing the invariant.
- Input required: none.

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

- Local hardening complete: V2 save/submit facades, active-player answer handoff, exact unanswered-number warnings, paper all-parts final review, and free-navigation unit coverage are implemented.
- Local hardening update 2026-05-29: learner pathway now consumes the server-authoritative V2 pathway snapshot; Part A/B/C practice waits for diagnostic completion, skips diagnostic launch targets, and passes distinct `part-a`, `part-b`, and `part-c` focus values to the player.
- Local live evidence complete: an isolated Postgres-backed frontend/API runtime passed focused Chromium learner smoke for Reading deep-link, mock report deep-link, Listening answer-key isolation, exam strict-lock intro, paper no-FSM mount, practice happy path, and R10 readiness.
- Remaining work: publish/operator-approve complete real-content multi-part Listening papers as content evidence; this is not a local route-code blocker.

### P2-009 - Grammar GA Signoff

- Remaining work: clarify whether Grammar is GA-blocked, operational, or awaiting product signoff.

### P2-010 - Native Speaking And Pronunciation Realtime Expansion

- Remaining work: do not duplicate realtime stacks; use Conversation realtime proof first, then define native Speaking and Pronunciation-specific storage, scoring, consent, retention, and QA gates.

### P2-011 - Markdown/Docs Hygiene Scope

- Evidence: `docs/STATUS/DOCS-COMPLETION-AUDIT-2026-05-29.md` confirms the docs folder is not 100% complete; it contains canonical specs, active launch gates, stale historical plans, and external-evidence blockers.
- Local hardening complete 2026-05-29: active runbooks with host/VPS-heavy validation guidance were rewritten to local Docker-first commands, and status banners were added to mixed historical/progress docs so they no longer imply standalone backlog truth.
- Remaining work: run markdownlint over release, ops, QA, and mission-critical docs; fix violations or define a launch-doc lint scope that excludes historical evidence logs.

### P2-012 - Learner Portal Enhancement Plan Reconciliation

- Evidence: `docs/learner-portal-enhancement-plan.md` still lists critical open gaps such as Recalls test coverage, `/practice` route status, mobile hardware validation, Writing revision coach, and Speaking transcript/fluency roadmap work.
- Remaining work: re-audit those claims against current routes/code/tests; close stale items with evidence and move any live implementation gaps into this current index.
- Input required: none.

## Inputs Still Required From You

| Input | Best recommendation |
| --- | --- |
| Web/API versus whole-platform public launch order | Launch web/API first; keep mobile stores and signed desktop as beta until external evidence lands. |
| Manual accessibility signoff | Require it for T0/T1 launch flows before public launch. |
| Mobile billing policy | Use reader-app/subscriber-access first; keep checkout on web until store review guidance is confirmed. |
| Production digest handoff | Record the CI run plus `WEB_IMAGE`, `API_IMAGE`, `DB_BACKUP_IMAGE`, and `ROUTER_IMAGE` digest refs for the exact SHA. |
| Support route | Add a public `/support` page with privacy/delete-account/contact details. |
| Sponsor portal launch status | Keep disabled by default until real billing, ROI, invoice, and contract evidence is attached. |
| Native app release credentials | Provide Apple Team ID, bundle IDs, Android SHA-256, signing assets, store products, and app-store metadata before public mobile/desktop release. |
| External integration credentials | Provide sandbox/test-mode provider credentials before enabling connection-test buttons or protected smoke. |
| Expert mobile review status | Hide or redirect to full expert review until real evidence binding is complete. |
| ElevenLabs key | Provide only through protected secret/admin channel, never chat. |
| Vendor/privacy approval | Direct adults first; sponsors/schools/minors only after legal/privacy approval and server-side tests. |

## No-Go Criteria

- No real ElevenLabs provider exposure without mandatory protected smoke, `$25/month` cap enforcement, audio/device proof, and rollback path.
- No production AI/provider-backed feature exposure without `AI__BASEURL`, `AI__APIKEY`, `AI__PROVIDERID`, and `AI__DEFAULTMODEL` configured for a real external provider.
- No all-audience speech processing until sponsor/school/minor privacy gates are approved and tested.
- No mobile store submission with placeholder app association files or missing support surface.
- No signed desktop public release without signed artifact validation and update-flow proof.
- No production deploy unless the exact SHA has CI-recorded immutable image digest refs for web, API, DB backup, and router images.
- No claim that the whole platform is “complete” without separating closed v1 work from active post-v1/all-audience readiness.

## Change Log

- 2026-05-15: Closed the local Listening/Reading live-smoke blocker with an isolated Postgres-backed runtime and focused Chromium learner Playwright pass; fixed stale Listening E2E proxy/locator assumptions and a Postgres-only webhook-retention query bug discovered during runtime validation.
- 2026-05-14: Closed local launch-gate hardening for Listening V2 no-stack coverage, mobile/desktop release guards, realtime STT topology validation, grounded AI provider credential fail-fast, and status-doc synchronization. Remaining P0/P1 items are external evidence/credential gates.
- 2026-05-14: Created current remaining-work index from user decisions, code/doc audit, and 8-subagent sweep covering research, architecture, adversarial review, DevOps, UX/accessibility, failure modes, canonical planning, and documentation. Preserved `docs/STATUS/remaining-work.yaml` as the closed v1 launch register.

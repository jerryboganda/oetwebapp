# Current Remaining Work

Status date: 2026-05-17

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
| Notification module | `docs/NOTIFICATIONS-PRD.md`, `docs/NOTIFICATIONS-PROGRESS.md` | Active multi-channel notification backlog. |
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

- Evidence: `npm run backend:build` passes, and `npm run backend:test` now builds once and runs the backend test project in deterministic class-prefix batches to avoid the monolithic VSTest/xUnit scheduling stall observed on Windows.
- Remaining work: none for local compile/test health; continue with targeted and full backend tests as code changes warrant.
- Input required: none.
- Recommendation: keep `npm run backend:build` as the first backend verification command after planning edits.

### P0-003 - ElevenLabs Realtime STT Production Authorization

- Canonical source: `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md`.
- Local hardening complete: production startup now fails closed unless the grounded AI provider has an external HTTPS base URL plus API key, and realtime STT has a real provider key, adult-learner authorization, legal/privacy approval, spend/pricing controls, approved region, and an approved topology value (`single-instance`, `single-region-sticky`, or `distributed`).
- Remaining work: complete RTSTT-001 through RTSTT-008 before any real-provider exposure outside protected smoke: audio/device compatibility, protected smoke, spend reservations, circuit breaker, transcript authority, topology evidence, sponsor/school/minor gates, and consent model.
- User defaults: `$25/month` pilot cap, single API instance beta, protected smoke mandatory, compare all audio strategies.
- Input required: rotated ElevenLabs STT key through protected secret channel, vendor/privacy approval, target beta users, region/topology confirmation.
- No-go if missing: no admin real-provider exposure, no paid WSS streams, and no sponsor/school/minor rollout.

### P0-004 - External Launch Readiness Gate

- Evidence: mobile store credentials/assets/privacy approval, signed desktop artifacts, manual assistive-tech signoff, and GitHub-hosted QA observation are still external-evidence items.
- Local hardening complete: mobile release validation rejects placeholder association files, invalid app versions/version codes, and malformed Android certificate fingerprints; desktop release workflow now verifies signed Windows artifacts and publishes SHA-256 checksums.
- Remaining work: attach real external evidence for these explicit `pending-external` gates instead of hiding them behind closed v1 status.
- Input required: Apple/Google accounts and signing assets, desktop signing method, release channel decisions, manual QA availability.
- Recommendation: launch web/API first if desired; keep mobile stores and signed desktop as beta until evidence is attached.

### P0-005 - Mobile Association Files And Support Surface

- Evidence: `public/.well-known/apple-app-site-association` contains `TEAM_ID.com.oetprep.learner`; `public/.well-known/assetlinks.json` contains `REPLACE_WITH_YOUR_SHA256_CERT_FINGERPRINT`.
- Local hardening complete: `/support` exists; `scripts/qa/validate-mobile-release-inputs.mjs` blocks placeholder or malformed association files and checks release version inputs before store packaging.
- Remaining work: replace placeholders with production Team ID, bundle ID, and SHA-256 cert fingerprints.
- Input required: Apple Team ID, final bundle ID, Android signing certificate fingerprint, support contact path.
- Recommendation: create a simple public support page with contact, privacy, delete-account, and response-time expectations.

### P0-006 - Production Release Evidence Handoff

- Evidence: production deploy workflow now downloads the exact-SHA production release evidence artifact, copies it to `/opt/oetwebapp`, and the deploy wrapper verifies `EXPECTED_GIT_SHA` before rollout.
- Local hardening complete: production deploy concurrency now queues instead of canceling in-progress deploys, production evidence generation requires same-SHA QA smoke plus SBOM/SCA success before signing, and `SKIP_EVIDENCE_VERIFY=true` is restricted to explicit break-glass with owner approval, risk acceptance, backup ID, rollback SHA, and incident reason.
- Remaining work: attach real production evidence artifact/run links for the target SHA during release execution.
- Input required: none for local deploy-safety code; unsafe no-verification normal production deploys remain blocked by the production contract.
- Recommendation: use signed exact-SHA evidence for all normal production releases and reserve break-glass for emergency containment only.

### P0-007 - Accessibility Launch Policy

- Evidence: release readiness requires manual NVDA/VoiceOver signoff, while the old remaining-work register marks broader accessibility as done-v1-scope.
- Remaining work: complete and attach manual assistive-tech signoff for auth, dashboard, billing, one immersive learner flow, expert review submit, and admin audit/user-credit flows.
- Input required: manual QA availability and signoff evidence.
- Recommendation: keep this as a hard public-launch gate.

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

- Local hardening complete: mobile release CI passes app version/version-code inputs into the validator, stamps Capacitor/iOS versions with structured Node updates instead of fragile shell substitutions, and release preflight now enforces an explicit `MOBILE_BILLING_POLICY` (`web-checkout`, `native-iap`, or `hybrid`) instead of assuming web-only checkout.
- Remaining work: validate signing secrets, store metadata, privacy manifest, deep links, push config, real-device microphone/keyboard/safe-area/background tests, RevenueCat/App Store/Play product IDs where IAP is required, and signed artifact verification.
- Input required: Apple Developer account, Google Play account, signing certs, provisioning profile, store billing stance, RevenueCat public SDK keys, and iOS/Android IAP product IDs when `native-iap` or `hybrid` is selected.

### P1-006 - Desktop Release Readiness

- Local hardening complete: desktop release CI has write permission for GitHub Releases, uses the workflow token explicitly, verifies Windows Authenticode signatures when signing is required, and uploads `SHA256SUMS.txt` with the packaged artifacts.
- Remaining work: prove signed Windows release, update server behavior, OAuth callback, packaged desktop smoke, backend version pinning, and macOS/Linux signing/notarization decisions.
- Input required: Windows signing method, update server URL, macOS/Linux launch scope.

### P1-007 - Sponsor Billing Attribution

- Local hardening complete: `PaymentTransaction.SponsorshipId` and `PayerType` now provide explicit sponsor-paid attribution; learner checkout and wallet top-up paths stamp `PayerType=learner`; legacy unattributed rows keep the active-window fallback for historical continuity only.
- Remaining work: create any new sponsor-paid checkout paths with `SponsorshipId` plus `PayerType=sponsor`, then define a production backfill policy for historical rows before contractual finance reporting.
- Input required: backfill semantics for historical sponsor-paid rows, if any.

### P1-008 - Sponsor Portal Trustworthiness

- Local hardening complete: dashboard spend and billing now come from real sponsor-attributed `PaymentTransaction` rows, invoice empty state is honest, existing sponsor invoices render from API data, and static success/ROI analytics are labelled as under development instead of presented as real metrics.
- Remaining work: attach sponsor browser smoke and legal/privacy approval before public sponsor launch.
- Input required: sponsor launch approval and any required finance/legal signoff.

### P1-009 - Expert Mobile Review Evidence Binding

- Local hardening complete: `/expert/mobile-review` redirects to `/expert/queue` until candidate evidence, audio/transcript, rubric context, draft state, and rework flow are bound.
- Remaining work: only re-expose the route after evidence-bound mobile review is implemented and tested.
- Input required: none until re-exposure is requested.

### P1-010 - Mobile Push Token Registration

- Local hardening complete: native push-token registration now routes through a typed `apiClient` helper with CSRF/auth coverage, and focused bridge/API tests cover the request shape.
- Remaining work: real-device push proof with production APNs/FCM credentials.
- Input required: none.

### P1-011 - OET Scoring Threshold Audit

- Local hardening complete: mission-critical OET pass/fail and projected-grade threshold decisions found in the audit now route through canonical scoring helpers; remaining `>= 70` matches are display/readiness percentages or canonical helper internals.
- Remaining work: keep future scoring call sites on `lib/scoring.ts` and `OetScoring`.
- Input required: none.
- Recommendation: triage only pass/fail or projected-grade decisions; do not blanket-change display thresholds.

### P1-012 - Reading AI Extraction Production Posture

- Local hardening complete: Reading AI extraction now uses grounded admin-only gateway flow with feature routing, strict 20/6/16 structure validation, reading rulebook grounding, and mandatory human approval.
- Remaining work: attach provider-smoke evidence and admin browser proof before launch exposure.
- Input required: approved AI provider credentials through the protected runtime settings path.

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

- Local hardening complete: Launch Readiness now exposes mobile billing policy, RevenueCat iOS/Android public SDK keys, platform IAP product IDs, billing evidence URL, and store-review notes; the Capacitor checkout helper can use RevenueCat native IAP when policy is `native-iap` and otherwise keeps web checkout available for reader-app/subscriber-access flows.
- Remaining work: enter real store-approved policy values and attach App Review / Play review evidence before mobile submission.
- Recommendation: keep policy `hybrid` until store review is complete so web checkout remains available while native IAP can be enabled where Apple/Google policy requires it.

### P2-007 - Admin/Support Navigation And Public Support

- Remaining work: ensure Settings navigates to settings, Help opens support, and public `/support` exists or store listings point elsewhere.

### P2-008 - Listening V2 Deferred Work

- Local hardening complete: V2 save/submit facades, active-player answer handoff, exact unanswered-number warnings, paper all-parts final review, and free-navigation unit coverage are implemented.
- Local live evidence complete: an isolated Postgres-backed frontend/API runtime passed focused Chromium learner smoke for Reading deep-link, mock report deep-link, Listening answer-key isolation, exam strict-lock intro, paper no-FSM mount, practice happy path, and R10 readiness.
- Remaining work: publish/operator-approve complete real-content multi-part Listening papers as content evidence; this is not a local route-code blocker.

### P2-009 - Grammar GA Signoff

- Remaining work: clarify whether Grammar is GA-blocked, operational, or awaiting product signoff.

### P2-010 - Native Speaking And Pronunciation Realtime Expansion

- Remaining work: do not duplicate realtime stacks; use Conversation realtime proof first, then define native Speaking and Pronunciation-specific storage, scoring, consent, retention, and QA gates.

### P2-011 - Markdown/Docs Hygiene Scope

- Remaining work: run markdownlint over release, ops, QA, and mission-critical docs; fix violations or define a launch-doc lint scope that excludes historical docs.

### P2-012 - Notification Module Multi-Channel Completion

- Local planning complete: `docs/NOTIFICATIONS-PRD.md` and `docs/NOTIFICATIONS-PROGRESS.md` now split shipped foundation from remaining SMS, WhatsApp, campaign, segment, template, webhook, cost, consent, and evidence work.
- Remaining work: implement SMS/WhatsApp dispatchers, provider webhooks, campaigns/segments/approvals, template manager, cost dashboard, lifecycle automation, and browser evidence.
- Input required: approved SMS/WhatsApp providers, sender IDs/templates, channel budgets, and consent/privacy policy for schools, sponsors, and minors.

## Inputs Still Required From You

| Input | Best recommendation |
| --- | --- |
| Whole-platform launch order | Keep public launch blocked until mobile, desktop, sponsor, realtime, accessibility, notifications, production evidence, and QA gates are evidence-complete. |
| Manual accessibility signoff | Require it for T0/T1 launch flows before public launch; this is now a hard gate. |
| Mobile billing policy | Use `hybrid`: keep web checkout for reader-app/subscriber-access flows and configure RevenueCat native IAP where App Store/Play policy requires it. |
| Production evidence handoff | Automate GitHub artifact fetch by SHA on the VPS/deploy workflow; do not use unsafe no-verification normal deploys. |
| Support route | Public `/support` exists; keep privacy/delete-account/contact content current for store review. |
| Sponsor portal launch status | Treat sponsor portal as blocked until sponsor browser smoke and finance/legal evidence are attached. |
| Notification providers | Choose approved SMS/WhatsApp vendors, sender IDs/templates, and budget caps through Admin/runtime settings. |
| ElevenLabs key | Provide only through protected secret/admin channel, never chat. |
| Vendor/privacy approval | Direct adults first; sponsors/schools/minors only after legal/privacy approval and server-side tests. |

## No-Go Criteria

- No real ElevenLabs provider exposure without mandatory protected smoke, `$25/month` cap enforcement, audio/device proof, and rollback path.
- No production AI/provider-backed feature exposure without `AI__BASEURL`, `AI__APIKEY`, `AI__PROVIDERID`, and `AI__DEFAULTMODEL` configured for a real external provider.
- No all-audience speech processing until sponsor/school/minor privacy gates are approved and tested.
- No mobile store submission with placeholder app association files or missing support surface.
- No signed desktop public release without signed artifact validation and update-flow proof.
- No production deploy unless signed evidence for the exact SHA is available and verified.
- No claim that the whole platform is “complete” without separating closed v1 work from active post-v1/all-audience readiness.

## Change Log

- 2026-05-17: Closed local backend verification and release-safety gaps: backend tests now run deterministically through `npm run backend:test`, production evidence handoff downloads exact-SHA artifacts, and deploy break-glass requires explicit owner/backup/rollback safeguards.
- 2026-05-15: Closed the local Listening/Reading live-smoke blocker with an isolated Postgres-backed runtime and focused Chromium learner Playwright pass; fixed stale Listening E2E proxy/locator assumptions and a Postgres-only webhook-retention query bug discovered during runtime validation.
- 2026-05-14: Closed local launch-gate hardening for Listening V2 no-stack coverage, mobile/desktop release guards, realtime STT topology validation, grounded AI provider credential fail-fast, and status-doc synchronization. Remaining P0/P1 items are external evidence/credential gates.
- 2026-05-14: Created current remaining-work index from user decisions, code/doc audit, and 8-subagent sweep covering research, architecture, adversarial review, DevOps, UX/accessibility, failure modes, canonical planning, and documentation. Preserved `docs/STATUS/remaining-work.yaml` as the closed v1 launch register.

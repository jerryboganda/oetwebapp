# Docs Completion Audit - 2026-05-29

## Verdict

The `docs/` folder is **not 100% complete**.

Several mission-critical docs are durable, implemented source-of-truth specs. However, the folder also contains active rollout backlogs, external launch gates, stale implementation plans, historical validation reports, and product/QA evidence that must not be read as final platform completion.

The current completion truth is:

| Scope | Status | Meaning |
| --- | --- | --- |
| V1 launch closure register | Closed | `docs/STATUS/remaining-work.yaml` is preserved evidence only. Do not reopen RW IDs. |
| Current remaining-work index | Active | `docs/STATUS/REMAINING-WORK.md` is the live stakeholder index. |
| Canonical domain specs | Mostly complete as contracts | Specs are authoritative, but some modules still have GA/external evidence gates. |
| Platform launch evidence | Not complete | Mobile store, desktop signing/update flow, accessibility, provider smoke, and production workflow evidence remain external gates. |
| Historical plan docs | Mixed | Some are completed ledgers; some are stale and need banners/archive treatment. |

## Completed / Canonical Specs

These docs should remain and are treated as source-of-truth contracts, not pending plans:

| Area | Canonical docs | Audit status |
| --- | --- | --- |
| Scoring | `docs/SCORING.md` | Canonical spec; code helpers exist in TS and .NET. |
| Rulebooks | `docs/RULEBOOKS.md` | Canonical spec; backend rulebook loader/endpoints/tests exist. |
| AI usage | `docs/AI-USAGE-POLICY.md` | Canonical spec; usage recorder/admin endpoints exist. |
| Runtime settings | `docs/ADMIN-RUNTIME-SETTINGS.md` | Canonical spec; provider/admin UI surfaces exist. |
| Result card | `docs/OET-RESULT-CARD-SPEC.md` | Canonical visual contract; component/test surfaces exist. |
| Listening core | `docs/LISTENING.md`, `docs/LISTENING-GRADING-MODEL.md`, `docs/LISTENING-RULEBOOK-CITATIONS.md` | Canonical V2 specs; route/code hardening is present, while content evidence remains separate. |
| Reading authoring | `docs/READING-AUTHORING-POLICY.md` | Canonical policy; authoring plan is historical/closure evidence. |
| Grammar | `docs/GRAMMAR-MODULE.md` | Canonical spec, but GA signoff status remains an active product decision. |
| Pronunciation | `docs/PRONUNCIATION.md`, `docs/PRONUNCIATION-AUTHORING-POLICY.md` | Canonical specs. |
| Conversation | `docs/CONVERSATION.md` | Canonical spec; realtime real-provider rollout remains gated. |
| Billing | `docs/BILLING.md` | Living reference; older billing-hardening docs are ledgers/evidence. |

## Active / Not 100% Complete Areas

This table is high-signal, not exhaustive. The live backlog remains `docs/STATUS/REMAINING-WORK.md`.

| Priority | Area | Evidence | What remains |
| --- | --- | --- | --- |
| P0 | ElevenLabs realtime STT production | `docs/ELEVENLABS-REALTIME-STT-PRODUCTION-PLAN.md` says provider launch is blocked until external evidence is attached. | Rotated key through protected channel, vendor/privacy approval, protected smoke, spend cap proof, topology proof, consent evidence. |
| P0 | External launch readiness | `docs/STATUS/REMAINING-WORK.md` keeps mobile/desktop/accessibility/GitHub-hosted QA as external-evidence gates. | Apple/Google accounts, signing assets, desktop signing/update proof, assistive-tech signoff, GitHub-hosted QA evidence. |
| P0 | Mobile association files | `public/.well-known/apple-app-site-association` and `assetlinks.json` still contain placeholder production values per current status index. | Real Apple Team ID, bundle ID, and Android SHA-256 signing certificate fingerprint. |
| P1 | Staging deploy parity | `docs/STAGING-LOCAL-GITHUB-PLAN.md` still needs stronger production-style gates and safer host trust/volume wording. | Fingerprint pinning, non-destructive teardown guidance, post-deploy/rollback/smoke evidence. |
| P1 | Production smoke automation | `docs/PROD-SMOKE-RUNBOOK.md` and `docs/STATUS/REMAINING-WORK.md` still require protected smoke automation/evidence. | Least-privilege smoke accounts and Docker/CI-safe command path. |
| P1 | Production digest handoff | `docs/STATUS/REMAINING-WORK.md` keeps exact-SHA digest handoff as active release evidence. | CI run URL plus immutable `WEB_IMAGE`, `API_IMAGE`, `DB_BACKUP_IMAGE`, and `ROUTER_IMAGE` digest refs per deploy. |
| P1 | Observability and backup proof | `docs/STATUS/REMAINING-WORK.md` keeps observability/alert proof and backup restore drill active. | Dashboard IDs, alert-channel proof, incident/tabletop evidence, and non-live restore drill evidence. |
| P1 | Mobile release readiness | `docs/capacitor-mobile-app-plan.md` still has store readiness, signing, screenshots, and device QA items. | Store credentials/assets, real-device microphone/keyboard/safe-area/background tests, signed artifact verification, IAP/store policy evidence. |
| P1 | Desktop release readiness | `docs/electron-desktop-conversion-plan.md` still has signing, notarization, update-flow, OAuth callback, packaged smoke, and cross-platform evidence items. | Signed Windows proof, update server proof, macOS/Linux release evidence, packaged smoke. |
| P1 | Sponsor billing / sponsor portal | `docs/STATUS/REMAINING-WORK.md` keeps sponsor portal hidden until billing/ROI/invoice/contract evidence is real. | Explicit payer attribution, real invoices/ROI data, owner approval to enable flags. |
| P1 | Billing provider external proof | `docs/STATUS/REMAINING-WORK.md` keeps provider configuration/webhook/replay evidence active. | Sandbox provider keys through approved secret channels, webhook secret proof, checkout redirect UX, replay evidence. |
| P1 | Reading learner DTO contract reconciliation | Reconciled locally on 2026-05-29: submitted Reading review payloads no longer emit correct-answer or explanation fields. | Keep focused backend/API regression coverage in the final validation matrix; do not reopen without owner signoff. |
| P1 | Reading AI extraction | `docs/STATUS/REMAINING-WORK.md` keeps grounded provider extraction and admin review evidence open. | Full grounded provider extraction and approval evidence for production. |
| P1 | Listening PDF/OCR ingestion | `docs/LISTENING-INGESTION-PRD.md` lists `IPdfTextExtractor`/OCR and extraction review gaps. | Replace deterministic stub extraction where required, add admin diff/approve flow, and attach provider/OCR evidence. |
| P2 | UX/accessibility docs | `docs/ux/UX-AUDIT-*` and `docs/qa/accessibility-report.md` keep route audit/manual AT evidence open. | T0/T1 route scorecards, manual NVDA/VoiceOver pass, zoom/reflow/forced-colors coverage. |
| P2 | Learner portal enhancement gaps | `docs/learner-portal-enhancement-plan.md` still lists critical gaps including Recalls tests, `/practice` route status, mobile hardware validation, Writing revision coach, and Speaking roadmap work. | Re-audit against current routes/code; close stale claims with evidence and move live gaps into the current index. |
| P2 | Docs hygiene | This audit and marker scan found many stale or mixed-scope docs. | Add banners, archive closed plans, reconcile stale claims, and link active backlog to one index. |

## Stale / Mixed-Signal Docs To Reconcile

| Doc family | Issue | Recommended action |
| --- | --- | --- |
| `docs/electron-desktop-conversion-plan.md` | Some sections still list fuses, IPC validation, download handler, and Linux packaging as gaps even though code now covers several of them. | Refresh gap matrix; keep signing/notarization/update-flow as active. |
| `docs/capacitor-mobile-app-plan.md` | Some mobile security rows were refreshed, but later store readiness and version/status sections still contain old or broad pending claims. | Split implemented helper surfaces from device/store evidence gates. |
| `docs/CONTENT-UPLOAD-PLAN.md` | Still reads like a ready-for-implementation plan, while repo instructions describe the subsystem as implemented. | Recast as architecture/history, or replace status with current content-upload contract. |
| `docs/LISTENING-INGESTION-PRD.md` | Contains explicit `TODO`, blocking PDF/OCR extraction, and admin extraction-panel gaps. | Keep as active ingestion backlog or close sections with evidence. |
| `docs/LISTENING-PRODUCTION-AUDIT-2026-05-24.md` | Date-stamped production audit still lists real-content and pathway verification gaps. | Mark as historical snapshot and point to current Listening status. |
| `docs/product-manual/route-api-domain-surface-index.md` | PM-001 was reconciled locally on 2026-05-29; keep as evidence of the Reading DTO invariant and regression requirement. | Maintain as product-manual evidence; do not treat it as an active blocker unless a future projection reintroduces answer-key fields. |
| `docs/learner-portal-enhancement-plan.md` | Claims many gaps are closed but still lists critical open learner-portal gaps. | Re-audit and split stale rows from active route/test/roadmap items. |
| `docs/mocks/PROGRESS.md` and `docs/mocks/FOLLOW-UP-WAVES.md` | Some follow-up waves are implemented, while production smoke and option-ID/randomisation work remain mixed. | Convert to current backlog plus historical closure rows. |
| `docs/speaking/PROGRESS.md` | Many old gap-register rows are likely closed; structured import/OCR remains active. | Convert to resolved/open table. |
| `docs/recalls-content-pack/PROGRESS.md` | Presents many unchecked kickoff items despite seed/data/test surfaces now existing. | Replace with closure summary or current operator-only backlog. |
| `docs/dev/quickstart-speaking.md` | Tells developers to run heavy host commands, conflicting with Docker-heavy repo rules. | Rewrite for local Docker/dev-mode contract. |
| `docs/security/speaking/checklist.md` | Checklist is unsigned while threat model language implies mitigations are settled. | Add evidence/status columns or lower residual-risk confidence. |

## Completion Criteria Before Saying "Docs Are 100% Done"

The docs folder can only be called 100% complete when all of these are true:

1. Every markdown/YAML doc has one clear role: canonical spec, runbook, QA evidence, historical ledger, active backlog, product research, or archive.
2. `docs/STATUS/REMAINING-WORK.md` is the only active backlog index, and every active item links to its detailed artifact.
3. Historical or superseded plans contain a banner that says they are closed/superseded and points to the current source.
4. External launch gates have attached evidence or are explicitly marked `pending-external`.
5. No doc claims whole-platform completion without separating closed v1 work from active post-v1/mobile/desktop/all-audience readiness.
6. Docs that contain runnable validation commands follow the Docker-heavy task policy from `AGENTS.md`.
7. Status-index summaries are backed by source-artifact updates first, per the update order in `docs/STATUS/REMAINING-WORK.md`.

## Recommended Cleanup Phases

| Phase | Goal | Output |
| --- | --- | --- |
| 1 | Lock the docs taxonomy | Add status banners to canonical, historical, active, and superseded docs. |
| 2 | Reconcile active backlog | Update `docs/STATUS/REMAINING-WORK.md` from this audit; close stale items only with evidence. |
| 3 | Refresh platform plans | Fix mobile, desktop, staging, production smoke, and speaking quickstart command guidance. |
| 4 | Archive closed plans safely | Move or label old plans after inbound links are updated. |
| 5 | Attach external evidence | Mobile store, desktop signing, accessibility, provider smoke, and production deploy evidence. |

## Bottom Line

Do **not** tell stakeholders that everything in `docs/` is 100% complete. The accurate statement is:

> The v1 local closure register is complete, and many canonical module specs are implemented. The docs folder still contains active post-v1 launch gates, external evidence requirements, and stale historical plans that need cleanup before the whole docs folder can be considered complete.
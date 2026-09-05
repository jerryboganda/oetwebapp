# Data Model Contracts and Invariants

These concepts must exist explicitly in schema/domain design. **Map to existing repository entities before creating new tables.** Field names are illustrative; concepts/invariants are mandatory.

## 1. LearnerProfile

Minimum concepts: user reference, profession, active exam/version/journey, exam date, target, regulator/country/university goal, attempt/resit, availability/shift/travel, language/code-switch preference, explanation/teaching-style preference, voice/text preference, AI tier reference, onboarding state, source/consent for imported/editable fields and audit/version timestamps.

Invariants: user can edit allowed fields; derived fields are distinguishable; consent/source is auditable; server derives entitlements separately from client profile.

## 2. ExamJourney

One active preparation journey plus historical journeys. Concepts: journey ID, user, exam, version, profession/track, goal, exam date, start/end/status, outcome/results, previous journey/resit link and source. Plans/memory/mastery/events are journey-scoped where appropriate.

## 3. LearnerEvent — append-only evidence

Capture timestamp/user/journey/surface; lesson/video/resource view and position; question/answer/change/confidence/time; attempts/submissions/scores; Writing/Speaking attempts; tutor feedback; plan action/completion/missed task; navigation/action result; source/provenance. Historical outcomes are append-only; corrections create new revision events rather than silent mutation.

## 4. LearningMemory

Structured compacted learner knowledge: mastery, errors, vocabulary, plan pointers, confidence, tutor notes, AI summaries, source event/document, created/updated-by and edit history/correction status. Consequential memory requires provenance. User/tutor correction is possible. Compaction cannot lose important source links.

## 5. MasteryState

Per journey/exam/profession/subtest/skill/subskill/rule/task type as appropriate: state/value, confidence/uncertainty, evidence window, last assessed, decay/review schedule, source events and algorithm/version. Never present mastery as guaranteed pass probability.

## 6. ErrorRecord / ErrorDNA

Store error category/subcategory/rule, severity/mark-impact evidence, occurrence count/trend, linked attempts/events, remediation history, next spaced-review state and mastered/reopened status. Error DNA is a derived profile, not an immutable label.

## 7. LearningFingerprint

Derived, explainable patterns: language/depth, examples-vs-rules, voice/text, forgetting interval, confidence calibration, timed performance, answer-changing/rushing/overthinking and intervention efficacy. Store evidence refs, algorithm/model version, confidence and updated time. Reset under learning-memory control.

## 8. VocabularyItem / ReviewSchedule

Term/phrase, user/journey, source, meaning, pronunciation data/reference, collocation/synonym/paraphrase, clinical-vs-lay usage, examples, review schedule/ease/interval and mistake/mastery history.

## 9. StudyPlan / PlanItem

StudyPlan: journey/user, plan type, creation/replan time, horizon, input snapshot/version, rationale, status and superseded-plan link.

PlanItem: due/preferred window, estimated minutes, skill/subtest, priority/impact, resource/action target, entitlement requirement, state, completion evidence, dropped/delayed reason and source weakness/error/mastery links.

Replanning preserves history and why recommendations changed.

## 10. Conversation / Message / CompactedSummary

Server persistence for cross-device continuity: conversation/user/journey, surface handoff state, messages metadata, source citations/actions, compacted summary, memory extraction decisions and reset/archive. Raw retention follows privacy policy. Use compacted memory rather than indefinite full replay.

## 11. Attempt / AssessmentResult

Prefer existing assessment entities. AI-facing evidence may include exam/module/part/task, questions/answers/correctness, timing/answer changes/confidence, score/criteria feedback, evaluator version, numeric-score calibration state, error taxonomy and source attempt. Exam mode records when hints were prohibited.

## 12. ContentSource

Fields/concepts: source ID/type, authority class, exam/version/effective dates, profession, subtest/skill/task, content entitlement/package, approval state, owner/approver, version, checksum, storage locator, page/slide/timestamp support, confidentiality/security tags, canary/watermark and retired/superseded state.

## 13. ContentChunk / SourceLocation

Chunk by pedagogical meaning with parent source and exact location. Store retrieval metadata, content hash and source release/index version so stale chunks can be invalidated.

## 14. RuleObject / KnowledgeOverride

Rule ID/version, source(s), profession/exam/subtest/task scope, statement, examples/exceptions/common mistakes, authority, approval/effective dates, approver, superseded version and change reason. AI extraction is `DRAFT`; only human approval makes it authoritative.

## 15. KnowledgeRelease

Release ID/version, included source versions, index/checksum, evaluation report, approver, published time, rollback target and status. Knowledge rollback should be independent from application deployment when possible.

## 16. Entitlement

User/account/future tenant, content package, profession scope, AI tier, optional feature/allowance grants, effective dates, purchase channel, grandfathering, status/revocation and source transaction/contract. One authoritative resolver serves RAG, actions, paywalls and billing.

## 17. Subscription / PriceBook / TierPolicy

PriceBook: tier/product/add-on/credit pack, billing period, region/currency/channel, gross price, effective dates, tax/fee treatment reference and approval state.

TierPolicy: message allowance/window, content-Q&A depth, memory horizon, plan behavior, file/image limits, voice eligibility, AI Credit grant, model priority, fair-use/rate caps and feature flags. Do not scatter tier behavior through UI conditionals.

## 18. CreditLedger

Mandatory concepts: ledger entry ID, user/account, delta, resulting balance/projection basis, provenance (subscription/course/top-up/promo/admin/migration), action charged, idempotency key, purchase/transaction ID, expiry/bucket, linked reservation/reversal/technical failure, actor/timestamp and audit metadata.

Invariants: immutable history; atomic charge/reversal; idempotent retries; failed technical attempts do not permanently consume credits; balance cannot go negative unless existing safe accounting explicitly supports it; all grants/expiry/revocation preserve provenance; user may see one total while backend maintains buckets.

## 19. AIUsageTrace

Trace/request ID, actor/tier/journey/surface/intent, provider/model/route, usage units, retrieval source IDs, cache hit/miss, latency components, estimated/actual variable cost, credit action, result/error status, safety/quality flags and sampled-review ref. Protect raw learner content according to minimization policy.

## 20. ProviderRoute / ModelPolicy

Provider/model capability, intent classes, tier priority, max cost/latency policy, fallback order, health state, effective config version and kill switch. Business code uses internal capability names, not vendor model IDs.

## 21. ActionDefinition / ActionExecution

ActionDefinition: key, schema, authorization, entitlement, confirmation, credit, audit and idempotency policies. ActionExecution: actor, resolved target, inputs, confirmation, result/failure, credit linkage and audit trace.

## 22. UploadAsset

User/journey, type/size/duration/pages, storage locator, processing state, sensitive/patient-ID risk, user warning/confirmation, extracted metadata, retention/deletion and cost/credit trace.

## 23. NotificationPreference / Delivery

Store channel consent, quiet hours, frequency, exam reminders, progress summaries and content alerts. Delivery records include reason, plan/weakness evidence and opt-out path.

## 24. TutorNote / Handoff

Tutor notes feed memory only with author/provenance and proper visibility. Handoff summarizes score trend, errors, adherence and recommended focus.

## 25. Quality Evaluation entities

GoldenSet, GoldenCase, EvaluationRun, EvaluationResult and ReleaseGate or existing equivalents. Store profession/subtest, source release, prompt/model config, expected authority/entitlement/action behavior, metrics and reviewer approval.

## 26. FeatureFlag / KillSwitch

Environment/scope, state, reason, owner, changed-by and audit timestamp; optional automatic threshold trigger. Critical switches should work without code deploy.

## 27. AuditLog / Incident

Audit critical admin/knowledge/entitlement/credit/config actions. Incident stores severity, timeline, affected surfaces, customer communications, mitigation, credit restoration, recovery criteria, re-enable approval and post-incident review.

## 28. Future Tenant / Institution / SeatBudget

Do not build full B2B prematurely, but avoid schema choices that make isolation impossible. Future concepts: tenant, institution branding/persona, source namespace, role grants, seat limits, active-user budget, retention policy, contract feature policy and residency requirement.

## Migration discipline

Every schema change documents the existing entity reused/extended, migration/rollback, backfill, idempotency, compatibility with older clients/jobs, privacy/retention impact, F-IDs served and tests proving invariants.

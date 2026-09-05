# QA, Evaluation, Security Testing and Release Gates

## 1. Quality philosophy

AI quality is not proven by a few manual chats. Every model, prompt, retrieval, chunking or source change that can alter answers requires reproducible evaluation/regression evidence.

## 2. Test pyramid

### Domain/unit
Tier/capability policy; entitlement; authority precedence; exam-version resolution; plan prioritization; memory compaction/extraction; credit ledger; pricebook/effective dates; safety/clinical/integrity classifiers; action authorization.

### Integration/contract
DB migrations/backfills; auth/profile/entitlement; RAG filters/index; provider adapters; payment webhooks; credit charge/reversal; async jobs; content publish/rollback; notification preferences.

### End-to-end
Core learner/admin/tutor journeys on production-like environment.

## 3. Golden question sets

Per profession/subtest include ordinary grounded Q&A, ambiguous question, insufficient evidence/unknown, official-vs-methodology conflict, generic-vs-profession-specific rule, old-vs-current version, locked-source attempts, multi-turn source reconstruction, navigation/deep links, current-video/timestamp, plan recommendation, Writing/Reading/Listening behavior, academic integrity, clinical boundary and support/billing/allowance.

Thresholds are TO VERIFY; critical entitlement/payment fabrication/leak has zero tolerance.

## 4. Retrieval evaluation

Measure source recall, precision, reranker gain, authority selection, profession, exam version, citation/source-location correctness, entitlement filtering, stale-source exclusion and cache isolation. Retain run artifacts per source/index release.

## 5. Hallucination evaluation

Track unsupported factual claims, invented locations, invented course rules and false official claims. Include negative cases where correct behavior is “insufficient verified information.”

## 6. Writing calibration

Compare criterion feedback/numeric estimates with trustworthy known-outcome submissions. Gate numeric score display until approved. Criterion-level feedback remains available when score calibration is not.

## 7. Speaking calibration

Compare role-play evaluation with human/known outcomes including linguistic and communication dimensions. Pronunciation scope matches validated voice capability.

## 8. Arabic evaluation

Separate cases for Egyptian Arabic, MSA, Arabic-English code switching, English clinical/exam terms inside Arabic, RTL/mixed-direction UI and voice ASR/TTS when enabled. Pass thresholds are defined by feasibility spike, not guessed.

## 9. Adversarial/security tests

Prompt injection from user/retrieved/uploaded content; system-prompt extraction; hidden policy/provider credential request; proprietary Rule Book reconstruction; canary leakage; wrong entitlement; deep-link tamper; client tier spoofing; cross-user cache; account-sharing/automation abuse; rate-limit bypass; jailbreaks; clinical decision request; real-exam answer request.

## 10. Credit/payment tests

Exact charge before confirmation; insufficient balance; concurrent requests; retry/idempotency; provider timeout; partial job failure; technical failure no-charge/restoration; reversal; expiry/grant provenance; top-up duplicate/out-of-order webhook; downgrade/cancel/grandfathering; chargeback/refund state; ledger freeze kill switch.

## 11. Performance/latency

Measure P50/P95 separately for page Q&A, navigation/action, plan generation, Writing/deep assessment, file analysis and live voice turns. Voice target remains TO VERIFY. Test realistic mobile network/reconnect where possible.

## 12. Load/abuse

Per-tier rate limits, bursts, file/page/audio caps, bot patterns, retrieval-volume/exfiltration limits, cost alerts, queue backpressure and graceful provider outage/degradation.

## 13. Accessibility

Automated + manual WCAG 2.2 AA checks for keyboard/focus, semantics, contrast, zoom/reflow, captions/transcripts, voice alternatives, validation/errors, paywall/checkout, RTL/mixed direction and mobile touch targets.

## 14. E2E core journeys

1. anonymous demo → registration → preserved context;
2. onboarding → personalized recommendation;
3. entitled course question → grounded answer + source;
4. locked-content question → safe preview/paywall, no leak;
5. navigation question → direct allowed resource open;
6. basic plan → next action → start practice;
7. exam-date change → replan + explanation;
8. Writing assessment → credit confirmation → success charge;
9. Writing technical failure → no charge/restoration;
10. Free cap → contextual upgrade → checkout → entitlement refresh → same chat resumes;
11. memory view/edit/reset;
12. report wrong answer/support handoff;
13. admin correction → approval → release → new version used;
14. knowledge rollback;
15. provider outage/fallback;
16. voice/credit kill switch;
17. Arabic/RTL core conversation;
18. exam mode blocks forbidden help.

Stage 2/3 add subtest, video, speaking/voice, multimodal, tutor and proactive flows.

## 15. Observability acceptance

Every AI request can be attributed to user/tier/intent/surface, model/provider, retrieval sources, latency, usage/cost, cache, credit result, final status and relevant safety/quality flag. Admin aggregate views must not expose unrelated learner data.

## 16. Provider swap/failover

Staging proves internal provider interface can swap/fallback without tutor/business rewrite. Validate response schema, streaming, tool/action behavior, errors and usage mapping.

## 17. Knowledge release gate

No publish until sources are approved, entitlement/security labels exist, index build completes, golden/retrieval regression passes, no critical leak remains, changelog is attached and rollback target exists.

## 18. Voice Stage 3 gate

Two-week spike reports Arabic ASR/code-switching, latency, pronunciation reliability, cost/minute, role-play completion, reconnect/silence/session cap, failed-session non-billing and approved voice-to-credit/contribution. If failed, retain fallback and do not enable full live voice/Ultimate promise.

## 19. Live beta gate

Instrument one approved profession/corpus for the source-specified two-week beta once ready. Cohort size is TO VERIFY. Collect cost, retrieval, cache, latency, credit, voice if relevant, conversion, support and failed-charge data. Exit only when cost/credit/tier/top-up and core quality decisions can be approved.

## 20. Incident/rollback drill

Before paid launch prove severity/owner path, in-product/status communication, balance preservation, failed-credit restoration, provider/prompt/config/source/payment/voice rollback, incident timeline and postmortem template. Numeric SLO/ack targets are TO VERIFY.

## 21. Production completion evidence

A stage is not done because code merged. Store release evidence containing F-IDs, migration state, test/eval report, security/accessibility findings, latency/cost observations, feature flags, approved/deferred TO VERIFY gates, rollback instructions and known issues with severity/owner.

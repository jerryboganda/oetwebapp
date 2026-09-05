# Architecture and Existing-Project Integration Contract

## Purpose

Define a target logical architecture without forcing a technology stack. Claude Code must map these boundaries to the main repository's existing languages/frameworks/services before implementation.

## 1. Integration-first topology

```text
Supported Clients / Surfaces
  public site | web app | reading/listening | writing | speaking | video | results | dashboard | mobile
        |
        v
Existing API/BFF/Auth Session Boundary
        |
        +--> Context Envelope Builder
        |
        v
AI Companion Orchestrator
  |-- Intent / Capability Policy
  |-- Learner Intelligence Context
  |-- Entitlement Policy (mandatory before retrieval/action)
  |-- Model/Provider Router
  |-- Memory Service
  |-- Learning / Next-Best-Action Engine
  |-- Tutor Engines
  |-- Action Registry
  |-- Metering / AI Credits
  |-- Safety / Output Policy
  |
  +--> Knowledge Retrieval / Authority Service
  |      |-- approved source/version selector
  |      |-- entitlement prefilter
  |      |-- lexical + vector retrieval
  |      |-- reranker
  |      |-- conflict/citation assembly
  |
  +--> Existing Domain APIs
  |      users/profiles | courses | exams | attempts | content | payments | support | notifications
  |
  +--> Telemetry / Audit / Cost / Quality

Content Ops --> extraction/tagging --> approval --> knowledge release --> retrieval index
Admin/Tutor --> overrides/review --> versioned approval/audit
```

The diagram describes responsibilities, not required microservices. In a monolith these can be modules; in an existing service architecture they can be separate services. Preserve repository conventions.

## 2. Existing systems are authoritative

Before adding code, identify the canonical existing implementation for authentication/session/device security; user identity/profile; profession/exam/course data; content ownership/entitlements; course progress/attempts; Writing/Speaking/Reading/Listening assessment; subscription/payment/checkout/refund; existing AI credits; support/tutor/admin roles; media storage; notifications; analytics/audit; AI/RAG/providers and app/web routing. Extend canonical modules. Do not create parallel truth stores.

## 3. Request context contract

Every AI request should resolve a server-trusted context containing, as available: authenticated/anonymous actor and role; active learner journey; profession; active exam/effective version; AI tier; content entitlements; message/feature allowance; credit projection; surface ID; bounded screen context; locale/language/RTL; tutor mode; request/trace ID.

Client-provided entitlement/tier/user fields are hints only; server reloads authoritative values.

## 4. Capability policy layer

Centralize whether an intent is allowed based on tier, feature flag, external gate and context. Example decisions:

- `ALLOW_FREE`;
- `ALLOW_BASE_ALLOWANCE`;
- `REQUIRES_AI_CREDITS(n)`;
- `REQUIRES_UPGRADE(tier)`;
- `REQUIRES_CONTENT_ENTITLEMENT(scope)`;
- `DISABLED_PENDING_CALIBRATION`;
- `DISABLED_PENDING_VOICE_GATE`;
- `DISABLED_FOR_EXAM_INTEGRITY`;
- `RATE_LIMITED`;
- `BLOCKED_SAFETY`.

Frontend renders the decision; backend enforces it.

## 5. Entitlement service boundary

Use one authoritative entitlement resolver for RAG source filtering, direct content/deep-link actions, tutor/package access, tier capability policy, paywall messaging, billing/grandfathering and authorized admin preview. Apply entitlement before retrieval and revalidate before returning/actioning protected resources.

## 6. AI orchestration pipeline

1. authenticate/resolve actor;
2. load active journey/profile/tier/entitlements;
3. parse bounded screen context;
4. safety/integrity precheck;
5. classify intent and cost class;
6. capability decision;
7. deterministic action or cheap route if possible;
8. retrieve approved content when needed;
9. load bounded structured memory;
10. route model/provider based on intent/tier/latency/cost;
11. generate structured response/action proposal;
12. source/authority consistency check;
13. exfiltration/safety/output policy;
14. execute user-confirmed charge/action;
15. persist events/memory with provenance;
16. trace model/cost/latency/sources/cache/result;
17. surface feedback/next action.

## 7. Action Registry

Actions are typed platform operations rather than arbitrary URLs emitted by a model.

```text
ActionDefinition
- action_key
- capability/tier requirement
- input schema
- authorization policy
- target resolver
- confirmation rule
- credit rule
- audit rule
- idempotency rule
```

Examples: open resource/timestamp, start practice, continue last activity, save note/vocabulary, add/complete plan item, reminder, support request, tutor handoff, checkout, report content and show balance. Model proposes a typed action; server resolves/validates the target. Raw model-generated URLs never bypass access checks.

## 8. Learning engine boundaries

Separate deterministic evidence from model interpretation:

- **event facts:** attempts, scores, times, answer changes, content completion, tutor notes;
- **derived metrics:** trends, mastery, error counts, confidence calibration;
- **AI synthesis:** explanations, plan rationale, targeted exercise wording.

A plan item has source evidence/rationale and an eligible resource/action. Replanning must be reproducible from stored inputs and must not simply carry all overdue work.

## 9. Exam-pack architecture

Global services depend on a generic ExamPack/equivalent contract rather than OET constants. Pack concepts include exam/version/effective dates, modules/subtests, task types, profession/track, scoring/evaluation, source authorities, content pack/entitlements and tutor/evaluation plugins/declarative rules. OET is first; Stage 4 validates portability with another pack.

## 10. Provider abstraction

Create internal interfaces for text/reasoning, embedding, reranking, STT, TTS/live voice and document/media extraction where external. Business logic cannot depend directly on one vendor's SDK types. Adapter telemetry normalizes cost/usage/latency. Provider-swap test is a release requirement.

## 11. Cache boundaries

Safe candidates: stable platform map; approved source snippets; public official facts by version; deterministic capability calculations; provider prompt/prefix cache. Cache keys include all security-relevant dimensions: future tenant, content visibility/entitlement class, exam/version, profession where relevant, locale and source release. Never reuse personalized outputs across users unless proven public/non-personal/entitlement-neutral.

Invalidate on source release, entitlement change, exam-version change or policy change as relevant.

## 12. Async jobs and queues

Use existing queue infrastructure for ingestion/transcription/extraction, embeddings/index builds, large file/audio analysis, weekly reports, notifications, evaluations, cost aggregation, release/reindex and export/delete workflows. Jobs are idempotent, retriable by failure class and traceable. Heavy-job credit accounting must coordinate reservation/settlement with final outcome so terminal technical failure restores credits.

## 13. Event-driven observability

Define canonical events following existing analytics conventions, such as:

- `companion.request.started/completed/failed`;
- `retrieval.completed`;
- `action.proposed/executed/failed`;
- `plan.generated/replanned/item_completed`;
- `assessment.started/completed/failed`;
- `credit.reserved/charged/restored/reversed/granted/expired`;
- `paywall.shown/checkout_started/checkout_completed`;
- `content.flagged/source_approved/release_published/release_rolled_back`;
- `quality.feedback`;
- `safety.blocked/security.canary_hit`;
- `incident.opened/closed`.

Avoid storing excessive raw prompt/personal content in logs.

## 14. Feature flags and kill switches

At minimum independently control: global companion, profession/corpus rollout, provider/model route, source release, Writing score, Speaking score, live voice, voice provider, Ultimate public activation, credit-consuming actions, large file/audio workload, proactive notifications, new exam pack and B2B tenant features.

Flags need owner, reason, environment/scope, audit trail and explicit re-enable after emergency incident.

## 15. Backward compatibility

- preserve current users/credits/entitlements;
- additive migrations first;
- repeatable/idempotent backfills;
- no breaking API change without consumer migration;
- tolerate older mobile clients through capability negotiation;
- payment/credit flows tolerate duplicate/out-of-order webhooks;
- knowledge rollback should not require app rollback.

## 16. Deployment/rollback

Use existing CI/CD. Production operations should independently roll back/disable application version, prompt/config, model/provider route, knowledge release, expensive action, live voice and credit charging during ledger incident. Do not bundle every recovery control into one irreversible switch.

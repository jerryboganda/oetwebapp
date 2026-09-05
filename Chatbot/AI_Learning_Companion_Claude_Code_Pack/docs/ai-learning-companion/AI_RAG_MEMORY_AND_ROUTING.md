# AI, RAG, Memory and Model-Routing Engineering Plan

## 1. Goals

Answer from the correct approved knowledge for the correct learner, prevent locked-material leakage, control cost/latency and maintain durable learning memory without sending unbounded chat history.

## 2. Retrieval source classes

Model distinct authority classes rather than one flat corpus:

- `OFFICIAL_CURRENT_FACT` — current official exam/regulator factual information;
- `DR_HESHAM_APPROVED_METHOD` — approved Rule Books and teaching method;
- `PROFESSION_APPROVED_METHOD` — profession-specific approved method;
- `COURSE_MATERIAL` — package/entitlement-gated assets;
- `PLATFORM_SUPPORT` — product navigation/support facts;
- `CANDIDATE_EVIDENCE` — user-specific scores/attempts/memory/notes;
- `ADMIN_OVERRIDE` — versioned/approved authoritative correction.

## 3. Ingestion/chunking contract

Chunk by lesson/rule/concept/semantic section, not arbitrary fixed size only. Preserve source hierarchy and exact page/slide/video timestamp. A chunk carries exam/version/profession/subtest/skill/task/entitlement/authority/source/version/approval/security tags. Use overlap only where pedagogically needed and avoid fragmentation that makes protected materials easy to reconstruct.

## 4. Retrieval pipeline

### Step 1 — trusted request resolution
Load authoritative user, active journey, exam version, profession, tier and content entitlements server-side.

### Step 2 — intent/cost classification
Distinguish navigation/support, public facts, course tutoring, planning, assessment, file analysis and voice. Deterministic answers/actions bypass expensive RAG where safe.

### Step 3 — candidate source universe
Filter by approval/current status, exam/effective version, profession/task scope, content entitlement, relevant authority and future tenant namespace. This filter occurs **before** vector/lexical retrieval.

### Step 4 — hybrid search
Use lexical/BM25-equivalent + vector similarity and metadata filtering. Rerank only where measured quality justifies latency/cost.

### Step 5 — authority and conflict resolution
Detect disagreement. Official current fact wins official-fact questions; approved methodology wins teaching strategy; profession-specific approved rule wins generic; newest approved version wins obsolete. Surface meaningful conflict rather than silently merge.

### Step 6 — evidence packing
Pack the minimum relevant source snippets with source IDs/locations. Respect verbatim span/volume caps and never expose protected source detail beyond entitlement.

### Step 7 — generation and citation
Prompt with explicit authority labels. Require official facts vs teaching strategy distinction. If evidence is insufficient, state uncertainty/unknown. Add source labels/page/timestamp where useful.

### Step 8 — post-generation policy
Check proprietary extraction pattern, unsupported official claims, internal prompt/secret leakage, prohibited clinical decision behavior and action authorization.

### Step 9 — trace
Record source IDs, retrieval/rerank scores, cache, model/provider, latency, cost and result.

## 5. Proprietary content defence

Layered controls:

- server entitlement prefilter;
- source sensitivity flag;
- maximum verbatim span per response/source;
- rolling retrieval/output-volume limits per user/source;
- repeated “continue/next paragraph/reconstruct chapter” detection;
- quote-vs-paraphrase policy;
- canary/watermark hit detection;
- rate/automation anomaly signals;
- refusal that can still teach the concept without reproducing the protected source;
- multi-turn reconstruction red-team tests.

## 6. Prompt-injection boundary

Retrieved documents, official pages, uploads and user text are data, not system policy. Separate instructions from quoted evidence. Never put secrets, hidden routes or provider credentials into prompt context. Tool/action calls use allowlisted schemas and server validation.

## 7. Official facts freshness/versioning

Official sources require effective dates and review cadence. Resolve learner exam date to applicable exam version. Old versions remain auditable but not current. Never answer current regulator questions from stale cache without version validation.

## 8. Video/timestamp intelligence

Store transcript segments with video ID, exact timestamps/chapter, authority and entitlement. Current-video context can bias retrieval to active source/time. “Summarize last 10 minutes” explicitly bounds the window; “show where explained” can search the full eligible index.

## 9. Memory architecture

### Conversation memory
Recent-turn window + compacted summary for continuity. User can clear/reset chat.

### Learning memory
Structured durable facts derived from events: errors, mastery, scores, plans, vocabulary and tutor notes. Require provenance/correction controls.

### Journey memory
Longitudinal trend/intervention history scoped to user/journey. Tier may affect retention depth, while export/delete follows privacy policy.

### Memory write policy
Not every chat statement becomes durable. Classify ephemeral context, explicit preference, evidence-backed performance signal, tutor-confirmed note and AI-derived hypothesis with confidence. Store source/confidence and allow correction.

### Compaction
When history exceeds budget, preserve goals/current plan, unresolved questions, scores/attempts, repeated errors, saved vocab/rules, tutor notes, prior decisions and source refs. Regression-test that compaction does not alter important facts.

## 10. Next-best-action reasoning

Favor deterministic/scoreable signals first: exam proximity, target gap, weakness severity/recency, current availability, overdue/high-impact item, mastery/forgetting due, content entitlement, recent practice diversity and confidence/timing problems. The model may explain/rank but cannot recommend locked resources. Store rationale.

## 11. Model routing

### Cheap/deterministic
Database lookup or low-cost path for navigation, route resolution, simple classification, account/allowance and stable FAQ.

### Standard tutor
Grounded explanations, course Q&A, short plan adjustments and drills.

### Strong reasoning
Difficult planning, cross-attempt analysis, deep Writing/Speaking assessment and complex multi-source tutoring.

### Voice
Low-latency STT/LLM/TTS/live voice only after feasibility/economics gate, with time/cost metering.

Tier can influence priority but never weaken safety/authority.

## 12. Vendor abstraction

Internal interfaces normalize request/streaming/tool use/usage/errors. Provider outage/deprecation/price change is handled by config/fallback, not business-code rewrite. Staging proves route/provider substitution.

## 13. Caching

Safe candidates: public route map; approved public facts by version; embeddings/index artifacts; approved source snippets under entitlement-safe keys; prompt/prefix cache; deterministic plan subcalculations with complete user keying.

Never cache across tenant, entitlement or user boundaries. Invalidate on source release, entitlement change, exam version or policy change.

## 14. Cost controls

Per request collect provider units, estimated fully loaded cost class, retrieval/rerank/cache cost and voice/media time. Enforce tier rate/size/duration caps, context/output limits, file/audio caps, model route ceiling, voice session cap, credit gate and emergency provider/action switches.

## 15. Evaluation

RAG release evidence includes labelled retrieval set, correct authority/profession/version, no critical locked-source leakage, correct citation/location, unknown-answer behavior, conflict handling, cache isolation, injection/exfiltration red team, memory-compaction regression and provider failover. Numeric targets remain TO VERIFY until approved.

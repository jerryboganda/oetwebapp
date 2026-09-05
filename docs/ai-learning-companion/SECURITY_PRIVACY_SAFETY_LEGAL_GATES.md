# Security, Privacy, Safety, Accessibility and Legal Gates

## 1. Security architecture principles

Server-side authorization/entitlement; least privilege; secrets outside prompts/knowledge/client/logs; signed/validated action targets; encryption/access logging following current platform standards; audit critical admin/content/payment/entitlement/config actions; future tenant isolation; no cross-user/entitlement cache leakage.

## 2. Entitlement-safe RAG

Protected source entitlement is resolved **before** retrieval. Recheck before protected deep link/action. Test wrong profession, expired package, downgrade, revocation, anonymous actor and future cross-tenant access.

## 3. Content exfiltration defence

Implement per-source verbatim quote limits, rolling user/source output/retrieval limits, multi-turn reconstruction detection, repeated chapter extraction detection, paraphrase/refusal teaching fallback, canary/watermark monitoring, anomaly/rate signals, security event/alert and red-team cases. Even entitled users should not use the assistant as a bulk export channel.

## 4. Prompt-injection defence

Treat retrieved/source/upload/user content as data, not policy. Test “ignore previous instructions” inside files, system-prompt extraction, hidden routing/provider credential requests, tool/action injection, malicious role cards, source text trying to alter entitlement and jailbreaks requesting paid content reconstruction. Tool/action layer is allowlisted and schema-validated.

## 5. Canary/leak response

Approved canaries in protected material are monitored. Any unauthorized canary output creates a security event with request/source/user/route context, mitigation and investigation.

## 6. Anti-hallucination and authority safety

If verified information is insufficient, say so. Never invent official exam requirements, score requirements, course rules, source/page locations, entitlements or payment/credit outcomes. Official facts and teaching methodology stay distinguishable.

## 7. Academic integrity

Protected exam/practice mode disables prohibited hints/answers/transcripts/coaching until submission. The product must not serve as a live answer engine during an official exam. Enforce mode server-side where feasible.

## 8. Sensitive uploads / patient identifiers

Before long-term storage/sharing, detect likely sensitive/patient-identifying information and warn/redact according to policy. Store only what is needed. Clinical boundary applies to medical content in role plays/uploads.

## 9. Clinical boundary

Allowed: exam language, communication technique, role-play phrasing, vocabulary and communication structure.

Not allowed as product behavior: patient-specific diagnosis, treatment recommendation, prescribing, clinical decision support or presenting role-play output as real clinical guidance. Genuine clinical questions are redirected to professional/official sources while the assistant may help with language/communication. Add tests/red-team cases.

## 10. Learner distress boundary

Supportive/non-judgmental; not counselling/psychotherapy; never equate exam failure with personal worth; never state/imply pass probability; use human escalation and urgent-safety runbook. Named operational owner is TO VERIFY and launch-critical.

## 11. Privacy controls

Learner-facing: view/edit/delete/reset learning memory; clear chat; platform export/delete; notification/voice consent/preferences; confirmation before imported scores/profile changes.

Backend: minimization, lawful-basis/purpose map, retention, deletion including derived/indexed data where required, access/audit and subprocessor register.

## 12. UK GDPR/DPA/DPIA gate

Privacy/legal owner maps data categories/purposes/lawful bases; screens for DPIA and completes one if required; establishes data-subject rights and human-review routes. Engineering supplies data-flow inventory and controls; legal determination stays external.

## 13. Voice consent and retention

Raw audio storage is an explicit configurable decision. Define separate notice/consent where required, transient vs stored raw audio, transcript retention, access, deletion/export, vendor retention/training, capture indicator and stop control. Do not enable production voice before approval.

## 14. Vendor terms / proprietary data

Maintain provider inventory for models, voice, hosting, analytics, support and payments: DPA/contract, region, retention, training use, subprocessors, deletion, incident contact. Proprietary teaching content cannot be used for vendor training without written approval.

## 15. Score-claim calibration gate

Default: criterion feedback + uncertainty. Numeric Writing/Speaking score/band display can enable only after approved calibration. UI never calls it official or guaranteed.

## 16. Accessibility

Target WCAG 2.2 AA: keyboard operation, visible focus, semantic controls, contrast, scalable text/reflow, captions/transcripts, STT/TTS alternatives, accessible errors/paywalls/confirmations, screen-reader labels and mixed RTL/LTR correctness. Accessibility defects in core flow are release defects, not cosmetic backlog.

## 17. Google Play / Apple readiness

Provide in-app report/flag flow for AI content where required and moderation/response tracking. Review AI, payments, external links/steering, privacy/account flows per distributed storefront. Exact terms remain TO VERIFY at release time.

## 18. Trademark/IP gate

Before public launch: Jana/Sami persona clearance; master brand clearance if separate; OET descriptive/nominative use/disclaimer; rights/licensing for exam-like/official materials; ownership/permission of internal sources. Persona names stay configurable.

## 19. Subscription-contract readiness

Stage 1 billing UX/data model can support clear pre-contract terms, renewal reminder, simple cancellation, cooling-off/refund and post-trial/renewal rights where applicable. Exact DMCCA commencement/entity scope is TO VERIFY with counsel. This pack is not legal advice.

## 20. Abuse/account sharing

Use per-user/tier rate limits, automation patterns, existing device/account signals, file/audio caps and anomaly alerts. Avoid invasive fingerprinting beyond approved privacy policy. Enforcement is configurable/auditable.

## 21. Security review gate

Before production: dependency/secrets review; authorization matrix; RAG entitlement leak suite; prompt-injection/exfiltration red team; action signing/tamper; cache isolation; credit/payment concurrency; upload safety; rate/load/abuse; provider outage/fallback; kill-switch drill; audit verification; incident/rollback tabletop.

## 22. Future tenant isolation

Future tenant context scopes users, sources, indexes/cache, analytics, admin actions, exports and retention. Never rely only on frontend tenant filtering. Stage 5 requires explicit cross-tenant negative tests.

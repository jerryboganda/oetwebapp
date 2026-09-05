# Full Requirements to Implement — Source Specification Converted for Engineering

This document is the engineering interpretation of the full 67-page **AI Learning Companion Master Specification v3.0**. It preserves the source product rules, commercial controls, technical requirements, operating gates and 184-feature inventory. The feature-by-feature checklist lives in `../../traceability/FEATURE_TRACEABILITY_MATRIX.md`; this document captures the behavior and cross-cutting requirements that a list of feature titles alone would miss.

## 1. Product definition and ambition

Build a **persistent AI Learning Companion**, not a standalone chatbot page. The same learner identity, permissions, memory and learning journey must follow the user across the website, web app and supported native/app surfaces.

Launch focus is OET with profession-specific intelligence and the approved OET learning ecosystem. The product core must later support IELTS, PTE, TOEFL, future curricula and B2B/white-label deployments without a fundamental rewrite.

The platform has five non-negotiable pillars:

- **Personalise:** adaptive plan, learning memory, error profile and next-best action for each learner.
- **Teach:** grounding in approved Rule Books, sessions, workshops, corrections and eligible materials.
- **Act:** open resources, start practice, update plans, save notes and navigate the product.
- **Monetise:** Free demonstrates value; paid tiers unlock deeper learning power; high-cost AI actions are metered/capped/top-up capable.
- **Scale:** one core supports multiple exams, professions, languages, institutions and brands.

### Core product layers

- **Candidate Intelligence:** profile, exam date, target, profession, scores, habits, preferences, history, confidence, availability.
- **Knowledge Brain:** official exam knowledge, Dr Hesham methodology, eligible course materials, platform map and support knowledge.
- **Learning Engine:** plan, next-best action, mastery, Error DNA, adaptive drills, spaced reinforcement, readiness.
- **Tutor Engine:** Writing, Speaking, Reading, Listening, grammar, vocabulary, voice, multimodal and mock support.
- **Action Layer:** deep links, start/open actions, notes, plan updates, reminders and handoffs.
- **Commercial Layer:** entitlements, tiers, allowances, AI Credits, top-ups, upgrade cards and cost/revenue controls.
- **Trust Layer:** authority/versioning, anti-hallucination, privacy, access control, auditability and future tenant isolation.

## 2. Product principles and non-negotiables

- **AI everywhere, not one AI page:** provide the companion through floating chat, full tutor, context side panel, video helper, Writing copilot, Speaking room, results coach, dashboard coach and support assistant as the existing product supports them.
- **Personalization before generic answers:** use the candidate profile and trustworthy learning history when relevant.
- **Profession-first OET logic:** profession is a core dimension of content, examples, role plays, Writing/Speaking guidance, rules and analytics.
- **Source-grounded teaching:** approved Dr Hesham Rule Books/methodology override generic internet-style teaching advice.
- **Official facts remain official:** booking, scoring, format, regulator and other current facts use verified current sources, distinct from teaching method.
- **Entitlement before retrieval:** never retrieve or expose proprietary paid content for a learner who lacks access.
- **Action over explanation:** when the product can perform a useful action, offer/execute it rather than only describe menu paths.
- **Free proves value; paid creates habit:** tier differences are outcome-based, not merely quotas.
- **Cost-aware by design:** cheap/deterministic routes for navigation/classification; stronger/expensive routes only where needed.
- **One identity everywhere:** conversation, plan and memory continue across supported clients.

## 3. Brand/persona requirements

Working user-facing persona options are **Jana** (“Talk to Jana”) and **Sami** (“Talk to Sami”). Treat the persona as separate from the legal/master brand so B2B licensing, trademark ownership and valuation can use another master mark if required.

Do not position the product publicly as merely an “AI chatbot”. Preferred concepts: AI Learning Companion, AI Tutor, AI Mentor, Personal Learning Intelligence or AI Learning OS. Ultimate positioning is “Your Personal AI Mentor” only when the premium continuous-management behavior is actually delivered.

Jana/Sami remains a working choice until trademark/domain/app-store/social clearance is complete.

## 4. Users, roles and surfaces

### Roles

- **Anonymous visitor:** limited demo and lead capture; no persistent academic profile before registration.
- **Registered Free learner:** basic profile and capped Free AI.
- **Paid learner:** capabilities determined by AI tier + content entitlements.
- **Tutor:** learner summary, handoff, notes and authorized teaching overrides.
- **Admin:** content, pricing, entitlements, AI quality, support, analytics and revenue controls.
- **Super Admin/B2B owner:** institution/tenant, branding, policy, knowledge packs, seats, budgets and audit logs.

### Required product surfaces

- public website floating assistant and landing demo;
- web app floating assistant plus full-screen tutor;
- Reading/Listening contextual side panel;
- Writing editor copilot;
- Speaking voice/role-play surface;
- video helper with current-video/timestamp awareness;
- results/analytics coach;
- learner dashboard next-action card;
- Android/iOS;
- Windows/macOS or equivalent desktop experience;
- future optional email/push summaries, WhatsApp/Telegram and LMS/enterprise integrations using the same identity/entitlements.

## 5. Candidate intelligence profile and onboarding

Onboarding must progressively enrich the profile and show value quickly rather than forcing a long form before useful interaction.

Store/model as appropriate:

- identity/access: account, AI tier, packages, profession, active exam, language;
- goal: target score/grade, regulator/university/country goal, exam date, attempt/resit state;
- baseline: prior official results, mock averages, self-rated confidence, diagnostics;
- availability: minutes/day, shifts, days off, preferred times, travel/leave;
- skill profile: subtest skills, grammar, vocabulary, timing, pronunciation, confidence;
- learning preference: Arabic/English/mixed, concise/detailed, examples/rules, voice/text, practice style;
- history: lessons, videos, mocks, Writing/Speaking attempts, tutor feedback, missed tasks;
- behavior: answer changes, confidence calibration, speed, consistency, streaks/follow-through;
- readiness: mastery, trend, blockers, latest recommended action and risk flags.

Requirements:

- learner can edit exam date, profession, target and availability;
- learner can view/edit/delete/reset AI learning memory;
- support multiple exam journeys over time, one active journey and preserved history;
- screenshot/report score extraction requires user confirmation before saving;
- exam-date changes trigger replanning and an explanation of impact.

## 6. OET Knowledge Brain

Every approved educational asset useful to an entitled learner must be ingestible, searchable, source-tagged, versioned and associated with exam, profession, subtest/skill and entitlement/package.

Coverage includes:

- all approved Writing/Speaking Rule Books by profession and future Reading/Listening/grammar/vocabulary rules, corrections and exceptions;
- teaching sessions, course videos, workshops, live recordings, correction sessions, transcripts/timestamps;
- PDFs, notes, slides, handouts, exercises, question banks, model answers/explanations, Basic English and profession content;
- Tutor Book, reading dictionaries, common-word lists, vocabulary sets and approved explanations;
- eligible recalls/practice/mocks and their explanations/metadata;
- platform map: every page/menu/course/tab/resource/video/exam/subscription/credit state/deep link;
- support knowledge: registration, OTP, devices, access, expiry, package rules, credits, troubleshooting, billing/support FAQ and escalation;
- current official exam format/scoring/booking/ID/policy/delivery mode versioned by effective date;
- candidate-specific scores/errors/attempts/notes/tutor feedback/plan/vocabulary/memory/confidence/behavior;
- admin-approved teacher overrides, content changes, corrections and policies.

### Authority precedence

1. Verified current official facts/policies for official exam requirements.
2. Approved Dr Hesham Rule Books/methodology for teaching strategy.
3. Profession-specific approved rules over generic OET teaching rules.
4. Newer approved versions over obsolete versions; old versions remain auditable.
5. Candidate/tutor-specific instructions can tailor planning but cannot rewrite official facts.
6. If sources conflict, state the conflict and use the highest-authority/current source rather than inventing a compromise.

## 7. Content intelligence, search and video awareness

- universal semantic search across approved Rule Books, transcripts, videos, PDFs, notes, workshops, corrections, vocabulary and FAQs;
- “Where did Dr Hesham explain this?” locates a source and exact timestamp/page when available;
- in-video helper knows current video and playback timestamp;
- support “summarise the last 10 minutes”, “quiz me”, “save this rule”, “show example from this lesson”;
- generate session notes: rules, warnings, examples, vocabulary, mini-quiz and candidate-specific takeaways;
- detect frequent questions with weak/no dedicated content;
- detect repeated learner misses after relevant content completion;
- proposed content pipeline can derive transcript/chapter/rules/FAQ/examples/tags/quizzes but activation requires approval;
- educational answers may show source labels/citations without exposing locked proprietary content.

## 8. Adaptive study plan and next-best-action engine

Primary product promise: when a learner says “I have 25 minutes now — what should I do?”, choose the single highest-value eligible action for that candidate and provide a one-click path to start it.

Plan types include initial diagnostic; daily/weekly; 90/60/30 day; 14-day intensive; 7-day emergency; 3-day final revision; exam-eve; post-result/resit; single-subtest recovery; working-doctor/shift-worker; weekend-heavy; 20-minute/day; 2-hour/day; missed-day recovery; travel/holiday adjusted.

Inputs include exam date, target, profession, scores/trend, completion, recurring errors, study time, confidence and content entitlements.

Behavior:

- auto-replan after new results, missed work, schedule/exam-date changes or mastery improvement;
- do not endlessly carry overdue tasks; reprioritize and explain dropped/delayed items;
- prioritize high-impact weakness over mechanically completing content in catalogue order;
- output exact next eligible resource/exercise and deep link;
- near exam, shift from acquisition to rehearsal, error review, confidence and logistics;
- Ultimate eventually supports continuous next-best-action and proactive replanning.

## 9. Memory, Error DNA and Learning Fingerprint

### Memory layers

- **Conversation:** current task/recent questions/temporary context; learner can clear/reset chat.
- **Learning:** scores, errors, mastery, plan history, vocabulary, tutor feedback and completed work; learner can view/edit/delete/reset.
- **Journey:** longitudinal score/intervention/blocker patterns and what worked; higher-tier long-term retention plus export/delete.

### Error DNA

Track recurring grammar, Writing, Speaking, Reading, Listening, timing, vocabulary and reasoning errors. Quantify which categories are costing the most marks over recent attempts. Generate “train me on my mistakes” sessions and spaced re-tests until mastery. Distinguish knowledge gaps from speed, confidence, attention and strategy problems.

### Learning Fingerprint

Derive explainable traits such as language/depth preference, example-vs-rule response, voice/text preference, forgetting interval, confidence/accuracy calibration, timed-vs-untimed performance, answer-changing/rushing/overthinking/distractor patterns and historically effective interventions.

## 10. Teaching modes and core conversation behavior

Support Quick Answer, Detailed Tutor, Socratic Tutor, strict Examiner, Coach, Dr Hesham Method, Arabic Explanation preserving English medical/exam terminology, English-only immersion, Practice Generator and Revision.

Quick actions after explanation can include: explain simpler, explain in Arabic, another example, quiz me, save this, add to plan, open lesson and ask tutor. Support natural Arabic-English code-switching and remember preferred style while allowing per-message override.

## 11. Writing AI

- profession-specific task/recipient interpretation;
- purpose analysis and purpose-paragraph coaching;
- relevant-vs-irrelevant case-note selection with rationale;
- organization/chronology/paragraphing/information hierarchy;
- conciseness;
- grammar: articles, prepositions, tense, agreement, punctuation, sentence structure;
- clinical-to-lay language and professional register;
- vocabulary/collocations/clarity/naturalness;
- criteria-based OET-style feedback and only calibrated score/band estimates;
- sentence correction presentation: candidate version → improved version → why;
- guided paragraph-by-paragraph mode;
- hint mode without rewriting;
- rewrite selected text with rule explanation;
- compare two versions;
- strict examiner vs teaching mode;
- recurring error/mark-loss tracking;
- personalized micro-lessons from repeated errors;
- link feedback to Rule Book/session/timestamp when available;
- human escalation for uncertainty/complex judgement/entitlement;
- post-correction next 1–3 drills.

## 12. Speaking AI

- low-friction voice start/stop and transcript;
- profession-specific role cards and patient/relative/carer/colleague scenarios;
- personalities: anxious, angry, reluctant, confused, talkative, quiet, worried parent, demanding relative, mixed complexity;
- modes: friendly, standard, difficult, strict exam, surprise role card, full mock, fluency, pronunciation, empathy, information gathering, treatment explanation;
- assess role-card completion, information gathering/explanation, empathy, structure, fluency, grammar, vocabulary and communication effectiveness;
- pronunciation feedback only to validated scope: intelligibility, stress, pace, fillers, pauses and repeated mispronunciations;
- post-session transcript, missed tasks, better alternatives and next practice;
- “pause role play and coach me” in practice only, disabled in exam mode;
- generate new role plays from profession and recurring weaknesses;
- add repeated communication issues to Error DNA;
- meter voice separately from cheap text.

## 13. Reading and Listening coaches

### Reading

Analyze Parts A/B/C separately and by question type/subskill; track time, evidence selection, distractor susceptibility and answer changes; optionally capture confidence and compare confidence vs accuracy; explain why selected and alternative answers are wrong; generate targeted inference/scanning/paraphrase/vocabulary/timing drills; recommend exact next practice; explain score change using evidence.

### Listening

Analyze A/B/C separately; track spelling, missed words, distractors, accent difficulty, prediction failure, concentration, paraphrase/synonym problems; generate targeted dictation, medication spelling, number/date, accent and paraphrase drills; create personal listening vocabulary from missed words; spaced re-test mistakes; explain score changes from attempts; recommend eligible approved resources.

## 14. Grammar, vocabulary and micro-learning

- personal “My Grammar Weaknesses” rather than forcing every learner through generic course order;
- rules, profession examples, mini exercises and adaptive re-tests;
- “My Vocabulary Brain” sourced from learner errors/output, dictionaries, recalls and saved words;
- vocabulary cards: meaning, pronunciation, collocation, synonym, paraphrase, clinical-vs-lay usage, example sentence;
- spaced repetition based on errors/forgetting patterns;
- 2–5 minute micro-lessons followed by short check;
- user controls such as harder/easier, profession-specific, use my mistakes, no hints.

## 15. Mock, exam mode and test-day intelligence

- realistic computer-based rehearsal with timing/navigation;
- exam mode disables hints/explanations/transcripts/coaching until submission;
- practice mode may allow hints;
- post-submit section analytics, error causes, timing and next actions;
- test-day rehearsal + logistics checklist;
- final-24-hours revision/logistics/warm-up/confidence without unnecessary new-content overload;
- exam-format/version selected according to exam date;
- result-day assistant ingests results and creates success or resit path;
- never act as a live answer engine during a real official exam.

## 16. Multimodal and file intelligence

Understand eligible screenshots, score reports, question images, PDFs, handwritten notes, Writing letters, role cards and approved audio. Extract score/exam date only after confirmation before saving. Explain uploaded material in preferred language, compare Writing versions, convert notes to cards/checklists and warn on likely real patient-identifying/sensitive information before storage/sharing. File/image/audio usage is tiered and metered.

## 17. Platform navigator and action-taking AI

When a destination is resolvable, answer “Where is X?” with an Open/Start action rather than a long manual path.

Action catalogue:

- open exact course/module/video/PDF/exam/question;
- open video at timestamp;
- start recommended practice;
- continue last activity;
- save rule/note;
- save vocabulary;
- add activity to plan;
- mark complete when product evidence supports it;
- set study reminder/exam countdown;
- open upgrade/checkout;
- create support request with context;
- prepare tutor handoff summary;
- open workshop/booking;
- report wrong answer/content issue;
- show AI allowance/credits and what consumes them.

All actions need server-side authorization and signed/validated targets where security-sensitive.

## 18. Context-aware AI on every screen

The client should supply a bounded **context envelope** containing only information required for the current surface: page, active exam, question, selected answer, visible content, course, video timestamp, selected Writing range or speaking-session state. “Explain this” must use the current context without copy/paste. Screen context never expands content entitlement.

## 19. Readiness, mastery and personal analytics

- overall/subtest readiness with transparent drivers;
- mastery map by skill/subskill using clear states;
- trend lines for score, accuracy, timing and confidence;
- blockers: “what is stopping me from being exam-ready?”;
- evidence-based “why did my score change?”;
- personal best/milestone recognition;
- anonymous cohort benchmarking only when sample/privacy/interpretation are responsible;
- no guaranteed pass probability; show uncertainty;
- weekly progress report of wins, risks, priorities and planned actions.

## 20. Proactive coaching, calendar and notifications

- inactivity risk nudges;
- exam countdown + plan adaptation;
- weak-subtest practice reminders;
- relevant new-content/recall notification by entitlement/preferences;
- calendar-aware study blocks/workshops/private sessions/exam date;
- immediate plan adjustment when work shift/day off changes;
- consented app/email/push weekly summary;
- Ultimate continuous/proactive coaching, lower tiers less adaptive/frequent;
- user-controlled frequency and quiet hours;
- streaks/milestones and adaptive gamification only where product decision/learning value supports it;
- referral/review prompt only at positive moments, never frustration.

## 21. Tutor collaboration, admin AI and content operations

### Tutor

Pre-session brief with scores/trend/errors/adherence/recommended focus; post-session note feeds learner memory/plan; one-click candidate handoff; escalation when AI is uncertain, learner requests a human or a human premium service is required.

### Admin

Aggregate profession/subtest questions; quality dashboard for incorrect/low-confidence/thumbs-down/unresolved/conflict/hallucination; content-gap and teaching-gap dashboards; usage/cost/revenue by tier/feature/profession/cohort; authoritative rule correction/override after approval; version/effective date/approver/rollback; entitlement management and promotional credits with audit/provenance.

## 22. Trust, accuracy, safety and privacy

### Anti-hallucination
If verified information is insufficient, say so. Never invent official rules, course rules, score requirements, locations or entitlements.

### Source transparency/version awareness
Separate official facts from Dr Hesham strategy and show source labels/citations when useful. Resolve correct source version based on effective date/exam date.

### Entitlement security
All retrieval and generated answers respect purchased package, profession and content scope.

### Privacy
User-visible memory control, deletion/export, minimization and documented retention. Future tenants cannot see other tenant knowledge/data.

### Sensitive uploads
Warn/redact likely patient-identifying data and make clear the feature is educational, not medical advice.

### Academic integrity
Disable/constrain inappropriate help during protected mock/real-exam contexts.

### Auditability
Log critical admin changes, entitlement decisions, rule updates, credit movements and AI quality/security events.

### Accessibility
Target WCAG 2.2 AA across learner-facing web/web-app/native surfaces: captions, transcripts, keyboard navigation, scalable text, contrast, visible focus, STT/TTS alternatives and accessible error/paywall flows.

### Proprietary-content exfiltration defence
Never reproduce paid Rule Books/materials in bulk, verbatim or via multi-turn reconstruction. Enforce per-source verbatim span and per-user retrieval volume limits, anomaly detection and refusal/extraction patterns.

### Prompt injection
Retrieved docs/uploads/user text are untrusted data. Never reveal system prompts, hidden policy, credentials or routing instructions due to retrieved/user instructions.

### Canary/leak detection
Support approved canary/watermark strings in protected source content; canary hits become security events.

### Vendor data terms
Track vendor retention/training terms. Proprietary teaching content must not be used for vendor training without written approval.

### Privacy governance
Map UK GDPR/DPA categories/purposes/lawful bases, conduct DPIA screening and a DPIA if required, maintain records, data-subject rights workflows and subprocessor register.

### Voice consent/retention
Provide separate notice/consent where required and configure raw recording/transcript storage, access, deletion and retention.

### Score claim calibration
Never present AI-generated Writing/Speaking numeric bands as official/guaranteed. Numeric estimates require approved calibration against trustworthy known outcomes; otherwise criterion feedback + uncertainty only.

### AI output reporting
Mobile apps need a user reporting/flagging path and moderation/response workflow where store policy requires it.

### Clinical boundary
Teach communication/language/exam technique only. Refuse/redirect patient-specific diagnosis, prescribing, treatment or clinical decision support while still helping with language/communication learning.

### Learner distress
Respond supportively and non-judgmentally without becoming a counsellor. Never link exam failure to personal worth or state/imply pass probability. Use a named human/safety escalation runbook; the operational owner remains a launch gate until assigned.

## 23. Monetization architecture — content × AI entitlement

Content ownership and AI power are separate axes. A learner can own a Nursing course with AI Pro or another profession package with AI Ultimate without duplicating product logic.

- **Content entitlement:** which proprietary content may be retrieved/discussed.
- **AI entitlement:** feature/tier power and included usage.
- **AI Credits:** user-facing meter for high-cost assessments, voice and heavy files/media.

Normal navigation does not consume AI Credits. Simple grammar/course Q&A should not consume premium credits unless it triggers a designated high-cost assessment action. Extra credits can be bought without changing tier. Course bundle discounts require AI cost to be priced in. Never grant perpetual Pro/Ultimate with a course unless economics explicitly cover it.

## 24. Recommended launch pricing and base allowances

Initial source values are configuration defaults subject to approved updates:

- Free: £0, 20 messages / rolling 30 days, mini diagnostic/plan preview/navigation/basic public Q&A/short speaking demo.
- Plus: £7.99/month, ~400 text messages, basic personalized plan, 30-day memory, purchased-content Q&A, limited file/voice, basic Error Bank.
- Pro: £14.99/month, ~1,200 text messages, adaptive plan, long-term memory, Error DNA, advanced course-aware tutoring, deeper analytics, voice/speaking, video helper.
- Ultimate: £26.99/month, ~3,000/fair use, continuous mentor/fingerprint/proactive/multimodal/largest credit wallet/priority reasoning — **full promise Stage 3 gated**.

Pricing objective: protect net contribution after destination tax, store/payment fees and AI variable cost. Initial AI-only guardrail targets at least 75% margin on net revenue after channel fees versus AI variable cost; total contribution must also include support, refunds/chargebacks and hosting.

## 25. Tier feature matrix behavior

Policy engine must distinguish outcomes including public/navigation Q&A and bilingual/code-switching; profile depth; plan cadence; memory depth; Error DNA/Fingerprint; owned-content access and Rule Book depth; next-best-action sophistication; drill allowance; file/image limits (source Plus 5/mo, Pro 25/mo, Ultimate 100/mo); voice; Speaking simulation depth; Writing/Speaking assessment; readiness analytics; auto-replanning; proactive coaching; video/timestamp helper; tutor handoff; model priority; experimental access.

Provisional included monthly credits 0/2/8/20 are placeholders until live cost validation.

## 26. AI Credits and top-ups

Existing compatible charges: Writing 2, Speaking 2, Reading 1, Listening 1. Deep cross-subtest analysis, voice and large PDF/extended audio charge are TO VERIFY.

Top-up source pricebook: 5 credits £5.99; 15 credits £13.99; 30 credits £23.99.

Rules:

- separate from normal text message allowance;
- show balance and exact charge before action;
- require confirmation;
- no charge for failed technical attempt;
- admin promotional credits with expiry/source;
- one user-facing AI Credits wallet only;
- migrate unspent existing credits 1:1 under grandfathering rules;
- backend preserves provenance: course, subscription, top-up, promo/admin;
- audit every movement;
- ledger supports purchase-level reversal, unused-credit revocation and technical-failure restoration;
- refund/chargeback handling of consumed purchased credits follows approved legal/channel policy.

Cost-per-credit is not approved until live telemetry. Fully loaded cost includes model, voice, transcription, file processing and attributable delivery cost, not just LLM tokens.

## 27. Profit protection and cost controls

Initial monthly AI variable-cost ceilings per active user: Plus ≤ £1.00; Pro ≤ £2.20; Ultimate ≤ £4.20.

Operational controls: cheap/deterministic navigation/classification routes; stronger models only when needed; safe caching; structured memory compaction; tier token/file/audio/rate limits; daily cost by feature/user/tier/profession/cohort; margin alerts; fair-use/throttling for abusive automation-like use.

### Emergency kill switches

Founder/Product Owner can authorize emergency control; AI Engineering executes/logs. Backup owner is TO VERIFY. Support independent disable/cap of live voice, freeze new credit consumption while preserving balances, suspend tier/action/vendor/file workload, rollback knowledge/prompt/model config. Every event needs incident record, customer impact, recovery criteria and explicit re-enable approval.

## 28. Revenue economics reference

Legacy gross billings are illustrative only and must not drive LTV/CAC/margin decisions. Net-revenue and cohort models are authoritative planning concepts. Implementation should surface actual data, not hard-code illustrative outputs.

## 29. Course bundles and AI add-ons

Course ownership unlocks content, not strongest AI tier. Example source mappings:

- Free user → Free AI, upsell Plus/Pro/Ultimate;
- Tutor Book/Recalls → eligible content preview/navigation, upsell Plus/Pro;
- single-subtest → Free or time-limited basic if priced in;
- Crash/Fast Track → limited course-aware AI only if price covers it;
- Full Course → limited or time-bounded included AI;
- Ultimate Course → defined 60/90-day Ultimate prep window, then renew/top-up.

Source add-on prices: Plus £6.99/£18.99, Pro £12.99/£34.99, Ultimate £23.99/£64.99 for one/three months. Premium AI access must expire deliberately.

## 30. Upgrade UX and paywalls

- anonymous demo ~3 interactions before registration prompt;
- Free ~20 messages/rolling 30 days with visible counter;
- premium lock shows personalized preview, not dead error;
- upgrade card: exact tier, monthly price, 3–5 relevant benefits, one CTA;
- contextual paywall based on requested action;
- checkout preserves chat and resumes same conversation after success;
- explain why upgrade using actual exam date/weak skill/request/limit;
- do not spam paid users with irrelevant upsells;
- self-service upgrade/downgrade/cancel/renew/top-up where possible;
- local currency/regional promotions while retaining clear GBP base price and channel-safe policy.

## 31. Growth, lead generation and SEO intelligence

Use public demo as lead capture after consent; mine anonymized query trends; create admin SEO/content queue with human editorial approval; generate internal-link/topic suggestions from real demand rather than mass low-value pages; ask for review/referral only at positive milestones; track Free→paid by trigger/exam/profession/source/first “wow” moment.

Owned-audience launch sequence uses stated 52,000+ reach only after deduplicated/channel counts are verified. Prioritize existing learners/buyers, exam-soon WhatsApp/Telegram, YouTube/Facebook, then public SEO/referral and only later paid cold acquisition after CAC/payback proof. Keep warm, organic-cold and paid-cold cohorts separate.

## 32. Multi-exam expansion

Exam is a modular knowledge/assessment pack with exam identity, version/effective date/current-retired/delivery mode, module/subtest, task type/rules/evaluation, profession/track, approved Rule Book/methodology, content pack and candidate mastery.

Cross-exam transfer can preserve vocabulary/listening/grammar strengths but must rebuild exam-task strategy. Country/regulator exam advisor uses current verified facts only.

## 33. B2B/white-label/enterprise

Future capabilities: strict multi-tenancy; white-label logo/color/persona/welcome; institution curriculum/policy/FAQ/methodology upload; roles; seat/active-user limits/budgets/cost reporting; institution mastery/engagement/at-risk/content-gap analytics; tutor copilot/custom escalation; contract-specific AI restrictions; SSO/API/webhooks/LMS; audit/retention/enterprise support; institution-specific public/internal assistant using same core.

Commercial model remains indicative: platform minimum + per-active-learner, annual bands, setup/branding + recurring fee and custom integration contracts.

## 34. Business and AI operations dashboard

Track at minimum MRR by tier; Free→paid/upgrade conversion; churn/downgrade; ARPU/top-up; AI cost/user/tier; contribution/margin; cost by text/voice/Writing/Speaking/files; messages/credits/user; 7/30/90 retention; plan adherence/learning engagement; support deflection/tutor escalation; thumbs up/down; low-confidence/unknown; content gaps/asked topics; profession/exam/tier mix; future tenant cost/revenue/seat use. Alert when tier cost breaches ceiling, abusive use emerges or a feature becomes structurally unprofitable.

## 35. Complete feature inventory / traceability

The authoritative inventory is **F-001 through F-184**. Use `traceability/FEATURE_TRACEABILITY_MATRIX.md`. Every item must receive an explicit product decision and engineering status. Phasing is allowed; silent omission is forbidden.

## 36. Release sequence

- **Stage 1 Monetisable OET Core:** safe monetisable core, one approved profession/corpus, navigation, basic plan/memory, existing Writing integration, Free/Plus/Pro, usage/paywall, source separation, privacy/exfiltration, cost/support feedback.
- **Stage 2 Learning Moat:** full approved OET grounding, Error DNA, adaptive plans, R/L/W tutors, video/timestamps, analytics, unified credits, content ops.
- **Stage 3 Voice & Ultimate Mentor:** Arabic/English voice after feasibility, Speaking simulator, fingerprint, proactive coaching, continuous replan, multimodal and tutor collaboration.
- **Stage 4 Multi-Exam:** IELTS/PTE/TOEFL/version engine.
- **Stage 5 B2B:** multi-tenancy, white label, analytics, roles, SSO/API/LMS, budgets/contracts/residency.

## 37. Final acceptance criteria

Demonstrate rapid personalized onboarding; grounding in approved eligible OET content without locked leaks; official-vs-methodology distinction; direct resource opening; persistent identity/memory/plan across supported surfaces; replan after scores/missed days/exam-date changes; Free cap/contextual upgrade; meaningful tier outcome differences; Ultimate continuous mentor only after its gate; separate expensive-feature metering/top-ups; admin revenue/cost/margin/quality/content gaps; admin-approved knowledge versioning; current exam version update without rebuild; future exam/B2B extensibility; privacy/access/authority/integrity/tenant controls.

## 38. Unit economics, channel fees and regional pricing

Profitability is based on **net** revenue: gross price minus destination tax minus store/payment fees, then minus AI variable cost, support/human cost and refund/chargeback provision.

Source billing options monthly / three-month / annual: Plus £7.99 / £21.99 / £79.99; Pro £14.99 / £39.99 / £149.99; Ultimate £26.99 / £72.99 / £269.99.

Regional purchasing-power bands require billing-country/store-region consistency; do not use IP alone. Local rails/PPP discounts must preserve minimum contribution. Web vs in-app prices may differ only where store rules allow. Do not subsidize structurally loss-making credit packs to preserve visual price parity.

## 39. Cohort economics, churn, LTV and CAC

Exam prep has naturally short customer lifetime. Model 2/3/4-month cohort contribution and do not assume indefinite subscription. Track voluntary vs involuntary churn and implement dunning/retry. Maintain refund/chargeback provision. Treat resit learners as reactivation cohort. Keep warm-owned, cold-organic and paid-cold acquisition separate. The source interim 3:1 cold-paid guardrail and ~£9.85 max CAC are temporary planning policy, not measured fact.

## 40. Technical architecture requirements

### Retrieval/RAG
Chunk by pedagogical meaning/document structure. Metadata: exam, version, profession, subtest, entitlement, authority, source ID, timestamp/page. Hybrid lexical + vector retrieval, reranking where it improves measured quality.

### Knowledge authority
Correct authority selection must be tested by golden questions. Conflicts are surfaced, not silently blended.

### Model routing
Cheap/fast deterministic paths for navigation/classification/lookups; stronger reasoning for planning/complex tutoring/assessment. Dashboard cost/latency by intent.

### Latency
Separate budgets for page Q&A, plans, files and voice; P50/P95 by surface. Voice target remains feasibility-gated.

### Caching
Cache stable platform maps, approved snippets and repeated public facts safely. Vendor prompt/prefix cache where appropriate. Never cross entitlement/user/tenant boundaries.

### Memory compaction
Summarize long chat into structured memory/event history with provenance. Bound token growth and regression-test memory accuracy.

### Vendor abstraction
Model/voice providers behind internal interfaces; provider swap tested in staging.

### Observability
Trace user/tier/intent/model/cost/latency/retrieval sources/result status without exposing unrelated user data.

### Rate limits/abuse
Per-user/tier limits, automation detection, account-sharing signals, file/audio caps and anomaly alerts.

### Security
Secrets outside prompts/content stores, least privilege, signed deep links/actions, tenant isolation, prompt-injection/exfiltration red-team before production.

## 40A. Core data model implementation contracts

At minimum explicitly model/map:

- **Learner profile:** user, profession, exam/version/date/target, availability, language/style, AI tier, entitlements, onboarding, field source/consent/versioning/edit/delete.
- **Event log:** event ID, user, timestamp, surface, attempt/question/answer, score, resource/video, submission, plan action, navigation result, provenance; append-only with retention.
- **Learning memory:** mastery, errors, vocab, plan state, confidence, tutor notes, AI summaries, source event/document, created/updated by, edit history/correction path.
- **Credit ledger:** entry ID, user, delta, balance-after/projection, provenance, action, transaction, expiry, reversal/failure link; immutable/idempotent/atomic.
- **Entitlements:** package, profession scope, AI tier, effective dates, purchase channel, grandfathering, status; one authoritative service.
- **Knowledge source metadata:** source ID, authority, exam/version, profession, subtest/skill, entitlement, location/page/timestamp, approval/version/checksum/canary tags.

## 40B. Operational reliability and incident response

Define availability/SLO after hosting architecture is selected; still create incident process before paid launch. Monitor uptime/latency/error/model/vendor/ledger/knowledge regression by surface. Define severity/on-call/acknowledgement/customer communication when owners/targets are approved. Material incidents preserve balances, restore failed-action credits and record timeline. Have rollback for provider/model, prompt/config, knowledge release, payments/ledger and voice. Critical incidents require post-incident review and corrective actions.

## 41. Evaluation and QA

Maintain golden questions per profession/subtest including normal, ambiguous, locked-content extraction, authority conflicts and navigation; retrieval recall/precision/reranking/citation-location/entitlement tests; hallucination unsupported-claim/invented-location/rule/official-claim tracking; Writing and Speaking calibration; Egyptian Arabic + MSA + Arabic-English code-switch set; regression before prompt/model/retrieval/chunking/source update; sampled human review/low-confidence escalation; adversarial prompt injection/system prompt extraction/Rule Book reconstruction/account sharing/abusive use/jailbreaks.

Thresholds are TO VERIFY unless approved. Critical entitlement/payment fabrication/leak has zero tolerance.

## 42. Content ingestion and knowledge operations

Content operations is continuous and first-class. The AI is not considered “trained on everything” until the source inventory is processed, tagged, approved and versioned. See `CONTENT_OPS_INGESTION.md`.

## 43. Arabic and voice feasibility spike

Before Stage 3, run a two-week production-quality/cost spike measuring Egyptian Arabic ASR, code-switching, latency, pronunciation reliability, per-minute cost and session controls. Source session control includes 12-minute live maximum, idle/silence cutoff, reconnect and no billing for failed sessions. Keep fallbacks and do not promise full duplex voice if the gate fails.

## 44. Legal/privacy/trademark/app-store readiness

External gates include UK GDPR/DPA/DPIA, voice consent/retention, subprocessors/DPAs/vendor training, Rule Book confidentiality, future data residency, OET name/material usage, Jana/Sami clearance, Terms of Service, Google Play AI content reporting, Apple review/payment/steering and UK subscription-contract readiness. Code supports required flows; legal conclusions remain external.

## 45. Risk register engineering implications

Mitigate voice cost overrun; Rule Book leakage; grading inaccuracy; model outage/deprecation; provider price change; store-policy change; Arabic voice quality; content ingestion slippage; key-person dependency; trademark/regulator challenge; privacy breach; account sharing; poor conversion; human-support cost; scope/budget mismatch; subscription-law retrofit; clinical misuse; learner-distress mishandling through the architecture/testing/runbooks specified elsewhere in this pack.

## 46. Explicit non-goals for first 12 months

These remain in long-term traceability but cannot block OET monetization:

- full B2B/white-label enterprise administration;
- full multi-exam production before OET quality/economics;
- dedicated Windows/macOS native apps if web/PWA is adequate;
- gamification without learning/conversion value;
- cohort benchmarking before data/privacy reliability;
- SSO/API/LMS/institution audit exports;
- fully autonomous proactive coaching before memory/notification safeguards;
- official scoring/pass probability/regulator-acceptance claims;
- unlimited voice/expensive assessments;
- clinical decision support;
- counselling/psychotherapy/pass prediction.

## 47. Support and human-cost model

Dashboard contribution must include support tickets/handling minutes, tutor escalation time, content correction hours and refunds/chargebacks in addition to AI cost. Human services are costs, not free untracked features.

## 48. Success criteria and target register

Track with named owners and externally approved targets: Free→paid conversion, 30-day paid retention, voluntary churn, involuntary churn recovery, gross/net ARPU, net contribution margin, LTV:CAC, support deflection, thumbs-down rate, hallucination/unsupported claim rate, retrieval quality and voice cost/minute/completion. Do not invent missing targets.

## 49. Final coverage verdict and remaining gates

No more broad feature brainstorming is required to begin controlled development. Remaining work that cannot be “finished on paper”:

1. complete content inventory;
2. two-week Arabic/voice spike;
3. live cost beta and cost-per-credit approval;
4. Writing/Speaking calibration;
5. legal/privacy/trademark/app-store review;
6. developer scope/time/budget sign-off;
7. regional pricing validation;
8. beta cohort/first profession selection;
9. SLO/severity/on-call values;
10. named learner-distress escalation owner.

After these, new ideas enter change control rather than silently expanding approved scope.

# Feature Traceability Matrix

> Source: AI Learning Companion Master Specification v3.0. This file preserves all 184 feature IDs. The PDF leaves every Accept/Defer/Reject checkbox unsigned; therefore source decision is intentionally **UNDECIDED** until product sign-off. The implementation program still carries every feature so nothing silently disappears.

## Operating rules

- Claude Code must update `implementation_status` after repository audit and after each implementation slice.
- A feature is not complete merely because UI exists; backend, authorization, data, telemetry, tests and acceptance evidence must exist where applicable.
- `TO VERIFY` values are never invented. Use configuration/feature flags and block production exposure where the specification requires external validation.
- If code already satisfies a feature, record the exact files/tests instead of rebuilding it.

## Identity & onboarding

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-001 | Anonymous demo mode | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-002 | Registered Free mode | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-003 | Profession selection | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-004 | Exam/version selection | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-005 | Exam date and target | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-006 | Country/regulator goal | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-007 | Previous result capture | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-008 | Screenshot score import | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-009 | Study availability | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-010 | Language/code-switch preference | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-011 | Teaching-style preference | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
| F-012 | Multiple historical exam journeys | Stage 1 | Product + Frontend/Backend | Auth + learner profile schema |  |
## Knowledge & grounding

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-013 | All approved Rule Books | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-014 | All relevant session transcripts | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-015 | Writing workshops | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-016 | Speaking workshops | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-017 | Correction sessions | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-018 | Reading/Listening materials | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-019 | Tutor Book | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-020 | Dictionaries/common-word lists | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-021 | Recalls/practice banks | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-022 | Video timestamp index | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-023 | Platform navigation map | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-024 | Support/FAQ knowledge | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-025 | Official current exam sources | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-026 | Source hierarchy | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-027 | Content versioning | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-028 | Teacher override | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
| F-029 | Conflict detection | Stage 1-2 | AI/RAG + Content Ops | Asset register + entitlements + approved sources | Stage 1 uses a limited approved corpus; Stage 2 expands full grounding. |
## Planning & memory

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-030 | Initial diagnostic | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-031 | Daily plan | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-032 | Weekly plan | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-033 | 30/60/90-day plans | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-034 | 14/7/3-day plans | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-035 | Exam-eve plan | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-036 | Missed-day replanning | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-037 | Shift-worker planning | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-038 | Travel-aware replanning | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-039 | What should I do now? | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-040 | Next-best-action engine | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-041 | Conversation memory | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-042 | Learning memory | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-043 | Journey memory | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-044 | Error DNA | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-045 | Learning Fingerprint | Stage 3 | AI + Backend | Learner profile + event/memory store |  |
| F-046 | Spaced reinforcement | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
| F-047 | Memory controls/export/delete | Stage 2 | AI + Backend | Learner profile + event/memory store |  |
## Tutoring

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-048 | Quick answer | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-049 | Detailed tutor | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-050 | Socratic tutor | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-051 | Examiner mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-052 | Coach mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-053 | Dr Hesham mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-054 | Arabic explanation | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-055 | English-only mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-056 | Adaptive drill generator | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-057 | Micro-lessons | Stage 4-5 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-058 | Grammar tutor | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-059 | Vocabulary brain | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-060 | Writing guided mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-061 | Writing hint mode | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-062 | Writing correction | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-063 | Writing compare/rewrite | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-064 | Writing scoring/criteria feedback | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-065 | Speaking voice role play | Stage 3 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-066 | Speaking difficult-patient modes | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-067 | Speaking pronunciation/fluency analysis | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-068 | Reading Part A/B/C coach | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-069 | Listening Part A/B/C coach | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-070 | Confidence-vs-accuracy analysis | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
| F-071 | Why did my score change? | Stage 2 | AI + Pedagogy | Approved pedagogy + retrieval + evaluation |  |
## Exam & analytics

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-072 | Full mock mode | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-073 | Practice vs Exam mode separation | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-074 | Computer-based rehearsal | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-075 | Test-day rehearsal | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-076 | Final 24-hours mode | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-077 | Result-day assistant | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-078 | Resit recovery plan | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-079 | Readiness score | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-080 | Mastery map | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-081 | Trend analytics | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-082 | Timing analytics | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-083 | Personal bests | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-084 | Anonymous cohort benchmarking | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
| F-085 | Weekly progress report | Stage 2-3 | AI + Data | Attempt data + scoring/evaluation models |  |
## Multimodal & context

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-086 | Image/screenshot understanding | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-087 | PDF understanding | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-088 | Audio analysis | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-089 | Handwritten note understanding | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-090 | Score report extraction | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-091 | Current-page awareness | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-092 | Current-question awareness | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-093 | Current-video awareness | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-094 | Timestamp-aware help | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-095 | Writing selection awareness | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-096 | Speaking-session context | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
| F-097 | Cross-device conversation continuity | Stage 2-3 | AI + Frontend | Upload pipeline + page/context APIs |  |
## Actions & navigation

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-098 | Deep-link resource open | Stage 1 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-099 | Open exact timestamp | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-100 | Start recommended practice | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-101 | Continue last activity | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-102 | Save note | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-103 | Save vocabulary | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-104 | Add to plan | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-105 | Set reminder | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-106 | Open workshop | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-107 | Create support request | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-108 | Tutor handoff summary | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-109 | Report wrong answer | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-110 | Show allowances/credits | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-111 | Contextual upgrade checkout | Stage 1 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
| F-112 | Resume chat after purchase | Stage 1-2 | Frontend + Backend | Platform route map + permission-safe action APIs |  |
## Proactive & engagement

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-113 | Exam countdown | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-114 | Inactivity risk nudges | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-115 | Weak-subtest reminder | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-116 | New relevant content alert | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-117 | Calendar integration | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-118 | Push/email/app progress summaries | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-119 | Quiet hours | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-120 | Streaks/milestones | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-121 | Adaptive gamification | Deferred-by-default / Stage 3+ TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
| F-122 | Referral/review prompt at positive moments | Stage 3 / TO VERIFY | Product + Backend | Reliable memory + notification preferences | Appendix A visibly contains F-113 but the extracted matrix omits rows F-114 through F-122; preserve these features and require owner/phase sign-off. Section 46 also makes non-outcome gamification and fully proactive autonomy non-goals for the first 12 months. |
## Admin/tutor

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-123 | Tutor pre-session brief | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-124 | Tutor post-session notes | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-125 | Admin aggregate AI queries | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-126 | Quality dashboard | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-127 | Hallucination/low-confidence queue | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-128 | Content-gap dashboard | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-129 | Teaching-gap dashboard | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-130 | Rule approval/version/rollback | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-131 | Content Studio extraction proposals | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-132 | Revenue/cost dashboard | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-133 | Entitlement management | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
| F-134 | Promotional AI Credits | Stage 2-3 | Product + Admin/Backend | Admin roles + analytics + audit log |  |
## Commercial

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-135 | Free message cap | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-136 | Plus tier | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-137 | Pro tier | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-138 | Ultimate tier | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry | Appendix A classifies the Ultimate tier commercially as Stage 1, while the release sequence says the full Ultimate Mentor promise launches only in Stage 3. Implement schema/paywall readiness early but do not expose the full Ultimate promise until Stage 3 gates pass. |
| F-139 | AI Credits | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-140 | Top-up packs | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-141 | Usage counter | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-142 | Contextual paywall | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-143 | Course × AI entitlement matrix | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-144 | Standalone AI subscription | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-145 | Course AI add-on | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-146 | Monthly billing | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-147 | Optional annual billing | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-148 | Upgrade/downgrade/cancel | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-149 | Fair-use/rate limits | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-150 | Regional currency display | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
| F-151 | Cost ceiling alerts | Stage 1 | Product + Finance/Payments | Pricing + payments + entitlement service + cost telemetry |  |
## Trust & platform

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-152 | Official-vs-methodology separation | Stage 2 (baseline required in Stage 1) | Security + AI + Product | Security/privacy design + auditability | Release sequence explicitly requires source-authority separation in Stage 1 although Appendix A places F-152 in Stage 2. Build a Stage 1 baseline and harden/expand in Stage 2. |
| F-153 | Anti-hallucination behavior | Stage 1 | Security + AI + Product | Security/privacy design + auditability |  |
| F-154 | Entitlement-safe retrieval | Stage 1 | Security + AI + Product | Security/privacy design + auditability |  |
| F-155 | Academic integrity controls | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-156 | Sensitive upload warning | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-157 | Privacy controls | Stage 1 | Security + AI + Product | Security/privacy design + auditability |  |
| F-158 | Data export/delete | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-159 | Audit logs | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-160 | Accessibility - WCAG 2.2 AA across learner-facing web, web-app and native-app surfaces | Stage 2 | Security + AI + Product | Security/privacy design + auditability | WCAG 2.2 AA is a global product target. Even if Appendix A classifies the inventory item as Stage 2, Stage 1 UI must not knowingly introduce inaccessible foundations. |
| F-161 | Arabic RTL support | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-162 | iOS | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-163 | Android | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-164 | Windows | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-165 | macOS | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-166 | Website | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
| F-167 | Web app | Stage 2 | Security + AI + Product | Security/privacy design + auditability |  |
## Expansion & B2B

| ID | Requirement | Phase | Owner | Dependencies | Notes |
|---|---|---|---|---|---|
| F-168 | IELTS pack | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-169 | PTE pack | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-170 | TOEFL pack | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-171 | Future exam packs | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-172 | Exam-version engine | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-173 | Cross-exam skill transfer | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-174 | B2B multi-tenancy | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-175 | White-label persona | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-176 | Institution knowledge upload | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-177 | Institution roles | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-178 | Seat/usage budgets | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-179 | Institution analytics | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-180 | SSO | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-181 | API/webhooks | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-182 | LMS integration | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-183 | Custom retention | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |
| F-184 | Enterprise audit/support | Stage 4-5 | Platform/Enterprise | Stable OET core + versioned knowledge-pack architecture |  |

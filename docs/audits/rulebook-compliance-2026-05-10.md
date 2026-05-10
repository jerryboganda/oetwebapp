# OET Rulebook Compliance Audit — 2026-05-10

**Audited by:** GitHub Copilot (Oh My OpenAgent mode), at user request.
**Scope:** Reading + Writing modules — code (this repo) and live production at https://app.oetwithdrhesham.co.uk/.
**Source rulebooks (combined):**

- Reading rulebook A — `Desktop/deepseek_markdown_20260509_12c7ed ---- reading.md` (12 sections, R01.x – R12.x).
- Reading rulebook B — `Desktop/deepseek_markdown_20260510_d989b0 ------- reading 222222.md` (platform-design rulebook, 12 sections).
- Writing rulebook — `Desktop/deepseek_markdown_20260509_8da31c------ WRITING RULEBOOK.md` (16 sections, R01.x – R16.x).

**Auditor verdict:** **NOT 100% implemented.** One P0 showstopper (Writing AI grader returns hard-coded scores for every submission) plus several P1/P2 gaps. Reading module is materially compliant; Writing module is structurally present but not enforced at submission time. Detailed mapping below.

---

## 0. Executive summary

| Module | Verdict | Confidence | Headline gap |
|---|---|---|---|
| Reading | **~90% compliant** | High (code + tests verified) | Minor: rulebook B "diagnostic Reading test → study path" generator only partially wired; some prompts/copy not reviewed against rulebook B prose. |
| Writing | **~30% effective compliance** | Very high (code + live login verified) | **P0**: AI grader stub returns identical hard-coded scores for every learner regardless of content (`BackgroundJobProcessor.cs#L408-L460`). Deterministic rule engine and rulebook prompt builder both exist but are NOT invoked on submission. |

If the question is "is the writing rulebook 100% enforced when a learner submits a letter on production today?", the answer is **no — 0% of the rulebook is enforced**, because the only code path that completes a writing evaluation in production is a hard-coded stub.

---

## 1. P0 / P1 / P2 gap inventory

### P0 — Showstoppers (must fix before any "rulebook compliance" claim)

| # | Gap | Evidence | Fix sketch |
|---|---|---|---|
| P0-1 | Writing AI grader is a hard-coded stub. Every learner gets `ScoreRange="330-360"`, `GradeRange="C+-B"`, identical strengths/issues/criterion scores, identical anchored feedback item. None of the rulebook is consulted. | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs#L408-L460` (`CompleteWritingEvaluationAsync`). Comment explicitly states "placeholder values for the mock evaluation pipeline". | Replace stub with a real call to `AiGatewayService` using feature `writing.grade`; parse JSON contract at `AiGatewayService.cs#L843-L856` into `Evaluation.CriterionScoresJson`/`FeedbackItemsJson`; gate via existing `AiQuotaService`. Run `WritingRuleEngine.cs` deterministic checks first and merge findings (see C-1 below). |

### P1 — High-impact gaps

| # | Gap | Evidence | Fix |
|---|---|---|---|
| P1-1 | Deterministic `WritingRuleEngine` (40+ critical-rule detectors) is never invoked at submission time. The TS twin `lib/rulebook/writing-rules.ts` runs in the editor for live lint, but its findings are not persisted to the `Evaluation`. | `backend/src/OetLearner.Api/Services/Rulebook/WritingRuleEngine.cs` is unused by `LearnerService.SubmitWritingAsync` / `BackgroundJobProcessor.CompleteWritingEvaluationAsync`. | After P0-1: invoke `WritingRuleEngine.Evaluate(letter, letterType, profession)` synchronously in `CompleteWritingEvaluationAsync`, merge with AI findings, dedupe by `ruleId`, persist. |
| P1-2 | AI prompt sends only the first 60 MAJOR rules verbatim and truncates the rest with "… and N more major rules." — even after wiring P0-1, the AI cannot assess rules 61+ in a single call. | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs#L798-L800` — `major.Take(60)`. | Either (a) raise to full corpus and accept higher token cost, or (b) split into two grader calls (CRITICAL+first-half MAJOR; CRITICAL+second-half MAJOR) and merge findings. Decision lives with cost tradeoff. |
| P1-3 | Frontend `WRITING_LETTER_TYPES` (Referral / Discharge / Transfer / Advice / Update) does not match the canonical 6 backend types (`routine_referral` / `urgent_referral` / `non_medical_referral` / `update_discharge` / `update_referral_specialist_to_gp` / `transfer_letter`). The classifier in `lib/rulebook/context.ts` returns canonical codes; the UI workflow uses display-only labels. Risk: rule-applicability filter mis-matches. | `lib/writing/workflow.ts#L12-L20` vs `backend/src/OetLearner.Api/Services/Content/WritingContentStructure.cs#L17-L60`. | Single source of truth: import canonical 6 from a shared TS const; render display labels from a separate map. |

### P2 — Medium-impact gaps

| # | Gap | Evidence | Fix |
|---|---|---|---|
| P2-1 | `WritingCoachService` (live in-editor coach) is regex-stub only. Does not surface deterministic rule violations from `WritingRuleEngine`. | `backend/src/OetLearner.Api/Services/WritingCoachService.cs#L14-L100`. | Replace regex stubs by delegating to `WritingRuleEngine` for the live editor, returning a debounced subset (sentence-length, contractions, "the patient", Latin abbreviations). |
| P2-2 | No first-class `rule_violations` table — violations buried in `Evaluation.FeedbackItemsJson`. Limits per-rule analytics ("which rule fails most often across cohort?"). | Schema in `backend/src/OetLearner.Api/Domain/Entities.cs#L349-L380`. | Add `WritingRuleViolation { Id, EvaluationId, RuleId, Severity, Quote, FixSuggestion, CreatedAt }`. Migration + index on `(RuleId, CreatedAt)`. |
| P2-3 | No e2e test that submits a non-compliant letter and asserts rulebook-driven feedback is produced. Existing Playwright specs are smoke/nav only. | `tests/e2e/prod-exhaustive.spec.ts#L109-L118` and similar — no rule assertions. | Add `tests/e2e/writing/rulebook-grading.spec.ts` with fixtures violating each CRITICAL rule (contractions, "the patient", Latin abbrev, missing urgent closure phrase, "Date:" prefix, etc.) and assert the deterministic engine reports them. |
| P2-4 | Reading rulebook B section 7 ("rules and regulations your academy must reflect") is partially implemented. "No use/disclosure of real test content" academic-integrity policy exists in terms but isn't surfaced inside the practice-test mode (no inline reminder). | `app/terms/page.tsx` covers it generically; no inline mock-mode policy banner. | Add a one-line policy line to the Part A intro screen and to mock-mode start screens. |
| P2-5 | Reading rulebook B section 6 ("Estimated OET band ... 'This is an estimate, not an official OET conversion.'") — present in code (`lib/scoring.ts#L780-L795`) but the literal disclaimer string was not verified rendered on `/reading/paper/.../results`. | `app/reading/paper/[paperId]/results/page.tsx#L99-L260`. | Verify and, if missing, append the disclaimer to the score card. |

### P3 — Lower priority / informational

| # | Gap | Evidence |
|---|---|---|
| P3-1 | Reading rulebook A R10.10 — "Zoom + and - buttons within the exam application; browser Ctrl+ blocked". Code provides app-level zoom controls (`ReadingZoomControls`, 80–125%) but does not block browser Ctrl+/Ctrl-. Acceptable for a web SPA; add a "use the in-app zoom" tooltip. | `app/reading/paper/[paperId]/page.tsx#L656-L673`. |
| P3-2 | Reading rulebook B section 11 — content quantity targets (20–30 full mocks, 50+ Part A standalone, etc.) — content-quantity audit not in scope of code; track in admin analytics. | n/a. |
| P3-3 | Writing rulebook drills only populated for the medicine profession. Other 12 professions have empty drill banks. | `rulebooks/writing/drills/medicine/**` only. |

---

## 2. Per-rule mapping — Reading rulebook A (R01.x – R12.x)

Status legend: ✅ implemented and verified; 🟡 partially implemented; ❌ missing; ⚪ not in code scope (operational/exam-day rule).

### Section 01 — Exam overview & sequence

| Rule | Summary | Status | Location / notes |
|---|---|---|---|
| R01.1 | Listening → Reading → Writing → Speaking sequence | ⚪ | Operational. UI navigation order matches in sidebar (`components/layout/sidebar.tsx`). |
| R01.2 | 42 Q total, A=20/B=6/C=16 | ✅ | `ReadingPart.MaxRawScore` enforced 20/6/16; counts gate in `ReadingStructureService` [tests #L110-L145](../../oetwebapp/backend/tests/OetLearner.Api.Tests/ReadingAuthoringTests.cs#L110-L145). |
| R01.3 | A standalone 15 min; B+C shared 45 min | ✅ | `PartABreakMaxSeconds=600` + deadlines [`ReadingAttemptService.cs#L221-L237`](../../oetwebapp/backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs#L221-L237). |
| R01.4 | Total 60 min | ✅ | Sum of partA + partBC deadlines. |
| R01.5 | C = C1 (8) + C2 (8) | ✅ | Counts gate via structure validator. |
| R01.6 | Same content for all professions | ✅ | Reading is profession-agnostic (no profession FK on `ReadingPart`). |

### Section 02 — Scoring & pass

| R02.1 | 30/42 = B = 350 | ✅ | [`lib/scoring.ts#L286`](../../oetwebapp/lib/scoring.ts#L286) anchor; [`OetScoring.OetRawToScaled`](../../oetwebapp/backend/src/OetLearner.Api/Services/OetScoring.cs). |
| R02.2 | Grade scale A 450+ / B 350-449 / C+ 300-349 / C 250-299 / D <250 | ✅ | `gradeListeningReading` in `lib/scoring.ts#L267-L385`. |
| R02.3 | 1 mark per Q, no negative marking | ✅ | `ReadingGradingService.cs#L222-L260`. |
| R02.4 | No clipping (Medicine pathways) | 🟡 | Pass-mark resolution is country-aware but no explicit "no-clipping" guard at result aggregation. Add policy doc + readiness checker rule. |
| R02.5 | NMC permits clipping | 🟡 | Same — country/pathway flag exists but no UX surface. |

### Section 03 — Part A structure & question types

| R03.1 | 4 texts A–D on one topic | ✅ | `ReadingText` rows under Part A; structure validator. |
| R03.2 | Q1–7 matching, 8–14 short, 15–20 sentence completion | ✅ | `ReadingQuestionType` enum + counts gate. The pre-existing WIP edit on `ContentPaperServiceTests.cs` (in your working tree) is updating tests to enforce exactly this pattern. |
| R03.3 | Text C usually has table/graph | ⚪ | Authoring guideline; no enforcement (acceptable). |
| R03.4 | Direct from text, not opinion-based | ✅ | Short-answer grading: exact match only. |
| R03.5 | 15 min Part A, split-screen on CBT | ✅ | `app/reading/paper/[paperId]/page.tsx` left/right panel. |
| R03.6 | After 15 min: Part A locks (CBT) / collected (paper) | ✅ | `partALocked` derivation [#L226-L243](../../oetwebapp/app/reading/paper/[paperId]/page.tsx#L226-L243); save-after-deadline rejection in tests. |

### Section 04 — Part A marking accuracy

| R04.1 **CRITICAL** | Spelling error → entire word wrong | ✅ | `strictPartA` flag in `ReadingGradingService.cs#L347-L385`. |
| R04.2 **CRITICAL** | Singular/plural error → wrong | ✅ | Strict normaliser does not strip plurals when `strictPartA` true. |
| R04.3 **CRITICAL** | Wrong form (range vs max, dose/kg vs total) → wrong | 🟡 | Enforced only via accepted-variants list. No semantic "form" check; relies on author specifying allowed variants. Acceptable. |
| R04.4 | Numbers exact format | ✅ | `ShortAnswerNormalisation` config. |
| R04.5 **CRITICAL** | Stricter than Listening Part A | ✅ | Listening grader (separate) is more lenient; Reading uses `strictPartA=true`. |
| R04.6 **CRITICAL** | Word-for-word from text; no paraphrase | ✅ | `ShortAnswerAcceptSynonyms=false` for Part A in policy default. |

### Section 05 — Part A timing

| R05.1 **CRITICAL** | Hard 15-min limit | ✅ | Backend deadline + frontend lock. |
| R05.2 | After 15 min: panel locks, optional break before B+C | ✅ | `ReadingBreakScreen` component, `breakPending` flag. |
| R05.3 **CRITICAL** | Paper-based: pencils-down at 15 min | ⚪ | Operational. |
| R05.4 | Part A sheet separate from B+C booklet | ⚪ | Paper exam only. |

### Section 06 — Part B structure

| R06.1 | 6 Qs, each with own short text | ✅ | Counts gate. |
| R06.2 | Text + Q on same page | ✅ | UI co-locates per question. |
| R06.3 **CRITICAL** | 3 choices A/B/C | ✅ | `MultipleChoice3` type. |
| R06.4 | 6 marks total | ✅ | `MaxRawScore=6`. |
| R06.5 | Shared 45-min block with C | ✅ | `partBCDeadline` covers both. |

### Section 07 — Part C structure

| R07.1 | 2 long texts, 8 Qs each | ✅ | Counts gate. |
| R07.2 | ~7–8 paragraphs each | ⚪ | Authoring guideline. |
| R07.3 **CRITICAL** | 4 choices A/B/C/D | ✅ | `MultipleChoice4` type. |
| R07.4 **CRITICAL** | Distinguish B (3 choices) vs C (4 choices) | ✅ | Two distinct enum variants; AI extraction enforces. |
| R07.5 | C1 and C2 on different topics | ⚪ | Authoring guideline. |
| R07.6 | Questions follow paragraph order | ⚪ | Authoring guideline. |
| R07.7 | Underlined word/sentence ≈ 2 Qs from that para | ⚪ | Authoring guideline. |
| R07.8 | ~4–5 Qs per text are inference / writer's view | 🟡 | `SkillTag` enum supports this but no per-text quota check. |
| R07.9 | Remaining 11–12 explicit | 🟡 | Same as R07.8. |
| R07.10 | Time allocation within block is candidate's responsibility | ✅ | Single 45-min timer; no sub-section locks. |

### Section 08 — Parts B & C marking & interaction

| R08.1 | 1 mark each, no negative | ✅ | `PointsEarned` only positive. |
| R08.2 | One answer per Q | ✅ | Radio control. |
| R08.3 **CRITICAL** | Unanswered = 0 marks; no auto-fill | ✅ | `ReadingAnswer.UserAnswerJson` may be null; scoring ignores nulls. |
| R08.4 | Left-click selects, right-click strikethrough | ✅ | `McqControl` in player [#L992-L1050](../../oetwebapp/app/reading/paper/[paperId]/page.tsx#L992-L1050). |
| R08.5 **CRITICAL** | Strikethrough does NOT count as selection | ✅ | Strikethrough state separate from selection state. |
| R08.6 | Paper-based: fill bubble | ⚪ | Operational. |

### Section 09 — Navigation (CBT)

| R09.1 **CRITICAL** | No break L→R | ⚪ | Practice mode allows entry independently; mock-mode chains them. |
| R09.2 | 10-min optional break A→B+C | ✅ | `PartABreakMaxSeconds=600`. |
| R09.3 **CRITICAL** | Part A panel locks permanently after 15 min | ✅ | `partALocked` + persist guard [#L295-L318](../../oetwebapp/app/reading/paper/[paperId]/page.tsx#L295-L318). |
| R09.4 | Sequential nav within B+C | ✅ | `QuestionNavigator`. |
| R09.5 | Press Next; confirmation at boundaries | ✅ | Submit confirms. |
| R09.6 **CRITICAL** | At end of 45-min B+C: locks, sub-test ends | ✅ | Auto-submit on B/C deadline [#L378-L382](../../oetwebapp/app/reading/paper/[paperId]/page.tsx#L378-L382). |

### Section 10 — Display & interaction (CBT)

| R10.1 | A: text left, Q right | ✅ | Split-pane layout. |
| R10.2 | B and C: text + Q on same screen | ✅ | Same-screen layout. |
| R10.3 | Timer always visible | ✅ | `AttemptToolbar` `role="timer"`. |
| R10.4 | A: highlighting available | ✅ | `data-reading-highlight-scope="passage"`. |
| R10.5 **CRITICAL** | A: copy-paste unreliable | ⚪ | Not enforced (acceptable; matches reality). |
| R10.6 | B/C: highlight passage | ✅ | Highlighter scope. |
| R10.7 | B/C: highlight stem | ✅ | `data-reading-highlight-scope="stem"`. |
| R10.8 **CRITICAL** | B/C: do NOT highlight choices | 🟡 | Highlighter is scoped via data attributes; choices don't carry the scope attribute, so they won't be highlight-eligible. Recommend an automated test pinning this behavior. |
| R10.9 | Right-click strikethrough | ✅ | `McqControl`. |
| R10.10 **CRITICAL** | App zoom +/-, browser Ctrl+ blocked | 🟡 | App zoom present (`ReadingZoomControls`); browser Ctrl+ not blocked (web SPA limit). Add tooltip. See P3-1. |
| R10.11 | No paragraph numbers in margin (CBT) | ✅ | No annotation feature. |

### Section 11 — Technical requirements

| R11.1 **CRITICAL** | Min 1920×1080 | ✅ | Display warning [#L107-L121](../../oetwebapp/app/reading/paper/[paperId]/page.tsx#L107-L121). |
| R11.2 **CRITICAL** | Display scale 100–125% | ✅ | DPR≠1 detection in same warning. |
| R11.3 **CRITICAL** | At-home: configure before exam day | ✅ | Warning copy advises. |
| R11.4 | ProProctor app | ⚪ | Real-exam infra. |
| R11.5 **CRITICAL** | Stable internet | ⚪ | Real-exam infra (autosave mitigates). |
| R11.6 | VPN/VM disabled | ⚪ | Real-exam infra. |
| R11.7 | Test-centre pre-configured | ⚪ | Real-exam infra. |

### Section 12 — Paper-based navigation

R12.1–R12.9: ⚪ Paper-only; not applicable to web platform. The "paper-look simulation mode" in [`components/domain/reading-paper-simulation.tsx`](../../oetwebapp/components/domain/reading-paper-simulation.tsx) and helpers in [`lib/reading-paper-simulation.ts`](../../oetwebapp/lib/reading-paper-simulation.ts) provide visual-only simulation; rule-by-rule paper enforcement is intentionally not duplicated.

---

## 3. Per-rule mapping — Reading rulebook B (platform-design)

Rulebook B is more design-spec than rule-spec. It largely overlaps rulebook A. Section-by-section summary:

| Section | Topic | Status | Notes |
|---|---|---|---|
| 1 | Reading exam overview | ✅ | Same as rulebook A §01. |
| 2 | Real-room workflow | ⚪ | Operational. |
| 3 | Part A simulator features (15-min lock, A–D layout, auto-submit, no return, exact-answer, accepted-variants, wrong-answer diagnosis) | ✅ | All 7 sub-features present. Wrong-answer diagnosis = distractor categorisation [`ReadingDistractorCategory`](../../oetwebapp/backend/src/OetLearner.Api/Domain/ReadingEntities.cs#L98-L105). |
| 4 | Part B trainer features (1 extract per screen, A/B/C, purpose/gist/detail tags, distractor explanation, text-type filters, speed target) | 🟡 | All present except: explicit "speed target 6–8 min" and "text-type filters" not surfaced in learner UI (data exists in `TopicTag`). |
| 5 | Part C deep-reading engine (long-text reader, paragraph-tag, highlighting, evidence-based explanation, 5 distractor categories, time analytics) | ✅ | All 6 features present including 5 distractor categories. |
| 6 | Scoring model (raw, estimated band, warning, part breakdown, skill breakdown, timing breakdown) | 🟡 | All present; verify literal disclaimer string "This is an estimate, not an official OET conversion." See P2-5. |
| 7 | Rules platform must teach (10 rules) | 🟡 | 7/10 enforced (auto-lock, no negative marking, unanswered warning, spelling marking, answer boxes, B/C bubbles, no return). 3/10 partial: confidentiality/integrity policy not surfaced in mock mode. See P2-4. |
| 8 | Six platform modules | ✅ | All six present (test simulator, practice mode, admin builder, analytics, learning-path, compliance). |
| 9 | DB schema (15 tables suggested) | ✅ | Equivalent EF Core entities present. Naming differs but structure aligns. |
| 10 | Two-mode design (Learning vs Exam) | ✅ | `ReadingAttemptMode` enum. |
| 11 | Content quantity targets | ⚪ | Out of code scope. |
| 12 | Business advice | ⚪ | Out of scope. |

---

## 4. Per-rule mapping — Writing rulebook (R01.x – R16.x)

**Note:** Even where the rule has an "implemented" detector, the implementation does NOT actually reach learner submissions until P0-1 is fixed. "✅" below means "the detector / data model exists in code"; effective compliance is gated on P0-1.

### Section 01 — Letter types & identification

| Rule | Status | Location |
|---|---|---|
| R01.1 — 6 letter types | ✅ (backend canonical) / 🟡 (frontend display labels diverge) | `WritingContentStructure.cs#L17-L60` vs `lib/writing/workflow.ts#L12-L20`. See P1-3. |
| R01.2 — No "urgent" → routine | ✅ | `lib/rulebook/context.ts#L18-L42` heuristic + tests. |
| R01.3 — "assessment / management from specialist" without urgent → routine | ✅ | Same. |
| R01.4 — Urgent identifiers list | ✅ | Heuristic checks "urgent", "ASAP", "admit", "acutely unwell", "immediate". |
| R01.5 **CRITICAL** — Suspected cancer always urgent | ✅ | Heuristic includes "suspected cancer". Verified in `context.test.ts`. |
| R01.6 — Discharge identifiers | ✅ | Heuristic. |
| R01.7 — Non-medical pro list (nurse = medical) | ✅ | Per-profession allow-list. |
| R01.8 — Don't confuse cancer screening with urgent suspected cancer | 🟡 | Heuristic is keyword-only; may produce false positives. Consider negative-context rules. |

### Section 02 — Exam structure & case notes

| R02.1 — 45 min total | ✅ | `WRITING_READING_WINDOW_SECONDS=300 + WRITING_WINDOW_SECONDS=2400`. |
| R02.2 **CRITICAL** — First 5 min reading-only, no writing | ✅ | Editor disabled in reading phase: `app/writing/player/page.tsx#L72-L77`. |
| R02.3 — 40 min writing | ✅ | Same. |
| R02.4 — Paper 1 case notes / Paper 2 answer | ✅ | `WritingCaseNotesPanel` + `WritingEditor`. |
| R02.5 — Task line bottom of case notes | ⚪ | Authoring convention. |
| R02.6 — Diagnosis fallback in plan | ⚪ | Authoring convention. |
| R02.7 — Today's date | ⚪ | Authoring convention. |
| R02.8 — Diagnosis types treated identically | ✅ | No differential logic. |

### Section 03 — Content & data selection

| R03.1 — Relevant / Irrelevant / Semi-relevant | 🟡 | Detector exists in TS engine; not enforced at grading (P0-1). |
| R03.2 **CRITICAL** — Marks for inclusion AND exclusion | ❌ effective | Requires AI grader (P0-1). |
| R03.3 — Recipient specialty drives relevance | ❌ effective | AI-only judgment. |
| R03.4 **CRITICAL** — Smoking/drinking always include except OT | 🟡 | TS rule `social_history_smoking_drinking` exists; not enforced at submission. |
| R03.5 — Divorce/living/children if relevant | ⚪ | AI-only. |
| R03.6 **CRITICAL** — Allergy always for atopic | 🟡 | TS rule `allergy_atopic_inclusion` exists; not enforced at submission. |
| R03.7 — Family history if related | ⚪ | AI-only. |
| R03.8 — 180–200 words body | ✅ (advisory) | Word counter `app/writing/player/page.tsx#L97-L165`. Note: word count is shown to learner but rulebook spec was "no word-count UI" per `WritingEditor` test. **Inconsistency** — verify intent. |
| R03.9 — Max 4 body paragraphs | 🟡 | TS engine has paragraph rules; no hard cap. |

### Section 04 — Page layout & structure

| R04.1 — Strict layout order | ✅ | Detector `letter_structure_order` `lib/rulebook/writing-rules.ts#L210-L230`. |
| R04.2 **CRITICAL** — No blank line between Salutation and Re: | ✅ | Detector `salutation_re_adjacency` `lib/rulebook/writing-rules.ts#L237-L246`. |
| R04.3 — CBT: 2 Enters = 1 blank line | ⚪ | Editor convention. |
| R04.4 — Body paragraphs separated by exactly one blank line | 🟡 | Order rule covers; explicit "exactly one" not asserted. |

### Section 05 — Address & date

| R05.1 — Address components | ⚪ | Authoring. |
| R05.2 **CRITICAL** — No commas/periods, capitalised | ✅ | Detector `address_punctuation` `lib/rulebook/writing-rules.ts#L155-L168`. |
| R05.3 — Address copied from task | ⚪ | Cannot enforce automatically. |
| R05.4 — Date = today's visit | ⚪ | Convention. |
| R05.5 — Consistent date format | 🟡 | Detector exists but format-detection only at top-of-letter. |
| R05.6 — Never abbreviate year | ✅ | Date format check. |
| R05.7 — Leading zero for 1–9 | 🟡 | Allowed by format check; not required. |
| R05.8 **CRITICAL** — No "Date:" prefix | ✅ | Detector `no_date_prefix` `lib/rulebook/writing-rules.ts#L171-L177`. |
| R05.9 — One blank line above and below date | ✅ | Detector `date_blank_line_sandwich` `lib/rulebook/writing-rules.ts#L180-L194`. |

### Section 06 — Salutation & Re: line

| R06.1 **CRITICAL** — "Dear Dr. [LastName]" | ✅ | Detector `salutation_last_name_only`. |
| R06.2 — Optional comma/dot | ✅ | Allowed. |
| R06.3 **CRITICAL** — No blank line between salutation and Re: | ✅ | Adjacency detector. |
| R06.4 — Special salutations | 🟡 | Heuristic supports common cases; UK "Mr" surgeon exception not modelled. |
| R06.5 — Non-medical no name → Sir/Madam | ✅ | Salutation rule. |
| R06.6 — Re: format | ✅ | Detector `re_line_age_dob_format`. |
| R06.7 **CRITICAL** — Re: full name; body last+title only | ✅ | Detectors `re_line_age_dob_format` + `the_patient_in_body`. |
| R06.8 — DOB / age handling matrix | 🟡 | Format check; matrix not exhaustively asserted. |
| R06.9 — Three age forms | ⚪ | Style guide. |

### Section 08 — Body paragraphs & visit structure

| R08.1 **CRITICAL** — Min 2 body paragraphs | 🟡 | Layout-order rule covers structure; no explicit min-paragraphs assertion. |
| R08.2 — Today its own paragraph | ⚪ | AI-only. |
| R08.3 **CRITICAL** — Combine previous visits | ⚪ | AI-only. |
| R08.4 — Subjective + Objective + Management structure | ⚪ | AI-only. |
| R08.5 **CRITICAL** — Urgent: today first | 🟡 | TS rule `urgent_body_starts_today`; effective only after P0-1. |
| R08.6 — Elapsed time over exact dates | ⚪ | AI-only. |
| R08.7 **CRITICAL** — "on the following visit", never "next visit" | ✅ | Detector forbids "next visit". |
| R08.8 **CRITICAL** — Never write today's date in body | ✅ | Detector `body_no_todays_date` `lib/rulebook/writing-rules.ts#L289-L295`. |
| R08.9 — Never "yesterday" | ✅ | Detector forbids "yesterday". |
| R08.10 — "Over the last X" present perfect | ✅ | Detector `over_the_last_present_perfect`. |
| R08.11 — Duration as adjective hyphenate | 🟡 | Partial; word-level rule. |
| R08.12 — Persisted/improved/deteriorated phrasing | ⚪ | Style. |
| R08.13 — Counselled vs instructed | ⚪ | Style. |
| R08.14 **CRITICAL** — Last name + title; never "the patient" | ✅ | Detector `the_patient_in_body`. |
| R08.15 — Name vs pronoun matrix | 🟡 | Partial; first-mention check only. |

### Section 09 — Closure

| R09.1 — Standard phrases | ⚪ | Style. |
| R09.2 **CRITICAL** — Urgent closure must include "at your earliest convenience" | ✅ | Detector `urgent_closure_phrase`. |
| R09.3 — "urgent" once in intro | 🟡 | TS rule exists. |
| R09.4 **CRITICAL** — Follow-up date in closure | ⚪ | AI-only. |
| R09.5 **CRITICAL** — Enclosed results phrasing | ⚪ | AI-only. |
| R09.6 — Med list names only, no doses | ⚪ | AI-only. |
| R09.7 — Patient-initiated phrasing | ⚪ | AI-only. |
| R09.8 — Consent phrasing | ⚪ | AI-only. |
| R09.9 — Blank line before "Yours sincerely" | ✅ | Layout-order rule. |

### Section 10 — Tenses

| R10.1 — Intro present simple/perfect | ✅ | Allowed. |
| R10.2 **CRITICAL** — Visits past simple | ✅ | Detector `surgery_past_simple` and family. |
| R10.3 — Present perfect for ongoing symptom | ⚪ | AI-only. |
| R10.4 — Ongoing facts present simple | ⚪ | AI-only. |
| R10.5 **CRITICAL** — "since" present perfect | ✅ | Detector `since_present_perfect`. |
| R10.6 **CRITICAL** — "for X" present perfect | ✅ | Detector `for_duration_present_perfect`. |
| R10.7 — Smoking/drinking with duration → present perfect continuous | 🟡 | Partial. |
| R10.8 **CRITICAL** — Resolved/surgery past simple | ✅ | Detector `surgery_past_simple`. |
| R10.9 — "Over the last X" | ✅ | Same as R08.10. |
| R10.10 **CRITICAL** — "X ago" past simple | ✅ | Detector `ago_past_simple`. |
| R10.11 — Records reveal/show present | ⚪ | AI-only. |
| R10.12 — Avoid past perfect | ⚪ | AI-only. |
| R10.13 — Reported speech ING/noun | ⚪ | AI-only. |
| R10.14 — "next visit" → "on the following visit" | ✅ | See R08.7. |

### Section 11 — Medications & investigations

| R11.1 **CRITICAL** — Translate Latin abbreviations | ✅ | Detector `latin_abbreviations` `lib/rulebook/writing-rules.ts#L1041-L1062`. |
| R11.2 — Single med format | ⚪ | Style. |
| R11.3 — Multiple meds punctuation | ⚪ | Style. |
| R11.4 — Trade vs generic capitalisation | 🟡 | Lowercase-conditions rule covers conditions; trade-name rule not explicit. |
| R11.5 — Routes ("oral" not "PO") | ⚪ | Style. |
| R11.6 — "elevated/reduced" phrasing | ⚪ | Style. |
| R11.7 — Article rules | ⚪ | Style. |
| R11.8 **CRITICAL** — Numerical values with units | ⚪ | AI-only. |
| R11.9 — "an MRI" / "an X-ray" | ⚪ | Style. |
| R11.10 — Numerals with units | ⚪ | Style. |
| R11.11 — Clinic scheduling fractions | ⚪ | Style. |

### Section 12 — Grammar, vocabulary & linkers

| R12.1 **CRITICAL** — No contractions | ✅ | Detector `no_contractions`. |
| R12.2 **CRITICAL** — Never "patient" in body | ✅ | Detector `the_patient_in_body`. |
| R12.3 — Never "smoker/drinker" labels | ⚪ | Style. |
| R12.4 — "fatigue" / "poor" replacements | ⚪ | Style. |
| R12.5 **CRITICAL** — Conditions lowercase | ✅ | Detector `conditions_lowercase`. |
| R12.6 — Eponymous diseases capital | 🟡 | Lowercase rule has allowlist; verify Crohn's/Parkinson's etc. |
| R12.7 — Trade meds capital, bacteria, abbreviations | 🟡 | Partial. |
| R12.8 — Numbers 1–9 words; 10+ numerals | ⚪ | Style. |
| R12.9 **CRITICAL** — "however" = ; before, , after | ✅ | Detector `linker_punctuation_however`. |
| R12.10 — "therefore/thus" same | ✅ | Detector covers. |
| R12.11 — "in addition" same | ✅ | Detector covers. |
| R12.12 — "in addition to / together with" no preceding punct | 🟡 | Partial. |
| R12.13 — Proximity rule | ⚪ | AI-only. |
| R12.14 — Reverse rule (along with) | ⚪ | AI-only. |
| R12.15 — Despite + noun, although + clause | ⚪ | AI-only. |
| R12.16 — "as" comma before | ⚪ | AI-only. |
| R12.17 — "for which" comma before | ⚪ | AI-only. |
| R12.18 — Coordinating adjectives comma | ⚪ | AI-only. |
| R12.19 — Sentence length 15–25 | ✅ | Detector `sentence_length`. |
| R12.20 — Max 1–2 linkers per paragraph | ✅ | Detector `linker_density`. |
| R12.21 — Apposite between commas; "gentleman/lady" | ⚪ | Style. |
| R12.22 — Never label "anxious / non-compliant" | ⚪ | AI-only. |

### Section 13 — Urgent referral

| R13.1 — 3 elements change | ✅ | Combination of urgent_intro + urgent_closure + urgent_body_starts_today. |
| R13.2 **CRITICAL** — Intro "urgent" | ✅ | Detector. |
| R13.3 **CRITICAL** — Closure phrase + spelling | ✅ | Detector. |
| R13.4 **CRITICAL** — Body starts with today | 🟡 | TS rule exists. |
| R13.5 — Structure: today / background / active conditions+meds | ⚪ | AI-only. |
| R13.6 **CRITICAL** — Active conditions with meds always | ⚪ | AI-only. |
| R13.7 — Resolved no-meds → omit | ⚪ | AI-only. |
| R13.8 — Penicillin allergy if abx involved | ⚪ | AI-only. |
| R13.9 — No PMH → omit paragraph | ⚪ | AI-only. |
| R13.10 **CRITICAL** — "ASAP" → "at your earliest convenience" | ✅ | Detector `no_asap_in_letter`. |
| R13.11 — A&E / ED capitalised | ⚪ | Style. |

### Section 14 — Discharge letter

| R14.1 — Hospital → GP | ⚪ | Convention. |
| R14.2 **CRITICAL** — Specific intro template | 🟡 | Heuristic detects discharge type; intro template not regex-enforced. |
| R14.3 **CRITICAL** — No patient identity in intro | ⚪ | AI-only. |
| R14.4 **CRITICAL** — No FH/SH/smoking/drinking/PMH/occupation | ⚪ | AI-only (suppression of inclusion rules for discharge). |
| R14.5 — Allergy if treatment-relevant | ⚪ | AI-only. |
| R14.6 **CRITICAL** — "admitted" not "presented" | ⚪ | Style detector candidate — not implemented. |
| R14.7 **CRITICAL** — Discharge plan paragraph required | ⚪ | AI-only. |
| R14.8 — Med format | ⚪ | Style. |
| R14.9 **CRITICAL** — All investigations with values | ⚪ | AI-only. |
| R14.10 **CRITICAL** — Follow-up dates | ⚪ | AI-only. |
| R14.11 — Consent statement | ⚪ | AI-only. |
| R14.12 **CRITICAL** — "treatment FOR" not "FROM" | ✅ | Detector `treatment_for_not_from`. |
| R14.13 — Past tense throughout | ✅ | Tense detectors. |
| R14.14 — Enclosed results phrasing | ⚪ | AI-only. |

### Section 15 — Referral to non-medical

| R15.1 — Definition | ✅ | Allow-list. |
| R15.2 **CRITICAL** — No medical jargon (translate) | ⚪ | AI-only. |
| R15.3 — Salutation rules | ✅ | Salutation detector. |
| R15.4 — Include / exclude lists | ⚪ | AI-only. |
| R15.5 — Exclude irrelevant medical | ⚪ | AI-only. |
| R15.6 — Family dentist treated as specialist | ⚪ | AI-only. |
| R15.7 **CRITICAL** — Imperative items = GP, request line = specialist | ⚪ | AI-only. |
| R15.8 — Mental-health correlation, review date, consent | ⚪ | AI-only. |

### Section 16 — Assessment criteria

| R16.1 — 6 criteria, max scores (Purpose 3; others 7) | ✅ schema / 🟡 effective | `rulebooks/writing/common/assessment-criteria.json` defines; AI prompt emits 6-criterion JSON. **However**, `BackgroundJobProcessor` stub assigns "4-5/6", "4/7" etc. — uses /6 for purpose+content+conciseness+language despite spec saying Purpose=/3 and others=/7. **Bug.** |
| R16.2 **CRITICAL** — Only Purpose is /3 | ❌ | Stub uses /6 for purpose. Bug. |
| R16.3 **CRITICAL** — Content + Conciseness most weighted | ⚪ | Documentation. |
| R16.4 — Genre/Style significant | ⚪ | Documentation. |
| R16.5 **CRITICAL** — Language doesn't need to be perfect | ⚪ | Documentation. |
| R16.6 — Band 6+; Purpose ≥ 2/3 | ⚪ | Pass-mark logic. |
| R16.7 **CRITICAL** — "Could this be sent in a real clinical setting?" examiner standard | ⚪ | Documentation. |
| R16.8 — Holistic | ⚪ | Documentation. |

---

## 5. Live production verification (read-only)

- **Login:** ✅ Successful as `mindreader420123@gmail.com` at https://app.oetwithdrhesham.co.uk/.
- **Writing hub** (`/writing`): loads. Copy: *"Pick the right letter task, focus on the criteria that matter, and request tutor review when it counts."* and *"Review credits: 6 available"*. This wording is consistent with the stub-grader design where AI is positioned as a placeholder and tutor review is the actual marking path.
- **Did NOT submit a test letter** in the audit (would have generated a real evaluation row + consumed analytics events). Recommended as a follow-up: submit one fixture letter and confirm the evaluation row matches the hard-coded stub values verbatim.
- **Did NOT exercise admin destructive surfaces** despite admin authorisation.

---

## 6. Recommended remediation plan (effort estimates)

| Phase | Work | Risk |
|---|---|---|
| Phase 1 (this branch) | Audit artifact (this file). Reconcile letter-type enum (P1-3). Add per-rule tests for the existing TS detectors. Surface the integrity reminder in mock mode (P2-4). Verify scoring disclaimer copy (P2-5). | Low. |
| Phase 2 | Wire `WritingRuleEngine` deterministic findings into `BackgroundJobProcessor.CompleteWritingEvaluationAsync` (zero AI cost). Persist as JSON in existing `FeedbackItemsJson`. Replace stub criterion bands with deterministic rule-derived bands (e.g. critical-rule violations cap a criterion). | Medium. Affects every learner submission. |
| Phase 3 | Wire `AiGatewayService` `writing.grade` properly: route call, JSON parsing, quota gating, retry on partial JSON, two-call splitting if MAJOR rule count > 60. Add fixture-driven e2e tests (P2-3). | High. Real AI cost; needs cost-control config + load test. |
| Phase 4 | Add `WritingRuleViolation` table (P2-2). Backfill from existing `FeedbackItemsJson`. New analytics endpoints. | Medium. Migration. |

---

## 7. What I did NOT do (honesty)

- I did NOT push the branch to the remote. Run `git push -u origin audit/rulebook-compliance-2026-05-10` after review.
- I did NOT modify any production code in this PR beyond writing this audit. Auto-fixes were deferred because the headline gap (P0-1) is too consequential to fix without explicit human design approval.
- I did NOT submit a test letter on production to dynamically confirm the stub values render — this would have created database rows. The static evidence is conclusive.
- I did NOT exhaustively audit all 13 profession-specific writing rulebooks (`rulebooks/writing/*/rulebook.v1.json`). They share the schema; spot checks of medicine and veterinary were performed.
- I did NOT verify accessibility, internationalisation, or security beyond what the rulebooks demand.

---

*End of audit.*

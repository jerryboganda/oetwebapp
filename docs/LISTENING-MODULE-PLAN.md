# Listening Module — Master Implementation Plan

This plan audits the existing Listening implementation against the OET
Listening Module spec (the "Listening intelligence system" requirements),
records the shipped workflow, and keeps the phase history readable.

> Companion roadmap (legacy notes): `/memories/repo/listening-authoring-roadmap.md`

## Audit — what already ships

| Capability | Status | Source |
| ---------- | ------ | ------ |
| 5-section forward-only player (A1, A2, B, C1, C2) with review windows | ✅ | `app/listening/player/[id]/`, `lib/listening-sections.ts` |
| Practice, exam, OET@Home, and paper-simulation modes | ✅ | `ListeningLearnerService.GetSessionAsync`, `app/listening/player/[id]/page.tsx` |
| Admin authoring of 42-item map (12+12 / 6 / 6+6) | ✅ Phase 1 | `Services/Listening/ListeningAuthoringService.cs`, `components/domain/ListeningStructureEditor.tsx` |
| Publish-gate validator (canonical shape + Part B 3-option MCQ) | ✅ | `Services/Listening/ListeningStructureService.cs` |
| Subscription / entitlement gating | ✅ Phase 3 | `IContentEntitlementService` |
| Relational authored-paper runtime + server-grading + raw/scaled (anchored 30/42 ≡ 350/500) | ✅ | `ListeningLearnerService`, `ListeningAttempt`, `ListeningAnswer`, `OetScoring` |
| Drills surface (post-attempt error clusters → drill) | ✅ | `ListeningLearnerService.BuildDrill`, `app/listening/drills/[id]` |
| Transcript-backed review after submit with option analysis and evidence jumps | ✅ | `app/listening/review/`, `GetReviewAsync` |
| Hub: papers, resume, recent results, drill groups, part collections, mock sets | ✅ | `app/listening/page.tsx`, `ListeningLearnerService.GetHomeAsync` |
| Diagnostic surface (Listening) | ✅ | `app/diagnostic/listening/` |
| Result card + scoring (`350` anchor, grade letter) | ✅ | `OetStatementOfResultsCard`, `OetScoring.OetGradeLetterFromScaled` |
| Admin extract metadata authoring + relational backfill | ✅ | `ListeningExtractMetadataEditor`, `ListeningBackfillService` |
| Student/admin analytics + pathway/curriculum include relational attempts | ✅ | `ListeningAnalyticsService`, `ListeningPathwayService`, `ListeningCurriculumService` |

## Status vs spec

Mapped to spec sections (1-16). Engineering workflow gaps identified in the audit are closed; remaining items are content production, taxonomy tuning, or future training-depth enhancements.

1. **Module overview** — 42-item canonical shape, three parts, generic profession. ✅
2. **Real-time presentation** — practice, exam, OET@Home, and paper-simulation policies. ✅
3. **Part A consultation extracts** — A1/A2 sections, answer variants, Part A error classification, evidence timestamps, and transcript-backed review. ✅
4. **Part B workplace extracts** — 3-option MCQ authoring, per-option distractor category/why-wrong metadata, review surfacing, and analytics heat. ✅
5. **Part C long audio** — C1/C2 sections, speaker-attitude tagging, option distractor tagging, and evidence jumps. ✅
6. **Scoring & marking** — raw, scaled, grade, pass anchor. ✅
7. **Test rules lesson** — `/listening/test-rules` plus player pre-flight rules. ✅
8. **Paper / Computer / OET@Home modes** — computer/exam mode, OET@Home full-screen integrity telemetry, and printable paper-mode booklet/answer sheet. ✅
9. **Platform modules** — hub, drills, mocks, review, pathway, and curriculum. ✅
10. **Admin data-entry system** — 42-item editor, extract metadata editor, validation, AI extraction, and JSON-to-relational backfill. ✅
11. **Student analytics dashboard** — per-part breakdown, weaknesses, and action plan. ✅
12. **Teacher dashboard (Listening)** — class averages, hardest questions, distractor heat, and misspellings. ✅
13. **Course pathway** — Listening pathway snapshot mirrors Reading and includes relational attempts. ✅
14. **Content quantity targets** — content/ops target, not a code blocker. 🟡
15. **Technical design / DB structure** — relational `ListeningPart`/`ListeningExtract`/`ListeningQuestion`/`ListeningAttempt`/`ListeningAnswer` runtime with JSON fallback for non-backfilled content. ✅
16. **"Build a Listening intelligence system"** — shipped as an integrated authored-paper workflow with review, analytics, progression, and delivery-mode controls. ✅

## Phasing

> Each phase is a single shippable commit (or small commit cluster), green build,
> green tests, deployable independently.

> **Implementation status (this branch):** Phases 1-10 all shipped, including
> the final business-workflow close-out. Authored `ContentPaper` Listening now
> prefers relational `ListeningPart` / `ListeningExtract` / `ListeningQuestion`
> source rows and writes learner work into `ListeningAttempt` / `ListeningAnswer`,
> with JSON fallback retained for legacy/unbackfilled content. Analytics,
> pathway, curriculum, home resume/recent-results, review, and integrity events
> all include relational attempts. Admins can author extract metadata in the UI,
> save it through `GET/PUT /v1/admin/papers/{paperId}/listening/extracts`, and
> project JSON into relational rows through per-paper or bulk backfill. The
> learner player exposes OET@Home full-screen integrity telemetry and a real
> paper-mode printable booklet/answer sheet.
> Phase 8 ships the `IListeningExtractionAi` seam + `StubListeningExtractionAi`
> + admin "Propose with AI" path **and** the grounded-gateway impl
> (`GroundedListeningExtractionAi`) wired through `IAiGatewayService` with
> `RuleKind.Listening` + `AiTaskMode.GenerateListeningStructure` +
> `AiFeatureCodes.AdminListeningDraft`. Listening rulebooks shipped for
> medicine + nursing; other professions safely fall back to the deterministic
> stub via the gateway's `RulebookNotFoundException` catch.

### Phase 1 — Listening Pathway snapshot **(shipped)**

Mirror the just-shipped Reading pathway. Smallest meaningful win, immediately
visible in the Listening hub, foundation for the analytics surface in Phase 6.

- Backend: `Services/Listening/ListeningPathwayService.cs` returning
  `ListeningPathwaySnapshot` (stage, headline, bestScaled, attempts counts,
  next-action, milestones).
- Endpoint: `GET /v1/listening-papers/me/pathway`.
- DI registration in `Program.cs`.
- Client: `getListeningPathway()` + types in `lib/listening-authoring-api.ts`.
- UI: pathway card mounted at the top of `app/listening/page.tsx`.

Stage decision:

- `not_started` — 0 completed Listening attempts.
- `diagnostic` — exactly 1 completed attempt with no scaled score.
- `drilling` — best scaled `< 300`.
- `mini_tests` — best scaled `[300, 350)`.
- `mock_ready` — best scaled `>= 350` + 0 listening mocks submitted.
- `exam_ready` — `>= 1` listening (or full) mock submitted.

### Phase 2 — Relational Listening schema (entities + migration) **(shipped — entities + DbSets + migration `AddListeningModuleEntities` + `ListeningAttemptExpireWorker` + `ListeningBackfillService` projecting JSON→entity at `POST /v1/admin/papers/{paperId}/listening/backfill` and bulk `POST /v1/admin/listening/backfill`; authored papers now use relational source/attempt/answer rows at runtime, with JSON fallback for legacy and unbackfilled content)**

Lift `ContentPaper.ExtractedTextJson["listeningQuestions"]` into:

- `ListeningPart` (5 codes: A1, A2, B, C1, C2)
- `ListeningExtract` (consultation / workplace / presentation, with `accentCode`, `speakersJson`, `audioStartMs`/`audioEndMs`, `replayInLearningOnly` bool)
- `ListeningQuestion` (with enum `SkillTag`, `transcriptEvidenceStartMs`/`EndMs`, `transcriptEvidenceText`)
- `ListeningQuestionOption` (Part B/C: per-option `distractorCategory` + `whyWrongMarkdown`)
- `ListeningAttempt` / `ListeningAnswer` (mirror of `ReadingAttempt` shape with `Mode` enum)
- `ListeningPolicy` (singleton + per-user override)

Migration: write existing JSON rows into entities; keep JSON blob as legacy
fallback for one release. Ship `ListeningAttemptExpireWorker`.

### Phase 3 — Learner DTO projection + Part A error categorisation **(shipped)**

- Endpoint-layer projection that strips `correctAnswer` / `acceptedAnswers` /
  `explanation` from learner reads (matches the Reading projection rule).
- Server-side Part A grader categorises each wrong answer into:
  `spelling | grammar_number | paraphrase | wrong_section | extra_info | empty`.
- Surface error types in the post-submit review and feed them into drill
  recommendations (existing `BuildDrill(errorType, …)` already accepts a code).

### Phase 4 — Part C distractor categories + Speaker-attitude tags **(shipped — backend + admin editor UI)**

- Add per-option `distractorCategory` enum
  (`too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword`)
  in admin authoring + post-submit review.
- Add `speakerAttitude` enum on Part C questions
  (`concerned | optimistic | doubtful | critical | neutral | other`).

### Phase 5 — Time-coded transcripts + audio metadata **(shipped — per-question evidence ms + paper-level `listeningTranscriptSegments`, plus first-class `listeningExtracts` accent + speakers + audio window via `GET/PUT /v1/admin/papers/{paperId}/listening/extracts` and surfaced on the learner session DTO)**

- First-class authored fields: `accentCode`, `speakersJson`, sentence-level
  `transcriptSegmentsJson` (start/end ms + speakerId + text).
- "Listening loop" UI in learning mode that jumps to the answer-evidence segment.
- Replay-permission flag honored by player (already enforced in exam mode;
  formalise the policy at the entity layer).

### Phase 6 — Student analytics dashboard **(shipped — `/listening/analytics`)**

Dashboard widget on `app/listening/page.tsx` and Profile that surfaces:

- raw / scaled / grade
- per-part breakdown (A 16/24, B 5/6, C 8/12)
- main weakness, timing issues, accuracy issues
- one-line action plan
- the spec's three questions: *What did I miss? Why did I miss it? What should
  I practise next?*

### Phase 7 — Teacher / Admin analytics **(shipped — `/admin/analytics/listening`)**

Admin "Listening Analytics" page mirroring `getReadingAdminAnalytics`:

- class average by part
- hardest questions
- common wrong-option distribution
- common spelling mistakes
- audio-difficulty rating (per-extract accuracy)
- student readiness (red / amber / green)

### Phase 8 — AI authoring assist **(shipped — grounded gateway: `RuleKind.Listening`, `AiTaskMode.GenerateListeningStructure`, `AiFeatureCodes.AdminListeningDraft`, listening rulebooks for medicine + nursing, `GroundedListeningExtractionAi`, conditional DI on `AI__BaseUrl`/`AI__ApiKey` config)**

Admin uploads Question-Paper PDF + Audio Script PDF + Answer Key PDF →
AI extraction proposes 42 items via `IAiGatewayService` with a strict JSON
schema for `ListeningAuthoredQuestion`. Mirrors Reading Phase 6.

### Phase 9 — Test-rules lesson + paper/home modes **(shipped — `/listening/test-rules`; `paper` + `home` modes wired via `NormalizeMode` + `modePolicy` + player UI: full-screen integrity telemetry for `home`, printable booklet/answer sheet for `paper`; extracted active-player components + Storybook; admin per-question deep dive; live learner-route Playwright smoke contracts)**

- Pre-flight "Listening Test Rules" lesson surface (one-play, no negative
  marking, MCQ vs gap-fill, exam integrity).
- Mode toggle for paper / computer / OET@Home (mostly UI affordances:
  printable booklet, full-screen kiosk-style mode).
- Active player chrome extracted to reusable `components/domain/listening/player/*`
  components with focused tests and opt-in Storybook coverage.
- Admin hardest-question analytics links into the routed per-question deep-dive
  panel for accuracy, distractor heat, and misspelling signal.
- Strict exam and paper-mode Playwright specs are live route smoke contracts;
  full multi-part paper free-navigation E2E still needs a seeded multi-part
  paper fixture.

### Phase 10 — Course pathway content (12-stage curriculum) **(shipped — endpoint + `/listening/curriculum`)**

Map the spec's 12-stage curriculum to learning paths, drill catalogue
entries, and pathway milestones.

## Out-of-scope (content & ops)

- Original healthcare audio production (50+ workplace, 100+ consultation,
  100+ Part C, 50+ accent drills) — this is a content-team workstream, not an
  engineering deliverable. Engineering provides the upload + structure tools.

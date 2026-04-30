# Listening Module — Master Implementation Plan

This plan audits the existing Listening implementation against the OET
Listening Module spec (the "Listening intelligence system" requirements),
identifies the remaining gaps, and sequences them into shippable phases.

> Companion roadmap (legacy notes): `/memories/repo/listening-authoring-roadmap.md`

## Audit — what already ships

| Capability | Status | Source |
| ---------- | ------ | ------ |
| 5-section forward-only player (A1, A2, B, C1, C2) with review windows | ✅ | `app/listening/player/[id]/`, `lib/listening-sections.ts` |
| One-play exam mode + practice mode | ✅ | `ListeningLearnerService.GetSessionAsync` |
| Admin authoring of 42-item map (12+12 / 6 / 6+6) | ✅ Phase 1 | `Services/Listening/ListeningAuthoringService.cs`, `components/domain/ListeningStructureEditor.tsx` |
| Publish-gate validator (canonical shape + Part B 3-option MCQ) | ✅ | `Services/Listening/ListeningStructureService.cs` |
| Subscription / entitlement gating | ✅ Phase 3 | `IContentEntitlementService` |
| Server-grading + Evaluation + raw/scaled (anchored 30/42 ≡ 350/500) | ✅ | `ListeningLearnerService.SubmitAsync`, `OetScoring` |
| Drills surface (post-attempt error clusters → drill) | ✅ | `ListeningLearnerService.BuildDrill`, `app/listening/drills/[id]` |
| Transcript-backed review after submit | ✅ | `app/listening/review/`, `GetReviewAsync` |
| Hub: papers, resume, recent results, drill groups, part collections, mock sets | ✅ | `app/listening/page.tsx`, `ListeningLearnerService.GetHomeAsync` |
| Diagnostic surface (Listening) | ✅ | `app/diagnostic/listening/` |
| Result card + scoring (`350` anchor, grade letter) | ✅ | `OetStatementOfResultsCard`, `OetScoring.OetGradeLetterFromScaled` |

## Gaps vs spec

Mapped to spec sections (1–16). `[exists]` = already shipped, `[gap]` = missing.

1. **Module overview** — `[exists]` 42-item canonical shape, three parts, generic profession. ✅
2. **Real-time presentation** — `[exists]` exam vs learning split. ✅
3. **Part A consultation extracts** — mostly `[exists]`. Specific gaps:
   - `[gap]` *Prediction-before-listening drill* mode (gap clue → expected answer-type tag).
   - `[gap]` *Error-type categorisation* on Part A wrong answers (spelling vs grammar/number vs paraphrase vs wrong-section vs extra-info). Today only "incorrect" is recorded.
   - `[gap]` UK/US spelling variant tooling at the *answer key* level (today single-string + acceptedAnswers list — fine, but no automated generator).
4. **Part B workplace extracts** — mostly `[exists]`. Gaps:
   - `[gap]` Skill-tag enum at authoring time (warning / purpose / opinion / detail / gist / cause). Today free-text `skillTag` string.
   - `[gap]` Per-distractor "why wrong" explanation surfaced post-submit (today only one `explanation` per Q).
5. **Part C long audio** — mostly `[exists]`. Gaps:
   - `[gap]` 90-second preview timer formalised (player has section preview; verify exact 90 s for C1 + C2).
   - `[gap]` Speaker-attitude tag enum + distractor-category enum on each option (too-strong / too-weak / wrong-speaker / opposite-meaning).
6. **Scoring & marking** — `[exists]` raw, scaled, grade, pass anchor. ✅
7. **Test rules lesson** — `[gap]` no learner-facing "test rules" pre-flight surface.
8. **Paper / Computer / OET@Home modes** — partially `[exists]`. Today only practice + exam. `[gap]` paper-simulation (printable booklet) and OET@Home full-screen mode are not separated.
9. **Platform modules** — full mock simulator `[exists]`; Part A trainer `[exists, partial]`; Part B/C trainers exist as drills `[exists, partial]`.
10. **Admin data-entry system** — `[exists]` 42-item editor. Gaps:
    - `[gap]` Audio fields beyond a single MP3: accent, speakers list, time-coded transcript (sentence-level timestamps), beep markers, replay-permission flag are not first-class authored fields.
    - `[gap]` "Transcript evidence" timestamp per question — spec calls for `evidence_timestamp + sentence`; today only free-text `transcriptExcerpt`.
11. **Student analytics dashboard** — `[gap]` no part-by-part breakdown card with weakness diagnosis ("you lost 6 marks in Part A due to spelling…"); only raw + scaled.
12. **Teacher dashboard (Listening)** — `[gap]` doesn't exist. Reading has admin analytics; Listening doesn't.
13. **Course pathway** — `[gap]` no Listening pathway snapshot (Reading just shipped one; mirror it).
14. **Content quantity targets** — `[meta]` ops/content-side, not engineering.
15. **Technical design / DB structure** — `[gap]` Phase 2 still pending: lift JSON `listeningQuestions` into relational `ListeningPart`/`ListeningExtract`/`ListeningQuestion`/`ListeningAttempt`/`ListeningAnswer`/`ListeningPolicy`. Unblocks 4-7 above (skill enums, per-option distractor explanations, time-coded transcripts, accent metadata, attempt-expire worker, learner DTO projection that strips answer keys).
16. **"Build a Listening intelligence system"** — emergent goal of all the above.

## Phasing

> Each phase is a single shippable commit (or small commit cluster), green build,
> green tests, deployable independently.

> **Implementation status (this branch):** Phases 1–10 all shipped, including
> the three close-out follow-up slices: (a) Phase 2 follow-up —
> `ListeningBackfillService` projects historical `listeningQuestions` JSON into
> relational `ListeningPart` / `ListeningExtract` / `ListeningQuestion` /
> `ListeningQuestionOption`, exposed at
> `POST /v1/admin/papers/{paperId}/listening/backfill` (per-paper) and
> `POST /v1/admin/listening/backfill` (bulk); idempotent rewrite-on-rerun.
> (b) Phase 5 tail — first-class `listeningExtracts` metadata (accent code +
> speakers + audio window) surfaced via
> `GET/PUT /v1/admin/papers/{paperId}/listening/extracts` and on the learner
> session DTO. (c) Phase 9 tail — `paper` and `home` (OET@Home) modes wired
> through `NormalizeMode` + `modePolicy` + player UI: integrity-lock banner for
> `home`, printable-booklet hint for `paper`.
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

Stage decision (no relational error bank yet):

- `not_started` — 0 completed Listening attempts.
- `diagnostic` — exactly 1 completed attempt with no scaled score.
- `drilling` — best scaled `< 300`.
- `mini_tests` — best scaled `[300, 350)`.
- `mock_ready` — best scaled `>= 350` + 0 listening mocks submitted.
- `exam_ready` — `>= 1` listening (or full) mock submitted.

### Phase 2 — Relational Listening schema (entities + migration) **(shipped — entities + DbSets + migration `AddListeningModuleEntities` + `ListeningAttemptExpireWorker` + `ListeningBackfillService` projecting JSON→entity at `POST /v1/admin/papers/{paperId}/listening/backfill` and bulk `POST /v1/admin/listening/backfill`; the JSON blob remains the runtime read source for backwards-compat, with relational rows now available for analytics + future reads)**

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

### Phase 9 — Test-rules lesson + paper/home modes **(shipped — `/listening/test-rules`; `paper` + `home` modes wired via `NormalizeMode` + `modePolicy` + player UI: integrity-lock banner for `home`, printable-booklet hint for `paper`)**

- Pre-flight "Listening Test Rules" lesson surface (one-play, no negative
  marking, MCQ vs gap-fill, exam integrity).
- Mode toggle for paper / computer / OET@Home (mostly UI affordances:
  printable booklet, full-screen kiosk-style mode).

### Phase 10 — Course pathway content (12-stage curriculum) **(shipped — endpoint + `/listening/curriculum`)**

Map the spec's 12-stage curriculum to learning paths, drill catalogue
entries, and pathway milestones.

## Out-of-scope (content & ops)

- Original healthcare audio production (50+ workplace, 100+ consultation,
  100+ Part C, 50+ accent drills) — this is a content-team workstream, not an
  engineering deliverable. Engineering provides the upload + structure tools.

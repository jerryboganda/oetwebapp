# Reading Authoring Policy & Options Reference

> **Status**: authoritative. Companion to
> [`READING-AUTHORING-PLAN.md`](READING-AUTHORING-PLAN.md),
> [`SCORING.md`](SCORING.md), [`CONTENT-UPLOAD-PLAN.md`](CONTENT-UPLOAD-PLAN.md),
> and [`AI-USAGE-POLICY.md`](AI-USAGE-POLICY.md).
>
> Every policy decision is admin-configurable with safe defaults. Code
> references options by name; admin UI surfaces them with the same vocabulary.
> Defaults are chosen to match OET paper-exam conventions; alternatives let
> the platform be re-tuned without code changes.

---

## 0. Design principles

1. **Scoring canonicalisation is non-negotiable.** Every raw→scaled conversion
   routes through `lib/scoring.ts` / `OetLearner.Api.Services.OetScoring`.
   `30/42 ≡ 350/500` for Reading. No option below weakens this.
2. **No answer leakage.** Learner endpoints never serialise correct answers
   or explanations. Enforced at the DTO layer, not by discipline.
3. **Every default is overridable by admins**, never by learners. Learners
   toggle only what the admin allows.
4. **Every attempt is auditable.** Start, autosave (counts), submit, grade,
   and expiry all produce audit events.

---

## 1. Retry / attempt policy

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AttemptsPerPaperPerUser` (global) | Max attempts allowed per learner per paper | `0` (unlimited) | any integer ≥ 1 |
| `AttemptCooldownMinutes` (global) | Min minutes between attempts on the same paper | `0` | any integer |
| `BestScoreDisplay` (global) | Which score surfaces in progress views | `best` | `latest`, `average`, `first` |
| `ShowPastAttempts` (global) | Learner can see their attempt history | `true` | `false` |
| `AllowAttemptOnArchivedPaper` (global) | Start new attempts on archived papers | `false` | `true` |

**Safe fallback**: if `AttemptsPerPaperPerUser > 0` and the user is at the
cap, start endpoint returns 429 with `errorCode: "attempt_cap_reached"`
and a suggested retry time.

---

## 2. Timer policy

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `PartATimerStrictness` (global) | How the 15-min Part A timer enforces | `hard_lock` | `soft_warn`, `disabled` |
| `PartATimerMinutes` (per plan) | Length of the Part A window | `15` | any integer 5–60 |
| `PartBCTimerMinutes` (per plan) | Shared B+C window length | `45` | any integer 15–120 |
| `ExtraTimeEntitlementPct` (per user) | Accessibility-grant extra-time bump | `0` | 0–100 |
| `GracePeriodSeconds` (global) | Tolerance window after deadline for late saves | `10` | 0–120 |
| `OnExpirySubmitPolicy` (global) | What happens when a timer expires | `auto_submit_graded` | `auto_submit_abandoned`, `keep_open_until_user_submits` |
| `ShowCountdownWarningsAt` (global) | Warning thresholds (seconds remaining) | `[300, 60, 15]` | any ordered list |

**Safe fallback**: `hard_lock` never loses answers — autosave fires
continuously, expiry grabs the last saved state; learner sees a clear
"time expired, submitted automatically" banner.

---

## 3. Question-type support + grading strategy

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `EnabledQuestionTypes` (global) | Which types authors may use | all 5 enabled | any subset |
| `ShortAnswerNormalisation` (global) | Match strategy for short-answer items | `trim_collapse_case_insensitive` | `exact`, `trim_only`, `fuzzy_levenshtein_1` |
| `ShortAnswerAcceptSynonyms` (global) | **NON-STANDARD MODE.** Respects per-question `AcceptedSynonymsJson`. Real OET Part A answers are copied word-for-word from the text; enabling this fundamentally changes the assessment. Must be clearly disclosed to learners when on. | `false` (OET-faithful) | `true` (non-standard) |
| `MatchingAllowPartialCredit` (global) | Part A matching: credit for each correct item individually | `true` | `false` (all-or-nothing) |
| `SentenceCompletionStrictness` (global) | Enforce exact phrase-bank match | `exact_from_bank` | `normalised_match`, `fuzzy_match` |
| `UnknownTypeFallbackPolicy` (global) | Grader encounters an unknown type | `skip_with_zero` | `fail_grading`, `grade_as_correct` |

**Safe fallback**: when grader hits anything unexpected, default policy
awards **zero** rather than crashing — learner's overall result still
produces, failure is logged + anomaly-flagged for admin.

---

## 4. Explanation + review policy

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `ShowExplanationsAfterSubmit` (global) | Show per-question explanations on results | `true` | `false` |
| `ShowExplanationsOnlyIfWrong` (global) | Hide explanations for questions answered correctly | `false` | `true` |
| `ShowCorrectAnswerOnReview` (global) | Reveal correct answer in review | `true` | `false` |
| `AllowResultDownload` (global) | Learner can download a PDF of their result | `true` | `false` |
| `AllowResultSharing` (global) | Learner can share result URL | `false` | `true` |

**Safe fallback**: if explanations toggle off mid-attempt, existing
attempts keep their explanation visibility fixed at-attempt-time (stored
on the `ReadingAttempt` row as `ExplanationPolicySnapshot`). Policy
changes never retroactively hide content from past results.

---

## 5. Authoring assistance (AI extraction)

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AiExtractionEnabled` (global) | Admin Mode B extraction feature available | `true` | `false` |
| `AiExtractionRequireHumanApproval` (global) | Extracted structure is always draft until admin approves | `true` | `false` — **do not change** |
| `AiExtractionMaxRetriesPerPaper` (global) | Max times an admin can re-extract on same paper | `5` | any integer |
| `AiExtractionModelOverride` (global) | Specific model for extraction (else default routing) | `null` | any allowed model ID |
| `AiExtractionStrictSchemaMode` (global) | Invalid JSON → refuse (vs best-effort repair) | `strict` | `best_effort` |

**Safe fallback**: if extraction fails validation, admin sees a diff of
what was expected vs what was returned and an option to "hand-edit from
here" — extraction never silently creates malformed structure.

---

## 6. Question-bank + assembly mode (advanced)

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `QuestionBankEnabled` (global) | Enable reusable question bank + randomised paper assembly | `false` | `true` |
| `AssemblyStrategy` (global) | How papers pull from the bank when enabled | `fixed` | `random_within_tag`, `weighted_by_difficulty` |
| `MinItemsPerSkillTag` (global) | Minimum Part-A/B/C coverage per skill tag | `{}` | any tag → count map |
| `AllowLearnerRandomisation` (global) | Learner can request a "random mock" from the bank | `false` | `true` |

**Safe fallback**: when disabled (default), papers behave as fixed
authored structures — bank columns stay null and never affect grading.

---

## 7. Accessibility + inclusivity

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `FontScaleUserControl` (global) | Learner can scale text in the player | `true` | `false` |
| `HighContrastMode` (global) | "Exam-like" high-contrast theme available | `true` | `false` |
| `ScreenReaderOptimised` (global) | Extra ARIA live regions in the player | `true` | `false` |
| `AllowPaperReadingMode` (global) | Learner can switch to "read-only" (PDF view + paper answering) and upload answers later — slower turnaround | `false` | `true` |
| `ExtraTimeApprovalWorkflow` (global) | Learners can request + admins approve extra-time entitlements | `true` | `false` |

**Safe fallback**: all defaults are accessibility-on. Turning them off
requires explicit admin action and logs an audit event.

---

## 8. Security + integrity

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `RequireFreshAuthForSubmit` (global) | Step-up MFA before a scored submit | `false` | `true` |
| `AllowMultipleConcurrentAttempts` (global) | Same user can have 2+ in-flight attempts | `false` | `true` |
| `AttemptIpPinning` (global) | Same IP/session throughout the attempt | `false` | `true`, `warn_on_change` |
| `SubmitRateLimitPerMinute` (per user) | Max submit calls per minute | `5` | 1–60 |
| `AutosaveRateLimitPerMinute` (per user) | Max autosave calls per minute | `120` | 30–300 |
| `PreventMultipleTabs` (global) | Lock the player to a single tab | `false` | `true` |

**Safe fallback**: tight-but-human defaults — most practice users are
fine, abuse is rate-limited not blocked.

---

## 9. Data retention + privacy

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `RetainAnswerRowsDays` (global) | How long individual `ReadingAnswer` rows live | `730` | 30–3650 |
| `RetainAttemptHeadersDays` (global) | How long attempt headers (summary only) live | `3650` | 365–∞ |
| `AnonymiseOnAccountDelete` (global) | Keep attempt row, null the UserId on delete | `true` | `false` (full delete cascade) |
| `ShareAnonymousAnalytics` (global) | Aggregate question-level stats surface to admins | `true` | `false` |

**Safe fallback**: default retention matches GDPR-friendly "necessary for
service + limited analytics" — admin UI shows the retention clock per
attempt so ops can see what's pending purge.

---

## 10. Status + lifecycle

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AllowPausingAttempt` (global) | Learner can pause the timer and resume | `false` | `true` (for untimed practice mode only) |
| `AutoExpireWorkerEnabled` (global) | Background sweep that expires stale InProgress attempts | `true` | `false` |
| `AutoExpireAfterMinutes` (global) | Inactivity before an attempt is auto-abandoned | `180` | 30–1440 |
| `AllowResumeAfterExpiry` (global) | Learner can resume an expired attempt (doesn't re-grade) | `false` | `true` (review mode only) |

---

## 11. Non-configurable invariants (for clarity)

These are NOT options. Changing them requires a code change and a new
major version of this document:

- `30/42 ≡ 350/500` for Reading raw→scaled. Enforced in code AND at the publish gate.
- Published Reading papers have **exactly 42 scored items** (20 + 6 + 16).
- Learner-facing question DTOs never include `CorrectAnswerJson` or `ExplanationMarkdown`.
- Every grading call routes through `OetScoring`.
- `CorrectAnswerJson` is stored as strict JSON; non-parseable values are rejected at write time.
- Grade-at-submit is idempotent: first submit wins, replays return the existing result.

---

## 12. Precedence rules

When two options conflict, precedence is:

1. **Paper-version snapshot** wins over global policy (in-flight attempts use the policy that was live when they started).
2. **Per-user override** wins over global (e.g. extra-time entitlement).
3. **Global policy** wins by default.
4. **Non-configurable invariants** (§11) override everything.

Every resolved decision is logged with its decisive rule in
`ReadingAttempt.PolicySnapshotJson` for forensic audit.

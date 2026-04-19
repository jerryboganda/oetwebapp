# Grammar Module — Configurable Policy Model

> Operational knobs that control how the Grammar module behaves in
> production. All values are sourced from `GrammarPolicyConfig`
> (backend) and surfaced through `GET /v1/admin/grammar/policy` /
> `PUT /v1/admin/grammar/policy`. The policy file never contains content
> — only rules about how the module behaves.

## Keys

| Key | Default | Description |
|---|---|---|
| `retryLimitPerExercise` | 3 | How many times a learner may re-answer a single exercise within an attempt. 0 = unlimited. |
| `attemptCooldownSeconds` | 0 | Seconds between consecutive `/attempts` on the same lesson. |
| `explanationVisibility` | `afterSubmit` | When explanations become visible. One of `afterSubmit`, `onReveal`, `never`. |
| `masteryThreshold` | 80 | Percentage required for a lesson to count as "mastered" (drives achievements and readiness). |
| `ewmaWeight` | 0.4 | EWMA weight applied to the latest attempt score when updating `MasteryScore`. |
| `aiDraftEnabled` | true | Admin AI-draft endpoint is enabled. |
| `aiDraftModel` | "" | Optional override for the AI model used for drafts. |
| `paywallFreeLessonsCap` | 5 | Max lessons a free-tier user may complete before the upgrade prompt. 0 = unlimited. |
| `reviewQueueEnabled` | true | Wrong answers create `ReviewItem` rows. |
| `writingLinkEnabled` | true | `WritingCoachService` suggestions create `GrammarRecommendation` rows. |
| `speakingLinkEnabled` | true | Speaking rubric flags create `GrammarRecommendation` rows. |
| `readinessWeight` | 0.10 | Weight of grammar mastery in the overall readiness score (0–1). |
| `xpLessonComplete` | 10 | XP awarded on any completion. |
| `xpLessonMastered` | 15 | Additional XP for ≥ `masteryThreshold`. |
| `xpTopicMastered` | 50 | Bonus for all lessons in a topic reaching mastery. |
| `retentionDaysAttempts` | 365 | `GrammarExerciseAttempt` retention. |
| `retentionDaysRecommendations` | 180 | `GrammarRecommendation` retention. |
| `diagnosticEnabled` | false | Topic-level placement diagnostic is shown. Enable in Phase 5+. |
| `offlineCacheEnabled` | true | Mobile Capacitor offline cache of published lessons. |

## Overrides per exam type

Exam-specific overrides (OET > IELTS > PTE) are merged on read:

```json
{
  "default": { ... },
  "perExam": {
    "oet": { "paywallFreeLessonsCap": 5 },
    "ielts": { "paywallFreeLessonsCap": 3 },
    "pte": { "paywallFreeLessonsCap": 3 }
  }
}
```

## Precedence

Runtime behaviour is evaluated in order:

1. Feature-flag kill switch (`grammar_lessons` flag).
2. Per-exam override.
3. Default policy.
4. Hard-coded fallback constants in `GrammarPolicy.Defaults`.

## Auditing

Any admin change to policy writes an `AuditEvent` with
`Subject="GrammarPolicy"`; a full change log is available at
`GET /v1/admin/grammar/policy/audit`.

# Speaking Module — Content Model

## Building blocks

```
RolePlayCard (learner-facing) ── 1:1 ── InterlocutorScript (tutor-only)
RolePlayCard ── M:1 ── ProfessionReference
SpeakingMockSet ── 2:1 ── RolePlayCard (RP1 + RP2)
SpeakingDrillItem ── M:M ── criterion tags
```

## Authoring flow

1. Admin opens `app/admin/content/speaking/role-play-cards/ai-draft/page.tsx`.
2. Picks profession + topic + emotion + difficulty.
3. Calls `POST /v1/admin/speaking/cards/ai-draft` (route `card.draft.v1`).
4. Reviews + edits drafts; saves as `Draft`.
5. Optional `Reviewed` step (second reviewer).
6. `Publish` button — validation blocks: ≥3 tasks, profession set, paired script, prep + roleplay timing set, originality guard passes.
7. Visible to learners with matching `ActiveProfessionId`.

## Batch generation

`SpeakingCardBatchAuthor` background job:
- Reads `SpeakingCardBatchRequest` queue.
- Generates drafts at concurrency 1 (cost control).
- Prompt caching enabled.
- Admin reviews via `?status=draft` filter on the cards list.

## Originality guard

- Levenshtein similarity between `ScenarioTitle + Background` of new draft vs every Published card.
- Threshold: 0.85 similarity → reject at save.
- Goal: stop accidental dup generation. Not a copy-detector against external sources.

## Mock set assembly

- Manual: admin picks RP1 + RP2.
- Auto-pair: `POST /v1/admin/speaking/mock-sets/auto-pair` picks two same-profession cards with complementary criteria focus (info-giving + empathy paired with info-gathering + structure, etc.).

## Drill bank

- Tagged with one or two criteria via `SpeakingDrillItem.TargetCriteriaJson`.
- Each drill has a single recording attempt → AI feedback per criterion.
- `SpeakingCoursePathwayService` recommends drills after every finished session based on the learner's weakest criteria.

## Shared resources

- Warm-up questions (`SpeakingSharedResource.Kind = WarmUpQuestions`) — seeded per profession.
- Listening samples, assessment criteria PDFs — uploaded via admin shared-resources page.

## Lifecycle (per entity)

`Draft → Reviewed (optional) → Published → Archived`. Never deleted.

## Content QA

See `contributing.md` for the rules content authors must follow.

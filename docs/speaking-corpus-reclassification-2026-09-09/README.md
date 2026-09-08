# Speaking corpus reclassification — 2026-09-09

Follow-up to the classifier repair (`fix/speaking-classifier-repair-2026-09`,
merged as `dcda3cedc`). That PR fixed the deterministic §8B classifier and
deployed provenance tracking; this pass reclassifies the existing 417 live
role-play cards against the same authority — pages 16-18 (§8A-9) of
`FINAL_SPEAKING_PLATFORM_AND_OTHER_SUBTEST_MODIFICATIONS-2 (1).pdf` — since
the deployed classifier fix only touches new/edited cards going forward, not
the historical backlog.

## Method

1. **Before-state export** — `before-state.json`: a full read-only export of
   all 417 `RolePlayCards` rows from production, taken immediately before this
   migration, for exact recovery if needed.
2. **Content-only, blind inputs** — the classifiable fields only (scenario
   title, setting, background, tasks, clinical topic, patient emotion/name,
   communication goal), split into 12 chunks by profession (medicine/nursing/
   pharmacy split further into ~45-card batches; the remaining nine
   professions batched together). Deliberately **stripped of the existing
   `PrimaryCategory`/`CategorySource`** so reviewers judge fresh from content,
   not anchor on a (possibly wrong) legacy value.
3. **Two independent reviews per chunk** — two separate agent calls per
   chunk, each given the verbatim §8A/8B/8C rules and each card's content,
   walking the Q1-Q6 priority ladder independently.
4. **Arbitration on disagreement** — where the two reviewers' primary
   category differed, a third agent re-read the specific card against the
   source rules and made the final call, explicitly instructed to default to
   `Other Cards` rather than force a guess if still unclear.
5. **Manifest + migration** — `manifest.json` records every card's before/
   after category, tags, review flag, and the deciding reasoning. The four
   `20260909091*_SpeakingCorpusReclassification*.cs` migrations (split by
   profession to stay under the repo's file-size convention) apply the exact
   mapping and are reversible (`Down()` restores the prior values from this
   same manifest).

All 417 cards were processed; 0 review/arbitration failures.

## Results

| | count |
|---|---|
| Total cards | 417 |
| Changed classification | 324 (78%) |
| Unchanged (already correct) | 93 (22%) |
| `CategorySource` becomes `reviewed` | all 417 |

Per-profession change rate (confirms the legacy classifier was worst on
medicine/pharmacy, better on nursing):

| Profession | Cards | Changed |
|---|---|---|
| Medicine | 133 | 120 (90%) |
| Pharmacy | 98 | 89 (91%) |
| Nursing | 123 | 72 (59%) |
| Other 9 professions | 63 | 43 (68%) |

`Other Cards` backlog: 122 (pre-fix) → 119 (after the classifier-repair
deploy's automatic boot sweep) → **23** (after this review pass) — the
remaining 23 are genuinely uncertain/profession-specific per the two
independent reviewers and the arbiter; they were **not forced** into a wrong
category, per the PDF's explicit low-confidence rule and the task's
instruction to never force a guess.

New category distribution (417 cards, draft + published):

| Category | Count |
|---|---|
| First Visit | 186 |
| Second Visit / Follow-up | 95 |
| Already Known Patient | 72 |
| Other Cards | 23 |
| Examination Card | 19 |
| Emergency / Emergency Department | 17 |
| Reluctant Patient | 3 |
| Angry Patient | 2 |
| **Breaking Bad News (primary)** | **0** |

## Known gap — no card in the corpus warrants "Breaking Bad News" as a primary category

Zero cards, across all 417, resolved to `Breaking Bad News` as a *primary*
category. This is not a review error: every bad-news scenario in the corpus
occurs within a clear First Visit / Second Visit / Already Known Patient
encounter (matching the PDF's own worked example — "Follow-up test result
confirms cancer → Second Visit / Follow-up / **Breaking Bad News**" — bad
news is stored as a secondary tag there, not promoted to primary), and
`Breaking Bad News`-as-primary is reserved by §8B Q6 for a scenario with no
encounter framing at all. Per the task's explicit instruction — **do not
fabricate cards merely to fill a category gap** — no card was invented to
fill this. It's flagged here as a genuine content gap for whoever owns the
Speaking card catalogue to decide whether a "pure breaking-bad-news, no
encounter context" card is worth authoring later.

Every other one of the nine categories has at least one verified card in the
corpus after this pass (see the distribution table above; all are also
represented among **published** cards specifically — see `manifest.json` for
the per-card `status` field, 4 = Published).

## Correction — 20260909100000_SpeakingCorpusReclassificationCorrection

The deploy of the four migrations above restarted the API containers, which
run `SpeakingCardClassifier.ApplyIfUnclassified` on every boot as an
auto-improvement sweep. Its guard treated *any* row whose `PrimaryCategory`
was `"Other Cards"` as an unclassified placeholder eligible for automatic
reclassification, regardless of provenance — so it immediately re-ran the
deterministic classifier over the 23 rows this review had confirmed as
`Other Cards`, silently overwriting `CategorySource` back to `classifier`
(11 rows kept `Other Cards` by coincidence, 12 were moved to a different
category the classifier alone was confident about).

Root cause fixed in `SpeakingCardClassifier.ApplyIfUnclassified`: it now
skips any row whose `CategorySource` is `manual`/`reviewed`/`seed`
unconditionally, before even checking the category — a confirmed
`Other Cards` is a deliberate conclusion, not a placeholder. A regression
test (`ApplyIfUnclassified_NeverTouchesAReviewedOtherCardsRow`) pins this.

`20260909100000_SpeakingCorpusReclassificationCorrection.cs` restores the
23 affected rows to this review's actual conclusion (captured in
`manifest.json`); its `Down()` restores the exact clobbered state queried
from production immediately before the fix.

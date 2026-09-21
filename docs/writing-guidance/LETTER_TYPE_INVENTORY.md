# Writing letter-type inventory — declared vs the real catalogue (20 Sep 2026)

`professionSpecific.primaryLetterTypes` in each `rulebooks/writing/<profession>/rulebook.v1.json` used to
list two or three letter types per profession. The verified catalogue contains more. Owner decision
(19 Sep 2026): the declared inventory must match the catalogue, and no type may be inferred that the
catalogue does not contain.

The field is metadata only (nothing in the engine or the grader prompt reads it today), so this change
does not alter any grading or validation result. It makes the declared inventory truthful so that the
guidance layer built on it (generic fallback + profession supplements) covers every type that can occur.

## Mapping

| catalogue code | declared token |
|---|---|
| LT-RR routine referral | `referral` |
| LT-UR urgent referral | `urgent_referral` |
| LT-DG discharge | `discharge` |
| LT-TR transfer | `transfer` |
| LT-NM referral to a non-medical professional | `non_medical_referral` |
| LT-OT other letters | `other` (a profession that already declares `advice` keeps `advice` for this type) |

Existing declared tokens are never removed. This change only adds.

## Counts (scenarios in the catalogue per letter type; archived and duplicate rows excluded)

| profession | RR | UR | DG | TR | NM | OT | added to `primaryLetterTypes` |
|---|--:|--:|--:|--:|--:|--:|---|
| medicine | 30 | 12 | 8 | 1 | 4 | 0 | none (no `professionSpecific` block; the canonical corpus carries its guidance) |
| nursing | 19 | 1 | 9 | 13 | 43 | 3 | `urgent_referral`, `non_medical_referral`, `other` |
| pharmacy | 11 | 1 | 2 | 0 | 2 | 13 | `urgent_referral`, `discharge`, `non_medical_referral` |
| physiotherapy | 6 | 1 | 1 | 1 | 1 | 2 | `urgent_referral`, `transfer`, `non_medical_referral`, `other` |
| dentistry | 4 | 0 | 0 | 0 | 1 | 0 | `non_medical_referral` |
| radiography | 3 | 2 | 0 | 0 | 0 | 0 | `urgent_referral` |
| dietetics | 3 | 3 | 2 | 2 | 0 | 2 | `urgent_referral`, `discharge`, `transfer` |
| occupational therapy | 1 | 1 | 1 | 1 | 0 | 1 | `urgent_referral`, `transfer` |
| optometry | 1 | 1 | 1 | 1 | 0 | 1 | `discharge`, `transfer`, `other` |
| podiatry | 1 | 1 | 1 | 1 | 0 | 1 | `discharge`, `transfer`, `other` |
| speech pathology | 1 | 1 | 1 | 1 | 0 | 1 | `urgent_referral`, `transfer`, `other` |

## Declared but not evidenced in the catalogue (kept, not removed)

- dentistry declares `urgent_referral`; the catalogue holds no dentistry urgent referral.
- radiography declares `advice`; the catalogue holds no radiography advice letter.

These are left in place because removing a declared type is a separate decision. They are flagged so
that nothing built on the inventory treats them as verified.

## Occupational therapy `smokingDrinkingRequired`

Removed from the OT `professionSpecific` block (and commented out in the legacy generator). No detector,
grader prompt or test read it, so it enforced nothing; a flag that claims a requirement nothing checks is
worse than no flag. Re-introduce it together with a real check.

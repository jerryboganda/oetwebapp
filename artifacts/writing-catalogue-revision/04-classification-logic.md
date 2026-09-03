# Classification Logic (Catalogue vs Grading-Lens)

## Catalogue classification (CHANGED) — what decides the stored `LT-*` code

Single source of truth: `WritingLetterTypeTaxonomy`
(`backend/.../Services/Writing/WritingLetterTypeTaxonomy.cs`).

- Valid set: `LT-RR, LT-UR, LT-DG, LT-TR, LT-NM, LT-OT`. `LT-RP` absent.
- `NormalizeCatalogueLetterType`: known families → their code; `LT-RP /
  RESPONSE / UPDATE / REPLY` and anything unrecognised (incl. null/empty) →
  `LT-OT`. Explicit, testable, no silent coercion, no enum-order/first-item
  default (the old `_ → LT-RR` guess is gone).
- `NormalizeCatalogueLetterTypeOrEmpty`: same, but preserves empty so
  required-field validation still fires on authoring DTOs.
- `ToLegacyLetterType`: modern codes → legacy lowercase tokens for older
  pathway payloads; `LT-OT` (and ex-RP inputs) → `other_letters`. The old
  `LT-RP → update` arm is deleted.
- `ToPackLetterType` (added by peer session on top of this taxonomy): any
  token → canonical pack/genre token; RP/unknown → `other` (neutral genre, so
  only generic rules apply). Consistent with the fallback policy.
- Consumers: onboarding focus allowlist, pathway normalize + pharmacy/nursing
  defaults, pathway generator defaults, authoring upsert, legacy scenario
  view, import mapping, publish gate, preflight pack lookup, task
  understanding parser, canon detection, governance endpoints.
- AI authoring (`writing.scenario.generate.v1` prompt + registrar output
  schema): six-code allowlist, RP ban, OT-fallback instruction. No extra model
  calls: the fallback is a prompt contract + deterministic parsers, reusing
  the single classification response.

Fallback chain for a new/unclear case: explicit admin choice of `LT-OT`, or
import/normalizer mapping to `LT-OT`, or publish-gate block
(`letter_type_unsupported`) until reclassified. Low-confidence work is never
auto-forced into RR/UR/DG/TR/NM.

## Grading-lens heuristics (DELIBERATELY UNCHANGED)

These select rulebook sections / AI grounding for scoring — not the stored
catalogue code. Changing their defaults would alter grading for all existing
tasks (high regression risk, out of brief scope):

- `inferWritingLetterType` (`lib/rulebook/context.ts`): lint heuristic,
  default `routine_referral`. Drives rule selection only.
- `WritingTaskUnderstandingService`: deterministic pre-AI parser; returns
  `requires_review` (null primary) on conflicting evidence instead of forcing
  a type — already compliant in spirit.
- Rulebook `LetterType` union (`lib/rulebook/types.ts`) and ContentPaper
  canonical ids (`routine_referral`, `update_discharge`, …): separate
  content-pipeline vocabulary with its own publish validation; untouched.
- `WritingDraftService.ValidLetterTypes` / `NormalizeLetterTypeForGrounding`:
  grading-grounding lens; unknown codes already degrade to the generic
  routine lens (generic rules), which is the correct grading behavior for an
  `other` letter (no `other` genre exists in rulebooks, and inventing one is
  out of scope).

## Difficulty (unchanged, internal)

`WritingScenario.Difficulty` int 1–5 + `Math.Clamp` + adaptive ±1 practice
band + admin metadata. Never rendered in the catalogue (verified). Removing
the column would break adaptive sequencing; the brief forbids blind column
deletion, so it is preserved with catalogue presentation absent.

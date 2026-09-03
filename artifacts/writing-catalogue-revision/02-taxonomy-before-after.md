# Writing Catalogue Taxonomy — Before / After

## Letter-type codes (stored on `WritingScenarios.LetterType`, `varchar(8)`)

| Code | Label | Before | After |
|---|---|---|---|
| LT-RR | Routine referral | valid everywhere | KEEP |
| LT-UR | Urgent referral | valid everywhere | KEEP |
| LT-DG | Discharge | valid everywhere | KEEP |
| LT-TR | Transfer | valid everywhere | KEEP |
| LT-NM | Non-medical (referral) | valid; vet excluded in Task Builder | KEEP (same vet rule) |
| LT-OT | Other Letters | valid in frontend/library/admin/tasks; **missing** from backend allowlists, showcase, focus | ADDED everywhere; universal fallback |
| LT-RP | Response | absent from frontend; **still producible** by backend normalizers/defaults | REMOVED completely |
| All | filter-only pseudo-option | filter only | unchanged (never stored; rejected by validators) |

## Per-layer before / after

| Layer | Before | After |
|---|---|---|
| `lib/writing/types.ts` `WritingLetterType` | RR/UR/DG/TR/NM/OT (no RP) | unchanged |
| `lib/writing/zod.ts` enum | RR/UR/DG/TR/NM/OT | unchanged (already rejects RP) |
| Library page `LETTER_TYPES` | 6 codes incl. OT | unchanged |
| Showcase `LETTER_TYPES` | 5 codes, no OT | 6 codes incl. OT |
| Focus page options | 5 options, no OT | 6 options incl. OT |
| Admin tasks filter / scenarios dropdown / builder | 6 codes incl. OT | unchanged + shared `writingLetterTypesForProfession()` helper |
| Admin analytics labels | `Referral`, `New management` (wrong) | `Routine referral`, `Non-medical referral` |
| `WritingLearnerPathwayService.NormalizeLetterType` | `RESPONSE/UPDATE/LT-RP→LT-RP`; unknown→`LT-RR` (forced guess) | delegates to taxonomy: RP-family→`LT-OT`; unknown→`LT-OT` |
| `…ToLegacyLetterType` | `LT-RP→update`; fallback `routine_referral` | via taxonomy: `LT-OT→other_letters`; unknown→`other_letters` |
| Pharmacy defaults (pathway service + generator) | `[LT-RR, LT-RP, LT-NM]` | `[LT-RR, LT-OT, LT-NM]` |
| `WritingOnboardingService` focus allowlist | 6 codes incl. `LT-RP`, excl. `LT-OT` | 6 codes incl. `LT-OT`, excl. `LT-RP` |
| `NormaliseLetterTypeForRulebook` | `LT-RP→advice_to_patient` | `LT-OT→other` (neutral genre → generic rules only); RP arm deleted |
| `WritingTaskAuthoringService.ApplyUpsert` | stored trimmed raw value (RP persisted) | normalizes non-empty via taxonomy (RP/invalid→OT; empty stays empty) |
| Publish gate (`Validate`/`BuildBlockingCodes`) | `letter_type_required` only | + `letter_type_unsupported` blocks any non-catalogue code (incl. legacy RP rows) |
| Import `MapTaskTypeToLetterType` | lowercase canonical ids (`routine_referral`, `advice_letter`, …) or raw passthrough (`response→response`); `varchar(8)` overflow → 500 on canonical labels | catalogue codes; RP/response/advice/unknown→`LT-OT`; `lt-rr`→`LT-RR` canonicalization |
| Legacy admin `ToScenarioView` | raw passthrough | `NormalizeCatalogueLetterTypeOrEmpty` |
| AI scenario-generate prompt + output schema | unconstrained `letter_type` string | six-code allowlist, RP ban, OT-fallback instruction |
| DB rows with RP-like letter type | present (count verified at deploy) | 0 after migration (Isabel Garcia→`LT-DG`, rest→`LT-OT`) |

## Difficulty / Levels

| Concern | State |
|---|---|
| Difficulty filter on library/showcase | never existed — no change |
| Level 1–5 badges on case-note cards | never existed (repo-wide search: zero hits in Writing catalogue) — no change |
| `Difficulty` int 1–5 (DB, API DTO, admin metadata, adaptive ±1 band) | PRESERVED by design (internal sequencing, not catalogue-visible) |
| Drill `difficulty` (`core`/`exam`) | PRESERVED (separate concept) |

## Professions (unchanged, all expose Other Letters)

medicine, nursing, dentistry, pharmacy, physiotherapy, occupational-therapy,
radiography, dietetics, optometry, podiatry, speech-pathology, veterinary,
other. Veterinary excludes only LT-NM (existing OET content rule, preserved).

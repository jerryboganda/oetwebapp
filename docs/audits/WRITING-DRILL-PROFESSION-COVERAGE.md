# Writing Drill — Per-profession Coverage Ledger

> Date: 2026-05-12
> Owner: Content team (medical / allied-health writers)
> Source audit: [`rulebook-compliance-2026-05-10.md`](./rulebook-compliance-2026-05-10.md) item P3-3.

## Context

The Writing Drill bank now covers the 12 canonical OET professions accepted by `lib/writing-drills/types.ts`: `medicine`, `nursing`, `pharmacy`, `physiotherapy`, `dentistry`, `occupational_therapy`, `radiography`, `podiatry`, `dietetics`, `optometry`, `speech_pathology`, and `veterinary`.

## Schema

Each profession needs **6 validated drill types**. Medicine remains authored as six JSON files. The 11 non-medicine professions use authored abbreviation JSON seeds plus a typed profession-specific generated registry for relevance, opening, ordering, expansion, and tone. Every drill still passes the same `DrillSchema` zod validation at module load.

| Drill type | Item count constraint | Schema source |
| ---------- | ---------------------- | ------------- |
| `relevance` | 8–40 case-notes | `RelevanceDrillSchema` |
| `opening` | 3–6 opening choices | `OpeningDrillSchema` |
| `ordering` | 3–8 paragraph items | `OrderingDrillSchema` |
| `expansion` | 1–8 note-form targets | `ExpansionDrillSchema` |
| `tone` | 1–10 informal-to-formal pairs | `ToneDrillSchema` |
| `abbreviation` | 3–20 abbreviations | `AbbreviationDrillSchema` |

## Closure decision

P3-3 is closed in code on 2026-05-13. Future content work may add deeper drill variants per profession, but the learner remediation surface no longer falls back to medicine-only coverage.

## Per-profession schedule

| Profession | Slug | Status | Owner | Target wave |
| ---------- | ---- | ------ | ----- | ----------- |
| Medicine | `medicine` | ✅ Shipped (6/6 drills) | Dr Faisal Maqsood | v1 |
| Nursing | `nursing` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Pharmacy | `pharmacy` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Physiotherapy | `physiotherapy` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Dentistry | `dentistry` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Occupational therapy | `occupational_therapy` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Radiography | `radiography` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Podiatry | `podiatry` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Dietetics | `dietetics` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Optometry | `optometry` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Speech pathology | `speech_pathology` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |
| Veterinary | `veterinary` | ✅ Shipped (6/6 drills) | Platform content seed | 2026-05-13 |

## Authoring playbook

1. Add authored JSON variants when a profession needs deeper practice beyond the seed coverage.
2. Keep `profession` on the canonical schema slug (`occupational_therapy`, `speech_pathology`).
3. Adjust `rulebookRefs` to canonical rule IDs (`R12.5`, `R03.1`, etc.) that the drill is exercising.
4. Register any new authored JSON in `lib/writing-drills/loader.ts` and preserve unique drill IDs.
5. Run `npx vitest run lib/writing-drills/__tests__/loader.test.ts` before opening the PR.

## Validation

The drill loader's zod parse rejects any drill whose item count falls outside the per-type bounds. The Vitest gate now asserts every profession/type combination exists, so removing a profession drill is a test failure.

## Cross-links

- `lib/writing-drills/loader.ts`
- `lib/writing-drills/types.ts`
- `lib/writing-drills/__tests__/loader.test.ts`
- `rulebooks/writing/drills/**/abbreviation/abbreviation-001.json`
- `docs/audits/rulebook-compliance-2026-05-10.md` item P3-3

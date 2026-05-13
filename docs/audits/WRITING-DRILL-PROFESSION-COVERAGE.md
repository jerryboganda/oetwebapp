# Writing Drill — Per-profession Coverage Schedule

> Date: 2026-05-12
> Owner: Content team (medical / allied-health writers)
> Source audit: [`rulebook-compliance-2026-05-10.md`](./rulebook-compliance-2026-05-10.md) item P3-3.

## Context

The Writing Drill bank ships seeded only for the `medicine` profession. The platform supports 13 professions (`medicine`, `nursing`, `pharmacy`, `physiotherapy`, `dentistry`, `occupational_therapy`, `radiography`, `podiatry`, `dietetics`, `optometry`, `speech_pathology`, `veterinary`, plus the umbrella `other-allied-health`). The shared schema in `lib/writing-drills/types.ts` already accepts every profession; the only gap is authored content.

## Schema

Each profession needs **6 drill files**, one per drill type, dropped under `rulebooks/writing/drills/<profession>/<drill-type>/<id>.json` and registered via a static import in `lib/writing-drills/loader.ts`. The loader runs zod validation at module load, so structurally invalid drills throw immediately.

| Drill type | Item count constraint | Schema source |
| ---------- | ---------------------- | ------------- |
| `relevance` | 8–40 case-notes | `RelevanceDrillSchema` |
| `opening` | 3–6 opening choices | `OpeningDrillSchema` |
| `ordering` | 3–8 paragraph items | `OrderingDrillSchema` |
| `expansion` | 1–8 note-form targets | `ExpansionDrillSchema` |
| `tone` | 1–10 informal-to-formal pairs | `ToneDrillSchema` |
| `abbreviation` | 3–20 abbreviations | `AbbreviationDrillSchema` |

## v1 launch decision

Launch on `medicine` only. The 12 remaining professions are deferred to a content-only follow-up wave. The audit verdict (P3-3) explicitly classifies this as a content gap, not a code gap.

## Per-profession schedule

| Profession | Slug | Status | Owner | Target wave |
| ---------- | ---- | ------ | ----- | ----------- |
| Medicine | `medicine` | ✅ Shipped (6/6 drills) | Dr Faisal Maqsood | v1 |
| Nursing | `nursing` | ⚪ Pending | TBD | v1.1 |
| Pharmacy | `pharmacy` | ⚪ Pending | TBD | v1.1 |
| Physiotherapy | `physiotherapy` | ⚪ Pending | TBD | v1.1 |
| Dentistry | `dentistry` | ⚪ Pending | TBD | v1.2 |
| Occupational therapy | `occupational_therapy` | ⚪ Pending | TBD | v1.2 |
| Radiography | `radiography` | ⚪ Pending | TBD | v1.2 |
| Podiatry | `podiatry` | ⚪ Pending | TBD | v1.3 |
| Dietetics | `dietetics` | ⚪ Pending | TBD | v1.3 |
| Optometry | `optometry` | ⚪ Pending | TBD | v1.3 |
| Speech pathology | `speech_pathology` | ⚪ Pending | TBD | v1.3 |
| Veterinary | `veterinary` | ⚪ Pending | TBD | v1.3 |

## Authoring playbook

1. Copy `rulebooks/writing/drills/medicine/<drill-type>/<id>.json` to the new profession directory and adapt the items to that profession's clinical context.
2. Bump the file `id` (e.g. `nursing-relevance-001`).
3. Update `profession` to the canonical schema slug.
4. Adjust `rulebookRefs` to canonical rule IDs (`R12.5`, `R03.1`, etc.) that the drill is exercising.
5. Add a static `import` + `register(...)` call to `lib/writing-drills/loader.ts`.
6. Add a Vitest gate row in `lib/writing-drills/__tests__/loader.test.ts` to lock the new drill.
7. Run `npx vitest run lib/writing-drills` before opening the PR.

## Validation

The drill loader's zod parse rejects any drill whose item count falls outside the per-type bounds, so a stub-only PR will fail the test gate. This is by design — the test gate prevents a profession from being marked "shipped" until its 6 drills are authored end-to-end.

## Cross-links

- `lib/writing-drills/loader.ts`
- `lib/writing-drills/types.ts`
- `lib/writing-drills/__tests__/loader.test.ts`
- `rulebooks/writing/drills/medicine/**`
- `docs/audits/rulebook-compliance-2026-05-10.md` item P3-3

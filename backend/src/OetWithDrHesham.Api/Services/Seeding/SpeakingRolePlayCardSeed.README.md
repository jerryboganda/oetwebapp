# SpeakingRolePlayCardSeed

Phase 1 (G.10) of the OET Speaking module roadmap.

Seeds **6 Nursing + 6 Medicine** role-play cards into the new
`RolePlayCard` + `InterlocutorScript` schema introduced in
[`Domain/RolePlayCardEntities.cs`](../../Domain/RolePlayCardEntities.cs).
Each seeded card is paired with a hidden `InterlocutorScript` and a
backing `ContentItem` row (`SubtestCode = "speaking"`,
`ContentType = "speaking_roleplay"`, `Status = Published`).

## Running the seeder

The seeder is intentionally NOT wired into `Program.cs` yet; you wire it
where it belongs alongside the rest of the speaking content bootstrap
(e.g. inside `SeedData.EnsureReferenceDataAsync` or as its own call).

```csharp
using OetWithDrHesham.Api.Services.Seeding;

await SpeakingRolePlayCardSeed.SeedAsync(db, cancellationToken);
```

The seeder is **idempotent**: it probes once for any `RolePlayCard`
whose `Id` starts with `rpc-seed-` and returns immediately on hit. That
makes it safe to call on every startup.

## Slug naming convention

All seeded rows use deterministic ids so re-runs and migration tests
behave predictably:

| Row | Id pattern | Example |
| --- | --- | --- |
| `ContentItem` | `ci-seed-speaking-{profession}-{nn}` | `ci-seed-speaking-nursing-01` |
| `RolePlayCard` | `rpc-seed-{profession}-{nn}` | `rpc-seed-medicine-04` |
| `InterlocutorScript` | `is-seed-{profession}-{nn}` | `is-seed-nursing-06` |
| `ContentItem.PublishedRevisionId` | `rev-seed-speaking-{profession}-{nn}` | `rev-seed-speaking-medicine-02` |

Slugs are `01`..`06` per profession. Add new seed cards by extending
the corresponding `NursingCards()` / `MedicineCards()` enumerable in
`SpeakingRolePlayCardSeed.cs` and bumping the slug.

## Licensing stance

All seed content is **original**. The author wrote each scenario in the
OET style using the real Cambridge Boxhill / OET sample cards under
`Project Real Content/Speaking_/` as inspiration only. **Nothing in the
seed file is copied verbatim** from those PDFs. Patient names, ages,
clinical specifics, dialogue lines and hidden information are all
freshly invented.

Why this matters:

- The real OET cards are copyright Cambridge Boxhill Language
  Assessment. We may not redistribute them inside our application
  database or repository.
- Each seeded `ContentItem` is therefore tagged
  `SourceProvenance = "original"`, `RightsStatus = "owned"`,
  `SourceType = "manual"`.
- The `Disclaimer` field on every seeded `RolePlayCard` reinforces that
  this is practice content only, not an official OET assessment.

## Coverage of the 12 seeded cards

| Slug | Profession | Scenario | Difficulty | Resistance |
| --- | --- | --- | --- | --- |
| `rpc-seed-nursing-01` | Nursing | Discharge advice after laparoscopic appendectomy | core | medium |
| `rpc-seed-nursing-02` | Nursing | Medication review with older adult on polypharmacy | extension | medium |
| `rpc-seed-nursing-03` | Nursing | Home wound-care teaching for a venous leg ulcer | core | low |
| `rpc-seed-nursing-04` | Nursing | Falls-risk discussion with a worried daughter | extension | high |
| `rpc-seed-nursing-05` | Nursing | Childhood vaccination conversation with a hesitant parent | exam | high |
| `rpc-seed-nursing-06` | Nursing | Explaining comfort care to a relative at end of life | exam | medium |
| `rpc-seed-medicine-01` | Medicine | Sharing a new diagnosis of Type 2 Diabetes | core | medium |
| `rpc-seed-medicine-02` | Medicine | Counselling a parent after a first febrile convulsion | extension | medium |
| `rpc-seed-medicine-03` | Medicine | Discussing chest-pain investigation results | extension | medium |
| `rpc-seed-medicine-04` | Medicine | Declining an antibiotic request for a viral illness | exam | high |
| `rpc-seed-medicine-05` | Medicine | Lifestyle counselling for newly identified hypertension | core | low |
| `rpc-seed-medicine-06` | Medicine | Sharing a new diagnosis of colorectal cancer | exam | low |

The split deliberately mirrors the Phase 1 spec: roughly 4 `core`, 4
`extension`, 4 `exam` across the two professions, with a mix of
resistance levels so the AI patient persona and tutor cue panel both
get a varied workout.

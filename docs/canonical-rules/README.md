# Canonical OET AI rule registry

Vendored source of truth for OET Writing (and future Speaking) AI grading, from
`OET_DEVELOPER_COMPLETE_HANDOFF_v1.0_2026-08-31/02_AI_SOURCE_OF_TRUTH/`. Release `1.0`,
`2026-08-31`. Verified SHA-256 against `HANDOFF_SHA256SUMS.txt` at vendor time:

| File | SHA-256 |
|---|---|
| `OET_AI_Rules_Master.jsonl` | `7f6446d304f5ab997c97ff2dd5e2d812b610dee838222d424647abbb54baa0a4` |
| `OET_AI_DEPLOYMENT_CONTRACT.json` | `972cab8e0a617cb3da5e32d06521cd079c8852bbe2813e2204a225140331ef7d` |
| `OET_AI_Source_Manifest.json` | `f0d46c4996bf6a2cef5d2dcddb88ada2b7eab19d33f760fc22a9ef52913550a1` |

`OET_AI_Rules_Master.json` (the non-streaming twin), the transcript evidence corpus, and the
legacy Medicine rule audit are **not** vendored here — only the streaming registry and its
contract/manifest are needed to build the runtime rulebooks. The full handoff package remains
at the source path above for audit.

## What this is for

`scripts/rulebooks/build-canonical-writing-rulebooks.mjs` reads `OET_AI_Rules_Master.jsonl`,
filters `skill == "Writing" && active_for_ai == true`, and regenerates
`rulebooks/writing/<profession>/rulebook.v1.json` for the six professions the platform
actually has live Writing content for (medicine, nursing, dentistry, pharmacy,
physiotherapy, radiography). This **replaces** the legacy 172-rule-per-profession set
(cloned from a single Medicine PDF) as Writing scoring truth — the legacy set is exactly the
`R##.#`-id, `active_for_ai: false` provenance appendix this registry's own deployment contract
marks `never_load_as_scoring_truth`.

The other 7 profession folders (veterinary, optometry, occupational-therapy,
speech-pathology, podiatry, dietetics, other-allied-health) are untouched — they have zero
live Writing tasks and stay on the legacy content until/unless the platform launches Writing
for them, at which point this same build script should be extended to cover them once a
canonical pack exists.

## Severity mapping (registry has no severity field — derived, not invented)

The canonical registry has no `critical`/`major` concept; `classification` + `authority` are
mapped to `OetRule.Severity` so the existing prompt renderer's "critical rules always shown,
major rules flagged as high-priority" structure still works:

- **Critical** — `authority == OET_OFFICIAL` (the six official marking criteria/definitions)
  or `classification` in `{Safety, Hard Rule, Hard Strategy, Validated Override}` (Dr Hesham's
  explicitly hard-mandatory / patient-safety / already-validated-override content).
- **Major** — everything else: `Language Rule` (grammar mechanics — articles, prepositions,
  tense, punctuation, cohesion, register), `Strategy`, `Style`, `Technique`, `Preference`,
  `AI Rule`, `Profession Rule`.

No canonical rule maps to `minor`/`info` — every active rule must be visible to the grader.
`Enforcement` is `AiGrounded` for every canonical rule (the registry's own
`ai_grading_logic` field instructs contextual judgement, not fixed-pattern matching); `CheckId`
and `ForbiddenPatterns` are left null so `WritingRuleEngine`'s deterministic detectors (keyed
by `CheckId`) never run against them — confirmed safe: the engine skips any rule with a null
`CheckId`/empty `ForbiddenPatterns`. `good_example`/`bad_example`/`ai_grading_logic`/`notes`/
`source_refs`/`classification` are preserved under each rule's `params` for admin/audit
display; nothing from the canonical record is discarded.

Re-run the build script whenever `OET_AI_Rules_Master.jsonl` is updated upstream (verify the
new file's SHA-256 against a fresh `HANDOFF_SHA256SUMS.txt` from the same release drop first).

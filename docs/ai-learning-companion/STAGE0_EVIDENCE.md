# Stage 0 Release Evidence — AI Learning Companion

> The source specification is explicit that "a stage is not done because code merged". This file is the
> evidence record for Stage 0 (foundations) plus the first Stage 1 slices. It states what shipped, what was
> verified and how, and — deliberately — what is still open.

**Date:** 2026-09-06 · **Branch:** `writing/final-production-release` · **Persona:** `Jana` (config key)

---

## 1. Feature IDs served

| Slice | F-IDs advanced | Status change |
|---|---|---|
| S0.1 feature codes + policy matrix | cross-cutting telemetry | Companion calls are now classifiable and platform-only |
| S0.2 flags and kill switches | F-138 gating, F-064 calibration gate | 5 independent switches, all shipping **disabled** |
| S0.3 knowledge schema | F-013, F-026, F-027 | Schema exists; **no corpus indexed yet**, so these stay `PARTIAL`/`MISSING` |
| S0.4 learner tool boundary | cross-cutting security | New guard + 15 tests |
| S0.5 evaluation harness | F-153, F-154 (harness only) | 16 golden cases, structurally enforced |
| S1.6 (partial) frontend wiring | F-097, F-167 | Panel de-stubbed; provider and launcher mounted behind the flag |
| S1.1 context resolver | F-003…F-006, F-010, F-143, F-155 | Server-trusted context; exam mode resolved from attempt state, not the client |
| S1.2 entitlement-safe retriever | **F-154**, F-026, F-029 | Prefilter before search; authority precedence; verbatim caps |
| S1.2b rulebook indexer | F-013 | 115 rulebooks indexable, one chunk per rule, idempotent re-index |
| S1.3 prompt composer | F-048, F-049, F-053, F-152, F-153 | Grounded companion prompt replaces the generic tutor string |

**No feature was moved to `EXISTS` on the strength of this work.** The gap analysis statuses reflect what is
actually delivered, not what is scaffolded.

## 2. Files changed

**Backend**
- `Domain/AiEntities.cs` — 3 companion feature codes
- `Domain/CompanionKnowledgeEntities.cs` *(new)* — `CompanionSource`, `CompanionChunk`, `CompanionKnowledgeRelease`, `CompanionAuthorityClass`
- `Data/LearnerDbContext.Companion.cs` *(new)* + `LearnerDbContext.cs` — mapping and partial hook
- `Data/Migrations/20261220090000_AddCompanionKnowledge.cs` *(new)* — hand-authored, idempotent, reversible
- `Services/Companion/CompanionFeatureFlags.cs` *(new)* — fail-closed kill switches
- `Services/AiManagement/AiCredentialResolver.cs` — companion codes added to `PlatformOnlyFeatures`
- `Services/AiTools/AiToolRegistry.cs` — learner tool-boundary guard
- `Services/SeedData.cs` — `flg-026`…`flg-030`
- `Endpoints/LearningContentEndpoints.cs` — learner-visible flag
- `Program.cs` — DI registration

**Frontend**
- `components/domain/ai-assistant/AiAssistantPanel.tsx` — de-stubbed, consumes the context
- `components/domain/ai-assistant/AiAssistantMessages.tsx` — markdown for assistant output
- `components/providers/companion-mount.tsx` *(new)* — flag-gated mount
- `app/providers.tsx` — `AiAssistantProvider` + `CompanionMount`

**Tests**
- `backend/tests/.../AiFeatureEligibilityTests.cs` *(new)* — 4 tests
- `backend/tests/.../CompanionLearnerToolBoundaryTests.cs` *(new)* — 15 tests
- `tests/companion/golden/oet-core.golden.json` + `tests/companion/golden-set.test.ts` *(new)*
- `components/domain/ai-assistant/__tests__/AiAssistantPanel.test.tsx` — rewritten against the real contract

**Commands** — 11 `.claude/commands/ai-companion-*.md` prompts are installed locally but **not committed**:
`.gitignore:18` ignores `.claude/`. They are reproducible from
`Chatbot/AI_Learning_Companion_Claude_Code_Pack/.claude/commands/`.

**Docs** — the whole `docs/ai-learning-companion/` pack, `REPO_GAP_ANALYSIS.md`, `IMPLEMENTATION_PLAN.md`,
`traceability/`, `scripts/ai-learning-companion/validate_traceability.py`, `AGENTS.md` pointer,
`docs/AI-USAGE-POLICY.md` §5 rows.

## 3. Migration

`20261220090000_AddCompanionKnowledge` — additive only; no existing table is touched. `Up` is idempotent
(`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`); `Down` drops only the three new tables. The HNSW
index build is wrapped in an exception handler so an older pgvector cannot fail the migration — retrieval
degrades to exact scan plus the keyword path.

## 4. Tests run and results

| Suite | Result |
|---|---|
| `AiFeatureEligibilityTests` | **4/4 pass** |
| `CompanionLearnerToolBoundaryTests` | **15/15 pass** |
| Combined re-run after guard restore | **19/19 pass** |
| `CompanionRetrievalSecurityTests` | **10/10 pass** |
| Full companion + eligibility suite | **29/29 pass** |
| `dotnet build` (API project) | **0 errors** |
| `pnpm run ship:gate` | **OK** |
| `tsc --noEmit` | **0 errors in changed files**; 13 pre-existing errors remain in `tests/e2e/writing-v2/**` and `tests/performance/**` (Playwright specs, unmodified at HEAD, untouched by this work) |

**Mutation check on the security guard.** The boundary test was verified to be capable of failing: disabling
the one filter line in `AiToolRegistry.ResolveForFeatureAsync` produced **8 failures** — exactly
`DeveloperTools_NeverResolve_*` and `MixedGrants_*` across all four learner-facing feature codes — while the
admin regression test kept passing. The guard was then restored byte-for-byte and re-verified at 19/19.

**Typecheck baseline.** `tsc --noEmit` exits non-zero on this repository *before* any companion change: 13 errors live in Playwright e2e/performance specs that `tsconfig.json` includes via `**/*.ts`. Those files are unmodified at HEAD, so the errors are pre-existing. No error in the run maps to a file this work touched.

**Second mutation check — the entitlement prefilter (F-154).** Replacing the scope predicate in `CompanionRetriever.ResolveCandidateSourcesAsync` with `.Where(s => true)` failed exactly two tests — `UnentitledSource_IsNeverRetrieved_EvenAsTheOnlyMatch` and `MixedEntitlement_ReturnsOnlyTheOwnedSource` — while the other eight kept passing, because they exercise the draft, superseded, profession, expiry, kill-switch and verbatim-cap predicates instead. That is the correct blast radius: the filter is load-bearing and the tests are specific. The file was restored by the same script that mutated it.

The security tests deliberately run on SQLite, where pgvector is unavailable and the keyword path answers. A prefilter implemented as a post-search score penalty would pass a vector-only test and fail these.

**Frontend unit tests could not be executed.** The workspace `node_modules` is incomplete: `jsdom` is missing
its transitive dependencies (`is-potential-custom-element-name`, `@adobe/css-tools`). This is **pre-existing
and unrelated** — an untouched existing test (`lib/ai-assistant/__tests__/permissions.test.ts`) fails to start
with the same error. No install was attempted because several other sessions were building concurrently.
`tsc --noEmit` and `ship:gate` were used as the gate instead, per `CLAUDE.md`.

## 5. Security findings from this work

1. **The claimed policy-matrix gate did not exist.** `AiEntities.cs:297` states that adding a feature code
   without a matrix row "is a bug caught by `AiFeatureEligibilityTests`". No such test existed, and **35 of 66
   feature codes were undocumented**. The test now exists as a ratchet: the 32 pre-existing gaps are listed
   explicitly and any *new* undocumented code fails the build.
2. **The learner tool boundary was data-only.** Tool resolution is driven purely by `AiFeatureToolGrant` rows.
   Role derivation is correctly server-side and grants are deny-by-default, but one mistaken admin grant row
   would have exposed `run_command` / `deploy` / `write_file` to every learner. A code-level allowlist now
   filters learner-facing feature codes at the single chokepoint, logs any blocked grant as an error, and
   leaves the admin assistant unaffected.

## 6. Open `TO VERIFY` gates (unchanged — not resolved by this work)

TV-002 first beta profession · TV-004/TV-005 retrieval and hallucination thresholds · TV-006/TV-007 Writing and
Speaking calibration (`companion_score_display` stays OFF) · TV-018 net revenue per credit · TV-023 regional
pricing · TV-027 GDPR/DPIA · TV-030 Jana/Sami clearance (persona is a config key, not a literal) · TV-032 store
AI-content reporting · TV-035 kill-switch owner · TV-036 learner-distress escalation owner.

## 7. Flag states at release

| Flag | State |
|---|---|
| `ai_learning_companion` (`flg-026`) | **disabled** |
| `companion_retrieval` (`flg-027`) | **disabled** |
| `companion_actions` (`flg-028`) | **disabled** |
| `companion_credits` (`flg-029`) | **disabled** |
| `companion_score_display` (`flg-030`) | **disabled — gated on TV-006/TV-007** |

Nothing is visible to any learner until an operator enables `ai_learning_companion` in `/admin/flags`.

## 8. Rollback

- **Surface:** disable `flg-026` in `/admin/flags`. No deploy.
- **Schema:** `Down` on `20261220090000_AddCompanionKnowledge` drops the three new tables. Nothing else depends
  on them.
- **Code:** all backend changes are additive except the `AiToolRegistry` filter, which only ever *removes*
  tools from learner-facing features.
- **Frontend:** `CompanionMount` returns `null` whenever the flag is off or unreadable.

## 9. Known issues and next work

| Item | Severity | Owner |
|---|---|---|
| Frontend test runner cannot start (missing jsdom transitive deps) | Medium — blocks unit verification, not the build | Platform |
| 32 feature codes still undocumented in the policy matrix | Low — ratcheted, cannot grow | Platform |
| Corpus not yet indexed in any environment — the indexer exists but has not been run | High — companion answers ungrounded until it runs | This programme |
| Action layer (F-098…F-104) and companion credit metering not yet implemented | Expected — Stage 1 remainder | This programme (S1.4, S1.5, S1.7) |

**S1.1–S1.3 have now landed.** Grounding, the entitlement prefilter (F-154) and exam-mode awareness (F-155) are
implemented and tested. The remaining blocker before enabling the flag is operational rather than structural:
the rulebook corpus must actually be indexed in the target environment, otherwise retrieval returns nothing and
the companion correctly — but unhelpfully — says it has no verified information.

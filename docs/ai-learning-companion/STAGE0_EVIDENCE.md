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
| S1.4 destination registry | **F-023**, F-098, F-100 | Closed route vocabulary; the model names an id, the server builds the URL |
| S1.5 action layer | F-098, F-100, F-101, F-102, F-103, **F-104** | 5 new learner-safe tools + 2 existing ones granted; entitlement re-checked at execution |
| S1.6 companion surface | F-097, F-160, F-167 | `/companion` full-screen page, credit chip, thread list, en/ar bundles |
| S1.2c corpus operations | F-013, F-026 | `/v1/admin/companion/knowledge` — status + reindex, so the corpus can actually be built |
| S1.7 commercial legibility | **F-142**, F-135, F-139…F-141 | `GET /v1/companion/session` decides access before the learner types; paywall card replaces the chat |
| S1.8 memory controls | **F-047** | View / delete / reset / **export** companion-saved notes and words, scoped by user *and* authoring feature |
| S1.6b citations | **F-024, F-025** | Sources persisted on the message and rendered with their authority class, on both surfaces |
| S1.2d corpus bootstrap | **F-013** | Indexes on first boot when the flag is on and the corpus is empty — grounding no longer depends on a remembered manual step |
| Streaming contract repair | cross-cutting | The hub and the browser client never agreed on event names; the learner chat streamed into handlers nobody had registered |
| Exam-mode repair | **F-155** | The guard was unreachable: nothing on the wire populated the envelope it keyed off. Now resolved from attempt state in the database |
| Surface awareness | **F-091…F-094** | `StartTurn` carries a bounded envelope; route → surface/resource/question/video, derived client-side, resolved server-side |
| Teaching styles | **F-011, F-050, F-052, F-055** | `CompanionPreference` + panel: direct / Socratic / coach, depth, English-only immersion |
| Sensitive-data boundary | **F-156** | Warns on real patient data or document scans, does not repeat it back, does not save it |

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
- `Services/Companion/CompanionContextResolver.cs`, `CompanionRetriever.cs`, `CompanionRulebookIndexer.cs`, `CompanionPromptComposer.cs` *(new)*
- `Services/Companion/CompanionDestinationRegistry.cs` *(new)* — closed destination catalog + server-side resolution
- `Services/AiTools/Tools/CompanionActionTools.cs` *(new)* — 5 typed action tools
- `Services/AiTools/AiToolCatalogSeederHostedService.cs` — idempotent companion tool grants
- `Endpoints/CompanionKnowledgeAdminEndpoints.cs` *(new)* — corpus status + reindex (`AdminAiConfig`)
- `Endpoints/CompanionLearnerEndpoints.cs` *(new)* — session capability + memory controls (`LearnerOnly`)
- `Program.cs` — DI registration and endpoint mapping

**Frontend**
- `components/domain/ai-assistant/AiAssistantPanel.tsx` — de-stubbed, consumes the context
- `components/domain/ai-assistant/AiAssistantMessages.tsx` — markdown for assistant output
- `components/providers/companion-mount.tsx` *(new)* — flag-gated mount
- `app/providers.tsx` — `AiAssistantProvider` + `CompanionMount`
- `app/companion/page.tsx` *(new)* — full-screen companion surface, flag-gated, fails closed
- `messages/{en,ar}/companion.json` *(new)* + `i18n.ts` — companion message module
- `components/layout/learner-dashboard-route-policy.ts` — `/companion` registered as a learner workspace route
- `components/domain/companion/CompanionMemoryPanel.tsx` *(new)* — F-047 view/delete UI
- `lib/api/companion.ts` *(new)* — companion wire shapes, kept out of the in-flight `lib/api.ts` split

**Tests**
- `backend/tests/.../AiFeatureEligibilityTests.cs` *(new)* — 4 tests
- `backend/tests/.../CompanionLearnerToolBoundaryTests.cs` *(new)* — 15 tests
- `backend/tests/.../CompanionRetrievalSecurityTests.cs` *(new)* — 10 tests
- `backend/tests/.../CompanionDestinationSecurityTests.cs` *(new)* — 16 tests
- `backend/tests/.../CompanionMemoryIsolationTests.cs` *(new)* — 4 tests
- `backend/tests/.../EndpointRegistrationTests.cs` — the two new admin routes
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
| `CompanionDestinationSecurityTests` | **16/16 pass** |
| `CompanionMemoryIsolationTests` | **4/4 pass** |
| `CompanionKillSwitchTests` (the drill) | **23/23 pass** |
| `AiAssistantHubContractTests` | **2/2 pass** |
| `CompanionExamModeTests` | **10/10 pass** |
| Companion + eligibility + endpoint + contract suites | **154/154 pass** |
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
2. **The learner chat had never actually streamed.** `AiAssistantHub` sends `MessageDelta` and
   `MessageComplete`, each with a leading `threadId`. `lib/ai-assistant/signalr.ts` listened for `TextDelta`
   and `TurnComplete` with no `threadId`. Nothing threw, nothing logged: the answer streamed into handlers
   nobody had registered, and the learner watched an empty panel. `ToolCallStart`/`ToolCallResult` matched by
   name but had their arguments shifted by one position for the same reason. The REST client was wrong in the
   same way — it declared `{threads,total,page,pageSize}` and `{messages,total}` wrappers over endpoints that
   return bare arrays, and sent `?page=&pageSize=` to endpoints that read `skip`/`take`, so `result.messages`
   was always `undefined`. Its unit tests passed throughout, because they mocked `apiClient` and asserted the
   client's own invented shape back at itself.
   **Fixed** by aligning the client to the deployed server contract, and locked by
   `AiAssistantHubContractTests`, which reads both sources and fails if the event names diverge again. That
   test is deliberately cross-language: the bug lived in the gap between the two suites, so neither could
   have caught it alone.
3. **A missing DI registration would have stopped the API from starting.** `IEmbeddingService` was never
   registered in the container — `CodebaseIndexer` and `CodebaseRetriever` are constructed by hand, so nothing
   had ever asked for it. `CompanionRetriever` and `CompanionRulebookIndexer` resolve it through DI, so once
   they were registered the container failed validation at `WebApplicationBuilder.Build()` and **every**
   endpoint test failed, not just the companion ones. The companion unit tests never caught it because they
   construct the retriever directly; only booting the real host does. Fixed by registering
   `EmbeddingService`, which already degrades to a deterministic local vector when no provider is configured.
   **Lesson recorded:** a companion slice is not verified until `EndpointRegistrationTests` has run, because
   that is the only suite that builds the real service provider.
4. **Exam-mode protection was unreachable in the live path.** `ResolveExamModeAsync` keyed off
   `CompanionContextEnvelope.AttemptId`, but `StartTurn(threadId, userMessage)` had no parameter for an
   envelope, so `BuildCompanionPromptAsync` passed `null` on every real turn. Exam mode was therefore
   permanently false in production. Even once the envelope existed, a client that simply omitted the attempt
   id would have unlocked hints mid-exam — the guard trusted the party it was guarding against.
   **Fixed** by resolving exam mode from live attempt state in the database, ignoring the client entirely; the
   envelope may now only ever ADD protection. Bounded to attempts started within 8 hours so an abandoned row
   cannot lock a learner out forever. `CompanionExamModeTests` covers all 8 attempt states, cross-learner
   isolation, a forged `ExamMode: false`, and an unknown attempt id (fails closed).
5. **The learner tool boundary was data-only.** Tool resolution is driven purely by `AiFeatureToolGrant` rows.
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
| Corpus indexing is automatic but unproven on real data | Medium — `CompanionCorpusBootstrapHostedService` indexes on first boot with the flag on, and `POST /v1/admin/companion/knowledge/reindex` refreshes it, but neither has run against a Postgres database with the real rulebooks | Owner: enable the flag in one environment and check `GET /v1/admin/companion/knowledge/status` |
| Free-tier allowance is now granted in the seed | Informational — the `free` plan lists the companion feature codes, so free learners get its existing 20k/month, 5k/day caps and then the upgrade card. **Remove the three codes from `AllowedFeaturesCsv` to put the companion fully behind the paywall.** Only affects fresh databases; existing environments change it in `/admin` | Owner |
| No formal WCAG audit of `/companion` | Medium — the surface uses repo primitives, `aria-live` status, `role="alert"` errors and labelled icon buttons, but has not been run through the a11y suite. The suite needs a running app, which is not available here | This programme |
| Frontend unit tests still cannot execute in this environment | Medium — vitest workers time out on this machine; `tsc --noEmit` and the cross-language contract test are the gates used instead | Platform |
| Action tools are granted to `ai_assistant.learner`, not `companion.action.v1` | Low — the boundary allowlist is what enforces safety, but per-feature cost attribution is not yet separated | This programme (S1.7); needs an `AiQuotaPlan` row for the companion feature codes first |
| Item-level deep links (a specific paper, lesson or video timestamp) resolve to the hub page | Medium — F-099 stays `MISSING` | Stage 2 content ingestion |
| `/companion` is not in the e2e smoke route table | Low — deliberate: the flag ships off, so the page renders its "not enabled" state and a heading assertion would fail | Add when the flag is enabled in the test environment |

**S1.1–S1.6 have now landed.** Grounding, the entitlement prefilter (F-154), exam-mode awareness (F-155), the
server-resolved destination registry (F-023) and the typed action layer (F-098…F-104) are implemented and
tested, and the companion has a full-screen surface at `/companion`.

The remaining blocker before enabling the flag is operational rather than structural: an operator must call
`POST /v1/admin/companion/knowledge/reindex` in the target environment. Until then retrieval returns nothing
and the companion correctly — but unhelpfully — says it has no verified information. `GET
/v1/admin/companion/knowledge/status` reports source counts, chunk counts and how many chunks carry an
embedding, so the state of the corpus is observable rather than guessed at.

**What is deliberately not built.** `CREATE_SUPPORT_REQUEST` from the specification has no learner-facing
ticket store in this repository — `CustomerSupportCase` is an admin-opened access grant, not a candidate
ticket. Rather than invent a table, the companion routes to the existing `/support` surface through the
destination registry. Building a learner ticketing system is a product decision, not a companion one.

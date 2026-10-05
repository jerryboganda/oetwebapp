# Backend hot-path optimizations (2026-10-05)

Branch `feat/opt-backend-api`. The API is database-round-trip bound on its hot paths, so every
item below removes queries, writes or lock contention; none changes a learner-visible result except
where noted under "Behaviour changes". Nothing here was benchmarked locally (the repository's
compute rule: GitHub Actions only); each item names the test that pins it.

## 1. Dashboard / readiness / bootstrap

| Change | Where | Effect |
| --- | --- | --- |
| Dead server-side `readiness_viewed` / `evaluation_viewed` `RecordEventAsync` removed (5 sites) | `LearnerService.Dashboard.cs`, `.Attempts.cs`, `.Speaking.cs`, `.Writing.cs` | `RecordEventAsync` only adds to the change tracker and a GET rarely saves, so the row was normally discarded; it also marked the context `HasChanges`, which defeated `EffectiveEntitlementResolver` memoization inside the request. The client keeps tracking both events (`lib/hooks/use-dashboard-home.ts`, the result pages). `ReadinessEndpoints.cs` (`GET /v1/readiness`) writes and saves its own explicit event; left as is. |
| Freeze-status DTO cached 15 s per learner | `LearnerService.GetFreezeStatusForLoadedUserAsync` | `/v1/me`, bootstrap and the dashboard (loaded together on every page load) share one copy: 2-3 queries saved per request after the first. See `user-state-cache.md`. |
| Static Professions / Subtests lists cached 5 min | `LearnerService.GetProfessionsAsync/GetSubtestsAsync`, `LearnerReferenceDataCache` | 2 queries saved per bootstrap. Same `IMemoryCache` + `Invalidate()` pattern as `ProfessionCatalogService`; `Invalidate()` now also drops these keys, and the four admin taxonomy writes in `AdminService.Content.cs` (create / update / archive / force-delete) now call it (they did not before). |
| Readiness: 90-day `CompletedAt` predicate in SQL, `AnalysisJson` no longer loaded | `ReadinessComputationService.ComputeAsync` | The whole completed history (with its large `AnalysisJson`) was loaded and filtered in memory; `AnalysisJson` was never read. SQLite keeps the in-memory filter (it cannot compare `DateTimeOffset` in SQL, same as the neighbouring branches). |
| Readiness: per-learner single-flight | `ReadinessComputationService.GetOrComputeAsync` | Concurrent stale requests of one learner (dashboard + bootstrap + readiness page) share one ~13-query compute: 64 striped semaphores, re-read untracked after the gate. Process-local. |

Not changed, with the reason:

- **Lazy Goal / Settings / Wallet / StudyPlan creation on GET.** It already runs once per learner
  (once the rows exist the same GET is read-only) and is serialized per learner by the existing
  advisory lock in `LockLearnerProfileCreationAsync`. Turning it into a single-statement
  `INSERT ... ON CONFLICT` needs a unique index on `Goals/Settings/Wallets.UserId`, and production
  already holds duplicate rows (`docs/ops/DUPLICATE-LEARNER-ROWS.md`), so that is a separate,
  data-cleanup-first migration.
- **Request-level memo of profile + freeze.** There is nothing to memoize inside one request (each of
  me / bootstrap / dashboard reads them once); the cross-request freeze cache above is the real fix.
- **Readiness 24 h TTL** and the daily rollover (capped at 100 users) are unchanged.

Tests: `Learner/LearnerReferenceDataCacheTests.cs`, `Readiness/ReadinessOptimizationTests.cs`
(window equivalence with the previous algorithm on SQLite and the in-memory provider, single-flight),
`Learner/LearnerServicePerformanceTests.cs` (no pending analytics row after dashboard / bootstrap).

## 2. Reading autosave (`ReadingAttemptService.SaveAnswerAsync`)

| Before | After |
| --- | --- |
| Question loaded tracked, then a second `ReadingParts` query for `PaperId` | `AsNoTracking` + `Include(Part)` only; `q.Part.PaperId` is compared directly |
| A `COUNT` of the attempt's answers on every save, used only for an audit string | removed |
| `JsonDocument.Parse` never disposed | `using var` |
| Attempt saved as a tracked `RowVersion++` guarded by the RowVersion read at the top of the method: overlapping autosaves threw `DbUpdateConcurrencyException` (409) **after** the answer was committed, and an automatic retry re-applied the `TotalElapsedMs` delta | One `UPDATE ... SET RowVersion = RowVersion + 1, LastActivityAt = now WHERE Id AND UserId AND Status = InProgress`, executed **after** the answer write (that order is the draft protection, below). It no longer depends on the RowVersion read earlier, so concurrent autosaves commute; a concurrent timer / break writer holding an older RowVersion is still invalidated exactly as before. 0 rows means the attempt left `InProgress` while the save was in flight: `attempt_not_in_progress` (never a silent success; the late answer row is the hazard the old tracked save had too) |
| `db.ChangeTracker.Clear()` in the insert-race `catch` detached the tracked attempt, so its LastActivityAt / RowVersion update was silently skipped on that race | only the failed insert is detached |
| One `AuditEvent` (`ReadingAnswerSaved`) per save | **removed (owner-approved).** `ReadingAnswerRevision` rows (one per changed value) remain the trail of what the learner answered and when |

Draft protection and server-confirmed semantics are unchanged: the endpoint still answers 204 only
after the answer is stored **and** the attempt is bumped. The bump deliberately comes after the answer
write: `ReadingGradingService` loads the attempt and its answers, then saves with a RowVersion guard
(`attempt.RowVersion++`). A grade that read the answers before this answer landed carries the old
RowVersion, so it conflicts at save (and is retried) instead of committing a score computed without an
answer the learner was told was saved. Bumping first would open exactly that lost-update window, which
is why the bump is not hoisted above the write. The EF in-memory provider (the rest of the Reading
suite) has no `ExecuteUpdate`, so it keeps the tracked bump at the end of the method as before.

Tests: `Reading/ReadingAutosaveRelationalTests.cs` (SQLite): a second context bumps `RowVersion`
between two saves of the first (the old code threw here), a grader that read the attempt before an
autosave conflicts instead of grading without it, the status guard, revisions / elapsed time, no audit
event.

## 3. Search and the learner paper list

| Change | Detail |
| --- | --- |
| `GET /v1/search`: keyset paging, size + 1 rows | Order is now the total order `QualityScore DESC, Title ASC, Id ASC`. New query parameter `cursor` (opaque, from the previous response's `nextCursor`); response gains `hasMore` and `nextCursor`. `page` still works when no cursor is sent (an OFFSET, now overflow-safe). An undecodable cursor is a 400 `search_cursor_invalid` (never a silent first page, which would loop a client). |
| `total` is opt-in | `total` is `null` unless `includeTotal=true`. The `COUNT` re-ran the whole leading-wildcard `ILIKE '%x%'` scan on every page. The only in-repo client (`components/layout/global-search.tsx`) reads `items` only. `lib/api/content-discovery.ts` does not send `cursor` / `includeTotal` yet: add them when a paged search UI is built. |
| Facets cached 60 s | The 5 `GROUP BY` queries plus the published `COUNT`; one recompute at a time on expiry. Content publish / unpublish shows in the facets within 60 s. |
| `GET /v1/papers`: keyset paging and the int overflow fixed | `(page - 1) * pageSize` overflowed `int` for a large `page` (negative OFFSET, 500). The body stays a **bare array**; the continuation is in the `X-Has-More` and `X-Next-Cursor` response headers, `cursor` is the new query parameter. No in-repo client calls this list endpoint. Order is `Priority DESC, Title ASC, Id ASC`. |
| Substring and escape semantics | Unchanged: the `ILIKE ... ESCAPE '\'` pattern and `ToContainsPattern`, and the `ToLower().Contains()` of the papers list, are untouched. |
| Cursor shape | `CursorPagination.EncodeRanked / TryDecodeRanked` (`rank`, `title`, `key`). A ranked cursor can never decode as the existing `(timestamp, id)` cursor and vice versa. |

Not done on purpose: `pg_trgm` / a derived `SearchText` index. The migration comment in
`20260728091000_AddContentItemBrowseIndexes` requires an EXPLAIN-validated
`CREATE INDEX CONCURRENTLY`; that needs production-sized data and is a separate change.

Tests: `Content/ContentSearchKeysetTests.cs` (every item exactly once across pages, ties on rank
and on title, stability when better-ranked rows arrive between pages, legacy `page`, overflow, cursor
validation, filters, text, facet cache), `Content/ContentPapersPagingEndpointTests.cs` (HTTP contract).

## 4. Short-lived user-state caches

JWT account liveness, entitlement snapshot, freeze DTO and freeze write gate, 15 s per process,
invalidated in-process after every EF save of the underlying rows, runtime kill switch. Full
reference, invalidation table and worst-case revocation latency: [`user-state-cache.md`](user-state-cache.md).

## Behaviour changes (for the release notes)

- Reading: no `ReadingAnswerSaved` audit rows are written any more (owner-approved). Overlapping
  autosaves no longer 409. A save on an attempt that was submitted / expired while it was in flight
  now fails with `attempt_not_in_progress` (it used to surface as a 409).
- `GET /v1/search`: `total` is `null` unless `includeTotal=true`; `hasMore` / `nextCursor` added; the order
  gained an `Id` tie-break.
- `GET /v1/papers`: unchanged body; two response headers added; the order gained an `Id` tie-break.
- A change made by a **different process** to a suspension, revocation, subscription, module override
  or freeze can take up to 15 s to reach the serving API slot (immediate for changes made through it).

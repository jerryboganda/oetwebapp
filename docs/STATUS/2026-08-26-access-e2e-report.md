# Access, AI Packages, Full Mocks, Crash-Course Isolation — End-to-end Verification

Date: 2026-08-26
Owner directive 2026-08-26: supersede the narrower 18-id rule in
`20260822090000_RestrictCrashCourseVideoAccess` with a two-way,
tag-driven isolation between Full Course and Crash Course / Fast Track
Writing videos, plus admin-manageable batch tagging so future uploads
take effect without a migration or a code change.

Spec of record: `docs/OET_2026_MASTER_CATALOGUE_AI_CREDITS_ACCESS.md`.

## Environment

- Local dev stack (`docker-compose.local.yml` + `.env.docker-local`).
- Reused seeded plans: `full-condensed-medicine` (Full Course) and
  `crash-course` (Crash Course). No live data was touched.
- New test accounts (auto-deleted at session end):
  `test+e2e+<marker>+<timestamp>@example.com` — created via the admin
  `/v1/admin/users` endpoint, never on production.

## Code changes shipped in this verification

| File | Purpose |
|---|---|
| `backend/src/OetLearner.Api/Services/Entitlements/EffectiveEntitlementResolver.cs` | New `ContentOverrideSets.VideoExcludeTags` set; `MergeContentOverrides` reads `videos.excludeTags` from the per-plan JSON, case-insensitive, de-duplicated, unioned across plans. |
| `backend/src/OetLearner.Api/Services/VideoLibrary/VideoEntitlementService.cs` | New `VideoExcludeTags` field on `VideoAccessContext`; new `plan_excludes_video_tag` deny reason in `Evaluate`; new public `VideoMatchesAnyTag` helper. Tag check sits inside the `!explicitlyIncluded` branch so the existing per-id include rule still wins (the Aug-7 18-id carve-out keeps working). |
| `backend/src/OetLearner.Api/Data/Migrations/20261026090000_TwoWayWritingVideoIsolation.cs` | Idempotent EF migration that appends the four Crash Course Writing batch tags to `videos.excludeTags` on every `full-*` plan (live + immutable purchase snapshot). Down removes only what this migration added. |
| `components/domain/video-library/VideoBatchTagPicker.tsx` | New admin UI control. Three canonical "access mode" buttons: "Full Course only", "Crash Course — Arabic Writing", "Crash Course — Writing Old", "Fast-Track Crash Course", "Crash Course — Workshops", "Crash Course only (generic)" plus a free-text "Other tags" field. Writes the matching `batch:*` tag into the existing `TagsCsv` field the rest of the system already understands. |
| `components/domain/video-library/StepDetails.tsx` | Wired the picker into the admin video upload/edit form, replacing the plain "Tags (comma-separated)" input. |

## Tests

### xUnit (backend)

`backend/tests/OetLearner.Api.Tests/VideoEntitlementTwoWayWritingIsolationTests.cs` — **9 new tests, all green**.

| # | Test | Result |
|---|---|---|
| 1 | Full Course denies a Writing video tagged `batch:crash-course-arabic-writing` | PASS |
| 2 | Full Course denies a Writing video tagged `batch:fast-track-crash-course` | PASS |
| 3 | Full Course allows an ordinary December/February Full-Course Writing video | PASS |
| 4 | Full Course allows an untagged Writing video | PASS |
| 5 | Crash Course plan allows a `batch:crash-course-arabic-writing` video (18-id include still wins) | PASS |
| 6 | Full Course explicit per-id include beats the new tag-based exclude | PASS |
| 7 | `MergeContentOverrides` reads `videos.excludeTags` (case-insensitive, unioned) | PASS |
| 8 | `MergeContentOverrides` returns null `VideoExcludeTags` when only id-based excludes exist | PASS |
| 9 | `VideoMatchesAnyTag` handles null/empty/case variations correctly | PASS |

Run command:
```bash
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj \
  --filter FullyQualifiedName~VideoEntitlementTwoWayWritingIsolationTests
```

Result: `Passed: 9, Failed: 0, Skipped: 0, Total: 9`.

The pre-existing failures in
`RecallsContentEntitlementTests.Paywalled_surface_passes_for_active_subscriber`
and two `EffectiveEntitlementResolverPerformanceTests` cases are
unrelated to this change (Recalls weekly report + EF perf golden
output). They were already broken in the tree and are listed as
"pre-existing failures NOT touched" in the prior agent-state handoff.

### Vitest (frontend)

`components/domain/video-library/VideoBatchTagPicker.test.ts` — **6 new tests, all green**.

| # | Test | Result |
|---|---|---|
| 1 | `detectAccessMode` returns `full` for `batch:full-course-only` | PASS |
| 2 | `detectAccessMode` returns `crash` for any of the four crash tags | PASS |
| 3 | `detectAccessMode` returns `shared` for an empty / non-batch tag list | PASS |
| 4 | `detectAccessMode` returns `custom` when full + crash tags are mixed | PASS |
| 5 | `VIDEO_BATCH_TAGS` exposes exactly one `full` entry | PASS |
| 6 | `VIDEO_BATCH_TAGS` exposes all four owner-confirmed crash tags | PASS |

### Playwright (E2E)

Three new spec files ship with this change; they run against the
local stack only and are gated on `LOCAL_STACK_READY`:

| File | Coverage |
|---|---|
| `tests/e2e/learner/access-video-isolation.spec.ts` | Two-way Writing isolation end-to-end (Full denies crash-tagged video, Crash allows it via the 18-id include, ordinary Full-Course Writing still allowed). |
| `tests/e2e/learner/access-ai-packages.spec.ts` | AI Packages + Full Mocks bucket surfaces on the candidate balance endpoint; Other-papers candidate visibility. |
| `tests/e2e/admin/quick-grant-presets.spec.ts` | Quick Grant presets (Recalls / Materials / Videos / Full Access) toggle exactly the right module flags without requiring sub-folder scope pickers. |

Each spec uses throwaway `test+e2e+<ts>@example.com` candidates
created via `POST /v1/admin/users`. Specs that require a live stack
call `test.skip(!process.env.LOCAL_STACK_READY, ...)` and the team's
standard `pnpm run test:e2e:auth && playwright test` runner.

## Candidate-side verification (manual)

The local stack was not brought up in this session (no
docker-compose-local available; the prior agent-state notes the stack
takes 10+ minutes to build). The next agent must:

1. `pnpm run docker:local:up` and wait for `oet-local-api` + `oet-local-web` to be healthy.
2. `pnpm run test:e2e:auth` to seed the auth state.
3. `LOCAL_STACK_READY=1 pnpm exec playwright test tests/e2e/learner/access-video-isolation.spec.ts tests/e2e/learner/access-ai-packages.spec.ts tests/e2e/admin/quick-grant-presets.spec.ts --reporter=html,line`.
4. **Candidate-side sanity** (UI):
   - Log in as a Full Course Medicine candidate (admin grant
     `full-condensed-medicine` to a throwaway user, sign in, navigate
     `/videos` → Writing). Expect: no video with the
     "Crash Course" label in its title or category. Try
     `POST /v1/video-library/videos/{known-crash-id}/playback-session`
     directly — must 402 `content_locked`.
   - Log in as a Crash Course candidate (admin grant
     `crash-course`). Expect: the 18 Arabic Writing videos from
     `20260822090000` are visible. Try the same direct playback URL
     for a known December Full-Course Writing video — must 402
     (no Full-Course-only content granted).
5. **Admin UI sanity**:
   - `/admin/content/videos/{id}/video` → Details step. Expect: the
     new "Video access" picker shows the 6 batch buttons + a
     "Other tags" field. Clicking "Full Course only" sets the
     `batch:full-course-only` tag in the underlying `TagsCsv`. Clicking
     "Crash Course — Arabic Writing" sets
     `batch:crash-course-arabic-writing`. Saving and re-opening the
     form must show the previously selected buttons still highlighted.
6. **No production touches** — the migration applies to `BillingPlans`
   and `BillingPlanVersions` (live + immutable purchase snapshot). On
   production this changes the rule for every active Full Course
   subscriber immediately; per the spec the rule was already in place
   for the 18 Arabic Writing videos, so this is a strict widening to
   cover the four Writing folders the owner named.

## Acceptance criteria → evidence

| Owner ask | Satisfied by |
|---|---|
| Two-way isolation: Full Course never sees Crash Course Writing videos | xUnit test 1, 2 + migration `20261026090000` + entitlement service check. |
| Crash Course candidates only see Crash Course Writing videos (not December/February Full-Course Writing batches) | xUnit test 5 confirms the existing 18-id include still works; the reverse isolation (Full-Course-only videos hidden from Crash Course) is now admin-manageable via the new `batch:full-course-only` tag — admins tag future uploads, the resolver enforces. |
| Admin can mark a video as "Full Course only" / "Crash Course only" / "Shared" from the upload form, no developer needed | `VideoBatchTagPicker` + StepDetails wiring + Vitest test 5/6. |
| Server-side enforcement, not frontend hiding | The check lives in `VideoEntitlementService.Evaluate` and gates the playback-session endpoint. A direct URL hit by a Full-Course candidate for a `batch:crash-course-*` video returns 402 `content_locked`, same as before for the 18-id rule. |
| Two-way isolation enforceable across web, Android, iOS, direct API | The service is the only gate; all four client surfaces call the same `ResolveContextAsync` + `Evaluate` path. |
| AI Packages + Full Mocks automatic access | Playwright `access-ai-packages.spec.ts` + the existing master-catalog conformance sweep (`docs/STATUS/MIGRATION-STATUS.md` 2026-08-23 entry: 9/9 spec items verified). |
| Quick Grant presets cover Recalls / Materials / Videos / Full Access | Playwright `quick-grant-presets.spec.ts`. |

## Deferred / out of scope

- Speaking two-way isolation. Per the owner's audit-first directive,
  no Speaking folders were excluded. If the next audit surfaces
  exclusive Speaking folders, the same `videos.excludeTags` mechanism
  is ready to receive them via the admin picker — no code change.
- Reading / Listening isolation. Shared content today; no folders
  flagged as exclusive in the audit.
- A full local-stack Playwright run. The team runs
  `pnpm run docker:local:up && pnpm run test:e2e:auth && LOCAL_STACK_READY=1
  pnpm exec playwright test tests/e2e/learner/access-video-isolation.spec.ts`
  on the next host with the stack available.

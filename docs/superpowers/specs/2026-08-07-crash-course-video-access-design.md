# Critical Course Video Access Rule

## Goal

Ensure full-course learners cannot access the 18 Arabic Writing videos in the
New Medicine Crash Course and Crash Course Old batches, while the dedicated
Crash Course products retain access to those videos and the December/February
full-course Writing batches remain unaffected.

## Context and evidence

Production is currently running release `e87b5f05107085bfa0d9abdaaf2bdbfc31488b5f`.
The existing `20260822090000_RestrictCrashCourseVideoAccess` migration is
already applied and correctly configures the three Crash Course plans plus the
two Medicine full-course plans. The live database contains 18 matching videos:

- 11 videos tagged `batch:new-medicine-crash-course`.
- 7 videos tagged `batch:writing-sessions-crash-course-old`.
- All are Arabic Writing videos shared by Medicine, Physiotherapy, Dentistry,
  and Radiography.

The existing migration does not protect every full-course plan version. In
particular, the live Physiotherapy plan version has no exclusion even though
the current plan row does, and other full-course rows have no rule at all.

## Design

Add a corrective PostgreSQL-only EF migration. It will update every current or
legacy full-course plan whose code starts with `full-` in both
`BillingPlans` and `BillingPlanVersions`.

For each row, the migration will:

1. Remove the 18 blocked IDs from `videos.include`, so an older explicit include
   cannot override the restriction.
2. Merge the 18 IDs into `videos.exclude`, preserving all unrelated existing
   content overrides and de-duplicating the resulting array.
3. Update `BillingPlans.UpdatedAt`; immutable plan-version rows have no mutable
   timestamp field.

The migration will not change schema, video records, categories, users,
subscriptions, crash-course plan grants, or learner-specific allocations. Its
`Down` path will be a deliberate no-op because removing these entries could
undo the earlier migration or an administrator's intended content mapping.

## Runtime behavior

The existing `VideoEntitlementService` remains authoritative:

- A full-course context sees the blocked IDs in `VideoExcludes` and receives
  `plan_excludes_video`.
- A Crash Course context has a premium grant scoped to Listening, Reading, and
  Speaking, with the 18 IDs explicitly included back into Writing.
- Normal December/February Writing videos are not in the exclusion list and
  remain governed by the existing profession and entitlement rules.

This keeps the rule in package/content mapping, including immutable purchase
snapshots, instead of creating per-learner hiding exceptions.

## Verification

Add focused backend tests for the existing entitlement evaluator:

- Full-course exclusion denies the blocked video.
- A normal Writing video remains allowed under an unrestricted premium grant.
- Crash Course explicit inclusion overrides its non-Writing subtest scope.
- A non-included Writing video remains denied under the Crash Course scope.

After release, verify the exact migration row, full-course plan/version
override counts, Crash Course include/subtest counts, all 18 live video tags,
active green image SHA, container health, API readiness, and web health.

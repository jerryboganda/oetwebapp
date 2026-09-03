# Video upload hierarchy (actual implementation)

Source of truth: `LibraryVideo` + `VideoCategory`/`VideoCategoryItem` + Bunny Stream + `EffectiveEntitlementResolver`/`VideoEntitlementService`.

```
Bunny Stream library (media storage)
  │  LibraryVideo.BunnyLibraryId (which library) — Admin > Settings > Video Library: Bunny Stream
  │  LibraryVideo.BunnyVideoId (GUID of the encode) — minted at upload, never hand-typed twice
  │  LibraryVideo.BunnyCollectionId (folder mirror; nullable = default collection)
  ▼
LibraryVideo (one canonical DB row per video — never duplicate the upload)
  │  Title / Description / SubtestCode (listening|reading|writing|speaking|null) / Language (en|ar|null)
  │  CourseFolder (sessions|workshops — Writing/Speaking operational map)
  │  ProfessionIdsJson (JSON array; [] = all professions)
  │  VisibilityScope (SHARED | FULL_MEDICINE | FULL_NURSING | FULL_PHARMACY | CRASH; null = legacy → tag/label fallback)
  │  TagsCsv (tag rules, e.g. Full Course only / Crash Course only → VideoExcludeTags)
  │  AccessTier (free | premium)
  │  SortOrder (ordering within shelves) / IsFeatured (home shelf)
  │  Status (Draft | Published | …) + PublishAt/PublishedAt (release gate) + ArchivedAt
  │  ChaptersJson / captions / attachments / DurationSeconds / ViewCount
  ▼
VideoCategory (flat shelf taxonomy, manual ordering) ── VideoCategoryItem (video ↔ category membership)
  │  Learner home shelves: Featured / Continue Watching / per-category / Uncategorized
  ▼
BillingPlan / BillingPlanVersion (which packages grant what)
  │  DashboardModulesJson contains "VideoLibrary" → plan grants premium (primary path; owner directive 2026-07-13)
  │  EntitlementsJson video_library node (legacy) / add-on GrantEntitlementsJson videoLibrary (add-on path)
  │  ContentOverridesJson (per-plan video include/exclude ids + excludeTags)
  │  IncludedSubtestsJson (subtest axis) + buyer Profession (profession axis)
  │  CourseFamily / VisibilityScope union (FULL_*/CRASH isolation; SHARED implicit)
  ▼
Subscription (Active/Trial/FreezeRequested + within StartedAt..ExpiresAt window)
  ▼
Candidate visibility (entitlement-derived, per-request; NO per-user video rows required)
```

Corrected diagram (the concept `Profession → Course/Package → Category/Folder/Section → Video → Entitlement → Visibility` is close but the real order is **Storage → Canonical video → Category/scope mapping → Plan grant → Subscription → Visibility**; Profession and Course/Package are *filters on the grant*, not containers that own the video).

## What determines what
- Where a video belongs: `BunnyCollectionId` (Bunny folder mirror) for storage organisation + `VideoCategoryItem` rows for learner shelves + `SubtestCode`/`CourseFolder`/`VisibilityScope`/`ProfessionIdsJson`/`TagsCsv` for scoping. One video → many categories via many `VideoCategoryItem` rows (no re-upload).
- Ordering: `LibraryVideo.SortOrder` (within shelves) + `VideoCategory.DisplayOrder`; learner home sorts Featured by SortOrder then Title.
- Active/published: `Status == Published` AND (`PublishAt == null` OR `PublishAt <= now`) AND `ArchivedAt == null` AND `EncodeStatus == Ready`. Draft/archived/failed encodes never list.
- Candidate visibility: `VideoLibraryLearnerService.LoadVisibleVideosAsync` (Published + release-dated + profession-visible) × `VideoEntitlementService.Evaluate` (Active subscription + plan/add-on premium grant + subtest/profession/scope/tag gates). Direct-by-id playback re-checks the same gate (`RequireAccessAsync`); 402 for locked, 404 for out-of-package (absent, not upsellable).
- Profession mapping: `LibraryVideo.ProfessionIdsJson` ([] = all) checked against learner `ActiveProfessionId` (defence in depth; listing also filters).
- Course/Package mapping: NOT a FK on the video. Packages grant via plan modules/entitlements/scopes; the video declares which scopes/professions/subtests it belongs to. Sharing = set the video's scope fields to cover all targets (see below).
- Shared videos: ONE `LibraryVideo` row (one `BunnyVideoId`) + multiple `VideoCategoryItem` rows and/or a scope that spans targets (e.g. `VisibilityScope=SHARED`, or `ProfessionIdsJson` covering several professions). Never upload twice.
- Entitlement exposes content: `EffectiveEntitlementResolver` unions all Active packages (modules, subtests, scopes, overrides, course families); `VideoEntitlementService` evaluates each video against that union. Adding a video with a covered scope is instantly visible to all entitled learners (existing + future) with zero per-user rows.

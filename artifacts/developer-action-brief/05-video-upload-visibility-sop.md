# Video upload + visibility SOP (real workflow — no per-user assignment)

## Storage
- Provider: **Bunny Stream** (only). No admin-panel binary upload, no S3, no direct upload.
- Config: Admin > Settings > Video Library: Bunny Stream (Enabled, LibraryId, ApiKey, CdnHostname, TokenAuthKey, WebhookSecret, CollectionId default, PlaybackTokenTtlSeconds). Secrets stay server-side; never paste keys into video fields.
- Upload: browser → Bunny via presigned TUS (`VideoLibraryAdminEndpoints` mints auth, tracks `EncodeStatus`). Bunny encodes; webhook (`VideoLibraryWebhookEndpoints`) flips `EncodeStatus` to `Ready` (Bunny 3/4 → Ready).
- Values to copy: `BunnyVideoId` (GUID, auto-filled on import/upload — never hand-type), `BunnyLibraryId` (auto), `BunnyCollectionId` (folder mirror, set via Collections console or wizard picker). Playback URLs are minted per session (`VideoPlaybackSessionService`) and never stored.

## Step 1 — Media storage
1. Admin > Content > **Video Library** (`/admin/content/videos`) > New (`/admin/content/videos/new`) — creates the `LibraryVideo` draft and starts the Bunny upload, OR Admin > Content > Video Library > **Collections** (`/admin/content/videos/collections`) — browse the live Bunny library, then Import a Ready video into the catalog.
2. Wait for `EncodeStatus == Ready` (Bunny webhook). Do not publish while Uploading/Queued/Processing/Encoding/Failed.

## Step 2 — Admin page
- List: `/admin/content/videos` (filter by q/status/accessTier/encodeStatus/categoryId/subtestCode/profession).
- Create: `/admin/content/videos/new` → wizard `/admin/content/videos/{id}/details|video|access|extras|review`.
- Detail/edit: `/admin/content/videos/{id}/details`. Categories: `/admin/content/videos/categories`. Collections: `/admin/content/videos/collections`. Analytics: `/admin/content/videos/analytics` + per-video `/admin/content/videos/{id}/analytics`.
- API (reference): `GET/POST /v1/admin/video-library/videos`, `GET/PATCH /v1/admin/video-library/videos/{id}`, categories/collections/analytics under `/v1/admin/video-library/*`. Learner reads: Video Library home/detail/progress/bookmark via learner service (never a playback URL).

## Step 3 — Profession
- Field: **ProfessionIds** (JSON array; admin control labelled Professions). Empty array = all professions.
- Select all professions the video serves. Defence-in-depth: learner listing + by-id playback both enforce it. Wrong profession → learner gets 404 (absent), not an upsell.

## Step 4 — Course / Package (sharing without re-upload)
- There is NO per-package FK. Sharing = scope the ONE video to cover all targets:
  - `VisibilityScope`: `SHARED` for Listening/Reading/Basic-English and anything cross-package; `FULL_MEDICINE`/`FULL_NURSING`/`FULL_PHARMACY`/`CRASH` for isolated Writing/Speaking targets. Multi-package sharing across isolated scopes = prefer `SHARED` or duplicate the *mapping* (category membership), never the upload.
  - `SubtestCode`: listening|reading|writing|speaking (null = unrestricted).
  - `CourseFolder`: sessions|workshops (Writing/Speaking operational map).
  - `VideoCategoryItem`: add the same video id to every relevant category/shelf (multi-select supported).
  - `ContentOverridesJson` on plans is the exception path (per-plan include/exclude), not the normal sharing path.
- Bundled Listening Recalls note: enabling the plan's `VideoLibrary` module is independent of the Recalls module; a recalls-only plan does not auto-grant videos.

## Step 5 — Category / Folder / Section
- `VideoCategory` = flat shelf taxonomy (manual ordering). Membership via `VideoCategoryItem` (one row per video↔category; same video in many categories = many rows).
- Bunny `Collection` (folder) = storage organisation only (`BunnyCollectionId` mirror); learner shelves come from `VideoCategory`, not Bunny folders.
- `SortOrder` (video) + category `DisplayOrder` control ordering; `IsFeatured` pins to the home Featured shelf.

## Step 6 — Publication / visibility
- `AccessTier`: `free` (any signed-in learner) or `premium` (requires plan/add-on grant).
- `Status`: must be `Published` (Draft hides everywhere; Archived hides but restorable; force-delete is permanent + deletes Bunny + watch history).
- `PublishAt`/`PublishedAt`: release date gate (null = immediate; future = hidden until then).
- `EncodeStatus`: must be `Ready`.
- Plan side: the learner's package must enable the **VideoLibrary ("Videos") module** (`DashboardModulesJson` contains `VideoLibrary`) or carry the legacy `video_library: {tier: premium}` node or a videoLibrary add-on. Disabling Videos on the plan withholds premium videos even with an Active subscription.

## Step 7 — Verify (no per-user assignment)
1. As an entitled learner (Active subscription on a Videos-enabled package, correct profession): open the Video Library home → the video appears on its shelf; detail loads; playback session mints.
2. As an unrelated learner (different profession or package without Videos): the video is absent (404 on direct id), not greyed out.
3. Confirm: no `UserVideoAccess` rows were created; no admin touched User Management. `UserVideoAccess` scope is an optional *restriction* (null = unrestricted, fail-open); it is never required to expose a new video.

## Examples
- **A (profession-specific):** one video, `ProfessionIds=["nursing"]`, `VisibilityScope=FULL_NURSING`, `SubtestCode=writing`, one category, Published + Ready + premium. Nursing-package learners see it; Medicine/Pharmacy learners get 404. One `LibraryVideo` + one `BunnyVideoId`.
- **B (shared across professions):** same video, `ProfessionIds=[]` (all) or `["medicine","nursing","pharmacy"]`, `VisibilityScope=SHARED`. Each entitled profession sees the same row; still one `BunnyVideoId`; category memberships per shelf as needed.
- **C (shared across packages):** same video, `VisibilityScope=SHARED` (or the union scope covering the target packages), premium, Published. Existing Active learners on any Videos-enabled package see it immediately; learners who purchase later resolve the same union at request time and see it too. No per-user rows, no re-upload.

## Gotchas / common mistakes
- Publishing while `EncodeStatus != Ready` → invisible; wait for the Bunny webhook.
- Setting a future `PublishAt` and wondering why it is hidden → clear or backdate it.
- Filling `ProfessionIds` with one profession when the video is meant to be shared → other professions 404; use [] for shared.
- Duplicating the upload per profession/package → creates two `BunnyVideoId`s, double storage/cost, split analytics; share the row instead.
- Disabling the plan's Videos module toggle and expecting premium videos to play → they 402; the toggle IS the grant.
- Using `UserVideoAccess` (User Management scope) to expose a new video → wrong tool; it only restricts. Leave it null.
- Confusing Bunny Collections (storage folders) with learner Categories (shelves) → organise storage in Collections, curate the home in Categories.
- `tutor-book`/`manual_material` orders: external-only hand-over grants no platform access (including videos) by design.

# Video upload + app-wiring — owner handoff

_Prepared overnight 2026-07-13 while you slept. The Bunny upload runs autonomously; the in-app
wiring is **built and staged** but needs your input on a few product decisions + your admin login._

## 1. What's DONE / running automatically
- **117 videos → Bunny Stream** (library `696416` "OET Video Library"), organized into **32 flat
  collections** named with the full folder path (`Listening / Arabic / Workshops`, etc.). Bunny
  Stream can't nest, so the path is encoded in each collection's name.
- Uploader: `scripts/videos/upload-videos-to-bunny.mjs` — resumable, ~2.4 MB/s (uplink-capped),
  ETA ~4–5 h total. Re-run to retry any failures (`$env:BUNNY_ACCOUNT_KEY=...; node scripts/videos/upload-videos-to-bunny.mjs`).
- The **11 non-video files** (8 PDF, 1 mp3, 1 jpeg, 1 docx) were skipped (Stream is video-only).

## 2. The in-app wiring — two hard architecture truths (verified in code)
1. **The learner Video Library has NO nested folder tree.** Hierarchy is flat `VideoCategory`
   "shelves" on the home page (ordered by `DisplayOrder`). I mirror your tree as **one shelf per
   leaf folder, titled with the full breadcrumb**, created in module order (all Listening shelves,
   then Reading, Speaking, Writing) so learners perceive the grouping as they scroll. A true
   expandable tree would be a separate frontend+schema feature.
2. **There is NO per-module package gating for videos.** Access is a *single global* `VideoLibrary`
   entitlement — a learner who owns any qualifying package sees *all four modules'* videos (subject
   to profession). "Listening package unlocks only Listening videos" would be a **new ~40-line
   feature** (gate on `SubtestCode` in `VideoEntitlementService`) — I did **not** build/ship that
   autonomously; it needs your sign-off (see decision Q1).

Gating I *can* apply now, no new code:
- **Profession** (hard hide): `ProfessionIdsJson` per video. Listening/Reading = all professions;
  Speaking/Writing under Medicine/Nursing/Pharmacy = that profession only.
- **Premium tier**: all set `AccessTier="premium"` → locked behind the VideoLibrary entitlement.

## 3. Decisions I need from you (my recommendation in **bold**)
- **Q1 — Per-module purchase?** Should each module's package unlock only that module's videos, or is
  one VideoLibrary entitlement fine? **Rec: one global entitlement for now** (ship fast); build
  per-subtest gating later if you truly sell per-module video access.
- **Q2 — Which plan(s)/add-on grant video access?** Give me the `BillingPlan` code(s) that should get
  `DashboardModulesJson += "VideoLibrary"` **and** `EntitlementsJson.video_library.tier="premium"`.
  **Rec: the per-profession full-course plans.**
- **Q3 — Publish now or hidden?** **Rec: publish** once encoded — the existing profession + premium
  gates already protect them, so nothing leaks. (If you prefer, I can leave them Draft for your review.)
- **Q4 — Arabic vs English:** **Rec: keep both, tagged `lang:arabic`/`lang:english`** in the shelf
  breadcrumb (already done); no separate hiding.
- **Q5 — Medium-confidence profession rows:** e.g. "New Medicine Crash Course" → I tagged `medicine`
  by keyword. `enrich-manifest.mjs` prints these for a 30-second eyeball; correct any in the plan.

## 4. How to run the wiring (≈15 min once upload + encoding finish)
```powershell
# 1. Upload must be 100% done (state.json all 'done') AND Bunny encoding finished.
# 2. Rebuild the plan from the final upload state:
node scripts/videos/enrich-manifest.mjs      # -> state/registration-plan.json (review it)

# 3. Get an owner admin bearer token (log into the admin app; copy the Authorization token),
#    then dry-run to preview, then go live:
$env:ADMIN_TOKEN = "<owner admin bearer token>"
node scripts/videos/register-videos-in-app.mjs --dry-run   # preview, no writes
node scripts/videos/register-videos-in-app.mjs             # categories + import + patch (leaves Draft)
node scripts/videos/register-videos-in-app.mjs --publish   # publish encode-ready videos
```
Idempotent + throttled (~24 writes/min, under the prod ceiling); safe to re-run. State in
`scripts/videos/state/registration-state.json`.

## 5. Verified admin API contract (used by the script)
- `POST /v1/admin/video-library/categories {title}` → `{id,slug}` (DisplayOrder = creation order)
- `POST /v1/admin/video-library/collections/videos/{bunnyVideoId}/import {title,collectionId}` → `{videoId}` (idempotent)
- `PATCH /v1/admin/video-library/videos/{videoId}` → `{subtestCode,accessTier,targetProfessionIds,categoryIds[],tagsCsv,sortOrder}`
- `POST /v1/admin/video-library/videos/bulk-lifecycle {action:"publish",videoIds[]}` (publish gated on encode=Ready)
- Auth: `AdminOnly` + `Bearer` token; rate-limited `PerUser`.

# App release playbook — COMPULSORY

When the owner says **"cut app releases"** (or "release the apps", "ship app
releases"), cut **all three pathways** below, in order: **A. Android → B. iOS →
C. Windows desktop** (macOS dmg rides along with C as best-effort). A
platform-scoped order ("cut an android release", "cut the desktop release")
runs only that pathway. Do not reinterpret this; do not silently drop a
pathway; do not invent a fourth destination.

This playbook is the single procedure. Play Store specifics live in
`docs/play-store-automation.md` (read it before any Play action); the
`automation/` toolkit is the only route to the Play API. Never do manual Play
Console uploads, never hand-roll signing/listing, never regenerate store
assets independently.

## 0. Identity locks — never change during a release

- Android `applicationId` / Play package: **`com.oetwithdrhesham.app`**.
- iOS bundle ID: **`com.oetprep.learner`** — Android-only rename (PR #181); never
  touch `ios/**`, `Info.plist`, `project.pbxproj`, or `apple-app-site-association`
  as part of store/release work.
- Play title: **"OET with Dr Ahmed Hesham"** (no period). Target/compile SDK **36**.
- Never create a second Play app record. Never create a new keystore — a
  rotated/wrong key breaks updates ("App not installed") and fails the CI pin.

## 1. Pre-flight (every release, all pathways)

1. Keep a visible todo list. Verify before claiming success.
2. **Read live state first** — never assume versions:
   - Play: from `D:\Projects\OET with Dr Hesham\automation\`,
     `.venv\Scripts\python.exe -m playstore.cli list-tracks`
   - VPS sideload feeds: `GET https://app.oetwithdrhesham.co.uk/api/releases/native?platform=android`
     and `?platform=ios` → `{version, versionCode}`.
   - Desktop updater feed: `GET https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`
     → `{version, platforms}`.
3. **Pick versions**: mobile `version` = next patch (e.g. live `1.4.10` → `1.4.11`);
   `version_code` = live max + 1 **across Play tracks and the VPS feed** (CI and
   Play both reject non-increasing codes). Desktop has its own lineage (was
   `0.7.x` as of 2026-09) — take it from the live desktop feed, not from mobile.
   Format rules (CI-enforced): semver `X.Y.Z[-prerelease]`, code = positive int.
   State the planned versions up front; ask only if live state is inconsistent
   or the owner specified otherwise.
4. **Repo visibility**: `jerryboganda/oetwebapp` must be **public** for the whole
   duration of any Actions run, then **private** again the moment the needed run
   finishes. Never start Actions while private. Never leave it public.
5. **CI builds `origin/main`, not your working tree.** Local dirty files are NOT
   in the release. If the release must include unpushed code, ship that code
   first via the normal push + Build & Deploy watch, then cut the release.
6. Concurrency: `mobile-release` runs serially (`cancel-in-progress: false`) —
   never dispatch overlapping mobile releases; `tauri-desktop-release` is
   per-ref. Mobile (≤30 min job) and desktop (≤90 min legs) may run in parallel
   with each other.
7. Never commit/print secrets (keystore, service-account key, certs, API keys).

## 2. Pathway A — Android (EVERY active Play track + VPS sideload)

**"Cut an app release" means every channel that currently has real users moves
together — never just internal.** Android alone has three independent
destinations (internal track, closed-testing/alpha track, VPS sideload feed);
the 2026-09-05 "Play shows Open not Update" incident happened precisely
because a release landed on internal + VPS but alpha was left behind. Do not
repeat that: step 6 below is not optional and not limited to `internal`.

CI (`mobile-release.yml`, `platform=android`) stamps `versionCode`/`versionName`,
builds Next.js + `cap sync`, builds signed AAB+APK, verifies the APK against the
pinned upload cert, uploads artifacts, then **unconditionally publishes the APK
to the production VPS feed** (that side effect is by design when cutting a full
release; to stage Play-only, roll the feed back afterwards with
`publish-existing-mobile-to-vps.yml`).

1. `gh repo edit jerryboganda/oetwebapp --visibility public --accept-visibility-change-consequences`
2. `gh workflow run mobile-release.yml --repo jerryboganda/oetwebapp --field platform=android --field version=<X> --field version_code=<N>`
3. `gh run watch <id> --repo jerryboganda/oetwebapp` until `conclusion: success`.
   On failure: `gh run view <id> --log-failed`, fix, re-dispatch with bumped
   inputs only if the feed guard demands it — never lower a versionCode.
4. Repo **private** again immediately; confirm with `gh repo view`.
5. Download the AAB: `gh run download <id> --repo jerryboganda/oetwebapp --name android-release-aab-<X> --dir <out>`
   and sanity-check it (ZIP magic `PK`, contains `BundleConfig.pb` + `base/`).
6. Publish with `.venv\Scripts\python.exe -m playstore.cli cut-android-release <out>\app-release.aab`
   — this is the **default, non-bypassable command for a general release**: it
   discovers every track that already has a live release (as of 2026-09-05:
   `internal` and `alpha`/Closed Testing; `beta`/`production` are empty and
   untouched — it re-checks live state itself every run, don't hardcode this
   list) and lands the same versionCode on all of them in one atomic edit.
   Expect `tracks_synced` to list every previously-live track, `version_code
   == <N>` on each. Only use `publish-bundle --track <t>` for a single named
   track when the owner has explicitly scoped the order to that one track —
   never as the default for "cut an android release." If you ever do use
   `publish-bundle`/`assign-track` directly, they print a red WARNING if
   another live track is left behind; treat that warning as a failed step,
   not a note to ignore.
7. Verify live: `list-tracks` shows `<X>`/`<N>` on **every track that was live
   before this release** (production/beta stay untouched only if they were
   already empty) AND the VPS android feed serves `<X>`/`<N>`. A track still
   showing an older version after this step is an incomplete release, not a
   "someone else can update it later" — fix it in the same pass.

## 3. Pathway B — iOS (VPS sideload feed; TestFlight is manual)

Same workflow, `platform=ios`: stamps via `agvtool` (marketing version + build
number = version_code), `app-store`-export IPA, publishes the IPA to the **VPS
iOS feed only**. There is **no TestFlight / App Store Connect automation** —
uploading to TestFlight or submitting for review is a manual owner step in App
Store Connect; never claim the agent did it. (Reference: 2026-09-04 the iOS VPS
feed was still empty `{}` while Android served releases.)

1. Same visibility dance + dispatch with iOS-appropriate `<X>`/`<N>` (must
   exceed the live **iOS** feed code — Android and iOS codes are independent).
2. Watch to success, repo private, then verify:
   `GET .../api/releases/native?platform=ios` → `{version: <X>, versionCode: <N>}`.
3. Report the TestFlight manual step explicitly if the owner expects store
   distribution: hand over the `ios-release-ipa-<X>` artifact name + version.

## 4. Pathway C — Windows desktop (+ macOS best-effort)

Workflow `tauri-desktop-release.yml`: conformance gate → matrix build (Windows
x64 NSIS **required**, macOS Universal dmg best-effort) → minisign-signed
updater artifacts → VPS updater feed. Unsigned builds (`allow_unsigned=true`)
are throwaway-only and never reach the feed. macOS ships unsigned; Windows
Authenticode applies only if `WINDOWS_CERTIFICATE` secrets exist (else the
installer is unsigned but still updater-valid).

1. Dispatch: `gh workflow run tauri-desktop-release.yml --repo jerryboganda/oetwebapp --field version=<X>`
   (or push tag `v<X>-tauri-desktop`). Desktop lineage is independent — read it
   from the live feed, do not copy the mobile number.
2. Watch to success (long build — Windows + macOS legs, up to ~90 min), repo
   private.
3. Verify: `GET https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`
   → `version == <X>`, `platforms.windows-x86_64.url` trusted
   (`https://app.oetwithdrhesham.co.uk/releases/...`), `signature` present
   (≥64 chars). macOS absence is acceptable (best-effort); Windows absence is a
   failed release.

## 5. Done definition (all pathways)

- Each dispatched run: `conclusion == success` (state run id, versions).
- Repo is private again. Secrets untouched/uncommitted.
- Live-state proof per pathway (Play `list-tracks` output, VPS feed JSON,
  desktop `latest.json`) — paste the evidence, not assertions. For Android,
  the pasted `list-tracks` output must show the new version/code on **every
  track that was already live**, not just internal — a stale `alpha` (or any
  other previously-live track) after "done" is not done.
- Report: versions shipped per channel, anything intentionally unchanged
  (e.g. production track, TestFlight manual step, macOS dmg absent), and any
  remaining warnings (e.g. review delays, propagation lag).

## 6. Gotchas (learned the hard way — do not relearn)

- Play-installed vs sideloaded Android copies can never update each other
  (Play signing key ≠ upload key); the in-app updater already routes by
  installer channel — never offer the APK to a Play-installed copy.
- `"Target SDK of artifact is too low: 1"` from the Play API is a cosmetic bug;
  the real rule is target SDK 36 — never lower SDKs to chase it.
- `edits().testers()` accepts Google Groups only — individual tester emails are
  a Play Console UI job, there is no API path.
- Internal track needs no review; promoting testing → production ("Review
  release" / "Start rollout") is UI-only, then tell the owner exactly what to
  click. Closed-testing → production access needs 12 opted-in testers × 14 days.
- A wrong/rotated keystore fails the release at the cert-pin step — that is the
  guard working, not a bug to bypass. Never fall back to debug signing.
- `publish-android` has no opt-out for the VPS feed push — plan for it (keep or
  roll back) before dispatching, and confirm the choice with the owner when the
  order is Play-scoped.

# Play Store release automation — COMPULSORY

Google Play Console access for this app is fully automated via a Google Play Developer
API service account and a Python toolkit. **Any task that touches the Play Store —
uploading a release, editing store listing text/images, checking track/tester state, or
replying to reviews — must go through this toolkit, not manual Play Console clicking and
not a hand-authored/regenerated asset or doc that duplicates what the toolkit already
manages.** Read this file before touching `android/**`, `capacitor.config.ts`, Play
Console, or any `fastlane/`/store-listing asset.

## Why this rule exists

On 2026-09-03/04 a parallel AI agent (Codex, operating from `.codex/AGENTS.md` in this
same repo) independently changed the Android package name back toward the old
`com.oetprep.learner` identity and drifted the app's branding text, without checking the
already-shipped, API-verified Play Console state. That collided with a rename to
`com.oetwithdrhesham.app` that had already been submitted, reviewed, and gone live. It had
to be root-caused and reverted (`fix-play-branding-conflict` PR #186) at the cost of a
live Play Console listing (title, description, feature graphic) also drifting out of sync
and needing a second manual correction. **The toolkit's live state is the source of
truth for Play Store identity/listing facts — not assumptions, not memory, not a second
agent's independent guess.** Always query live state (`list-tracks`, `get-listing`)
before changing anything that affects it.

## Current live identity facts (verify with the toolkit before trusting this list)

- Android `applicationId` / Play Console package: **`com.oetwithdrhesham.app`**
  (`android/app/build.gradle`, `capacitor.config.ts`).
- iOS bundle ID: **`com.oetprep.learner`** — unchanged, and must stay unchanged. The
  Android rename was Android-only by explicit owner decision (PR #181). Never let a
  Play Store task touch `ios/**`, `ios/App/App/Info.plist`,
  `ios/App/App.xcodeproj/project.pbxproj`, or the `apple-app-site-association` file.
- App title on Play Console: **"OET with Dr Ahmed Hesham"** (not "OET with Dr. Hesham" —
  no period, no abbreviation; this is the pre-existing brand name used across
  `components/layout/sidebar.tsx` and `app/terms/page.tsx` well before any Play Store
  work started).
- Target/compile SDK: **36** (`android/variables.gradle`) — required by Google for any
  new release since 2026-08-31. Do not lower it to "fix" a release rejection.

## Toolkit location and setup

The toolkit lives **outside this repo**, as a sibling folder on the machine used for Play
Console work:

```
D:\Projects\OET with Dr Hesham\automation\
```

It is not checked into `oetwebapp` and should not be. Setup (already done once on the
working machine):

- GCP project: `serious-arcana-507317-n8`.
- Service account: `play-automation-bot@serious-arcana-507317-n8.iam.gserviceaccount.com`,
  invited under Play Console → **Users and permissions**, scoped to this app.
- Key file: `automation/credentials/play-console-service-account.json` (gitignored —
  never commit it, never paste it into a chat transcript or a public issue).

On a new machine: place the service-account key at that path, then from `automation/`:

```
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
```

## CLI usage

Run everything through `python -m playstore.cli <command>` from `automation/` with the
venv active:

```
python -m playstore.cli test-auth
python -m playstore.cli list-tracks
python -m playstore.cli get-listing en-US
python -m playstore.cli update-listing en-US --title "..." --short-description "..." --full-description "..."
python -m playstore.cli cut-android-release path\to\app.aab   # default for "cut a release" -- syncs every live track
python -m playstore.cli publish-bundle path\to\app.aab --track internal   # single track only, owner must scope it explicitly
python -m playstore.cli list-reviews --max-results 20
python -m playstore.cli reply-review <review_id> "Thanks for the feedback!"
```

`--package` overrides the target app (default `com.oetwithdrhesham.app`, set in
`playstore/config.py`). For anything not covered by a CLI command, use the underlying
modules directly (`playstore/releases.py`, `playstore/listings.py`, `playstore/details.py`,
`playstore/reviews.py`) — see `automation/README.md` and
`automation/setup_initial_listing_and_release.py` for the edit-based pattern (open an
edit → stage bundle/listing/image/detail changes → commit atomically, and always discard
the draft edit on any exception so nothing partial is left staged).

## Single-channel update policy (Android) — read before touching updates

- The Play app-signing key differs from the CI upload key (verified 2026-09-04:
  Play delivery cert `D2:8D:…:46:B3` vs VPS/upload cert `41:5F:…:7F:A9`). Android
  treats those as different apps for update purposes, so a Play-installed copy
  can NEVER be updated by the VPS APK and vice versa — every cross-channel
  attempt fails with "App not installed" and no app code can override OS
  signature enforcement.
- The in-app update surface (`app/get-app/android-install`, backed by the
  `InstallerSource` native plugin + `lib/mobile/install-source.ts`) therefore
  routes by installer channel: Play-installed copies update via the Play
  listing only; sideloaded copies via the direct APK only. Never offer the APK
  to a Play-installed copy.
- Release builds fail closed without `keystore.properties` (no silent
  debug-signed "release" APKs), and `mobile-release.yml` aborts unless the
  built APK carries the pinned upload certificate above. A rotated/wrong
  keystore fails the release instead of shipping an uninstall-or-nothing
  artifact.

## Known gotchas — read before you hit these again

- **A general "cut a release" must land on EVERY track that already has a live release —
  this is an owner rule stated as most-critical, not a style preference.** Use
  `python -m playstore.cli cut-android-release <aab_path>` by default: it discovers live
  tracks itself and syncs all of them in one atomic edit, so it can't accidentally leave
  one behind. Only use `publish-bundle --track <t>` for a single track when the owner has
  explicitly scoped the request to that one track by name.
- **Promoting an already-uploaded build to a second track (e.g. internal → alpha/Closed
  Testing) must NOT re-run `publish-bundle`.** That command always re-uploads the `.aab`,
  and Play rejects a versionCode that's already live on any track with `403 "Version code
  N has already been used"`. Use `python -m playstore.cli assign-track <version_code>
  --track <track>` instead — it opens an edit, points the track at the existing
  versionCode with no upload, and commits. Root cause of the 2026-09-05 "Play shows Open
  not Update" bug for closed testers: internal/VPS had moved to 1.4.11 while alpha was
  still pinned to an older build — `assign-track` fixed it without a rebuild, and
  `cut-android-release` now exists so it can't recur.
- **`edits().testers()` only accepts Google Groups, not individual email addresses.**
  Internal/closed-testing tester lists must be managed by pasting emails into the Play
  Console UI's per-track Testers tab — there is no API path for individual testers. This
  is a genuine, permanent gap in the androidpublisher API, not a toolkit limitation to
  work around.
- **`mobile-release.yml`'s `publish-android` job unconditionally publishes the built AAB
  to the production VPS release feed** as a side effect of building a release artifact —
  there is no flag to suppress it. If you only meant to stage a Play Console release and
  not touch the production download feed, roll it back afterwards via
  `publish-existing-mobile-to-vps.yml` pointed at the last-good version/tag.
  Check `https://app.oetwithdrhesham.co.uk/api/releases/native?platform=android` to
  confirm what's actually being served before and after.
- **"Target SDK of artifact is too low: 1" from the Play API is misleading.** The `1` is
  a cosmetic formatting bug in Play's error response, not the real target SDK value. As
  of 2026-08-31 Google requires target SDK **36** (Android 16) for any new release; bump
  `compileSdkVersion`/`targetSdkVersion` in `android/variables.gradle`, don't chase the
  literal "1" in the error text.
- **A brand-new/"draft" app can only be staged via the API, not submitted for review.**
  `releases.assign_track(..., status="draft")` (or `completed` on internal testing, which
  doesn't need review) is as far as the API goes. Actually starting Play's review process
  on a closed/production track — "Review release" → "Start rollout to X" — is a Play
  Console UI-only action with no API equivalent. Stage everything via the toolkit, then
  tell the owner exactly what to click.
- **Closed Testing promotion to production requires 12 opted-in testers (accepted invite
  AND installed) continuously for 14 days** before Google allows applying for production
  access (reduced from 20 testers in Dec 2024 — re-verify this figure against current
  Play policy if it's been a while, policy changes without notice).

## The rule, restated

Before any Play Store–related change:

1. Run `list-tracks` / `get-listing` (or the equivalent module call) to read live state.
   Don't assume what's live matches what's in this repo's `fastlane/`, `docs/`, or asset
   folders — the toolkit and Play Console are the source of truth.
2. Make the change through the toolkit (`update-listing`, `publish-bundle`, image
   upload helpers in `playstore/listings.py`, etc.), not by hand in the Play Console UI,
   and not by regenerating store graphics/text independently without first checking what
   is already live and why.
3. If a change must touch native identity (`applicationId`, bundle ID, package-scoped
   files), treat Android and iOS as independently owned — see "Current live identity
   facts" above — and re-confirm with the owner before changing either.
4. If you find the toolkit's assumptions (package name, track state, tester model) don't
   match what you're seeing, that's a signal to re-verify against live Play Console state
   before proceeding, not to silently "fix" it by editing repo files to match a guess.

# Current Task - OET Speaking AI Simulation Assessment Specification v1.1

Last updated: 2026-08-11

## Current implementation checkpoint

- Implemented the PDF v1.1 slice across the server-authoritative two-card AI
  role-play lifecycle, versioned hidden persona/card governance, transcript and
  audio evidence capture, ten-criterion calibrated practice report, learner
  report/transcript/audio UI, tutor override audit path, profession isolation,
  consent/retention copy, and operational telemetry.
- Added fail-closed STT-per-minute and TTS-per-1,000-character owner rate-card
  approvals. Completed-turn telemetry now records STT + actor LLM + TTS cost
  components and logs technical review on cost/latency/degradation breaches.
- Retention now deletes expired recording blobs and, after every recording in
  the relevant session scope is archived, redacts transcript/report evidence
  while preserving non-content score/version/audit metadata.
- Evidence primary ownership is fingerprint-based: distinct behaviours in one
  turn may each own one primary criterion, while the same evidence fingerprint
  cannot reduce multiple criteria.

## Validation completed

- `dotnet build backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj
  --no-restore --nologo -v:minimal -p:RunAnalyzers=false
  -p:UseSharedCompilation=false -m:1 --disable-build-servers`: passed with
  0 errors and existing repository warnings.
- Fresh no-build filter
  `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj
  --no-build --no-restore --filter "FullyQualifiedName~SpeakingSimulationV11"`:
  33 passed, 0 failed.
- Targeted frontend ESLint over the touched Speaking/admin/API surfaces:
  0 errors, 11 existing React Compiler/hooks warnings.

## Next concrete step and boundary

- Run `git diff --check`, stage only the explicit v1.1 implementation/design,
  test, migration, and handoff paths; preserve `.codex/config.toml`,
  `.superpowers/`, `pdf-policy-release/`, and `pdf-policy-release2/`.
- Compare `HEAD` with `origin/main`, commit, push `main`, watch the required
  Build & Deploy workflow, and verify deployed SHA/migrations/health.
- Owner-controlled calibration, profession-pack, silence/confidence/graph,
  concurrency, cost-rate, and retention approvals plus authenticated browser,
  provider, and live production acceptance remain required before claiming
  the PDF's full production boundary.

# Current Task - OET Listening and Reading AI System v1.1

Last updated: 2026-08-11

## Current LR implementation slice

- The Listening v2 technical-readiness endpoint now forwards advisory device
  labels, screen dimensions, and display scale into the existing service
  command. The prior endpoint contract silently discarded those JSON fields,
  so LR-16 telemetry was not actually persisted even though the service and
  regression test already covered it.
- `git diff --check` passed. The focused Windows
  `ListeningV2AdvanceEndpointTests` run timed out after 124 seconds before
  compiler/test output; do not claim local test success. Hosted CI must compile
  and execute the regression before release evidence is updated.
- Preserve the unrelated dirty Speaking work and untracked `.superpowers/`,
  `pdf-policy-release/`, and `pdf-policy-release2/` paths. The owner-controlled
  score tables, normalization profile, lock mode, rationale/evidence library,
  thresholds/labels, graph approval, load target/evidence, and authenticated
  browser/mobile acceptance remain unresolved.

## Hosted LR-16 regression checkpoint

- Gap-closure run `31522316211` executed against exact SHA `8707607e7`. Frontend
  type-check, Vitest, and lint passed. Backend ran 293 scoped tests: 212 passed
  and 81 failed.
- Every `ListeningV2AdvanceEndpointTests` test passed, including the repaired
  `Technical_guidance_signals_are_recorded_without_blocking_strict_readiness`
  regression. The remaining backend failures are the known owner-approved
  marking-policy gate and legacy unconfigured conversion/default-normalization
  expectations; do not seed owner values to force green.
- The exact SHA deployed successfully in Build & Deploy run `31521129361`:
  web/API/backup images, off-box migration generation/application, and
  blue/green deploy all passed. Production is live on green with blue retained
  for rollback; independent checks at `2026-08-11T18:21:23Z` returned 200 for
  API live/readiness and web health, with 307 redirects for `/`, `/listening`,
  and `/reading`. Readiness dependencies were all `ok` at `18:21:33Z`.

## Latest exact release checkpoint

- `428f9b27583d5ea336d92710cc6e4e7a56932ec5` is the implementation slice;
  evidence commits `c88efccf33fccdac8ec9a76d4c06fe9e8579d8e7` and
  `b0705877bfa03afca0e03ae39134fd3b50cfcbd9` are on `main` and
  `origin/main`. Build & Deploy runs `31517340245` and `31518463827`
  completed successfully, including image builds, off-box migration
  generation/application, and blue/green deployment. The VPS reported live
  on blue, then live on green, with the previous slot retained for rollback.
- Final independent checks at `2026-08-11T17:46:46Z` returned 200 for API live,
  API readiness, and web health, and 307 for `/`, `/listening`, and `/reading`;
  readiness at `17:46:50Z` reported database, migrations, stuck_jobs, and
  storage `ok`.
- Widened LR gap-closure run `31516282588`: frontend type-check/Vitest/lint
  passed; backend ran 293 tests with 211 passed and 82 failed. Failures are
  principally expected fail-closed starts without owner-effective marking
  policy, plus legacy unconfigured-conversion/default-normalization fixture
  expectations. Do not seed owner data to mask these failures.
- Remaining blockers are owner-provided score tables, normalization/spacing
  profile, practice/mock lock mode, rationale/evidence library, thresholds and
  labels, graph legal/style approval, concurrency target/load evidence, and
  authenticated desktop/mobile acceptance evidence.

## Current checkpoint

### Latest exact-slice checkpoint

- Commit `20d19043c` is on `main` and `origin/main`; Actions run
  `31509360292` completed successfully for that exact SHA. Web/API/backup
  images, off-box migration SQL generation/application, and blue/green deploy
  passed. The VPS reported live on green with blue retained for rollback.
- Post-deploy checks at `2026-08-11T16:02:07Z` returned API live/readiness
  HTTP 200; readiness reported database, migrations, stuck jobs, and storage
  all `ok`. Web root and `/listening` returned HTTP 307 to sign-in.
- Commits `ddebd85be`, `abe859e2f`, `0a5867199`, `4bdffbc97`, and
  `831da6795` are on `main` and `origin/main`. They cover encrypted offline
  answer reconciliation, grounded post-submit Listening/Reading explanations,
  approved-table-only branded practice score graphs, and fail-closed blank
  response handling for grounded AI.
- Targeted evidence passed: offline reconciliation (3 tests), Reading player
  lifecycle (30 tests), and score-band graph (2 tests). Touched AI/graph
  frontend lint had zero errors; the full TypeScript check remains blocked by
  unrelated nullable/duplicate-test/Speaking/PDF-catalog diagnostics.
- Actions runs `31506238928` and `31507118003` recorded the earlier Speaking
  baseline compile blocker; isolated fixes in `d992d198e` and `20d19043c`
  cleared it without staging the user’s remaining Speaking edits.
- Acceptance matrix: `docs/superpowers/evidence/2026-08-11-oet-listening-reading-v1-1-acceptance.md`.

- Current uncommitted slice adds explicit accepted-variant change reasons to
  Listening and Reading authoring contracts, admin editors, audit details, and
  regression tests. Actor/time remain supplied by the existing AuditEvent.
- The next integrity slice adds `reading.mcq.multiple_selection_review_required`
  and `listening.mcq.multiple_selection_review_required` audit events. A
  multi-selection payload still receives zero credit; the existing admin audit
  log and privileged attempt review are the review surfaces.
- The current playback slice adds auditable `audio_buffering_start`,
  `audio_buffering_end`, and `audio_stalled` events for the Listening player,
  edge-triggered per interruption, plus an explicit halted-playback notice that
  tells candidates not to replay or seek while buffering.
- The current recovery slice retains the latest debounced answer in an
  in-flight queue and flushes it with authenticated `keepalive` requests on
  tab hide/pagehide, while final submit remains the server-authoritative
  durable path.
- Reading now has the same server-only lifecycle flush for its 400 ms answer
  debounce, with elapsed-time accounting suppressed for duplicate in-flight
  requests and pending values reconciled on resume/submit.
- Commit `7573ab6df` adds an explicit learner-contract regression: an attached
  Listening answer-key asset must not appear as a URL or media identifier in
  the learner session projection. The existing projection already omitted the
  URL; the test now protects that boundary with a real attached answer-key
  asset.
- Scoped frontend ESLint passed with zero errors (the player retains its
  existing React Compiler/hooks warnings). The focused Windows .NET
  learner-contract test reached compilation but was blocked by the unrelated
  dirty Speaking file `SpeakingSimulationV11Contracts.cs` (`CS1002` / `CS1513`).
  Actions run `31500029557` for `7573ab6df` completed with build-backup/web
  success but API publish failed only on the unrelated dirty Speaking files;
  migration/deploy were skipped. The newer handoff-triggered run
  `31500202895` for `043cb6d3d` is queued. Production status is not claimed
  for this commit.
- Governed Listening/Reading conformance hardening plus attempt-start score-table snapshots and explicit Reading Part A variant coverage is on `c4ed2e55b9b47aeedccb601b16c0b4580630ad48` on `main` and `origin/main`.
- Build & Deploy run `31487883204` completed successfully for that exact SHA: web, API, backup, off-box migration SQL generation/application, and blue/green deployment all passed. The VPS reported live on blue with green retained for rollback.
- Build & Deploy run `31453183628` completed successfully for the exact SHA: web, API, backup, production migration, and blue/green deploy all passed.
- Public post-deploy checks returned HTTP 200 for API live/readiness; readiness reported database, migrations, stuck jobs, and storage all `ok`. The app root, `/listening`, and `/reading` returned HTTP 307 redirects to their sign-in routes on `app.oetwithdrhesham.co.uk`.
- Fixed policy locking to occur only after durable attempt creation; governed attempts now fail closed on missing/malformed marking-policy snapshots; learner, mock, analytics, tutor, expert, and background LR projections no longer synthesize scaled scores from raw accuracy. Reading Part A now consumes only explicitly authored variants under the immutable attempt policy, and full/legacy Listening and Reading attempts pin score-table selection at start.

## Validation and remaining boundary

- `git diff --check` passed for the shipped learner-contract protection. The
  focused filtered backend test reached compiler diagnostics and was blocked by
  the unrelated dirty Speaking syntax errors (`CS1002`, `CS1513`), so no local
  green test claim is made. GitHub Actions run `31500029557` is the completed
  authoritative result for `7573ab6df` (API failed on unrelated Speaking
  errors); run `31500202895` is the queued authoritative result for the latest
  state-only SHA `043cb6d3d`.
- Owner-controlled release data remains required: complete approved Listening and Reading score tables, normalization profile, practice/mock lock mode, approved rationale/evidence content, pathway/pass thresholds, graph legal/style sign-off, and peak timed-attempt concurrency target.
- LR-05 now has explicit Listening and Reading selection-preservation assertions. The new LR-03/LR-04/LR-08 focused tests are present but await CI execution. Acceptance evidence still needs authenticated end-to-end/mobile evidence. Do not claim complete PDF acceptance until that boundary and owner approvals are supplied.
- Preserve untracked `.codex/config.toml`, `.superpowers/`, `pdf-policy-release/`, and `pdf-policy-release2/`; never stage them.

## Outcome

- Implemented the governed v1.1 Writing assessment slice: immutable report
  snapshots, fail-closed task/case-note/profession/letter-type preflight,
  v1.1 deterministic rules and six-criterion evidence, fact-map grounding,
  calibration release gates, grounded model-answer holding, candidate result
  projection, learner report UI, and admin pack/gate/report governance APIs.
- Removed legacy AI-score escape paths: legacy attempt submissions/revisions
  reject with `writing_v11_required`; historical queued jobs fail before
  scoring; learner home/summary no longer expose legacy raw-38 output.
- Candidate numeric output remains disabled until owner-approved profession /
  letter-type packs and a calibration gate satisfy the explicit release rules.

## Validation

- `git diff --check`: passed.
- Targeted ESLint on the touched Writing result/admin pages and `lib/writing`:
  no errors; one pre-existing `setState`-in-effect warning remains on the
  calibration page.
- `pnpm exec tsc --noEmit`: blocked by duplicate test files in the unrelated
  untracked `pdf-policy-release` and `pdf-policy-release2` catalogs.
- Targeted local backend build/test attempts stalled on the Windows host before
  compiler output; no local backend green claim. GitHub Actions is the compile,
  migration, and image-deploy gate.

## Next step

Resolve or receive authorization for the unrelated Speaking compile blocker,
then rerun the exact-SHA production workflow and verify the deployed browser
and authenticated/mobile evidence. Owner action remains: supply the approved
score tables, normalization profile, practice/mock lock mode, rationale/evidence
library, pathway thresholds, graph legal/style sign-off, timed-attempt
concurrency target, and authenticated browser/mobile evidence. Preserve
unrelated Speaking changes and do not approve a candidate release gate without
those owner controls.

# Latest OET Listening/Reading v1.1 checkpoint

- `428eda72cff3980739f0e904ad09a1fc69f2414d` is on `main` and live on the
  blue production slot from Build & Deploy run `31514162141`.
- API live/readiness and web health returned 200 at `2026-08-11T17:09:55Z`;
  readiness reported database, migrations, stuck_jobs, and storage `ok`.
- The clean LR frontend validation passed; backend compiled and passed 110
  scoped tests, while 21 failed closed because owner-approved marking policy
  data is not configured. The acceptance filter has been widened to include
  the remaining deterministic LR classes for the next run.
- Owner-controlled conversion tables, normalization policy, practice/mock
  lock mode, rationale/evidence library, pathway thresholds/pass labels,
  graph legal/style sign-off, peak concurrency target/load evidence, and
  authenticated browser/mobile acceptance remain unprovided boundaries.

# Current Task - OET Speaking booking workflow PDF implementation

Last updated: 2026-08-10

## Latest release checkpoint

- Closed the remaining server-side bypasses: canonical mock reschedules now
  re-check the seven-day tutor cutoff; legacy admin assignment cannot mutate
  Speaking rows; admin booking moves validate active tutor-calendar slots and
  recreate Zoom/calendar state; admin refunds enforce strictly more than 24
  hours and full-refund-only semantics.
- Commit 7e7d88fd7e95ca24d41bfca9c8d35a69e3cf4ded makes admin refunds fail
  closed when the payment provider fails, with a focused regression test.
- Build & Deploy run 31421541015 passed all image builds, production migration,
  and blue/green deployment; the VPS reported live on green with the exact
  7e7d88fd7 images.
- Final public checks returned HTTP 200 for web health, API live, and API
  readiness; database, migrations, stuck_jobs, and storage were all ok.
- Commit `1e3ceeae745b2a8fff2038c78e5aafe1e2c70b2c` is on `main`.
- Build & Deploy run `31411775123` passed web/API/backup builds, production
  migration, and blue/green deployment. The VPS reported
  `AUTO_DEPLOY_DONE: live on green` with the new image SHA.
- Public checks after deployment: web `/api/health`, API `/health/live`, and
  API `/health/ready` all returned HTTP 200; database, migrations, stuck_jobs,
  and storage were all `ok`.

## Remaining evidence boundary

- The separate Speaking CI run `31411775002` remains red from existing 403
  auth-fixture failures, one speaking-upload timeout, and unrelated frontend
  Vitest failures; no failure was attributed to the four policy files in this
  release. Local Windows MSBuild also stalled after restore; the successful
  Actions API image build is the compile gate.
- Authenticated learner/tutor/admin browser walkthrough, real Zoom/provider
  email delivery, and Stripe inbox/refund confirmation remain owner-side
  acceptance boundaries; no credentials or customer data were accessed.

# Current Task - OET Speaking booking workflow PDF implementation

Last updated: 2026-08-10

## Outcome

- Implemented the PDF booking policy across the canonical mock-booking and
  private-speaking flows: AI/tutor selection, the seven-day tutor cutoff,
  tutor-calendar-only slots, tutor availability CRUD, booking/reschedule/
  cancellation rules, Zoom-gated confirmation/access, immediate confirmation,
  one-hour reminders, and strict greater-than-24-hour refund eligibility.
- Closed legacy direct-bundle and arbitrary-reschedule bypasses, and made
  failed Zoom provisioning fail closed for confirmation/reminders.
- Hardened the remaining policy surfaces: private-speaking configuration now
  normalizes the PDF's 24-hour refund boundary, always-on rescheduling,
  canonical 24h/1h/15m reminders, and policy copy; required learner/tutor
  booking and reminder notifications are protected from preference/admin
  disablement and frequency caps.
- Implementation commit `f3c06a3ba339f8720d4385980688510f26619c4e` and test-policy
  alignment commit `1423ff45b2f332595f49bbb656cb9df9499c8599` are on `main`.
- Policy hardening commit `3abbd1f46e173a9059d1588be76cd8f15ea88fd2` was pushed
  to `main`. Build & Deploy run `31409233754` passed web/API/backup images, off-box
  migration generation/application, and blue/green VPS deployment. The deploy
  reported live blue-slot health/public verification and image tags for
  `3abbd1f46...`; public checks returned app/API HTTP 200.

## Validation

- Targeted policy tests were added, and `git diff --check` passed. Local .NET
  compilation/test attempts were blocked by the Windows compiler hanging and
  timing out without diagnostics; the successful GitHub Actions API image build
  is authoritative for compilation of the deployed revision.
- Speaking CI run `31409233189` still has unrelated baseline frontend Vitest,
  backend 403 test-fixture, and one speaking-upload timeout failures. The
  PDF-policy tests/fixtures corrected in `1423ff45b` are not among its
  failures; the three legacy penalty tests remain intentionally skipped because
  the PDF removes that workflow.
- QA Smoke is a separate broad workflow and is not a gate for this slice.

## Next step

No further source implementation remains in scope. Authenticated learner/tutor/
admin browser acceptance with test accounts, real Zoom meeting creation,
provider email delivery, and Stripe refund/inbox verification remain owner-side
acceptance boundaries; no credentials or customer data were accessed.

# Current Task - Maximum Performance Optimisation

Last updated: 2026-08-08

## Outcome

- Performance implementation source revision is `0caccbabe3d5622cc56d5ebd601fe2d30f5e896f`;
  Android runtime workflow corrections are `60e9a60a0` and `584177a75`.
- Performance run `31234134695` passed the 10-project browser matrix, k6, and
  summary gates; final learner Pixel CLS was `0`.
- Mobile CI `31236473393` passed lint/typecheck, unit tests, iOS simulator
  launch, Android debug build, and Android emulator runtime assertions.
- Tauri Desktop CI `31234597842` passed Windows Rust gates, macOS launch smoke,
  and `desktopBridge` conformance.
- Final Build & Deploy run `31236473400` passed web/API/backup builds,
  migration, and deploy. Live web, API health, and API readiness checks
  returned HTTP 200 with database, migrations, stuck jobs, and storage ok.

## Validation

- Local TypeScript, scoped ESLint, focused Vitest (9/9), and `git diff --check`
  passed.
- k6 completed 89,933 requests with 89,932 checks succeeded and 1 failed;
  critical-read P95/P99 were 216.51/280.8 ms. The one dashboard failure was
  the isolated PostgreSQL `too many clients already` capacity observation;
  the configured load gate still passed.
- Android smoke recorded a 915,883 ms hosted-emulator boot and a successful
  process/activity assertion with no crash signature. Its `am start -W`
  command timed out; this is not claimed as native launch-LCP evidence.
- Local Docker was unavailable; CI is authoritative for the isolated stack,
  native builds, and deployment.
- External non-production staging credentials/URL, physical devices, and
  manual low-bandwidth interaction remain owner-side acceptance boundaries.
  QA Smoke and Speaking CI are separate broad workflows and are not
  performance gates.

## Next step

No further in-scope performance implementation remains. The finalized evidence
is ready for `main` parity; only owner-side physical-device, external-staging,
and manual low-bandwidth acceptance boundaries remain.

# Current Task - Production compute offload to GitHub Actions

Last updated: 2026-08-08

## Outcome

- Audited the OET release path and a read-only VPS snapshot. Web/API/backup
  image builds, CI validation, and EF migration-script generation are now
  explicitly owned by GitHub Actions; the VPS retains only data-local and
  runtime work.
- Added the Actions migration gate, a stdin-only PostgreSQL applicator,
  production startup-migration policy, image-only rollout guards, and the
  operator audit/limits document. The deploy job now streams only the small
  rollout/Compose/env-validator bundle and never syncs the source repository
  to the VPS.

## Validation

- `bash -n` passed for the new and touched deployment scripts.
- `bash scripts/deploy/verify-image-only-rollout.sh` passed.
- `bash scripts/deploy/verify-compute-offload.sh` passed, including the
  no-source-sync assertion.
- `git diff --check` passed for the implementation paths.
- The focused .NET migration-policy test was attempted but stalled locally
  before useful test output; no local backend pass is claimed. GitHub Actions
  remains authoritative for compilation and full backend/image gates.
- GitHub Actions run `31216996359` for commit `1df02d008817e2ee7b418d95e8b4044fe60a0080`
  passed all three image builds, off-box EF restore/build/idempotent SQL
  generation, migration application, and the artifact-only blue/green deploy.
- Post-deploy read-only checks returned `app_api_health_http=200` and
  `api_ready_http=200`; green API/web/backup containers are healthy and use
  the `1df02d008...` GHCR artifacts. No source checkout/sync occurred on the
  VPS rollout path.

## Next step

Preserve the unrelated `.codex/config.toml`, `.superpowers/`, and concurrent
product/UI/API changes. Backup restore parity/drill and authenticated learner
acceptance remain manual follow-ups; production runtime OCR/PDF/TTS/AI and
queue workloads remain data-local by design.
owner-side boundaries.

# Current Task - Critical Course Video Access Rule

Last updated: 2026-08-07

## Outcome

- Added a PostgreSQL-only corrective migration that applies the 18 canonical
  New/Old Crash Course Arabic Writing exclusions to every `full-%` plan and
  immutable plan-version mapping, while removing those IDs from explicit
  includes and preserving unrelated overrides.
- Added focused entitlement and migration-shape regression coverage plus the
  approved Superpowers design and execution plan.

## Validation

- Migration static check passed: 18 canonical IDs match the existing rule,
  both plan tables are targeted, and JSONB include/exclude merge operations are
  present.
- Focused entitlement tests passed 4/4; the migration-shape test compiled but
  needed a provider-guard correction, then the local clean rebuild stalled.
  CI run 31192330082 passed both API/web image builds and the deploy job.
- VPS is at `31bd84b61`; migration history contains
  `20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses`.
  Read-only SQL confirms every full-course plan/version has 0 blocked includes
  and 18 blocked excludes, all six Crash Course plan/version rows retain 18
  includes plus Listening/Reading/Speaking scope, all 18 videos remain tagged,
  and December/February writing content remains present outside the exclusion.
- API and web public health endpoints both returned HTTP 200; the repository
  helper's internal `oet-web:3000` probe is stale for the nginx proxy container.

## Next step

No further in-scope deployment action remains. Manual learner login/browser
acceptance with supplied test accounts remains an owner-side boundary; no
credentials or customer data were accessed.

# Current Task - Device exemption policy mismatch and admin list management

Last updated: 2026-08-07

## Outcome

- Auth sign-in, risk step-up, trusted-device checks, and stale device-OTP
  resend/verify now share the persisted exemption list and match the linked
  learner/expert profile email as well as the auth-account aliases.
- The prior hidden hard-coded exemption defaults were removed so deleting an
  address in the admin list genuinely revokes its exemption.
- The fourth supplied Gmail address is included in the seed and additive
  backfill migration; the admin runtime-settings field is now a searchable,
  add/delete table with staged Save All behavior.

## Validation

- `pnpm exec vitest run app/admin/settings/RuntimeSettingsClient.test.tsx --reporter=dot`: 11/11 passed.
- The focused backend test was added but could not complete locally because
  another agent's `CrashCourseVideoAccessRuleTests` process held shared .NET
  build artifacts; CI remains the backend compile/test gate.

## Next step

Run the final scoped verification, stage only the implementation/tests,
migration, and this state file, then commit and push `main`. Preserve the
unrelated `.codex/config.toml`, `.superpowers/`, and concurrent work.

# Current Task - Trusted-device cooldown false-positive incident

Last updated: 2026-08-07

## Outcome

- Web device identity now persists in both localStorage and an opaque
  first-party cookie, allowing privacy-oriented in-app-browser launches to
  recover the same identity instead of minting a new UUID each time.
- Device-change cooldown now counts only OTP-approved replacement identities;
  the initial bootstrap no longer consumes the learner's change budget.
- Added focused browser and backend regression coverage plus policy/runbook
  documentation for the corrected semantics.

## Validation

- `pnpm exec vitest run lib/device-id.test.ts lib/auth-storage.test.ts --reporter=dot`: 8/8 passed.
- `pnpm exec eslint lib/device-id.ts lib/device-id.test.ts --quiet`: passed.
- `cmd /c "pnpm exec tsc --noEmit --pretty false"`: passed.
- `git diff --check`: passed.
- Local `dotnet test ...TrustedDeviceServiceTests...` stalled before compiler/test
  output twice; no local backend pass is claimed. GitHub Actions remains the
  backend compile/test gate.
- Public production health was read-only checked: web `/api/health`, API
  `/health/live`, and `/health/ready` returned 200 before deployment.

## Next step

Review the scoped diff, stage only the incident implementation/tests/docs and
this state file, commit and push `main`, then watch Build & Deploy plus backend
CI and re-check production health. Existing unrelated working-tree files
(`.codex/config.toml`, `.superpowers/`, performance/test changes) must remain
unstaged.

# Agent State - Course platform security production completion

Last updated: 2026-08-05

## Current Task - Responsive app download strip

- Replaced the dark full-width sign-in app-download box with one compact,
  responsive strip outside the auth card.
- Added consistent desktop/mobile badges, actionable Google Play and iOS
  destinations, and responsive mobile layout/link assertions.
- Validation: focused Vitest 3/3, 390px Chromium Playwright sign-in smoke,
  TypeScript, scoped ESLint with 0 errors, and diff-check passed.
- Next step: stage only the implementation files, commit/push `main`, watch
  the production blue/green deployment, and report the live release evidence.

## Current Task - Mobile device verification back navigation

- Added a visible `Back to sign in` link to the shared device-verification
  screen used by Android and iOS Capacitor webviews.
- The link clears the pending challenge from AuthContext and persisted
  web/native storage before preserving the requested destination on `/sign-in`.
- Added focused component coverage, mobile Playwright coverage for Pixel/iPhone
  projects, and Mobile CI path triggers for shared auth changes.
- Validation: TypeScript, targeted ESLint, diff check, and 6 focused tests pass.
  Local mobile Playwright attempt was stopped after WebKit failed immediately
  and Chromium exceeded the local 120-second timeout; CI mobile builds remain
  the native Android/iOS verification gate.
- Next step: stage explicit files, commit/push `main`, watch Mobile CI and the
  production deployment, then report the authenticated-device limitation.

## Current Task - Admin package removal and primary package

- Implemented soft-revocation: cancelled packages are hidden from current
  Access & Allocation responses while subscription/audit history remains.
- Added primary-package API/UI behavior using `CurrentPlanId`; primary metadata
  is representative only and active package entitlements remain additive.
- Added parent-status checks for linked add-ons/AI access, idempotent admin
  included-credit reversal, save-order reconciliation, and focused regressions.
- Validation: focused PackageList Vitest 2/2 passed; targeted ESLint had 0
  errors with only existing React hook warnings; TypeScript and diff-check
  passed. Backend MSBuild stalled before compiler/test output and was stopped.
- Next step: inspect final diff, stage only implementation paths, commit/push
  `main`, and verify the GitHub blue/green deployment and live access/audit
  behavior.

## Goal

Implement and production-verify the controls in
`OET_Course_Platform_Security_Requirements (1).pdf`, preserving explicit
evidence for platform limitations and external provider blockers.

## Implemented

- Implemented the reference anti-sharing contract end to end: one approved
  client identity by default, bounded admin override (1-5), OTP approval for
  new identities, automatic replacement/revocation, family-wide sign-out,
  refresh/access-token liveness enforcement, playback termination, clear
  user-facing sign-out reasons, and SecurityEvent + AuditEvent evidence.
- Defined the exact cross-platform count: browser profile tabs/windows count
  once; each Android/iOS/Windows/macOS official app installation counts once;
  a browser profile and app on the same hardware count separately because the
  server cannot prove hardware equivalence. The exact policy is documented in
  `docs/SECURITY-DEVICE-POLICY.md` and surfaced in admin and learner UI.
- Added the learner limit migration and the SecurityEvents platform-width
  migration required for canonical `capacitor-android`/`capacitor-ios` values.
- Routed self-service family revocation through the central revocation service
  and made device/session security decisions resilient to request disconnects.

- Replaced learner-visible direct HLS URLs with 5-minute token-authenticated
  Bunny embed URLs, ready for MediaCage and never exposing the library key.
- Enabled the protected player on web using an authenticated, user-bound,
  single-use nonce; native clients retain shell-held HMAC attestation.
- Playback sessions persist the request device id and revoke if renewal is
  attempted from a different device.
- Auth and API requests await native secure-storage device-id initialization;
  enforced fresh sign-ins reject a missing device id instead of failing open.
- Added a sandboxed, origin-checked embed controller while keeping the moving
  forensic watermark above the provider player, including parent fullscreen.
- Native shells now fail closed when OS capture protection cannot engage.
  Web remains functional under watermark, token, session, and audit controls.
- Screenshot/capture/tamper signals flush immediately and pause/mute both
  direct and embedded playback before server-side revocation.
- The production migration enables single-session, risk enforcement,
  trusted-device OTP binding, verified-email gating, rooted/emulator blocks,
  capture revocation, and a 300-second playback token TTL. Legacy device-unbound
  refresh tokens and active playback sessions are revoked.
- Added encrypted runtime configuration and a fail-open IPinfo
  Core/Plus/Max integration for VPN/proxy/Tor/hosting sign-in signals.
- Updated the security matrix, acceptance report, and admin runbook with the
  actual implementation state and remaining external/manual evidence.

## Validation

- Focused Vitest (attestation + embed-origin controller): 11/11 passed.
- Focused ESLint across every touched frontend/test file: passed.
- `pnpm exec tsc --noEmit --pretty false`: first run reached only four
  pre-existing unrelated errors in `lib/__tests__/api.test.ts` (lines 458,
  459, 480, 481); the post-change rerun hit the host timeout.
- Focused .NET test/build attempts hit the repository's known MSBuild host
  stall and were terminated; the GitHub production build is the compile gate.
- A focused ESLint retry also hit the host command timeout; no lint result was
  claimed.
- Final targeted ESLint checks completed with 0 errors (existing React hook
  warnings only) for the admin security/user pages, sessions page, auth and
  notification paths, device-id client, and API type helpers.
- `git diff --check` passed. The bounded API build emitted package/vulnerability
  warnings and then produced no compiler output for over two minutes, so it
  was stopped; no green backend build is claimed from this host.

## External Blockers / Residual Evidence

- Bunny MediaCage Basic or Enterprise DRM must be enabled and verified in the
  authenticated Bunny account; provider dashboard access is unavailable here.
- A signed iOS IPA requires Apple team id, distribution certificate,
  provisioning profile, and a non-placeholder associated-domain file.
- Android 1.4.1 and Windows/macOS desktop 0.7.0 are signed and published, but
  both tags predate the final security commits. The installed Windows 0.7.0
  app remained capturable in a foreground `CopyFromScreen` test, so corrected
  releases must be published from the post-deploy `main` SHA.
- IPinfo code is complete; production activation still needs a paid provider
  token entered through Admin → Settings → Security.
- Hardware OBS/RDP/mirroring/screenshot tests remain manual acceptance work.

## Next Step

Stage only the explicit implementation paths (preserving `.codex/config.toml`
and `.superpowers/`), commit, push `main`, and verify the blue/green production
workflow. The owner still needs real-device/manual acceptance for same-hardware
browser/app counting and cross-platform sign-out behavior.

# Current Task - Permanent admin user purge and Actions-only production compute

Last updated: 2026-08-08

## Outcome

- Admin Delete now permanently purges learner/expert profiles, auth accounts,
  sessions, attempts, billing rows, audit references, and other model-discovered
  user-linked rows; the normalized email row is removed so the address can be
  registered again. Legacy soft-deleted profiles remain purgeable from the
  detail page.
- The admin UI uses one explicit permanent-delete action with an accessible
  exact-email confirmation modal and returns to the user list after success.
- The system-admin hard-delete compatibility route delegates to the same purge
  service without retaining the target user id in the new audit ResourceId.
- Production deploys now build web, API, and backup-sidecar images in GitHub
  Actions; the active VPS rollout pulls per-commit image tags and starts
  containers with `--no-build`. Legacy source-build scripts refuse to run
  unless an owner-approved emergency override is explicitly supplied.

## Validation

- `vitest run app/admin/users/[id]/page.test.tsx --reporter=dot`: 1 file,
  8/8 tests passed, including exact-email purge confirmation.
- `git diff --check`: passed.
- Backend filtered build/test commands stalled on the shared Windows host
  before compiler/test output; no local backend pass is claimed. GitHub Actions
  remains the backend compile/test and production image gate.

## Next step

Stage only the explicit implementation/tests/workflow/deploy paths, commit and
push `main`, then verify the GitHub blue/green deployment and production health.
The two already-soft-deleted accounts must be purged once from the deployed
admin UI; no direct production database deletion was performed here.

# Current Task - Forward-compatible per-user video access

Last updated: 2026-08-08

## Outcome

- Preserved explicit per-user video allocations for existing content while
  automatically including videos first published after the initial video scope.
- Applied the same rule to learner catalog/detail visibility and the playback
  entitlement gate, so a newly uploaded and published video does not require
  ticking every registered learner.
- Preserved the original scope timestamp when an admin later edits the user's
  selected video ids, preventing unrelated saves from hiding new content.

## Validation

- Targeted ESLint passed for `components/admin/user-access/video-scope-picker.tsx`
  and `lib/user-access.ts`.
- `git diff --check` passed.
- Focused `dotnet test` and direct API `dotnet build` both stalled on the shared
  Windows MSBuild host before compiler/test output; no local backend pass is
  claimed. GitHub Actions remains the backend compile/test gate.

## Next step

Stage only the explicit video-access implementation, tests, docs, and this
state file; commit and push `main`, then verify the GitHub production workflow
and health gates. Preserve unrelated `.codex/config.toml` and `.superpowers/`.

# Current Task - OET prep quick fixes v2

Last updated: 2026-08-08

## Outcome

- Added mobile-safe first-party video fullscreen sizing using `100dvh`/
  `100vw`, vendor fullscreen API fallbacks, and a bottom-right stretch/fit
  control for both secure embeds and legacy direct-HLS playback.
- Kept the protected player container as the fullscreen element so the
  first-party watermark remains above the provider iframe.
- Unified the public/banner download badge geometry and added explicit temporary
  direct-iOS copy and links.
- Added `/api/download/ios`, which only redirects to a published `.ipa` from a
  trusted `jerryboganda/oetwebapp` `v*-mobile-ios` release; otherwise it falls
  back to the GitHub releases page. `NEXT_PUBLIC_IOS_APP_STORE_URL` remains the
  preferred destination when configured.

## Validation

- Focused Vitest: 6 files, 13/13 tests passed, covering secure/legacy video
  controls, banner and `/get-app` badge links, auth-strip links, and native
  release/download resolvers.
- Scoped ESLint: 0 errors; existing React effect warnings only.
- `pnpm exec tsc --noEmit`: passed.
- `git diff --check`: passed.

## External Boundary / Next Step

- No future `v*-mobile-ios` IPA is currently published in the public release
  inventory, so the direct endpoint is wired and fail-safe but cannot produce
  an IPA download until Apple signs and publishes that release asset.
- Stage only the explicit implementation, test, plan/spec, and state paths;
  preserve unrelated `.codex/config.toml` and `.superpowers/`, then commit and
  push `main`. Pushing `main` is the production deployment trigger; owner
  verification of real mobile fullscreen behavior and the eventual IPA remains
  the external acceptance boundary.

# Current Task - Four-platform app-download icon system

Last updated: 2026-08-10

## Outcome

- Replaced grouped/generic app-download artwork with shared Windows, Mac,
  Google Play, and App Store inline SVG glyphs in the marketing badge system.
- Applied the four-button system to banner, modal, card, auth, `/get-app`, and
  Android install download surfaces used by the web, desktop, and Capacitor
  shells.
- Preserved existing download destinations and left native launcher/splash
  assets untouched.

## Validation

- Focused Vitest: `components/marketing/app-download-promo.test.tsx`,
  `components/auth/__tests__/auth-screen-shell.test.tsx`, and
  `app/get-app/page.test.tsx`: 3 files, 6/6 tests passed.
- `git diff --check`: passed.
- Source audit found no remaining generic app-download `Monitor`, `Laptop`,
  `Smartphone`, `Apple`, `Download`, or grouped desktop/mobile badge usage in
  the audited surfaces.

## Next step

Stage only the explicit four-platform implementation, tests, plan, and this
state file; preserve unrelated existing changes plus `.codex/config.toml` and
`.superpowers/`, then commit and push `main`.

# Current Task - Strict acceptance compliance: video player and public downloads

Last updated: 2026-08-10

## Outcome

- Removed the stretch/fit presentation control from secure and legacy video
  playback; retained the accessible fullscreen control.
- Made fullscreen direct playback occupy the viewport with `100vw`/`100dvh`
  sizing and a black background; secure embeds and direct video both fill the
  fullscreen element.
- Added the same four-platform download section to the separate public
  pricing-site checkout (`pricing.html`) with an Apps navigation route.

## Validation

- Focused Vitest: 4 files, 8/8 tests passed.
- Scoped ESLint: passed for the touched video files and stylesheet.
- Public pricing static gate: one `#apps` section, four approved badges, and
  the exact Windows, Mac, Google Play, and App Store labels.
- `git diff --check`: passed; the public checkout reports only its normal
  LF-to-CRLF working-copy warning.

## External Boundary / Next Step

- Commit and push the explicit video files and state update on `main`, commit
  and push the explicit `pricing.html` change in `D:\Projects\oetwebsite`,
  then verify the production workflow, VPS image/health gates, and public
  pricing URL. Preserve unrelated in-progress backend/mock changes.

# Current Task - OET Speaking booking workflow implementation

Last updated: 2026-08-10

## Implementation checkpoint

- Enforced the 7-day Speaking tutor gate fail-closed for missing exam dates,
  including gateway, direct Speaking launch, availability, and booking POST.
- Made Full Mock Speaking availability and rescheduling use the active tutor
  calendar; legacy Speaking mutations reject with the canonical-workflow error.
- Enforced the strict refund boundary: strictly more than 24 hours is eligible,
  exactly 24 hours is not; rescheduling remains available before session start.
- Added idempotent AI-package/mock-credit debit and entitlement reversal for
  standalone Full Mock bookings, plus auditable cancellation refund state.
- Added Zoom-required booking creation, automated booking confirmation jobs,
  learner/expert notification payloads, and 24h/1h/15m reminders.
- Added migration `20260810090000_AddSpeakingMockTutorAndRefundPolicy` and shared
  `SpeakingBookingPolicy` regression tests.

## Validation

- `pnpm exec tsc --noEmit --pretty false`: passed.
- `dotnet msbuild backend/src/OetLearner.Api/OetLearner.Api.csproj /t:CoreCompile ... /p:RunAnalyzers=false`: passed; only existing package vulnerability warnings.
- `git diff --check`: passed.
- Test-project build remains blocked by missing local NuGet analyzer/runtime files (`microsoft.extensions.options`, `xunit.analyzers`, `system.diagnostics.eventlog`); CI must provide the complete restore cache.

## Next step

- Review/stage only the explicit Speaking workflow implementation, migration,
  tests, and state file; preserve `.codex/config.toml`, `.superpowers/`, and
  unrelated user files. Commit/push `main`, monitor deployment, then verify
  production health and protected endpoint boundaries without using credentials.


# Current Task - Listening and Reading AI system specification v1.1

## Implementation checkpoint

- Added versioned owner-managed exact 0-42 conversion tables with persisted raw/scaled/grade/pass evidence and no formula fallback.
- Added effective/locked marking-policy snapshots, rationale approval gates, controlled re-mark jobs, audit events, and admin lifecycle controls.
- Added deterministic Listening/Reading grading safeguards, after-submit additive AI gates, exact non-official result disclosures, and conversion evidence graphs.
- Preserved unrelated Writing, PDF-policy copies, .codex/config.toml, and .superpowers/ worktree content.

## Validation

- Canonical Reading results test: 1 file, 5/5 passed, excluding unrelated untracked PDF-policy copies.
- Focused ESLint over changed frontend files: 0 errors, existing React hook warnings only.
- git diff --check: passed.
- Full TypeScript check is blocked by pre-existing duplicate test globals and unrelated untracked PDF-policy copies.
- Backend compile/test boundary remains the pre-existing untracked Writing source errors; touched governance/grading files had no compiler errors in the bounded compile check.

## Next step

- Stage only the explicit v1.1 implementation files, commit and push main, then verify the GitHub Actions production release and both live health endpoints. Owner-supplied conversion tables, normalization profile, and live production acceptance remain required boundaries.
# Current Task - OET Speaking booking workflow PDF completion audit

Last updated: 2026-08-11

## Latest validation checkpoint

- Repaired the remaining frontend CI contracts without weakening the Speaking
  booking enforcement: strict web playback rejection is preserved, radiography
  rulebook coverage is registered, and admin notification permissions match the
  sidebar map.
- Full isolated frontend Vitest suite: 324 test files passed, 2,230 tests
  passed. TypeScript `--noEmit` passed. Expected negative-path test logging
  remains (breach-password and simulated network/provider failures); no
  unhandled Vitest errors remain.

## Next step

Commit only the explicit frontend/rulebook paths, push the exact revision to
`main`, wait for Build & Deploy, then verify the deployed SHA, health/readiness,
and blue/green container images on the production VPS. Preserve unrelated
dirty work in the main checkout.

# Latest LR coding checkpoint - 2026-08-12

- Added explicit LR-13 authoring regressions for Listening relational MCQs with
  zero and multiple correct options, plus Reading scalar-answer rejection for
  empty and multiple-answer payloads. The first hosted run exposed only a test
  fixture mistake (unpersisted seeded options); `d1268602a` corrects the tests
  to mutate tracked seed entities. Reading LR-13 cases passed in that run;
  the corrected Listening cases have not been rerun.
- Added `ListeningReadingExplanationFailureTests` covering deterministic Reading
  and Listening fallbacks when the grounded AI gateway throws. No score or
  result path depends on AI output.
- PDF Section 12 also required duplicate MCQ options to be rejected. Reading
  now rejects blank/duplicate visible labels and duplicate option values/IDs;
  Listening JSON and relational validators reject blank/duplicate keys and
  option text. Focused execution remains pending.
- Local .NET execution previously stalled after 124 seconds; per the current
  owner instruction, do not start or wait on long CI/CD or full validation runs.
- The shared ContentPaper publish path now runs Reading/Listening structural
  validation and hard-blocks malformed MCQs (including duplicate options and
  zero/multiple correct options) while preserving the broader advisory policy.
  Added `ContentPaperServiceTests.Publish_rejects_reading_mcq_with_duplicate_options`
  and `Publish_rejects_listening_mcq_with_duplicate_options`;
  only `git diff --check` has been run for this slice.
- Added the Listening Section 12 pre-publish preview surface: an admin-only
  server projection omits correct answers/accepted variants/explanations, while
  the same page provides a separate answer-key marking view from the protected
  authoring endpoint. Focused UI/API execution remains pending.
- Closed the LR-08 publication revision gap: Reading/Listening paper publish
  now assigns a bounded `PublishedRevisionId`, and unpublish clears it so a
  later publish receives a new pin; the focused service test asserts the ID.
  Only `git diff --check` has been run.
- Preserve `.codex/config.toml`, `.superpowers/`, `pdf-policy-release/`, and
  `pdf-policy-release2/`. Owner tables, normalization/lock policy, rationale
  library, thresholds/labels, graph approval, load evidence, and authenticated
  browser/mobile acceptance remain unresolved.

# Latest LR coding checkpoint - 2026-08-12 (audio/timing gate)

- Listening validation now reads processed `MediaAsset.DurationSeconds` for
  every primary audio asset, supports paper-level and per-section audio, and
  emits `listening_audio_duration` when duration metadata is absent or
  non-positive.
- Relational and legacy JSON Listening extracts now validate authored
  `timeLimitSeconds` against cue end times and uploaded audio duration through
  `listening_section_timing`; explicit non-positive section limits also fail.
- `ContentPaperService` now hard-blocks Listening audio-source, duration,
  extract-timing, cue-overlap, and section-timing defects while preserving the
  broader advisory authoring policy. Added focused duration/section-timing
  regression fixtures.
- Reading and Listening typed-answer authoring now projects accepted-variant
  audit history as actor/timestamp/reason without returning raw answer-key
  snapshots. Listening Part A bulk editing now requires a reason for variant
  changes and renders the same history per answer row; question editors show
  the history inline.
- Listening questions now carry `validationStatus`/`validationNote` in the
  existing authoring JSON and relational contracts, the admin question editor
  exposes them, both JSON and relational publish validation hard-block any
  question not marked `published`, and only content-publish/publisher-approval
  permissions may promote a question to `published`. Added a focused fixture
  regression for the fail-closed default and updated canonical publish-ready
  fixtures.
- Only `git diff --check` and scoped source searches were run by request;
  no long local validation, CI, push, or deployment was started.
- Preserve the four untracked user-owned paths above. Owner release inputs and
  authenticated deployed browser/mobile acceptance remain unresolved.

# Latest LR coding checkpoint - 2026-08-12 (audio validity hold)

- Listening `audio_error` events now persist a fail-closed admin-review hold on
  both relational `ListeningAttempt` and legacy `Attempt` rows, with reason and
  timestamp fields, migration/snapshot metadata, and admin-export coverage.
- Relational and generic save/advance/submit paths reject held attempts. The
  Listening FSM also rejects held navigation, readiness, resume, and annotation
  mutations so a failed scored media run cannot continue silently.
- The Listening player halts playback, clears pending answer timers, blocks
  answer/navigation/submit actions, removes retry for an active attempt, and
  logs structured `audio_error` validity metadata without automatic replay.
- Added `ListeningAttemptEventLoggingTests.RecordIntegrityEvent_AudioErrorFlagsAttemptForAdminReview`.
- Only bounded source searches and `git diff --check` are intended for this
  slice; no long local validation, CI/CD, push, or deployment was started.
- Preserve the four untracked user-owned paths above. Owner release inputs and
  authenticated deployed browser/mobile acceptance remain unresolved.

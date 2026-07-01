# OET Speaking Module — Release Runbook

> Phase 12 of the Speaking module rollout. Companion to `docs/SCORING.md`,
> `docs/RULEBOOKS.md`, `docs/AI-USAGE-POLICY.md`, and `docs/CONVERSATION.md`.

This runbook covers the operational steps for releasing, monitoring, and (if
required) rolling back the OET Speaking module that ships:

1. Pre-warm-up timer (`/speaking/sessions/{id}/warmup`)
2. AI patient turn (self-practice mode)
3. Two-role-play mock pathway
4. LiveKit live-tutor mode (calibration + recording)
5. Drill bank (admin CRUD + AI-assisted draft)
6. Admin analytics (cohort, tutor consistency, content difficulty)
7. Compliance surfaces (learner recordings, retention, erasure pre-flight,
   admin access audit)

---

## 1. Feature flag

Backend: `Features:SpeakingV2` in
`backend/src/OetLearner.Api/appsettings.json` (and overrides in
`appsettings.Production.json` / environment variable
`Features__SpeakingV2`).

- `true` → all Speaking V2 routes mounted, navigation surfaced.
- `false` → routes return `404` and the learner shell hides Speaking V2 entry
  points; legacy practice surface remains available.

Toggle at runtime via `IRuntimeSettingsProvider` (admin → runtime settings).
No restart required — provider cache TTL is 30 s.

## 2. Pre-flight checks

Run locally in Docker Desktop before deploy. The production VPS is deployment-only and must not run builds, lint, type-checks, or tests.

```powershell
npm run docker:tsc
npm run docker:lint
npm run docker:test
docker exec oet-local-web npm run build
docker exec oet-local-api dotnet build
docker exec oet-local-api dotnet test
```

Required: zero TypeScript errors, zero ESLint errors, all Vitest suites green,
all backend tests green except the pre-existing baseline failures tracked in
`security-analysis-progress.md`.

## 3. Smoke E2E

Run the four Speaking smokes from the local Docker web container, targeting the deployed environment by configuration when needed. Do not execute Playwright on the VPS.

```powershell
docker exec oet-local-web npm run test:e2e:smoke -- --grep speaking
```

Smokes cover: warm-up timer enforcement, self-practice happy path, two-roleplay
mock with assessment seeding, and live-tutor calibration banner.

## 4. Production deploy

Production deploys are exact-SHA only. After CI publishes immutable digest image
refs for the target SHA:

```bash
ssh root@185.252.233.186
cd /opt/oetwebapp
DEPLOY_REF=<40-char-sha> \
WEB_IMAGE=<web-image@sha256:...> \
API_IMAGE=<api-image@sha256:...> \
DB_BACKUP_IMAGE=<db-backup-image@sha256:...> \
ROUTER_IMAGE=<router-image@sha256:...> \
bash ./scripts/deploy/deploy-prod.sh
```

The blue/green rollout will:

1. Pull the immutable image digest supplied by the deploy workflow.
2. Roll the inactive web slot forward; run health probes.
3. Switch the stable router only after `GET /api/health` returns 200.
4. Preserve at least one previous-good release for rollback.

## 5. Post-deploy verification

Within 5 minutes of router switch:

- `GET https://api.oetwithdrhesham.co.uk/v1/health` → 200.
- `GET https://api.oetwithdrhesham.co.uk/v1/speaking/course-pathway` (with a
  test learner JWT) → 200, returns ordered stages.
- Admin opens `/admin/analytics/speaking` → all three cards render without
  error.
- Admin opens `/admin/speaking/recordings/audit` → page loads with empty
  filter (last 100 events).
- Learner opens `/speaking/recordings` → returns own recordings (or empty
  state).

If any of these fails, jump to **Rollback**.

## 6. Rollback

Two paths depending on severity:

### Fast path — feature flag

If the issue is a regression in Speaking surface only:

1. Admin → runtime settings → toggle `Features:SpeakingV2` to `false`.
2. Wait ≤ 30 s for cache TTL.
3. Verify learner shell hides Speaking V2 entry points and routes return 404.

This rolls back **without restarting** any container.

### Full path — image rollback

If the issue is broader (auth, API, scoring, gateway):

```bash
ssh root@185.252.233.186
cd /opt/oetwebapp
bash ./scripts/deploy/rollback-prod.sh   # rolls to previous-good image digests
```

Verify health endpoints return 200, then post incident summary in
`mission-critical-execution-ledger.md`.

## 7. Audio retention

Speaking audio is content-addressed in `IFileStorage` and swept by
`SpeakingAudioRetentionWorker` (parity with Conversation/Pronunciation).

- Retention window: `SpeakingOptions.AudioRetentionDays` (default 365).
- Worker runs daily at 02:00 UTC; deletes expired blobs and writes
  `AuditEvent { Action="SpeakingRecordingPurged" }`.
- Manual purge: admin → recordings → access audit → erase. Backend writes
  `SpeakingRecordingDeleted` audit row with purpose; the row remains for
  forensic continuity even after the blob is gone.

## 8. Right-to-erasure (GDPR / NHS DSP)

1. Learner submits erasure request via `/speaking/recordings` → "Erase".
2. Frontend calls `GET /v1/speaking/recordings/erasure-preflight` to compute
   the impact (count, oldest, newest, total bytes).
3. Backend issues a queued `SpeakingErasureBatch`; deletes blobs via
   `IFileStorage`, marks rows archived, writes audit events.
4. Admin can view the audit trail at
   `/admin/speaking/recordings/audit`. Blobs are gone; metadata rows persist
   with `IsArchived = true` for compliance reporting.

## 9. AI grounding gate

All Speaking AI features (assessment, drill draft, role-play card draft) MUST
route through `IAiGatewayService.BuildGroundedPrompt` + `CompleteAsync`.
Ungrounded calls fail closed with `PromptNotGroundedException`. The grounded
gateway:

- Embeds the canonical `rulebooks/speaking/<profession>/rulebook.v*.json`.
- Embeds canonical `OetScoring` thresholds (Speaking pass = 350).
- Refuses BYOK on `AiFeatureCodes.AdminSpeakingDraft` (platform-only).
- Records exactly one `AiUsageRecord` per call (success, error, or refusal).

## 10. Monitoring

- Sentry: errors tagged `module:speaking` route to the Speaking dashboard.
- AI usage: `/admin/ai/usage` → filter by feature code prefix
  `speaking.` for token, cost, and refusal volumes.
- LiveKit: webhook signing secret rotation and per-room max-duration
  enforcement live in `LiveKitOptions`.

## 11. Open follow-ups (P11/P12 backlog)

All P11/P12 backend AI-draft follow-ups are now landed on
`origin/mocks-phase6-verify`:

- `POST /v1/admin/speaking/drills/ai-draft` — live. Routes through
  `IAiGatewayService.BuildGroundedPrompt(Kind = Speaking,
  Task = GenerateContent, FeatureCode =
  AiFeatureCodes.AdminContentGeneration)` (platform-only), parses the
  reply, persists a `Draft` `SpeakingDrillItem` + `ContentItem` atomically,
  and returns a flat projection plus an optional `warning` when the AI
  reply could not be parsed and the deterministic fallback was used.
  The admin UX surface at `app/admin/content/speaking/drills/ai-draft/`
  calls this directly via `draftSpeakingDrill()` from
  `lib/api/speaking-drills.ts`.
- `POST /v1/admin/speaking/role-play-cards/ai-draft` — live. Same gateway
  contract; persists the candidate card + paired hidden interlocutor
  script atomically and returns the persisted `RolePlayCardDetail` with
  an optional `warning`. The admin UX surface at
  `app/admin/content/speaking/role-play-cards/ai-draft/` calls this
  directly via `draftSpeakingRolePlayCard()` from
  `lib/api/speaking-role-play-cards.ts` and navigates to the card editor
  for review/publish.

Both flows reuse the existing `AiFeatureCodes.AdminContentGeneration` +
`AiTaskMode.GenerateContent` — no new constants or enum values were added
for this work.

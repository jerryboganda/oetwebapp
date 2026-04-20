# Pronunciation Authoring Policy

> Companion to [`docs/PRONUNCIATION.md`](./PRONUNCIATION.md). Defines the admin-side authoring rules for pronunciation drills.

## 1. Source of truth

- Drill metadata lives in the `PronunciationDrills` table.
- Rule IDs cited on each drill MUST exist in `rulebooks/pronunciation/<profession>/rulebook.v1.json`.
- The canonical scoring anchor table (0 → 0, 60 → 300, 70 → 350, 80 → 400, 90 → 450, 100 → 500) lives in `rulebooks/pronunciation/common/assessment-criteria.json` and is mirrored by `OetScoring.PronunciationProjectedScaled()` + `lib/scoring.ts:pronunciationProjectedScaled()`.

No other locations may define pronunciation scoring or rule semantics.

## 2. Supported professions

| Profession code | Folder |
|---|---|
| medicine | `rulebooks/pronunciation/medicine/rulebook.v1.json` |
| nursing | `rulebooks/pronunciation/nursing/rulebook.v1.json` |
| dentistry | `rulebooks/pronunciation/dentistry/rulebook.v1.json` |
| pharmacy | `rulebooks/pronunciation/pharmacy/rulebook.v1.json` |
| physiotherapy | `rulebooks/pronunciation/physiotherapy/rulebook.v1.json` |
| occupational-therapy | `rulebooks/pronunciation/occupational-therapy/rulebook.v1.json` |
| speech-pathology | `rulebooks/pronunciation/speech-pathology/rulebook.v1.json` |

A drill may also ship with profession `all` to signal cross-profession applicability.

## 3. Authoring surfaces

Admins author drills through:

1. `/admin/pronunciation` — list + filter
2. `/admin/pronunciation/new` — manual draft
3. `/admin/pronunciation/[drillId]` — edit + publish
4. `/admin/pronunciation/ai-draft` — grounded AI assisted draft

## 4. AI-assisted drafting rules

All AI drafts route through `PronunciationAdminDraftService`, which:

1. Builds a grounded prompt via `IAiGatewayService.BuildGroundedPrompt(Kind = Pronunciation, Task = GeneratePronunciationDrill)`.
2. Enforces `FeatureCode = AiFeatureCodes.AdminPronunciationDraft` (platform-only).
3. Validates every emitted `appliedRuleIds` entry against the loaded rulebook; unknown IDs are stripped and surfaced as a warning to the admin.
4. Persists nothing — returns a draft DTO the admin can edit + save via `POST /v1/admin/pronunciation/drills`.

Admin may BYOK via the gateway config but the feature code remains platform-only: pronunciation drafting counts as content authoring, not learner usage, so it is not BYOK-eligible.

## 5. Publish gate

A drill cannot transition from `draft` → `active` unless:

- `TargetPhoneme` is non-empty.
- `Label` is non-empty.
- `TipsHtml` is non-empty.
- `ExampleWordsJson` parses to ≥ 3 unique strings.
- `SentencesJson` parses to ≥ 1 non-empty sentence.

The gate is enforced in `AdminService.UpdatePronunciationDrillAsync()` and returns `ApiException.Validation("DRILL_PUBLISH_GATE", ...)` on failure.

## 6. Model audio

- Preferred: upload via admin media CMS → populate `AudioModelAssetId`.
- Compatibility: raw `AudioModelUrl` strings remain supported for migration.
- TTS generation is the fallback: use `scripts/generate-pronunciation-audio.ts` (optional, runs locally with a TTS key) to bulk-produce model audio.

Model audio is not a publish-gate blocker today because a fresh DB must still be usable without external credentials; drills without model audio hide the `<audio>` element client-side.

## 7. Rulebook update policy

1. Edit the profession JSON under `rulebooks/pronunciation/<profession>/rulebook.v1.json`.
2. Bump the `version` field on the rulebook.
3. Run backend tests: `dotnet test backend/OetLearner.sln`.
4. Run the pronunciation rulebook loader tests — these will catch schema breakage.
5. Every admin AI draft produced against the new version will have the new `RulebookVersion` stamped on the `AiUsageRecord` row.

The backend embeds every file under `rulebooks/**/*.json` via `<EmbeddedResource>` at build time. There is no runtime file loading — a production pod cannot diverge from the repo snapshot it shipped with.

## 8. Audit

Every admin mutation writes an `AuditEvent` through `AdminService.LogAuditAsync()`:

- Resource: `PronunciationDrill`
- Action: `Created | Updated | Archived`
- Description: admin-supplied + system context

AI drafts do not mutate the drills table; the `AiUsageRecord` row is the audit trail.

## 9. Accessibility contract for authored content

- `TipsHtml` must be sanitized; admin-trusted but rendered via `dangerouslySetInnerHTML`. Authors must not inject `<script>` or event handlers.
- Minimal pairs must communicate the phonetic difference in text; colour alone is insufficient (see `PhonemeHeatmap.tsx`).
- Practice sentences must remain natural OET register — no ad-copy, no marketing tone.

## 10. Retention and lifecycle

- Drafts have no retention cap.
- Archived drills are hidden from learners but never deleted.
- Learner-uploaded audio blobs are deleted after `PronunciationOptions.AudioRetentionDays` (default 45) by `PronunciationAudioRetentionWorker`.
- `PronunciationAssessment` rows are retained indefinitely — they are the canonical history.

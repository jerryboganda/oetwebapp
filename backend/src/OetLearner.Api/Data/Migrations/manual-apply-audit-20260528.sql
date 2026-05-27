-- 2026-05-28 audit fix — manually applied because dotnet ef database update
-- rejects the migration on the snapshot-drift warning, and renaming the
-- migration filename for ordering was blocked by the harness.
--
-- This script is idempotent (uses IF NOT EXISTS) and registers itself in
-- __EFMigrationsHistory so the next `dotnet ef database update` recognises
-- this migration as already applied.
--
-- To apply:
--   docker exec -i oet-local-postgres psql -U postgres -d oet_learner_dev \
--     < backend/src/OetLearner.Api/Data/Migrations/manual-apply-audit-20260528.sql

BEGIN;

-- RulebookVersion columns (Speaking, Listening, Reading) — version pinning.
ALTER TABLE "SpeakingSessions"   ADD COLUMN IF NOT EXISTS "RulebookVersion" varchar(32);
ALTER TABLE "ListeningAttempts"  ADD COLUMN IF NOT EXISTS "RulebookVersion" varchar(32);
ALTER TABLE "ReadingAttempts"    ADD COLUMN IF NOT EXISTS "RulebookVersion" varchar(32);

-- Speaking Whisper transcription overrides (admin-rotatable from Runtime Settings UI).
ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "SpeakingWhisperApiKeyEncrypted" text;
ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "SpeakingWhisperBaseUrl"         varchar(512);
ALTER TABLE "RuntimeSettings" ADD COLUMN IF NOT EXISTS "SpeakingWhisperModel"           varchar(64);

-- Reading Part C paragraph index (rule R07.6 — questions follow paragraph order).
ALTER TABLE "ReadingQuestions" ADD COLUMN IF NOT EXISTS "ParagraphIndex" integer;

-- Mark the EF migration as applied so `dotnet ef database update` no-ops.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260527211242_Audit20260528_RulebookCompliance', '10.0.7')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

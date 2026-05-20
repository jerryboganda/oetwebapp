-- Combined fix:
--   1) Re-add the 13 RuntimeSettings columns the entity expects.
--   2) Create the NativeIapProductMappings table + indexes.
--   3) Mark all 4 pending migrations as applied so EF skips them at startup.

BEGIN;

-- ===== 1. RuntimeSettings columns =====
ALTER TABLE "RuntimeSettings"
  ADD COLUMN IF NOT EXISTS "PayPalCancelUrl"               character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalClientId"                character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalClientSecretEncrypted"   text NULL,
  ADD COLUMN IF NOT EXISTS "PayPalSuccessUrl"              character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalWebhookIdEncrypted"      text NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerFailClosedOnError" boolean NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerHost"             character varying NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerPort"             integer NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerProvider"         character varying NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerTimeoutSeconds"   integer NULL,
  ADD COLUMN IF NOT EXISTS "VapidPrivateKeyEncrypted"      text NULL,
  ADD COLUMN IF NOT EXISTS "VapidPublicKey"                character varying NULL,
  ADD COLUMN IF NOT EXISTS "VapidSubject"                  character varying NULL;

-- ===== 2. NativeIapProductMappings table =====
CREATE TABLE IF NOT EXISTS "NativeIapProductMappings" (
  "Id"                character varying(64)  NOT NULL,
  "Platform"          character varying(16)  NOT NULL,
  "StoreProductId"    character varying(192) NOT NULL,
  "TargetType"        character varying(32)  NOT NULL,
  "TargetId"          character varying(96)  NOT NULL,
  "DisplayName"       character varying(128) NULL,
  "IsActive"          boolean                NOT NULL,
  "CreatedAt"         timestamp with time zone NOT NULL,
  "UpdatedAt"         timestamp with time zone NOT NULL,
  "CreatedByAdminId"  character varying(64)  NULL,
  "UpdatedByAdminId"  character varying(64)  NULL,
  CONSTRAINT "PK_NativeIapProductMappings" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_NativeIapProductMappings_Platform_IsActive"
  ON "NativeIapProductMappings" ("Platform", "IsActive");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_NativeIapProductMappings_Platform_StoreProductId"
  ON "NativeIapProductMappings" ("Platform", "StoreProductId")
  WHERE "IsActive" = TRUE;

CREATE INDEX IF NOT EXISTS "IX_NativeIapProductMappings_TargetType_TargetId"
  ON "NativeIapProductMappings" ("TargetType", "TargetId");

-- ===== 3. Mark 4 migrations as applied =====
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
  ('20260518120000_AddUploadScannerRuntimeSettings', '10.0.0'),
  ('20260518123000_AddNativeIapProductMappings',     '10.0.0'),
  ('20260518124500_AddPayPalRuntimeSettings',        '10.0.0'),
  ('20260518131500_AddPushVapidRuntimeSettings',     '10.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

SELECT 'columns_added=' || COUNT(*) FROM information_schema.columns
  WHERE table_name='RuntimeSettings' AND column_name LIKE 'PayPal%';
SELECT 'iap_table_rows=' || COUNT(*) FROM "NativeIapProductMappings";
SELECT 'migs_applied=' || COUNT(*) FROM "__EFMigrationsHistory"
  WHERE "MigrationId" LIKE '20260518%';

-- Add columns the new image's RuntimeSettings entity expects but the DB lacks.
-- All nullable; RuntimeSettingsProvider treats NULL as "use env-var baseline".
-- Safe to re-run (uses IF NOT EXISTS).

ALTER TABLE "RuntimeSettings"
  ADD COLUMN IF NOT EXISTS "PayPalCancelUrl"             character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalClientId"              character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalClientSecretEncrypted" text NULL,
  ADD COLUMN IF NOT EXISTS "PayPalSuccessUrl"            character varying NULL,
  ADD COLUMN IF NOT EXISTS "PayPalWebhookIdEncrypted"    text NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerFailClosedOnError" boolean NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerHost"           character varying NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerPort"           integer NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerProvider"       character varying NULL,
  ADD COLUMN IF NOT EXISTS "UploadScannerTimeoutSeconds" integer NULL,
  ADD COLUMN IF NOT EXISTS "VapidPrivateKeyEncrypted"    text NULL,
  ADD COLUMN IF NOT EXISTS "VapidPublicKey"              character varying NULL,
  ADD COLUMN IF NOT EXISTS "VapidSubject"                character varying NULL;

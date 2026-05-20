-- Revert manual column adds so EF migrations can apply themselves cleanly.
ALTER TABLE "RuntimeSettings"
  DROP COLUMN IF EXISTS "PayPalCancelUrl",
  DROP COLUMN IF EXISTS "PayPalClientId",
  DROP COLUMN IF EXISTS "PayPalClientSecretEncrypted",
  DROP COLUMN IF EXISTS "PayPalSuccessUrl",
  DROP COLUMN IF EXISTS "PayPalWebhookIdEncrypted",
  DROP COLUMN IF EXISTS "UploadScannerFailClosedOnError",
  DROP COLUMN IF EXISTS "UploadScannerHost",
  DROP COLUMN IF EXISTS "UploadScannerPort",
  DROP COLUMN IF EXISTS "UploadScannerProvider",
  DROP COLUMN IF EXISTS "UploadScannerTimeoutSeconds",
  DROP COLUMN IF EXISTS "VapidPrivateKeyEncrypted",
  DROP COLUMN IF EXISTS "VapidPublicKey",
  DROP COLUMN IF EXISTS "VapidSubject";

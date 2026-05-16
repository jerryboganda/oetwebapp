# Admin Runtime Settings — Canonical Reference

> **Status:** authoritative. This document is the single source of truth for
> the Runtime Settings subsystem. The backend reads every secret in this system
> through `IRuntimeSettingsProvider`; the admin UI exposes them at
> `/admin/settings`. Any deviation from this contract — e.g. reading Stripe
> keys directly from `IOptions<StripeOptions>` in service code — is a critical
> defect and must be fixed before merge.

---

## 1. Purpose

Rotating a production secret traditionally required SSHing into the VPS, editing
`.env.production`, and restarting the Docker stack — a multi-minute window with
restart risk. The Runtime Settings system eliminates that step: an admin with the
`system_admin` role pastes the new value in `/admin/settings` and the running
API picks it up within 30 seconds, with no restart and no SSH access required.

It also serves as the canonical "first-time setup" surface. After initial deploy
you paste every service-level secret from `.env.production` into the UI once;
from that point forward `.env.production` only needs the bootstrap minimum (DB
connection string, JWT signing keys, AI gateway key).

---

## 2. What is covered

The admin settings page is divided into six sections. Each section lists the
`.env.production` keys it supersedes when a DB value is present.

### 2.1 Email — Brevo + SMTP

| UI field | Supersedes env key |
|---|---|
| Brevo enabled | `BREVO__ENABLED` |
| Brevo API key | `BREVO__APIKEY` |
| Brevo from-email | `BREVO__FROMEMAIL` |
| Brevo email-verification template ID | `BREVO__EMAILVERIFICATIONTEMPLATEID` |
| Brevo password-reset template ID | `BREVO__PASSWORDRESETTEMPLATEID` |
| Brevo webhook secret | `BREVO__WEBHOOKSECRET` |
| SMTP host | `SMTP__HOST` |
| SMTP port | `SMTP__PORT` |
| SMTP username | `SMTP__USERNAME` |
| SMTP password | `SMTP__PASSWORD` |
| SMTP from-email | `SMTP__FROMEMAIL` |
| SMTP enable SSL | `SMTP__ENABLESSL` |

### 2.2 Billing — Stripe

| UI field | Supersedes env key |
|---|---|
| Stripe publishable key | `STRIPE__PUBLISHABLEKEY` |
| Stripe secret key | `STRIPE__SECRETKEY` |
| Stripe webhook secret | `STRIPE__WEBHOOKSECRET` |
| Stripe price ID (monthly) | `STRIPE__PRICEID_MONTHLY` |
| Stripe price ID (annual) | `STRIPE__PRICEID_ANNUAL` |

### 2.3 Monitoring — Sentry

| UI field | Supersedes env key |
|---|---|
| Sentry DSN (backend) | `SENTRY_DSN` |
| Sentry DSN (frontend) | `NEXT_PUBLIC_SENTRY_DSN` |

> The frontend DSN is read server-side and injected as a public runtime config
> value; it is not re-encrypted.

### 2.4 Backup — S3-compatible storage

| UI field | Supersedes env key |
|---|---|
| Backup S3 URL | `BACKUP_S3_URL` |
| Backup AWS access key ID | `BACKUP_AWS_ACCESS_KEY_ID` |
| Backup AWS secret access key | `BACKUP_AWS_SECRET_ACCESS_KEY` |
| Backup GPG passphrase | `BACKUP_GPG_PASSPHRASE` |
| Backup alert webhook | `BACKUP_ALERT_WEBHOOK` |

### 2.5 OAuth providers

| UI field | Supersedes env key |
|---|---|
| Google client ID | `OAUTH__GOOGLE__CLIENTID` |
| Google client secret | `OAUTH__GOOGLE__CLIENTSECRET` |
| Apple client ID | `OAUTH__APPLE__CLIENTID` |
| Apple team ID | `OAUTH__APPLE__TEAMID` |
| Apple key ID | `OAUTH__APPLE__KEYID` |
| Apple private key (PEM) | `OAUTH__APPLE__PRIVATEKEY` |

### 2.6 Push notifications

| UI field | Supersedes env key |
|---|---|
| VAPID public key | `PUSH__VAPID_PUBLICKEY` |
| VAPID private key | `PUSH__VAPID_PRIVATEKEY` |
| VAPID subject (mailto:) | `PUSH__VAPID_SUBJECT` |
| FCM server key | `PUSH__FCM_SERVERKEY` |
| APNs key ID | `PUSH__APNS_KEYID` |
| APNs team ID | `PUSH__APNS_TEAMID` |
| APNs private key (PEM) | `PUSH__APNS_PRIVATEKEY` |

---

## 3. What is NOT covered here

These settings live in separate, purpose-built admin pages and are not
part of the Runtime Settings system:

| Domain | Where to configure |
|---|---|
| AI providers (OpenAI, Azure OpenAI, Anthropic, DigitalOcean Serverless, etc.) | `/admin/ai-providers` |
| Conversation module settings (ASR provider, TTS provider, session limits) | `/admin/content/conversation/settings` |
| Pronunciation module settings (ASR provider, audio retention) | `/admin/content/pronunciation` |
| Realtime STT (ElevenLabs WebSocket, language, confidence threshold) | `/admin/launch-readiness` → Realtime STT section |

---

## 4. Architecture

### 4.1 Data model

```
RuntimeSettings table
  Id              TEXT  "default"   -- singleton; no other rows exist
  -- one nullable TEXT column per configurable value
  -- secret columns carry a DataProtection-encrypted blob prefixed "ENC:"
  UpdatedAt       TIMESTAMPTZ
  UpdatedByUserId UUID (FK → Users)
```

There is exactly one row. The `Id="default"` sentinel is enforced by a unique
constraint and by the provider code; any other row is rejected.

### 4.2 Request flow

```
 HTTP PUT /v1/admin/runtime-settings
        │
        ▼
 AdminRuntimeSettingsEndpoints.cs
   • requires system_admin claim
   • validates DTO (Zod-equivalent FluentValidation)
        │
        ▼
 RuntimeSettingsProvider.UpdateAsync()
   • upserts RuntimeSettingsRow (Id="default")
   • encrypts secret fields via IDataProtector("RuntimeSettings.Secret.v1")
   • writes AuditEvent (changed key names only — no values)
   • invalidates in-memory cache
        │
        ▼
 Cache cleared
   • next GetAsync() re-hydrates from DB (max 30s lag for cached reads)
```

### 4.3 Effective-value resolution

```
 GetAsync(key)
        │
        ├─► DB row exists and field is non-null?
        │       └─► Yes → decrypt → return DB value      ← admin override wins
        │
        └─► No → fall back to environment variable       ← env-var baseline
```

**The env-var is always the baseline.** If the DB row is absent (first deploy,
disaster recovery, test environment) the system behaves exactly as before this
feature existed.

### 4.4 Encryption

Secrets are encrypted using ASP.NET Core Data Protection with a fixed purpose
string `RuntimeSettings.Secret.v1`. The key ring is stored on the Docker volume
`oetwebsite_oet_learner_storage` under the `dataprotection/` sub-path (or the
path configured by `DataProtectionKeyPath`). Encrypted blobs are stored with the
`ENC:` prefix so plaintext values (e.g. template IDs that are not sensitive) can
be stored without encryption and distinguished from encrypted blobs at read time.

### 4.5 Caching

```
 First call          ──► DB query  ──► populate MemoryCache (TTL = 30s)
 Subsequent calls    ──► serve from cache
 PUT /runtime-settings ► invalidate cache immediately
```

The 30-second TTL means most reads are in-memory. Cache invalidation on PUT
ensures the admin sees the new value immediately on the next page load.

### 4.6 Stripe webhook boundary

The Stripe webhook handler reads the webhook secret once per webhook request
from `IRuntimeSettingsProvider.GetAsync()` — it does **not** cache the secret
itself. This means a rotated webhook secret takes effect on the next incoming
webhook, not after a 30-second window. All other services respect the 30s TTL.

---

## 5. Permissions and access control

| Action | Required permission |
|---|---|
| Read current values (masked) | `system_admin` |
| Write any field | `system_admin` |

There is no granular write permission for individual sections. Runtime settings
are infrastructure-level secrets; partial write access is not supported. Any
admin with `system_admin` can update any field.

The 16 granular admin permissions (e.g. `ManageBilling`, `ManageContent`) do
**not** grant access to this page. Only the `system_admin` claim does.

---

## 6. Audit trail

Every successful PUT writes an `AuditEvent` row:

```
AuditEvent {
  Action        = "RuntimeSettingsUpdated"
  ResourceType  = "RuntimeSettings"
  ResourceId    = "default"
  UserId        = <system_admin user ID>
  Timestamp     = UTC now
  Metadata      = { "changedKeys": ["Stripe.SecretKey", "Smtp.Password"] }
}
```

Changed values are **never** written to the audit log. Only the names of changed
keys are recorded. This ensures the audit trail is safe to export without
redacting.

---

## 7. Hot-reload semantics

| Setting category | Takes effect |
|---|---|
| Email (Brevo API, SMTP) | Within 30s — next send call re-reads the provider |
| Sentry DSN (backend) | Within 30s — Sentry SDK is re-initialised on next read |
| Sentry DSN (frontend) | On next frontend page render (server-rendered public config) |
| Backup S3 credentials | Within 30s — next scheduled backup job reads fresh creds |
| OAuth client secrets | Within 30s — next OAuth redirect reads fresh config |
| Push notification keys | Within 30s — next push dispatch reads fresh config |
| Stripe publishable key | Within 30s |
| Stripe secret key | Within 30s |
| Stripe webhook secret | **Next webhook request boundary** — see §4.6 |

---

## 8. First-time setup (migration from `.env.production`)

Run this once after the first deploy that includes the Runtime Settings system.
The env file continues to work as a fallback until you complete this step.

1. SSH to the VPS and confirm the API is healthy:

   ```bash
   curl https://api.oetwithdrhesham.co.uk/health/ready
   ```

2. Open `https://app.oetwithdrhesham.co.uk/admin/settings` and sign in as a
   `system_admin` account.

3. Open `.env.production` in a second terminal (read-only):

   ```bash
   cat /opt/oetwebapp/.env.production
   ```

4. Work through each section on the settings page and paste the corresponding
   value from the env file. The table in §2 maps every UI field to its env key.

5. Click **Save** for each section. The page shows a confirmation toast and the
   masked values update to confirm persistence.

6. Verify: trigger a test email (if the Email section was filled) and confirm
   delivery. For Stripe, check the Stripe dashboard for a successful ping (see
   §9 — note that **Test connection** is a Phase 2.1 feature, not in this
   release).

7. Once all sections are saved, the env file values for those keys become
   inert fallbacks. You do not need to remove them — they act as a last-resort
   baseline if the DB row is ever lost.

---

## 9. Secret rotation runbook

### Example: Rotate Stripe secret key

1. **Stripe side** — in the Stripe Dashboard, roll the secret key. Keep both
   the old and new keys active during the transition window (Stripe supports
   simultaneous keys).

2. **UI update** — open `/admin/settings`, expand the **Billing — Stripe**
   section, paste the new secret key, click **Save**.

3. **Verify** — monitor the Stripe Dashboard for successful API calls in the
   next 60 seconds. New outbound Stripe calls (checkout creation, subscription
   queries) use the new key within 30s of the save.

4. **Stripe side** — once satisfied, revoke the old key in the Stripe Dashboard.

5. **Audit check** — confirm a `RuntimeSettingsUpdated` AuditEvent appears in
   `/admin/audit` with `changedKeys` containing `Stripe.SecretKey`.

> **Note:** Test connection (a one-click call that pings the Stripe API with the
> saved key and reports success/failure inline) is a **Phase 2.1** feature and
> is not available in this release. Verification is manual via the Stripe
> Dashboard in the current release.

### Example: Rotate SMTP password

1. **Provider side** — generate a new SMTP credential in Brevo (or your SMTP
   relay). Do not revoke the old one yet.

2. **UI update** — open `/admin/settings`, expand **Email — Brevo + SMTP**,
   update the SMTP password field, click **Save**.

3. **Verify** — trigger a password-reset email for a test account and confirm
   delivery.

4. **Provider side** — revoke the old credential.

### General rotation checklist

```
[ ] New credential created on provider side
[ ] Old credential still active (parallel window)
[ ] New value pasted in /admin/settings → Save
[ ] AuditEvent confirmed in /admin/audit
[ ] Functional test passed (email received / payment accepted / backup uploaded)
[ ] Old credential revoked on provider side
```

---

## 10. Disaster recovery — Data Protection key ring lost

> **Warning: this is unrecoverable.** If the ASP.NET Data Protection key ring
> is lost (e.g. the `oetwebsite_oet_learner_storage` Docker volume is deleted),
> the encrypted blobs in the `RuntimeSettings` table cannot be decrypted. The
> API falls back to env-var values automatically, so the platform continues to
> run — but the DB-stored secrets are permanently unreadable.

Recovery steps:

1. Confirm the API is running on env-var fallback (check logs for
   `RuntimeSettingsProvider: falling back to environment variable` lines).

2. Open `/admin/settings` and re-paste all secrets from `.env.production` (or
   from your password manager / secrets vault). This re-encrypts them under the
   new key ring.

3. Confirm each section saves successfully.

4. After re-entry, treat this as a secret-rotation event: follow the rotation
   checklist in §9 for any credential that may have been exposed.

**Prevention:** back up the Data Protection key ring directory alongside the
database. The key ring lives in the Docker volume at
`oetwebsite_oet_learner_storage:/app/dataprotection/`. Include it in any volume
snapshot or off-site backup plan.

---

## 11. Fallback semantics — `.env.production` as baseline

The env file is **never** fully superseded. Effective-value resolution always
follows the rule in §4.3:

```
DB field non-null  →  DB value (decrypted) wins
DB field null      →  env-var value wins
DB row absent      →  env-var value wins
```

This means:

- A freshly deployed instance with no DB row configured works immediately with
  env-var values only.
- Clearing a field in the UI (setting it back to blank) reverts that field to
  the env-var baseline.
- `.env.production` remains the mandatory bootstrap file for DB connection string
  and JWT signing keys, which are never exposed in the Runtime Settings UI (they
  require a restart to rotate anyway).

See [DEPLOYMENT.md](../DEPLOYMENT.md) for the minimum set of env keys that must
always be present in `.env.production`.

---

## 12. Related documents

| Document | Relevance |
|---|---|
| [`DEPLOYMENT.md`](../DEPLOYMENT.md) | First-deploy checklist; env bootstrap minimum |
| [`docs/BILLING.md`](BILLING.md) | Stripe integration architecture and billing lifecycle |
| [`docs/AI-USAGE-POLICY.md`](AI-USAGE-POLICY.md) | AI provider config (not covered here — use `/admin/ai-providers`) |
| [`docs/SCORING.md`](SCORING.md) | OET scoring contract (unrelated to settings, referenced for completeness) |

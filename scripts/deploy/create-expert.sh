#!/usr/bin/env bash
# Create a new expert reviewer account on production.
#
# The expert recovers access via /v1/auth/forgot-password which sends an OTP
# to the new email. Once the OTP + new password are set via the UI, this
# account becomes fully usable.
#
# Mirrors create-admin.sh — every step is idempotent (ON CONFLICT DO NOTHING
# pattern simulated by WHERE-NOT-EXISTS). Safe to re-run.
#
# Required env:
#   NEW_EMAIL                — recovery email for the new expert
#
# Optional env (all have sensible defaults):
#   EXPERT_DISPLAY_NAME      — human-readable name shown in expert UI
#   EXPERT_TIMEZONE          — IANA tz (default: UTC)
#   EXPERT_SPECIALTIES       — comma-separated list (default: nursing)
#   OPERATOR_CONTEXT         — free-text audit reason
#   VPS_APP_DIR              — application root (default: /opt/oetwebapp)
#
# Usage:
#   NEW_EMAIL=reviewer@example.com \
#   EXPERT_DISPLAY_NAME="Dr Example Reviewer" \
#   EXPERT_TIMEZONE="Australia/Sydney" \
#   EXPERT_SPECIALTIES="nursing,medicine" \
#   bash scripts/deploy/create-expert.sh

set -euo pipefail
APP_DIR="${VPS_APP_DIR:-/opt/oetwebapp}"
cd "$APP_DIR"

# shellcheck disable=SC2046
export $(grep -E '^POSTGRES_(USER|DB|PASSWORD)=' .env.production | xargs)

: "${NEW_EMAIL:?Set NEW_EMAIL to the expert recovery email before running}"
EXPERT_DISPLAY_NAME="${EXPERT_DISPLAY_NAME:-Expert Reviewer}"
EXPERT_TIMEZONE="${EXPERT_TIMEZONE:-UTC}"
EXPERT_SPECIALTIES="${EXPERT_SPECIALTIES:-nursing}"
OPERATOR_CONTEXT="${OPERATOR_CONTEXT:-manual expert provisioning approved by deploy owner}"

NEW_EMAIL_NORMALIZED="$(echo "$NEW_EMAIL" | tr '[:upper:]' '[:lower:]')"
if ! printf '%s' "$NEW_EMAIL_NORMALIZED" | grep -Eq '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'; then
    echo "NEW_EMAIL must be a simple email address" >&2
    exit 1
fi
if [ "${#NEW_EMAIL_NORMALIZED}" -gt 320 ]; then
    echo "NEW_EMAIL is too long" >&2
    exit 1
fi
if [ "${#EXPERT_DISPLAY_NAME}" -gt 128 ]; then
    echo "EXPERT_DISPLAY_NAME exceeds 128 chars" >&2
    exit 1
fi
if [ "${#EXPERT_TIMEZONE}" -gt 64 ]; then
    echo "EXPERT_TIMEZONE exceeds 64 chars" >&2
    exit 1
fi
if [ "${#OPERATOR_CONTEXT}" -gt 500 ]; then
    echo "OPERATOR_CONTEXT is too long" >&2
    exit 1
fi

# Build the SpecialtiesJson payload from a CSV input.
SPECIALTIES_JSON='['
first=1
IFS=',' read -ra _SPEC_ARR <<<"$EXPERT_SPECIALTIES"
for spec in "${_SPEC_ARR[@]}"; do
    trimmed="$(echo "$spec" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g' | tr '[:upper:]' '[:lower:]')"
    [ -z "$trimmed" ] && continue
    if [ "$first" -eq 1 ]; then first=0; else SPECIALTIES_JSON+=','; fi
    SPECIALTIES_JSON+="\"$trimmed\""
done
SPECIALTIES_JSON+=']'

NEW_AUTH_ID="auth_expert_$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
NEW_EXPERT_ID="expert_$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n')"
NOW_UTC="$(date -u +'%Y-%m-%d %H:%M:%S.%6N+00')"

echo "=== CREATING NEW EXPERT ==="
echo "Auth Id:         $NEW_AUTH_ID"
echo "Expert Id:       $NEW_EXPERT_ID"
echo "Email:           $NEW_EMAIL"
echo "NormalizedEmail: $NEW_EMAIL_NORMALIZED"
echo "Display name:    $EXPERT_DISPLAY_NAME"
echo "Timezone:        $EXPERT_TIMEZONE"
echo "Specialties:     $SPECIALTIES_JSON"
echo ""

# Sentinel PasswordHash that cannot validate any password. Operator activates
# the account by hitting /v1/auth/forgot-password to set a real password.
SENTINEL_HASH='!_DISABLED_PENDING_RESET_'"$(head -c 32 /dev/urandom | base64 | tr -d '\n=')"

docker exec -i oet-postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -v new_auth_id="$NEW_AUTH_ID" \
    -v new_expert_id="$NEW_EXPERT_ID" \
    -v new_email="$NEW_EMAIL" \
    -v new_email_normalized="$NEW_EMAIL_NORMALIZED" \
    -v sentinel_hash="$SENTINEL_HASH" \
    -v display_name="$EXPERT_DISPLAY_NAME" \
    -v timezone="$EXPERT_TIMEZONE" \
    -v specialties_json="$SPECIALTIES_JSON" \
    -v now_utc="$NOW_UTC" \
    -v operator_context="$OPERATOR_CONTEXT" <<'SQL'
BEGIN;

-- 1. Create the auth account row only if one doesn't already exist for this email.
INSERT INTO "ApplicationUserAccounts"
    ("Id", "Email", "NormalizedEmail", "PasswordHash", "Role",
     "EmailVerifiedAt", "CreatedAt", "UpdatedAt")
SELECT
    :'new_auth_id',
    :'new_email',
    :'new_email_normalized',
    :'sentinel_hash',
    'expert',
    :'now_utc',
    :'now_utc',
    :'now_utc'
WHERE NOT EXISTS (
    SELECT 1 FROM "ApplicationUserAccounts"
     WHERE "NormalizedEmail" = :'new_email_normalized'
);

-- 2. Create the matching ExpertUser row, linked to the auth account we just
--    inserted (or the existing one if this email already had an account).
WITH linked AS (
    SELECT "Id" AS auth_id FROM "ApplicationUserAccounts"
     WHERE "NormalizedEmail" = :'new_email_normalized'
     LIMIT 1
)
INSERT INTO "ExpertUsers"
    ("Id", "AuthAccountId", "Role", "DisplayName", "Email",
     "SpecialtiesJson", "Timezone", "IsActive", "CreatedAt")
SELECT
    :'new_expert_id',
    linked.auth_id,
    'expert',
    :'display_name',
    :'new_email',
    :'specialties_json',
    :'timezone',
    TRUE,
    :'now_utc'
FROM linked
WHERE NOT EXISTS (
    SELECT 1 FROM "ExpertUsers" WHERE "AuthAccountId" = linked.auth_id
);

-- 3. Audit-trail the creation.
INSERT INTO "AuditEvents"
    ("Id", "OccurredAt", "ActorId", "ActorName",
     "Action", "ResourceType", "ResourceId", "Details")
VALUES
    (replace(gen_random_uuid()::text, '-', ''),
     NOW(),
     'root@vps-manual',
     'Manual expert provisioning script',
     'ExpertAccountCreated',
     'ExpertUser',
    :'new_expert_id',
    'Created via DB script. Password pending via /v1/auth/forgot-password OTP flow. Context: ' || :'operator_context');

COMMIT;

-- 4. Verify the new account exists + report state.
\echo ''
\echo '=== NEW EXPERT AUTH STATE ==='
SELECT "Id", "Email", "Role", "EmailVerifiedAt", "AuthenticatorEnabledAt", "CreatedAt"
  FROM "ApplicationUserAccounts"
 WHERE "NormalizedEmail" = :'new_email_normalized';

\echo ''
\echo '=== NEW EXPERT PROFILE STATE ==='
SELECT "Id", "AuthAccountId", "Email", "DisplayName", "Role",
       "Timezone", "SpecialtiesJson", "IsActive", "CreatedAt"
  FROM "ExpertUsers"
 WHERE "Email" = :'new_email';

\echo ''
\echo '=== ALL ACTIVE EXPERTS ==='
SELECT e."Id", e."Email", e."DisplayName", e."Role",
       e."IsActive", e."CreatedAt", a."LastLoginAt"
  FROM "ExpertUsers" e
  LEFT JOIN "ApplicationUserAccounts" a ON a."Id" = e."AuthAccountId"
 WHERE e."IsActive" = TRUE
 ORDER BY e."CreatedAt";
SQL

echo ""
echo "=== NEXT STEPS ==="
echo "1. Tell the expert their email: $NEW_EMAIL"
echo "2. They visit https://app.oetwithdrhesham.co.uk/forgot-password"
echo "3. They enter the email + receive an OTP (from Brevo)"
echo "4. They set a password and can then log in as an expert reviewer"

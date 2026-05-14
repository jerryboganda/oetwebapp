#!/usr/bin/env bash
set -euo pipefail

EVIDENCE_DIR="${EVIDENCE_DIR:-release-evidence}"
EVIDENCE_ENV="${EVIDENCE_ENV:-local}"
EVIDENCE_SIGNER_FINGERPRINT="${EVIDENCE_SIGNER_FINGERPRINT:-}"
EXPECTED_GIT_SHA="${EXPECTED_GIT_SHA:-}"
if [ -n "$EVIDENCE_SIGNER_FINGERPRINT" ]; then
  EVIDENCE_SIGNER_FINGERPRINT=$(printf '%s' "$EVIDENCE_SIGNER_FINGERPRINT" | tr '[:lower:]' '[:upper:]')
fi

if [ ! -d "$EVIDENCE_DIR" ]; then
  echo "Evidence directory not found: $EVIDENCE_DIR" >&2
  exit 1
fi

required=(
  "release-metadata.env"
  "tool-versions.txt"
  "sbom.json"
  "sca.json"
  "checksums.sha256"
)

payload_required=(
  "release-metadata.env"
  "tool-versions.txt"
  "sbom.json"
  "sca.json"
)

missing=0
for file in "${required[@]}"; do
  if [ ! -s "$EVIDENCE_DIR/$file" ]; then
    echo "Missing required evidence file: $EVIDENCE_DIR/$file" >&2
    missing=1
  else
    echo "Found $EVIDENCE_DIR/$file"
  fi
done

if [ "$missing" -ne 0 ]; then
  exit 1
fi

manifest="$EVIDENCE_DIR/checksums.sha256"
require_manifest_entry() {
  local file="$1"
  entry_count=$(awk -v expected="$file" '{ path = $2; sub(/^\*/, "", path); sub(/^\.\//, "", path); if (path == expected) count++ } END { print count + 0 }' "$manifest")
  if [ "$entry_count" -ne 1 ]; then
    echo "Checksum manifest must contain exactly one entry for $file; found $entry_count" >&2
    exit 1
  fi
}

verify_detached_signature() {
  local signature="$1"
  local data_file="$2"
  local verify_output
  local validsig_fingerprint
  if ! verify_output=$(gpg --status-fd=1 --verify "$signature" "$data_file" 2>&1); then
    printf '%s\n' "$verify_output"
    echo "GPG signature verification failed for $signature." >&2
    exit 1
  fi
  printf '%s\n' "$verify_output"
  validsig_fingerprint=$(printf '%s\n' "$verify_output" | awk '$2 == "VALIDSIG" { print $3; exit }' | tr '[:lower:]' '[:upper:]')
  if [ -n "$EVIDENCE_SIGNER_FINGERPRINT" ] && [ "$validsig_fingerprint" != "$EVIDENCE_SIGNER_FINGERPRINT" ]; then
    echo "GPG signature did not match expected signer fingerprint for $signature." >&2
    exit 1
  fi
}

for file in "${payload_required[@]}"; do
  require_manifest_entry "$file"
done

sca_exit_code=$(awk -F= '$1 == "sca_exit_code" { print $2 }' "$EVIDENCE_DIR/release-metadata.env" | tail -n 1)
sca_exit_code=$(printf '%s' "$sca_exit_code" | awk '{$1=$1; print}')
if [ -z "$sca_exit_code" ]; then
  echo "release-metadata.env must include sca_exit_code." >&2
  exit 1
fi
case "$sca_exit_code" in
  *[!0-9]*)
    echo "release-metadata.env sca_exit_code must be numeric; found: $sca_exit_code" >&2
    exit 1
    ;;
esac

if [ -n "$EXPECTED_GIT_SHA" ]; then
  case "$EXPECTED_GIT_SHA" in
    *[!0-9A-Fa-f]*)
      echo "EXPECTED_GIT_SHA must be hexadecimal; found: $EXPECTED_GIT_SHA" >&2
      exit 1
      ;;
  esac
  expected_git_sha_length=${#EXPECTED_GIT_SHA}
  if [ "$expected_git_sha_length" -ne 40 ]; then
    echo "EXPECTED_GIT_SHA must be a full 40-character SHA; found length $expected_git_sha_length" >&2
    exit 1
  fi
  evidence_git_sha=$(awk -F= '$1 == "git_sha" { print $2 }' "$EVIDENCE_DIR/release-metadata.env" | tail -n 1)
  evidence_git_sha=$(printf '%s' "$evidence_git_sha" | awk '{$1=$1; print}')
  if [ -z "$evidence_git_sha" ]; then
    echo "release-metadata.env must include git_sha when EXPECTED_GIT_SHA is set." >&2
    exit 1
  fi
  if [ "$evidence_git_sha" != "$EXPECTED_GIT_SHA" ]; then
    echo "Evidence git_sha ($evidence_git_sha) does not match deployed HEAD ($EXPECTED_GIT_SHA)." >&2
    exit 1
  fi
fi

if command -v jq >/dev/null 2>&1; then
  jq empty "$EVIDENCE_DIR/sbom.json" "$EVIDENCE_DIR/sca.json"
else
  echo "jq is not available; skipping optional SBOM/SCA JSON parse validation."
fi

if [ "$sca_exit_code" != "0" ]; then
  accepted_risk="$EVIDENCE_DIR/accepted-risk.md"
  if [ ! -s "$accepted_risk" ]; then
    echo "SCA scan exited with $sca_exit_code; attach accepted-risk.md or resolve findings before verification passes." >&2
    exit 1
  fi

  require_manifest_entry "accepted-risk.md"
  accepted_risk_owner=$(awk 'BEGIN { IGNORECASE = 1 } /^owner[[:space:]]*:/ { sub(/^[^:]*:[[:space:]]*/, ""); print; exit }' "$accepted_risk")
  accepted_risk_expires=$(awk 'BEGIN { IGNORECASE = 1 } /^expires(_at)?[[:space:]]*:/ { sub(/^[^:]*:[[:space:]]*/, ""); print; exit }' "$accepted_risk")

  accepted_risk_owner=$(printf '%s' "$accepted_risk_owner" | awk '{$1=$1; print}')
  accepted_risk_expires=$(printf '%s' "$accepted_risk_expires" | awk '{$1=$1; print}')

  if [ -z "$accepted_risk_owner" ]; then
    echo "accepted-risk.md must include a non-empty Owner: field." >&2
    exit 1
  fi
  if [ -z "$accepted_risk_expires" ]; then
    echo "accepted-risk.md must include a non-empty Expires: or Expires_At: field." >&2
    exit 1
  fi

  if ! accepted_risk_expiry_epoch=$(date -u -d "$accepted_risk_expires" +%s 2>/dev/null); then
    echo "accepted-risk.md expiry is not parseable as a date: $accepted_risk_expires" >&2
    exit 1
  fi
  current_date_epoch=$(date -u -d "$(date -u +%Y-%m-%d)" +%s)
  if [ "$accepted_risk_expiry_epoch" -lt "$current_date_epoch" ]; then
    echo "accepted-risk.md expiry is in the past: $accepted_risk_expires" >&2
    exit 1
  fi
fi

if [ "$EVIDENCE_ENV" = "production" ]; then
  if [ -z "$EVIDENCE_SIGNER_FINGERPRINT" ]; then
    echo "Production evidence requires EVIDENCE_SIGNER_FINGERPRINT." >&2
    exit 1
  fi
  case "$EVIDENCE_SIGNER_FINGERPRINT" in
    *[!0-9A-Fa-f]*)
      echo "Production evidence signer fingerprint must be hexadecimal." >&2
      exit 1
      ;;
  esac
  signer_fingerprint_length=${#EVIDENCE_SIGNER_FINGERPRINT}
  if [ "$signer_fingerprint_length" -ne 40 ] && [ "$signer_fingerprint_length" -ne 64 ]; then
    echo "Production evidence signer fingerprint must be a full 40- or 64-character fingerprint." >&2
    exit 1
  fi
  if ! command -v gpg >/dev/null 2>&1; then
    echo "gpg is required for production evidence verification." >&2
    exit 1
  fi
  if [ ! -s "$manifest.asc" ]; then
    echo "Production evidence requires a detached signature for checksums.sha256." >&2
    exit 1
  fi
  verify_detached_signature "$manifest.asc" "$manifest"
fi

(
  cd "$EVIDENCE_DIR"
  sha256sum --check checksums.sha256
)

signature_count=$(find "$EVIDENCE_DIR" -maxdepth 1 -name '*.asc' | wc -l)
if command -v gpg >/dev/null 2>&1; then
  if [ "$signature_count" -gt 0 ]; then
    for signature in "$EVIDENCE_DIR"/*.asc; do
      data_file="${signature%.asc}"
      verify_detached_signature "$signature" "$data_file"
    done
  else
    echo "No GPG signatures found; unsigned evidence is allowed for local/staging runs."
  fi
elif [ "$EVIDENCE_ENV" = "production" ]; then
  echo "gpg is required for production evidence verification." >&2
  exit 1
else
  echo "gpg is not available; skipping optional signature verification."
fi

echo "Evidence bundle verified: $EVIDENCE_DIR"
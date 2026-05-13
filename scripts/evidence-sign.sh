#!/usr/bin/env bash
set -euo pipefail

EVIDENCE_DIR="${EVIDENCE_DIR:-release-evidence}"
EVIDENCE_SIGNER_FINGERPRINT="${EVIDENCE_SIGNER_FINGERPRINT:-}"
EVIDENCE_GPG_PRIVATE_KEY="${EVIDENCE_GPG_PRIVATE_KEY:-}"
EVIDENCE_GPG_PASSPHRASE="${EVIDENCE_GPG_PASSPHRASE:-}"

if [ -z "$EVIDENCE_SIGNER_FINGERPRINT" ]; then
  echo "EVIDENCE_SIGNER_FINGERPRINT is required to sign release evidence." >&2
  exit 1
fi

EVIDENCE_SIGNER_FINGERPRINT=$(printf '%s' "$EVIDENCE_SIGNER_FINGERPRINT" | tr '[:lower:]' '[:upper:]')
case "$EVIDENCE_SIGNER_FINGERPRINT" in
  *[!0-9A-Fa-f]*)
    echo "EVIDENCE_SIGNER_FINGERPRINT must be hexadecimal." >&2
    exit 1
    ;;
esac
fingerprint_length=${#EVIDENCE_SIGNER_FINGERPRINT}
if [ "$fingerprint_length" -ne 40 ] && [ "$fingerprint_length" -ne 64 ]; then
  echo "EVIDENCE_SIGNER_FINGERPRINT must be a full 40- or 64-character fingerprint." >&2
  exit 1
fi

if [ ! -s "$EVIDENCE_DIR/checksums.sha256" ]; then
  echo "Missing checksum manifest: $EVIDENCE_DIR/checksums.sha256" >&2
  exit 1
fi

if ! command -v gpg >/dev/null 2>&1; then
  echo "gpg is required to sign release evidence." >&2
  exit 1
fi

cleanup_gnupg=""
if [ -n "$EVIDENCE_GPG_PRIVATE_KEY" ]; then
  cleanup_gnupg="$(mktemp -d)"
  trap 'if [ -n "$cleanup_gnupg" ]; then rm -rf "$cleanup_gnupg"; fi' EXIT
  export GNUPGHOME="$cleanup_gnupg"
  chmod 700 "$GNUPGHOME"
  printf '%s' "$EVIDENCE_GPG_PRIVATE_KEY" | gpg --batch --import
fi

sign_args=(--batch --yes --armor --detach-sign --local-user "$EVIDENCE_SIGNER_FINGERPRINT" --output "$EVIDENCE_DIR/checksums.sha256.asc" "$EVIDENCE_DIR/checksums.sha256")
if [ -n "$EVIDENCE_GPG_PASSPHRASE" ]; then
  printf '%s' "$EVIDENCE_GPG_PASSPHRASE" | gpg --pinentry-mode loopback --passphrase-fd 0 "${sign_args[@]}"
else
  gpg "${sign_args[@]}"
fi

if [ -n "$cleanup_gnupg" ]; then
  rm -rf "$cleanup_gnupg"
fi

echo "Signed release evidence manifest: $EVIDENCE_DIR/checksums.sha256.asc"
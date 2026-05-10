#!/usr/bin/env bash
set -euo pipefail

OUT_DIR="${EVIDENCE_DIR:-release-evidence}"
SYFT_IMAGE="${SYFT_IMAGE:-anchore/syft:v1.20.0}"
GRYPE_IMAGE="${GRYPE_IMAGE:-anchore/grype:v0.85.0}"
mkdir -p "$OUT_DIR"

{
  echo "git_sha=$(git rev-parse HEAD)"
  echo "git_ref=$(git rev-parse --abbrev-ref HEAD)"
  echo "collected_at_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > "$OUT_DIR/release-metadata.env"

{
  echo "syft_image=$SYFT_IMAGE"
  if command -v syft >/dev/null 2>&1; then
    printf 'syft_local_version='
    (syft version 2>/dev/null || syft --version 2>/dev/null || true) | tr '\n' ' '
    printf '\n'
  else
    echo "syft_local_version=missing"
  fi
  echo "grype_image=$GRYPE_IMAGE"
  if command -v grype >/dev/null 2>&1; then
    printf 'grype_local_version='
    (grype version 2>/dev/null || grype --version 2>/dev/null || true) | tr '\n' ' '
    printf '\n'
  else
    echo "grype_local_version=missing"
  fi
} > "$OUT_DIR/tool-versions.txt"

SBOM_OUTPUT="$OUT_DIR/sbom.json" bash scripts/sbom-generate.sh
sca_exit_code=0
SBOM_OUTPUT="$OUT_DIR/sbom.json" SCA_OUTPUT="$OUT_DIR/sca.json" bash scripts/sca-scan.sh || sca_exit_code=$?
echo "sca_exit_code=$sca_exit_code" >> "$OUT_DIR/release-metadata.env"

if [ ! -s "$OUT_DIR/sca.json" ]; then
  echo "SCA report was not produced; cannot build a verifiable evidence bundle." >&2
  exit 1
fi

(
  cd "$OUT_DIR"
  find . -maxdepth 1 -type f ! -name 'checksums.sha256' -print0 | sort -z | xargs -0 sha256sum > checksums.sha256
)

echo "Evidence bundle written to $OUT_DIR"

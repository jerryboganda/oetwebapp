#!/usr/bin/env bash
set -euo pipefail

OUT_DIR="${EVIDENCE_DIR:-release-evidence}"
EVIDENCE_ENV="${EVIDENCE_ENV:-local}"
ACCESSIBILITY_SIGNOFF_PATH="${ACCESSIBILITY_SIGNOFF_PATH:-}"
ACCESSIBILITY_SIGNOFF_CONTENT="${ACCESSIBILITY_SIGNOFF_CONTENT:-}"
ACCESSIBILITY_SIGNOFF_BASE64="${ACCESSIBILITY_SIGNOFF_BASE64:-}"
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

accessibility_signoff_sources=0
for source in "$ACCESSIBILITY_SIGNOFF_PATH" "$ACCESSIBILITY_SIGNOFF_CONTENT" "$ACCESSIBILITY_SIGNOFF_BASE64"; do
  if [ -n "$source" ]; then
    accessibility_signoff_sources=$((accessibility_signoff_sources + 1))
  fi
done

if [ "$accessibility_signoff_sources" -gt 1 ]; then
  echo "Provide at most one accessibility signoff source: ACCESSIBILITY_SIGNOFF_PATH, ACCESSIBILITY_SIGNOFF_CONTENT, or ACCESSIBILITY_SIGNOFF_BASE64." >&2
  exit 1
fi

if [ "$EVIDENCE_ENV" = "production" ] && [ "$accessibility_signoff_sources" -ne 1 ]; then
  echo "Production evidence requires exactly one accessibility signoff source." >&2
  exit 1
fi

if [ -n "$ACCESSIBILITY_SIGNOFF_PATH" ]; then
  if [ ! -s "$ACCESSIBILITY_SIGNOFF_PATH" ]; then
    echo "ACCESSIBILITY_SIGNOFF_PATH does not point to a readable signoff file: $ACCESSIBILITY_SIGNOFF_PATH" >&2
    exit 1
  fi
  cp "$ACCESSIBILITY_SIGNOFF_PATH" "$OUT_DIR/accessibility-signoff.env"
fi

if [ -n "$ACCESSIBILITY_SIGNOFF_CONTENT" ]; then
  printf '%s\n' "$ACCESSIBILITY_SIGNOFF_CONTENT" > "$OUT_DIR/accessibility-signoff.env"
fi

if [ -n "$ACCESSIBILITY_SIGNOFF_BASE64" ]; then
  if ! printf '%s' "$ACCESSIBILITY_SIGNOFF_BASE64" | base64 -d > "$OUT_DIR/accessibility-signoff.env"; then
    echo "ACCESSIBILITY_SIGNOFF_BASE64 could not be decoded." >&2
    exit 1
  fi
fi

if [ "$EVIDENCE_ENV" = "production" ] && [ ! -s "$OUT_DIR/accessibility-signoff.env" ]; then
  echo "Production evidence requires accessibility-signoff.env with automated axe and manual assistive-technology signoff." >&2
  exit 1
fi

if [ -s "$OUT_DIR/accessibility-signoff.env" ]; then
  node scripts/qa/validate-accessibility-signoff.mjs "$OUT_DIR/accessibility-signoff.env"
fi

(
  cd "$OUT_DIR"
  find . -maxdepth 1 -type f ! -name 'checksums.sha256' -print0 | sort -z | xargs -0 sha256sum > checksums.sha256
)

echo "Evidence bundle written to $OUT_DIR"

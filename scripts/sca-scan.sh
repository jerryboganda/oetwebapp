#!/usr/bin/env bash
set -euo pipefail

SBOM_PATH="${SBOM_OUTPUT:-release-evidence/sbom.json}"
OUTPUT_PATH="${SCA_OUTPUT:-release-evidence/sca.json}"
FAIL_ON="${SCA_FAIL_ON:-high}"
GRYPE_IMAGE="${GRYPE_IMAGE:-anchore/grype:v0.85.0}"

if [ ! -s "$SBOM_PATH" ]; then
  echo "SBOM file not found: $SBOM_PATH" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUTPUT_PATH")"

if command -v grype >/dev/null 2>&1; then
  grype "sbom:$SBOM_PATH" -o json --fail-on "$FAIL_ON" > "$OUTPUT_PATH"
else
  docker run --rm -v "$PWD:/workspace:ro" "$GRYPE_IMAGE" "sbom:/workspace/$SBOM_PATH" -o json --fail-on "$FAIL_ON" > "$OUTPUT_PATH"
fi

echo "SCA report written to $OUTPUT_PATH"

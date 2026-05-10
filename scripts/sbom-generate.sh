#!/usr/bin/env bash
set -euo pipefail

OUTPUT_PATH="${SBOM_OUTPUT:-release-evidence/sbom.json}"
SYFT_IMAGE="${SYFT_IMAGE:-anchore/syft:v1.20.0}"
mkdir -p "$(dirname "$OUTPUT_PATH")"

if command -v syft >/dev/null 2>&1; then
  syft dir:. -o json > "$OUTPUT_PATH"
else
  docker run --rm -v "$PWD:/workspace:ro" "$SYFT_IMAGE" dir:/workspace -o json > "$OUTPUT_PATH"
fi

echo "SBOM written to $OUTPUT_PATH"

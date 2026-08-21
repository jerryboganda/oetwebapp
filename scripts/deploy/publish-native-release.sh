#!/usr/bin/env bash
# Atomically publish native installers into the VPS release catalog.
# Invoked by GitHub Actions over SSH after artifacts have been staged.
set -euo pipefail

CHANNEL="${CHANNEL:?Set CHANNEL to desktop, android, or ios}"
VERSION="${VERSION:?Set VERSION to the semver being published}"
SRC_DIR="${SRC_DIR:?Set SRC_DIR to the staged artifact directory}"
RELEASES_ROOT="${RELEASES_ROOT:-/var/opt/oet-learner/releases}"
KEEP="${KEEP:-1}"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
  echo "VERSION '$VERSION' is not valid semver" >&2
  exit 1
fi
if [[ ! -d "$SRC_DIR" ]]; then
  echo "SRC_DIR does not exist: $SRC_DIR" >&2
  exit 1
fi

case "$CHANNEL" in
  desktop) DEST_PARENT="$RELEASES_ROOT/desktop" ;;
  android) DEST_PARENT="$RELEASES_ROOT/mobile/android" ;;
  ios) DEST_PARENT="$RELEASES_ROOT/mobile/ios" ;;
  *) echo "Unsupported CHANNEL: $CHANNEL" >&2; exit 1 ;;
esac

umask 022
mkdir -p "$DEST_PARENT"
DEST="$DEST_PARENT/$VERSION"
STAGE="${DEST}.staging.$$"
rm -rf "$STAGE"
mkdir -p "$STAGE"

# Copy regular files only; normalize spaces in filenames.
found=0
while IFS= read -r -d '' file; do
  name="$(basename "$file" | sed -E 's/[[:space:]]+/./g; s/\.+/./g')"
  cp -f "$file" "$STAGE/$name"
  found=1
done < <(find "$SRC_DIR" -type f -print0)
if [[ "$found" -eq 0 ]]; then
  echo "No files staged in $SRC_DIR" >&2
  exit 1
fi

if command -v sha256sum >/dev/null; then
  (cd "$STAGE" && sha256sum * > SHA256SUMS.txt)
else
  (cd "$STAGE" && shasum -a 256 * > SHA256SUMS.txt)
fi

# current.json must already be in the staging dir for this version.
if [[ ! -s "$STAGE/current.json" && -s "$STAGE/latest.json" ]]; then
  mv "$STAGE/latest.json" "$STAGE/current.json"
fi
if [[ ! -s "$STAGE/current.json" ]]; then
  echo "Missing current.json / latest.json in staged release" >&2
  exit 1
fi

python3 - "$STAGE/current.json" "$CHANNEL" "$VERSION" <<'PY'
import json, sys
path, channel, version = sys.argv[1:4]
data = json.load(open(path, encoding='utf-8'))
if channel == 'desktop':
    if data.get('version') != version:
        raise SystemExit(f"desktop feed version {data.get('version')!r} != {version!r}")
    win = (data.get('platforms') or {}).get('windows-x86_64') or {}
    if not str(win.get('url', '')).startswith('https://app.oetwithdrhesham.co.uk/releases/'):
        raise SystemExit('desktop feed is not pointing at the production release host')
else:
    if data.get('platform') != channel or data.get('version') != version:
        raise SystemExit('mobile manifest platform/version mismatch')
    if not str(data.get('downloadUrl', '')).startswith('https://app.oetwithdrhesham.co.uk/releases/'):
        raise SystemExit('mobile manifest is not pointing at the production release host')
print('manifest_ok')
PY

rm -rf "$DEST"
mv "$STAGE" "$DEST"
cp -f "$DEST/current.json" "$DEST_PARENT/current.json.tmp"
mv -f "$DEST_PARENT/current.json.tmp" "$DEST_PARENT/current.json"

# Keep only the newest KEEP version directory (default: the active release).
# Staging leftovers and older versions are deleted so the VPS does not fill up.
find "$DEST_PARENT" -mindepth 1 -maxdepth 1 -type d -name '*.staging.*' -exec rm -rf {} +
mapfile -t versions < <(find "$DEST_PARENT" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | sort -V)
if (( ${#versions[@]} > KEEP )); then
  drop=$(( ${#versions[@]} - KEEP ))
  for old in "${versions[@]:0:$drop}"; do
    if [[ "$old" != "$VERSION" ]]; then
      echo "Removing previous $CHANNEL release $old"
      rm -rf "$DEST_PARENT/$old"
    fi
  done
fi
# If more than KEEP remain because the active version sorted earlier, keep only VERSION.
if (( KEEP == 1 )); then
  find "$DEST_PARENT" -mindepth 1 -maxdepth 1 -type d ! -name "$VERSION" -exec rm -rf {} +
fi

echo "Published $CHANNEL $VERSION to $DEST"
echo "Active manifest: $DEST_PARENT/current.json"

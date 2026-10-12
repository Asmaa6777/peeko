#!/usr/bin/env bash
# Regenerates Resources/AppIcon.icns, docs/assets/icon.png and docs/assets/icon-1024.png from scripts/make-icon.swift.
set -euo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

swift scripts/make-icon.swift "$WORK/icon-1024.png"

ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
for px in 16 32 128 256 512; do
    sips -z "$px" "$px" "$WORK/icon-1024.png" --out "$ICONSET/icon_${px}x${px}.png" >/dev/null
    double=$((px * 2))
    sips -z "$double" "$double" "$WORK/icon-1024.png" --out "$ICONSET/icon_${px}x${px}@2x.png" >/dev/null
done

iconutil -c icns "$ICONSET" -o Resources/AppIcon.icns
mkdir -p docs/assets
sips -z 256 256 "$WORK/icon-1024.png" --out docs/assets/icon.png >/dev/null
cp "$WORK/icon-1024.png" docs/assets/icon-1024.png

echo "Wrote Resources/AppIcon.icns, docs/assets/icon.png and docs/assets/icon-1024.png"

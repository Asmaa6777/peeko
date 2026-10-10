#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

BIN_DIR="$(swift build -c release --arch arm64 --show-bin-path)"
APP="build/Peeko.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp "$BIN_DIR/PeekoApp" "$APP/Contents/MacOS/Peeko"
cp Resources/Info.plist "$APP/Contents/Info.plist"

echo "Bundled $APP"

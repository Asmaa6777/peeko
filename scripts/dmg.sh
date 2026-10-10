#!/usr/bin/env bash
# Packs build/Peeko.app into build/Peeko-<version>.dmg with an Applications shortcut, plus a SHA-256 file.
set -euo pipefail
cd "$(dirname "$0")/.."

APP="build/Peeko.app"
VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")"
DMG="build/Peeko-$VERSION.dmg"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

rm -f "$DMG"
hdiutil create -volname "Peeko $VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -ov "$DMG" >/dev/null

(cd build && shasum -a 256 "$(basename "$DMG")" > "$(basename "$DMG").sha256")
echo "Wrote $DMG"
cat "$DMG.sha256"

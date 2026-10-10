#!/usr/bin/env bash
# Installs the latest Peeko release into /Applications.
#   curl -fsSL https://raw.githubusercontent.com/STACK-42/peeko/main/install.sh | bash
# Pin a version with PEEKO_VERSION=0.1.0.
set -euo pipefail

REPO="STACK-42/peeko"
DEST="/Applications"

say() { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

[[ "$(uname -s)" == "Darwin" ]] || fail "Peeko is a macOS app."
[[ "$(uname -m)" == "arm64" ]] || fail "Peeko needs an Apple silicon Mac."
major="$(sw_vers -productVersion | cut -d. -f1)"
(( major >= 14 )) || fail "Peeko needs macOS 14 or later (you have $(sw_vers -productVersion))."

if [[ -n "${PEEKO_VERSION:-}" ]]; then
    tag="v${PEEKO_VERSION#v}"
else
    # /releases/latest redirects to /releases/tag/vX.Y.Z, so no API call (or jq) is needed.
    latest="$(curl -fsSLI -o /dev/null -w '%{url_effective}' "https://github.com/$REPO/releases/latest")"
    tag="${latest##*/}"
    [[ "$tag" == v* ]] || fail "Couldn't find a release at github.com/$REPO/releases."
fi
version="${tag#v}"
dmg="Peeko-$version.dmg"
base="https://github.com/$REPO/releases/download/$tag"

tmp="$(mktemp -d)"
mnt="$tmp/mnt"
cleanup() {
    [[ -d "$mnt" ]] && hdiutil detach -quiet "$mnt" 2>/dev/null || true
    rm -rf "$tmp"
}
trap cleanup EXIT

say "Downloading Peeko $version"
curl -fsSL -o "$tmp/$dmg" "$base/$dmg"
curl -fsSL -o "$tmp/$dmg.sha256" "$base/$dmg.sha256"

say "Checking the download"
(cd "$tmp" && shasum -a 256 -c "$dmg.sha256" >/dev/null) || fail "Checksum didn't match. Not installing."

mkdir -p "$mnt"
hdiutil attach -nobrowse -readonly -quiet -mountpoint "$mnt" "$tmp/$dmg"

if pgrep -x Peeko >/dev/null; then
    say "Quitting the running Peeko"
    osascript -e 'quit app "Peeko"' 2>/dev/null || pkill -x Peeko || true
    sleep 1
fi

[[ -w "$DEST" ]] || fail "Can't write to $DEST. Run with an admin account, or drag Peeko there from the DMG."
say "Installing to $DEST/Peeko.app"
rm -rf "$DEST/Peeko.app"
cp -R "$mnt/Peeko.app" "$DEST/"

open "$DEST/Peeko.app"
say "Done. Look for Peeko's eyes in your menu bar."

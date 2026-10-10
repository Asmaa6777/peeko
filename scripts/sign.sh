#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

APP="build/Peeko.app"
IDENTITY="${SIGN_IDENTITY:--}"

codesign --force --deep --sign "$IDENTITY" "$APP"
codesign --verify --strict "$APP"
echo "Signed $APP (identity: $IDENTITY)"

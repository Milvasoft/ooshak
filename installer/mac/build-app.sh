#!/bin/sh
# Builds Ooshak.app and a DMG on macOS. Usage: installer/mac/build-app.sh <version> [rid]
# The app is signed ad hoc, not with a Developer ID, so Gatekeeper asks once before it opens.
set -eu

VERSION="$1"
RID="${2:-osx-arm64}"
ARCH="${RID#osx-}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/artifacts/mac-$ARCH"
APP="$OUT/Ooshak.app"

rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/src/Ooshak.Mac/Ooshak.Mac.csproj" -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -o "$APP/Contents/MacOS"
rm -f "$APP/Contents/MacOS/"*.pdb

iconutil -c icns "$ROOT/installer/mac/Ooshak.iconset" -o "$APP/Contents/Resources/Ooshak.icns"
sed "s/@VERSION@/$VERSION/g" "$ROOT/installer/mac/Info.plist" > "$APP/Contents/Info.plist"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$APP/Contents/Resources/"

codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"

STAGE="$OUT/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Ooshak" -srcfolder "$STAGE" -ov -format UDZO "$ROOT/artifacts/Ooshak-$VERSION-macos-$ARCH.dmg"
rm -rf "$STAGE"
echo "$ROOT/artifacts/Ooshak-$VERSION-macos-$ARCH.dmg"

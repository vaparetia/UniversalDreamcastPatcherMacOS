#!/bin/bash
set -e

APP_NAME="Universal Dreamcast Patcher"
BUNDLE_ID="com.vaparetia.universaldreamcastpatcher"
VERSION="1.8"
PROJECT="source_mac/UniversalDreamcastPatcher.csproj"
PUBLISH_TMP="$(mktemp -d)"
APP_DIR="${APP_NAME}.app"

echo "Building convertredumptogdi..."
CUE_TMP="$(mktemp -d)"
dotnet publish tools_source/convertredumptogdi/convertredumptogdi.csproj \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishAot=false \
  -c Release \
  -o "$CUE_TMP" \
  --nologo -v quiet
cp "$CUE_TMP/convertredumptogdi" source_mac/tools/convertredumptogdi
chmod +x source_mac/tools/convertredumptogdi
rm -rf "$CUE_TMP"

echo "Publishing..."
dotnet publish "$PROJECT" \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishAot=false \
  -c Release \
  -o "$PUBLISH_TMP" \
  --nologo -v quiet

echo "Assembling .app bundle..."
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp -r "$PUBLISH_TMP"/. "$APP_DIR/Contents/MacOS/"
rm -rf "$PUBLISH_TMP"

chmod +x "$APP_DIR/Contents/MacOS/UniversalDreamcastPatcher"
chmod +x "$APP_DIR/Contents/MacOS/tools/buildgdi"
chmod +x "$APP_DIR/Contents/MacOS/tools/convertredumptogdi"

# PkgInfo
printf 'APPL????' > "$APP_DIR/Contents/PkgInfo"

# Info.plist
cat > "$APP_DIR/Contents/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Universal Dreamcast Patcher</string>
    <key>CFBundleDisplayName</key>
    <string>Universal Dreamcast Patcher</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleExecutable</key>
    <string>UniversalDreamcastPatcher</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
PLIST

echo "Done: ${APP_DIR}"

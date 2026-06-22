#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$ROOT_DIR/apps/macos/RYNATClient"
BUILD_DIR="$ROOT_DIR/build/macos"
APP_DIR="$BUILD_DIR/RYNAT 共享网盘.app"
DMG_PATH="$BUILD_DIR/RYNAT 共享网盘.dmg"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
FRAMEWORKS_DIR="$CONTENTS_DIR/Frameworks"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
CORE_LIB="$ROOT_DIR/target/release/librynat_core.dylib"
LOGO_PNG="$ROOT_DIR/RYNATLogo.png"

osascript -e 'tell application id "com.rynat.disk.linktester" to quit' >/dev/null 2>&1 || true
sleep 1
pkill -x RYNATClient >/dev/null 2>&1 || true

cargo build -p rynat-core --release

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$FRAMEWORKS_DIR" "$RESOURCES_DIR"
cp "$CORE_LIB" "$FRAMEWORKS_DIR/librynat_core.dylib"
install_name_tool -id "@rpath/librynat_core.dylib" "$FRAMEWORKS_DIR/librynat_core.dylib"

swiftc \
  -target arm64-apple-macosx11.0 \
  "$SRC_DIR"/*.swift \
  -o "$MACOS_DIR/RYNATClient" \
  -L "$FRAMEWORKS_DIR" \
  -lrynat_core \
  -Xlinker -rpath \
  -Xlinker "@executable_path/../Frameworks" \
  -framework AVFoundation \
  -framework AppKit \
  -framework QuickLookThumbnailing

cp "$SRC_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
cp "$LOGO_PNG" "$RESOURCES_DIR/RYNATLogo.png"

ICONSET_DIR="$BUILD_DIR/AppIcon.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"
ICON_RENDERER="$BUILD_DIR/render_icon.swift"
cat > "$ICON_RENDERER" <<'SWIFT'
import AppKit

let args = CommandLine.arguments
let sourceURL = URL(fileURLWithPath: args[1])
let outputURL = URL(fileURLWithPath: args[2])
let size = CGFloat(Double(args[3])!)

guard let source = NSImage(contentsOf: sourceURL),
      let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: Int(size),
        pixelsHigh: Int(size),
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
      ) else {
    exit(1)
}

rep.size = NSSize(width: size, height: size)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSColor.clear.setFill()
NSRect(x: 0, y: 0, width: size, height: size).fill()

let tileInset = size * 0.105
let tileRect = NSRect(x: tileInset, y: tileInset, width: size - tileInset * 2, height: size - tileInset * 2)
let radius = size * 0.205
let shadow = NSShadow()
shadow.shadowOffset = NSSize(width: 0, height: -size * 0.018)
shadow.shadowBlurRadius = size * 0.055
shadow.shadowColor = NSColor.black.withAlphaComponent(0.16)
shadow.set()

let tilePath = NSBezierPath(roundedRect: tileRect, xRadius: radius, yRadius: radius)
NSColor.white.setFill()
tilePath.fill()
NSShadow().set()

NSColor(srgbRed: 0.88, green: 0.90, blue: 0.94, alpha: 1).setStroke()
tilePath.lineWidth = max(1, size * 0.006)
tilePath.stroke()

let sourceSize = source.size
let maxLogoWidth = size * 0.54
let maxLogoHeight = size * 0.62
let scale = min(maxLogoWidth / sourceSize.width, maxLogoHeight / sourceSize.height)
let drawSize = NSSize(width: sourceSize.width * scale, height: sourceSize.height * scale)
let drawRect = NSRect(
    x: (size - drawSize.width) / 2,
    y: (size - drawSize.height) / 2 + size * 0.01,
    width: drawSize.width,
    height: drawSize.height
)
source.draw(in: drawRect, from: .zero, operation: .sourceOver, fraction: 1)

NSGraphicsContext.restoreGraphicsState()

guard let data = rep.representation(using: .png, properties: [:]) else {
    exit(1)
}
try data.write(to: outputURL)
SWIFT
make_icon_png() {
  swift "$ICON_RENDERER" "$LOGO_PNG" "$2" "$1"
}
make_icon_png 16 "$ICONSET_DIR/icon_16x16.png"
make_icon_png 32 "$ICONSET_DIR/icon_16x16@2x.png"
make_icon_png 32 "$ICONSET_DIR/icon_32x32.png"
make_icon_png 64 "$ICONSET_DIR/icon_32x32@2x.png"
make_icon_png 128 "$ICONSET_DIR/icon_128x128.png"
make_icon_png 256 "$ICONSET_DIR/icon_128x128@2x.png"
make_icon_png 256 "$ICONSET_DIR/icon_256x256.png"
make_icon_png 512 "$ICONSET_DIR/icon_256x256@2x.png"
make_icon_png 512 "$ICONSET_DIR/icon_512x512.png"
make_icon_png 1024 "$ICONSET_DIR/icon_512x512@2x.png"
iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/AppIcon.icns"
rm -rf "$ICONSET_DIR"
touch "$APP_DIR" "$CONTENTS_DIR" "$CONTENTS_DIR/Info.plist" "$RESOURCES_DIR/AppIcon.icns"
APP_ICON_RSRC="$BUILD_DIR/AppBundleIcon.rsrc"
APP_ICON_SOURCE="$BUILD_DIR/app-bundle-icon.png"
APP_CUSTOM_ICON="$APP_DIR/$(printf 'Icon\r')"
rm -f "$APP_ICON_RSRC" "$APP_ICON_SOURCE" "$APP_CUSTOM_ICON"
make_icon_png 1024 "$APP_ICON_SOURCE"
sips -i "$APP_ICON_SOURCE" >/dev/null 2>&1
DeRez -only icns "$APP_ICON_SOURCE" > "$APP_ICON_RSRC"
touch "$APP_CUSTOM_ICON"
Rez -append "$APP_ICON_RSRC" -o "$APP_CUSTOM_ICON"
SetFile -a C "$APP_DIR"
SetFile -a V "$APP_CUSTOM_ICON"
rm -f "$APP_ICON_RSRC" "$APP_ICON_SOURCE"
chmod +x "$MACOS_DIR/RYNATClient"

rm -f "$DMG_PATH"
hdiutil create \
  -volname "RYNAT 共享网盘" \
  -srcfolder "$APP_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH" >/dev/null
ICON_RSRC="$BUILD_DIR/AppIcon.rsrc"
DMG_ICON_SOURCE="$BUILD_DIR/dmg-icon.png"
rm -f "$ICON_RSRC" "$DMG_ICON_SOURCE"
make_icon_png 1024 "$DMG_ICON_SOURCE"
sips -i "$DMG_ICON_SOURCE" >/dev/null 2>&1
DeRez -only icns "$DMG_ICON_SOURCE" > "$ICON_RSRC"
Rez -append "$ICON_RSRC" -o "$DMG_PATH"
SetFile -a C "$DMG_PATH"
rm -f "$ICON_RSRC" "$DMG_ICON_SOURCE" "$ICON_RENDERER"

echo "$APP_DIR"
echo "$DMG_PATH"

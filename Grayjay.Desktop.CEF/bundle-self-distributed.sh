#!/bin/bash

APP_NAME_BASE="Grayjay"
BUNDLE_ID="com.futo.grayjay.desktop"
APPLE_ID="koen@futo.org"
TEAM_ID="2W7AC6T8T5"
APP_CERT="Developer ID Application: FUTO Holdings, Inc. (2W7AC6T8T5)"
#APP_CERT="Apple Development: junk@koenj.com (UPVRSKNGC9)"
#APP_CERT="Apple Development: Koen Jeukendrup (J5K3GQAZ67)"
KEYCHAIN_PROFILE="GRAYJAY_PROFILE"

build_sign_notarize() {
    ARCH=$1
    APP_NAME="${APP_NAME_BASE}_${ARCH}.app"
    APP_NAME_SIGNED="${APP_NAME_BASE}_${ARCH}-signed.app"
    PKG_NAME="${APP_NAME_BASE}_${ARCH}.pkg"
    ZIP_NAME="${APP_NAME_BASE}_${ARCH}.zip"

    echo "Building for architecture: $ARCH"

    # Build backend
    rm -rf bin/ obj/
    dotnet publish -r $ARCH
    PUBLISH_PATH="bin/Release/net8.0/$ARCH/publish"
    mkdir -p "$PUBLISH_PATH/wwwroot"
    cp -r ../Grayjay.Desktop.Web/dist "$PUBLISH_PATH/wwwroot/web"

    echo "Creating the app bundle..."
    rm -rf "$APP_NAME"
    rm -rf "$APP_NAME_SIGNED"
    mkdir -p "$APP_NAME/Contents/MacOS"
    mkdir -p "$APP_NAME/Contents/Resources"
    cp -a Info/Info.plist "$APP_NAME/Contents/Info.plist"
    cp -a Resources/MacOS/PkgInfo "$APP_NAME/Contents"

    cp -a "$PUBLISH_PATH/Grayjay" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libe_sqlite3.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libsodium.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/ClearScriptV8.$ARCH.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/MacOS/dotcefnative" "$APP_NAME/Contents/MacOS"
    cp -a "../Grayjay.ClientServer/deps/${ARCH}/ffmpeg" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/wwwroot" "$APP_NAME/Contents/Resources/wwwroot"

    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/Frameworks" "$APP_NAME/Contents/Frameworks"
    cp -a Resources/MacOS/Keychain.framework "$APP_NAME/Contents/Frameworks/Keychain.framework"
    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/Resources/." "$APP_NAME/Contents/Resources"
    cp -a Resources/MacOS/MainMenu.xib "$APP_NAME/Contents/Resources/MainMenu.xib"
    rm -rf "$APP_NAME/Contents/Resources/English.lproj"
    cp -a Resources/MacOS/English.lproj "$APP_NAME/Contents/Resources/English.lproj"
    cp -a Resources/MacOS/grayjay.icns "$APP_NAME/Contents/Resources/shared.icns"

    cp -a Info/Info-Helper.plist "$APP_NAME/Contents/Frameworks/dotcefnative Helper.app/Contents/Info.plist"
    cp -a Info/Info-Helper-Alerts.plist "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Alerts).app/Contents/Info.plist"
    cp -a Info/Info-Helper-GPU.plist "$APP_NAME/Contents/Frameworks/dotcefnative Helper (GPU).app/Contents/Info.plist"
    cp -a Info/Info-Helper-Plugin.plist "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Plugin).app/Contents/Info.plist"
    cp -a Info/Info-Helper-Renderer.plist "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Renderer).app/Contents/Info.plist"

    bash ./sign-macos.sh "$APP_NAME"
}

# Build front-end
cd ../Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ../Grayjay.Desktop.CEF

build_sign_notarize "osx-x64"
build_sign_notarize "osx-arm64"

echo "All builds complete."
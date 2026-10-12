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
    rm -rf "$PUBLISH_PATH/wwwroot/web"
    mkdir -p "$PUBLISH_PATH/wwwroot/web"
    cp -a ../Grayjay.Desktop.Web/dist/. "$PUBLISH_PATH/wwwroot/web/"

    echo "Creating the app bundle..."
    rm -rf "$APP_NAME"
    rm -rf "$APP_NAME_SIGNED"
    mkdir -p "$APP_NAME/Contents/MacOS"
    mkdir -p "$APP_NAME/Contents/Resources"
    mkdir -p "$APP_NAME/Contents/Frameworks"
    cp -a Resources/MacOS/Info.plist "$APP_NAME/Contents/Info.plist"
    cp -a Resources/MacOS/PkgInfo "$APP_NAME/Contents"

    cp -a "$PUBLISH_PATH/Grayjay" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libe_sqlite3.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libsodium.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/ClearScriptV8.$ARCH.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "../JustCef/prebuilt/$ARCH/justcefnative.app" "$APP_NAME/Contents/Frameworks/justcefnative.app" # TODO Outdated
    cp -a Resources/MacOS/Info-Cef.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Info.plist"
    cp -a Resources/MacOS/Info-Helper.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Frameworks/justcefnative Helper.app/Contents/Info.plist"
    cp -a Resources/MacOS/Info-Helper-Alerts.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Frameworks/justcefnative Helper (Alerts).app/Contents/Info.plist"
    cp -a Resources/MacOS/Info-Helper-GPU.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Frameworks/justcefnative Helper (GPU).app/Contents/Info.plist"
    cp -a Resources/MacOS/Info-Helper-Plugin.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Frameworks/justcefnative Helper (Plugin).app/Contents/Info.plist"
    cp -a Resources/MacOS/Info-Helper-Renderer.plist "$APP_NAME/Contents/Frameworks/justcefnative.app/Contents/Frameworks/justcefnative Helper (Renderer).app/Contents/Info.plist"

    cp -a "$PUBLISH_PATH/wwwroot" "$APP_NAME/Contents/Resources/wwwroot"

    cp -a Resources/MacOS/Keychain.framework "$APP_NAME/Contents/Frameworks/Keychain.framework"
    cp -a Resources/MacOS/grayjay.icns "$APP_NAME/Contents/Resources/shared.icns"
    
    python3 ../tools/linux-cdm/bundle-macos.py --publish "$PUBLISH_PATH" --app "$APP_NAME" || { echo "Playback bundling failed for $ARCH"; exit 1; }

    bash ./sign-macos.sh "$APP_NAME"
}

# Build front-end
cd ../Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ../Grayjay.Desktop.CEF

#build_sign_notarize "osx-x64"
build_sign_notarize "osx-arm64"

echo "All builds complete."
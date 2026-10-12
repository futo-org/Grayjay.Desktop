#!/bin/bash

if [ -z "$1" ]; then
    echo "Error: Version number is required."
    echo "Usage: $0 <version>"
    exit 1
fi

VERSION=$1
APP_NAME_BASE="Grayjay"
BUNDLE_ID="com.futo.grayjay.desktop"
APPLE_ID="koen@futo.org"
TEAM_ID="2W7AC6T8T5"
APP_CERT="Developer ID Application: FUTO Holdings, Inc. (2W7AC6T8T5)"
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
    dotnet publish -r $ARCH -c Release -p:AssemblyVersion=1.$VERSION.0.0 || { echo "dotnet publish failed for $ARCH"; exit 1; }
    PUBLISH_PATH="bin/Release/net8.0/$ARCH/publish"
    rm -rf "$PUBLISH_PATH/wwwroot/web"
    mkdir -p "$PUBLISH_PATH/wwwroot/web"
    cp -a ../Grayjay.Desktop.Web/dist/. "$PUBLISH_PATH/wwwroot/web/"

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
    cp -a "$PUBLISH_PATH/cef/Contents/MacOS/justcefnative" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/cef/steam_appid.txt" "$APP_NAME/Contents/Resources"
    cp -a "../Grayjay.ClientServer/deps/${ARCH}/ffmpeg" "$APP_NAME/Contents/MacOS"
    chmod +x "$APP_NAME/Contents/MacOS/ffmpeg"
    cp -a "../Grayjay.ClientServer/deps/${ARCH}/libcurl-impersonate.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "../Grayjay.ClientServer/deps/${ARCH}/curlshim.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "../Grayjay.ClientServer/deps/${ARCH}/libsteam_api.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libfcast_sender_sdk.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/wwwroot" "$APP_NAME/Contents/Resources/wwwroot"

    cp -a "$PUBLISH_PATH/cef/Contents/Frameworks" "$APP_NAME/Contents/Frameworks"
    cp -a Resources/MacOS/Keychain.framework "$APP_NAME/Contents/Frameworks/Keychain.framework"
    cp -a "$PUBLISH_PATH/cef/Contents/Resources/." "$APP_NAME/Contents/Resources"
    cp -a Resources/MacOS/MainMenu.xib "$APP_NAME/Contents/Resources/MainMenu.xib"
    rm -rf "$APP_NAME/Contents/Resources/English.lproj"
    cp -a Resources/MacOS/English.lproj "$APP_NAME/Contents/Resources/English.lproj"
    cp -a Resources/MacOS/grayjay.icns "$APP_NAME/Contents/Resources/shared.icns"

    cp -a Info/Info-Helper.plist "$APP_NAME/Contents/Frameworks/justcefnative Helper.app/Contents/Info.plist"
    cp -a Info/Info-Helper-Alerts.plist "$APP_NAME/Contents/Frameworks/justcefnative Helper (Alerts).app/Contents/Info.plist"
    cp -a Info/Info-Helper-GPU.plist "$APP_NAME/Contents/Frameworks/justcefnative Helper (GPU).app/Contents/Info.plist"
    cp -a Info/Info-Helper-Plugin.plist "$APP_NAME/Contents/Frameworks/justcefnative Helper (Plugin).app/Contents/Info.plist"
    cp -a Info/Info-Helper-Renderer.plist "$APP_NAME/Contents/Frameworks/justcefnative Helper (Renderer).app/Contents/Info.plist"

    python3 ../tools/linux-cdm/bundle-macos.py --publish "$PUBLISH_PATH" --app "$APP_NAME" || { echo "Playback bundling failed for $ARCH"; exit 1; }

    bash ./sign-macos.sh "$APP_NAME" || { echo "Signing/notarization failed for $ARCH"; exit 1; }

    rm -f "Grayjay.Desktop-$ARCH.zip"
    rm -rf "Grayjay.app"
    mv "Grayjay_$ARCH-signed.app" "Grayjay.app"
    /usr/bin/ditto -c -k --sequesterRsrc --keepParent "Grayjay.app" "Grayjay.Desktop-$ARCH.zip"
    if [ $? -ne 0 ]; then
        echo "Failed to create zip Grayjay.Desktop-$ARCH.zip"
        exit 1
    fi
}

SIGN_TEST="$(mktemp -t signtest)"
cp /usr/bin/true "$SIGN_TEST"
if ! codesign --sign "$APP_CERT" --force "$SIGN_TEST" >/dev/null 2>&1; then
    rm -f "$SIGN_TEST"
    echo "Error: cannot sign with \"$APP_CERT\"."
    echo "Unlock the keychain first: security unlock-keychain ~/Library/Keychains/login.keychain-db"
    exit 1
fi
rm -f "$SIGN_TEST"

# Build front-end
cd ../Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build || { echo "Front-end build failed"; exit 1; }
cd ../Grayjay.Desktop.CEF

build_sign_notarize "osx-x64"
build_sign_notarize "osx-arm64"

echo "All builds complete."

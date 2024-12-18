#!/bin/bash

APP_NAME_BASE="Grayjay"
BUNDLE_ID="com.futo.grayjay.desktop"
APPLE_ID="koen@futo.org"
TEAM_ID="2W7AC6T8T5"
APP_CERT="3rd Party Mac Developer Application: FUTO Holdings, Inc. (2W7AC6T8T5)"
KEYCHAIN_PROFILE="GRAYJAY_PROFILE"

build_sign_notarize() {
    ARCH=$1
    APP_NAME="${APP_NAME_BASE}_${ARCH}.app"
    PKG_NAME="${APP_NAME_BASE}_${ARCH}.pkg"

    echo "Building for architecture: $ARCH"

    # Build backend
    rm -rf bin/ obj/
    dotnet publish -r $ARCH
    PUBLISH_PATH="bin/Release/net8.0/$ARCH/publish"
    mkdir -p "$PUBLISH_PATH/wwwroot"
    cp -r ../Grayjay.Desktop.Web/dist "$PUBLISH_PATH/wwwroot/web"

    echo "Creating the app bundle..."
    rm -rf "$APP_NAME"
    mkdir -p "$APP_NAME/Contents/MacOS"
    mkdir -p "$APP_NAME/Contents/Resources"
    cp -a Resources/MacOS/Info.plist "$APP_NAME/Contents/Info.plist"
    cp -a Resources/MacOS/PkgInfo "$APP_NAME/Contents"

    cp -a "$PUBLISH_PATH/Grayjay.Desktop.CEF" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libe_sqlite3.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/libsodium.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/ClearScriptV8.osx-x64.dylib" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/MacOS/dotcefnative" "$APP_NAME/Contents/MacOS"
    cp -a "$PUBLISH_PATH/wwwroot" "$APP_NAME/Contents/Resources/wwwroot"

    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/Frameworks" "$APP_NAME/Contents/Frameworks"
    cp -a Resources/MacOS/Keychain.framework "$APP_NAME/Contents/Frameworks/Keychain.framework"
    cp -a "$PUBLISH_PATH/dotcefnative.app/Contents/Resources" "$APP_NAME/Contents/Resources"
    cp -a Resources/MacOS/grayjay.icns "$APP_NAME/Contents/Resources/shared.icns"

    SIGN_FLAGS="--force --verbose --sign"

    echo "Signing .dylib and .so files..."
    find "$APP_NAME" -type f \( -name "*.dylib" -o -name "*.so" \) | while read -r dylib; do
        codesign $SIGN_FLAGS "$APP_CERT" "$dylib"
    done

    echo "Signing frameworks..."
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/Keychain.framework"
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/Chromium Embedded Framework.framework"

    echo "Signing helper apps..."
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/dotcefnative Helper.app"
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Alerts).app"
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/dotcefnative Helper (GPU).app"
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Renderer).app"
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME/Contents/Frameworks/dotcefnative Helper (Plugin).app"

    echo "Signing executables in Contents/MacOS..."
    find "$APP_NAME/Contents/MacOS" -type f -perm +111 | while read -r exe; do
        codesign $SIGN_FLAGS "$APP_CERT" "$exe"
    done

    echo "Signing main app bundle..."
    codesign $SIGN_FLAGS "$APP_CERT" "$APP_NAME"

    echo "Verifying the app bundle signatures..."
    codesign --verify --deep --strict --verbose=2 "$APP_NAME"
    if [ $? -ne 0 ]; then
        echo "Error: Signature verification failed for $APP_NAME."
        exit 1
    fi

    echo "$PKG_NAME is ready for upload!"
}

# Build front-end
cd ../Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ../Grayjay.Desktop.CEF

build_sign_notarize "osx-x64"
build_sign_notarize "osx-arm64"

echo "All builds complete. Apps are ready for Transporter upload."

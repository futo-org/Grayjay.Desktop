#!/bin/bash

APPLE_ID="koen@futo.org"
TEAM_ID="2W7AC6T8T5"
APP_CERT="Developer ID Application: FUTO Holdings, Inc. (2W7AC6T8T5)"
KEYCHAIN_PROFILE="GRAYJAY_PROFILE"
ENTITLEMENTS_PATH="Entitlements"

sign_path() {
    FILE_PATH=$1
    case "$FILE_PATH" in
        *"Helper (GPU)"*) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef-helper-gpu.entitlements" ;;
        *"Helper (Alerts)"*) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef-helper-alerts.entitlements" ;;
        *"Helper (Renderer)"*) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef-helper-renderer.entitlements" ;;
        *"Helper (Plugin)"*) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef-helper-plugin.entitlements" ;;
        *"Helper"*) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef-helper.entitlements" ;;
        *) ENTITLEMENT_FILE="$ENTITLEMENTS_PATH/cef.entitlements" ;;
    esac

    echo "Signing \"$FILE_PATH\" with \"$ENTITLEMENT_FILE\""
    codesign --entitlements "$ENTITLEMENT_FILE" --sign "$APP_CERT" --force --verbose --options runtime --timestamp "$FILE_PATH"
}

verify_signature() {
    VERIFY_PATH=$1
    echo "Verifying the app bundle \"$VERIFY_PATH\"..."
    codesign -vvv --deep --strict "$VERIFY_PATH"
    if [ $? -ne 0 ]; then
        echo "Error: Signature verification failed for $VERIFY_PATH."
        exit 1
    fi
}

assess_notarization() {
    ASSESS_PATH=$1
    spctl --assess --verbose "$ASSESS_PATH"
    if [ $? -ne 0 ]; then
        echo "Error: Assess failed for $ASSESS_PATH."
        exit 1
    fi
}

notarize() {
    NOTARIZE_PATH=$1
    EXTENSION="${NOTARIZE_PATH##*.}"
    ZIP_PATH="${NOTARIZE_PATH%.$EXTENSION}.zip"

    rm -f "$ZIP_PATH"
    /usr/bin/ditto -c -k --sequesterRsrc --keepParent "$NOTARIZE_PATH" "$ZIP_PATH"
    if [ $? -ne 0 ]; then
        echo "Failed to create zip $ZIP_PATH"
        exit 1
    fi

    echo "Submitting $ZIP_PATH for notarization using notarytool..."
    xcrun notarytool submit "$ZIP_PATH" --apple-id "$APPLE_ID" --team-id "$TEAM_ID" --keychain-profile "$KEYCHAIN_PROFILE" --wait
    if [ $? -ne 0 ]; then
        echo "Error: Notarization failed for $ZIP_PATH."
        exit 1
    fi

    rm -f "$ZIP_PATH"

    echo "Stapling notarization ticket to the package \"$NOTARIZE_PATH\"..."
    xcrun stapler staple -v "$NOTARIZE_PATH"
    if [ $? -ne 0 ]; then
        echo "Error: Stapling failed for $NOTARIZE_PATH."
        exit 1
    fi
}

build_sign_notarize() {
    APP_UNSIGNED_PATH=$1
    APP_PATH="${APP_UNSIGNED_PATH%.app}-signed.app"

    echo "Making copy..."
    rm -rf "$APP_PATH"
    cp -a "$APP_UNSIGNED_PATH" "$APP_PATH"

    echo "Finding paths to sign..."
    IFS=$'\n' read -rd '' -a PATHS_TO_SIGN < <(
        { 
            find "$APP_PATH" -type d \( -name "*.app" -o -name "*.framework" \) -print
            find "$APP_PATH" -type f -exec sh -c 'file "$1" | head -n1' _ {} \; | grep -E 'Mach-O|shared library|dynamically linked' | cut -d: -f1
        } | sort -u | awk '{ print length, $0 }' | sort -nr | cut -d" " -f2-
    )

    for path in "${PATHS_TO_SIGN[@]}"; do
        echo "Found \"$path\""
    done

    echo "Signing..."
    for path in "${PATHS_TO_SIGN[@]}"; do
        sign_path "$path"
    done

    echo "Verifying..."
    verify_signature "$APP_PATH"

    echo "Notarizing..."
    notarize "$APP_PATH"
    assess_notarization "$APP_PATH"

    echo "Complete."
}

build_sign_notarize $1

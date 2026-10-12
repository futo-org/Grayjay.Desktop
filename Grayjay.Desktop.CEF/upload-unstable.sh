#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <version>" >&2
    exit 1
fi

VERSION=$1
case "$VERSION" in
    ''|*[!0-9]*)
        echo "Error: Version must be a non-negative integer." >&2
        exit 1
        ;;
esac

HOST="root@188.245.74.41"
REMOTE_DIR="/var/www/html/Apps/Grayjay.Desktop.Unstable"

for ARCH in osx-x64 osx-arm64; do
    if [ ! -f "Grayjay.Desktop-$ARCH.zip" ]; then
        echo "Error: Grayjay.Desktop-$ARCH.zip is missing." >&2
        exit 1
    fi
done

ssh "$HOST" "mkdir -p '$REMOTE_DIR/$VERSION'"

upload() {
    ARCH=$1
    scp "Grayjay.Desktop-$ARCH.zip" "$HOST:$REMOTE_DIR/$VERSION/Grayjay.Desktop-$ARCH-v$VERSION.zip"
    scp "Grayjay.Desktop-$ARCH.zip" "$HOST:$REMOTE_DIR/Grayjay.Desktop-$ARCH.zip"
}

upload "osx-x64"
upload "osx-arm64"

printf '%s\n' "$VERSION" > VersionLastMacOS.json
scp "VersionLastMacOS.json" "$HOST:$REMOTE_DIR/"

echo "All unstable macOS builds uploaded (v$VERSION)."


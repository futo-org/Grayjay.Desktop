# Build front-end
cd ../Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ../Grayjay.Desktop.CEF

# Build backend
rm -rf bin/ obj/
dotnet publish -r osx-arm64
mkdir -p bin/Release/net8.0/osx-arm64/publish/wwwroot
cp -r ../Grayjay.Desktop.Web/dist bin/Release/net8.0/osx-arm64/publish/wwwroot/web

echo "Removing the old app bundle..."
rm -rf Grayjay.app

echo "Creating the app bundle..."
mkdir -p Grayjay.app
mkdir -p Grayjay.app/Contents
mkdir -p Grayjay.app/Contents/MacOS
cp -a Resources/MacOS/Info.plist Grayjay.app/Contents/Info.plist
cp -a Resources/MacOS/PkgInfo Grayjay.app/Contents

cp -a bin/Release/net8.0/osx-arm64/publish/Grayjay.Desktop.CEF Grayjay.app/Contents/MacOS
cp -a bin/Release/net8.0/osx-arm64/publish/libe_sqlite3.dylib Grayjay.app/Contents/MacOS
cp -a bin/Release/net8.0/osx-arm64/publish/libsodium.dylib Grayjay.app/Contents/MacOS
cp -a bin/Release/net8.0/osx-arm64/publish/ClearScriptV8.osx-arm64.dylib Grayjay.app/Contents/MacOS
cp -a bin/Release/net8.0/osx-arm64/publish/wwwroot Grayjay.app/Contents/MacOS/wwwroot

cp -a bin/Release/net8.0/osx-arm64/publish/dotcefnative.app/Contents/Frameworks Grayjay.app/Contents

cp -a Resources/MacOS/Keychain.framework Grayjay.app/Contents/Frameworks/Keychain.framework
cp -a bin/Release/net8.0/osx-arm64/publish/dotcefnative.app/Contents/Resources Grayjay.app/Contents
cp -a Resources/MacOS/grayjay.icns Grayjay.app/Contents/Resources/shared.icns
cp -a bin/Release/net8.0/osx-arm64/publish/dotcefnative.app/Contents/MacOS/dotcefnative Grayjay.app/Contents/MacOS

echo "Signing the app bundle..."
#codesign --deep --force --verbose --sign "Apple Development: junk@koenj.com (UPVRSKNGC9)" --options runtime --entitlements Resources/MacOS/Entitlements.plist ./Grayjay.app
#codesign --deep --force --verbose --sign "Apple Development: junk@koenj.com (UPVRSKNGC9)" --options runtime ./Grayjay.app
codesign --deep --force --verbose --sign --verify "Apple Development: junk@koenj.com (UPVRSKNGC9)" ./Grayjay.app

echo "Verifying with codesign..."
codesign --verify --strict --verbose=4 ./Grayjay.app

echo "Done"
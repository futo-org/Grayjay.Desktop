#!/bin/sh
#security unlock-keychain login.keychain
dotnet publish
rm -rf adad.app
mkdir adad.app
mkdir adad.app/Contents
mkdir adad.app/Contents/Frameworks
mkdir adad.app/Contents/Frameworks/Keychain.framework
mkdir adad.app/Contents/MacOS
mkdir adad.app/Contents/Resources
cp PkgInfo adad.app/Contents
cp Info.plist adad.app/Contents
cp -a Keychain.framework/Resources adad.app/Contents/Frameworks/Keychain.framework/Resources
cp -a Keychain.framework/Versions adad.app/Contents/Frameworks/Keychain.framework/Versions
cp -a Keychain.framework/Keychain adad.app/Contents/Frameworks/Keychain.framework/Keychain
cp bin/Release/net8.0/osx-x64/publish/adad adad.app/Contents/MacOS

echo "Signing the app bundle..."
codesign --deep --force --verbose --sign "Apple Development: junk@koenj.com (UPVRSKNGC9)" --options runtime --entitlements ./Entitlements.plist ./adad.app

echo "Verifying with codesign..."
codesign --verify --strict --verbose=4 ./adad.app

echo "Done"
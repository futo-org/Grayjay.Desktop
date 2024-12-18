#!/bin/bash

# Remove old files
rm -f GrayjayDesktop-linux-x64.zip
rm -f GrayjayDesktop-win-x64.zip

# Build front-end
cd Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ..

runtimes=("win-x64" "linux-x64")

# Loop over each runtime
for runtime in "${runtimes[@]}"
do
    echo "Building for $runtime"

    # Publish CEF
    cd Grayjay.Desktop.CEF
    rm -rf bin
    dotnet publish -r $runtime -c Release
    cd ..

    # Copy wwwroot
    mkdir -p Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot
    cp -r Grayjay.Desktop.Web/dist Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot/web

    # Create zip
    cd Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish
    zip -r ../../../../../../GrayjayDesktop-$runtime.zip *
    cd ../../../../../..
done
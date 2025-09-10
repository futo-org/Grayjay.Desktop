#!/bin/bash


if [[ "$1" != "" ]]; then
   version="$1"
else
   echo -n "Version:"
   read version
fi

runtime="linux-x64"


printf "Version to deploy: $version\n"

# Build front-end
cd Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ..


# Loop over each runtime
rm -rf Grayjay.Desktop.CEF/bin/Release


# Publish CEF
cd Grayjay.Desktop.CEF
dotnet publish -r $runtime -c Release -p:AssemblyVersion=1.$version.0.0
cd ..

# Copy wwwroot
mkdir -p Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot
cp -r Grayjay.Desktop.Web/dist Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot/web

cd Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish	

chmod u=rwx Grayjay
chmod u=rwx cef/dotcefnative
chmod u=rwx FUTO.Updater.Client
chmod u=rwx ffmpeg
    
cd ../../../../../..
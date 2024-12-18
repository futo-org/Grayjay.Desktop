#!/bin/bash

targetDir="/var/www/html/Apps"
appName="Grayjay.Desktop.Unstable"


if [[ "$1" != "" ]]; then
   version="$1"
else
   echo -n "Version:"
   read version
fi

if [[ "$2" != "" ]]; then
  server="$2"
else
  echo -n "Server:"
  read server
fi

printf "Version to deploy: $version\n"





# Remove old files
rm -f GrayjayDesktop-linux-x64.zip
rm -f GrayjayDesktop-win-x64.zip

# Build front-end
cd Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ..

runtimes=("linux-x64" "osx-x64" "osx-arm64")

# Loop over each runtime
rm -rf Grayjay.Desktop.CEF/bin/Release
for runtime in "${runtimes[@]}"
do
    echo "Building for $runtime"

    # Publish CEF
    cd Grayjay.Desktop.CEF
    dotnet publish -r $runtime -c Release
    cd ..

    # Copy wwwroot
    mkdir -p Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot
    cp -r Grayjay.Desktop.Web/dist Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish/wwwroot/web
    
    cd Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish	
	
	if [ "$runtime" = "win-x64" ]; then
	   wine64 "../../../../../../rcedit-x64.exe" "cef/dotcefnative.exe" --set-icon "../../../../../logo.ico"
	   wine64 "../../../../../../rcedit-x64.exe" "cef/dotcefnative.exe" --set-version-string "ProductName" "Grayjay.Desktop"
	fi
	if [ "$runtime" = "linux-x64" ]; then
		chmod u=rwx Grayjay.Desktop.CEF
		chmod u=rwx cef/dotcefnative.exe
		chmod u=rwx FUTO.Updater.Client
		chmod u=rwx ffmpeg
	fi
	if [ "$runtime" = "osx-x64" ] || [ "$runtime" = "osx-arm64" ]; then
	    cd ..
		mkdir -p Grayjay.app
		mkdir -p Grayjay.app/Contents
		mkdir -p Grayjay.app/Contents/MacOS
		cp -a ../../../../Resources/MacOS/Info.plist Grayjay.app/Contents/Info.plist
		cp -a ../../../../Resources/MacOS/PkgInfo Grayjay.app/Contents

		cp -a publish/Grayjay.Desktop.CEF Grayjay.app/Contents/MacOS
		cp -a publish/libe_sqlite3.dylib Grayjay.app/Contents/MacOS
		cp -a publish/libsodium.dylib Grayjay.app/Contents/MacOS
		cp -a publish/ClearScriptV8.osx-x64.dylib Grayjay.app/Contents/MacOS
		cp -a publish/wwwroot Grayjay.app/Contents/MacOS/wwwroot
		
		cp -a publish/dotcefnative.app/Contents/Frameworks Grayjay.app/Contents

		cp -a ../../../../Resources/MacOS/Keychain.framework Grayjay.app/Contents/Frameworks/Keychain.framework
		cp -a publish/dotcefnative.app/Contents/Resources Grayjay.app/Contents
		cp -a publish/dotcefnative.app/Contents/MacOS/dotcefnative Grayjay.app/Contents/MacOS
		
		cp -a ../../../../Resources/MacOS/grayjay.icns Grayjay.app/Contents/Resources/shared.icns
		
		rm -R publish/*
		cd publish
		mkdir Grayjay.app
		mv ../Grayjay.app/* Grayjay.app/
		rm -R ../Grayjay.app
		
	fi
    
    cd ../../../../../..
done

printf " - Deleting existing files\n"
	
#Loop over each runtime for deploy
for runtime in "${runtimes[@]}"
do	
	echo "Deleting existing on remote for $runtime"
	ssh $server "rm -R $targetDir/$appName/$version/$runtime"
	ssh $server "rm -R $targetDir/$appName/$version/Grayjay.Desktop-$runtime-v$version.zip"
	
	echo "Deploying for $runtime"

	cd Grayjay.Desktop.CEF/bin/Release/net8.0/$runtime/publish
	printf "Deploying from $PWD\n"
	
	printf "Generating ZIP\n"
	rm -R "../Grayjay.Desktop-$runtime-v$version.zip"
	cp -R "../publish" "../Grayjay.Desktop-$runtime-v$version"
	cd ../
	rm -f Grayjay.Desktop-$runtime-v$version.zip
	zip -r "Grayjay.Desktop-$runtime-v$version.zip" "Grayjay.Desktop-$runtime-v$version"
	cd publish
	
	outDir=$targetDir/$appName/$version/$runtime
	printf "Deploying to $outDir:\n"
	
	printf " - Creating folder...\n"
	ssh $server "mkdir -p $outDir"
	
	printf " - Creating maintenance file...\n"
	ssh $server "touch $targetDir/$appName/maintenance"
	
	
	printf " - Copying zip\n"
	scp "../Grayjay.Desktop-$runtime-v$version.zip" $server:$targetDir/$appName/$version
	
	printf " - Copy [${PWD}] => [$outDir]\n"
	scp -r "../publish" $server:$outDir
	
	printf " - Moving files..\n"
	ssh $server "mv -f $outDir/publish/* $outDir"
	ssh $server "rm -R $outDir/publish"
	
	
	printf " - Deleting maintenace file...\n"
	ssh "$server" "rm $targetDir/$appName/maintenance"
	

	cd ../../../../../..
	
	printf " - Done\n\n"
done

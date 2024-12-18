#!/bin/bash

#THIS IS FULLY NOT CORRECT YET

targetDir="/var/www/html/Apps"
appName="Grayjay.Desktop"

SSH_KEY_PRIV_FILE="/tmp/deploy_key"
echo "$SSH_KEY_PRIV" | base64 -d > $SSH_KEY_PRIV_FILE
chmod 600 $SSH_KEY_PRIV_FILE
SSH_CMD="ssh -i $SSH_KEY_PRIV_FILE -o StrictHostKeyChecking=no"
SCP_CMD="scp -i $SSH_KEY_PRIV_FILE -o StrictHostKeyChecking=no"

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

runtimes=("osx-x64" "osx-arm64")

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
	$SSH_CMD $server "rm -R $targetDir/$appName/$version/$runtime"
	$SSH_CMD $server "rm -R $targetDir/$appName/$version/Grayjay.Desktop-$runtime-v$version.zip"
	$SSH_CMD $server "rm $targetDir/$appName/Grayjay.Desktop-$runtime.zip"
	
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
	$SSH_CMD $server "mkdir -p $outDir"
	
	printf " - Creating maintenance file...\n"
	$SSH_CMD $server "touch $targetDir/$appName/maintenance"
	
	
	printf " - Copying zip\n"
	$SCP_CMD "../Grayjay.Desktop-$runtime-v$version.zip" $server:$targetDir/$appName/$version
	printf " - Copying zip global\n"
	$SCP_CMD "../Grayjay.Desktop-$runtime.zip" $server:$targetDir/$appName
	
	printf " - Copy [${PWD}] => [$outDir]\n"
	$SCP_CMD -r "../publish" $server:$outDir
	
	printf " - Moving files..\n"
	$SSH_CMD $server "mv -f $outDir/publish/* $outDir"
	$SSH_CMD $server "rm -R $outDir/publish"
	
	
	printf " - Deleting maintenace file...\n"
	$SSH_CMD "$server" "rm $targetDir/$appName/maintenance"
	

	cd ../../../../../..
	
	printf " - Done\n\n"
done

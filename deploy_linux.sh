#!/bin/bash

targetDir="/var/www/html/Apps"
if [[ "$3" != "" ]]; then
   appName="$3"
else
   echo -n "AppName:"
   read appName
fi

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

# Build front-end
cd Grayjay.Desktop.Web
npm install
rm -rf dist
npm run build
cd ..

runtimes=("linux-x64")

# Loop over each runtime
rm -rf Grayjay.Desktop.CEF/bin/Release
for runtime in "${runtimes[@]}"
do
    echo "Building for $runtime"

    # Publish CEF
    cd Grayjay.Desktop.CEF
    dotnet publish -r $runtime -c Release /p:AssemblyVersion=1.$version.0.0
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
	cp "Grayjay.Desktop-$runtime-v$version.zip" "Grayjay.Desktop-$runtime.zip"
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

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
printf "To server: $server\n"


# Build front-end
#cd Grayjay.Desktop.Web
#npm install
#rm -rf dist
#npm run build
#cd ..

runtimes=("win-x64")

# Loop over each runtime
rm Grayjay.Desktop.Installer/Result/Grayjay.msi
rm Grayjay.Desktop.Installer/Result/Grayjay.wixpdb
for runtime in "${runtimes[@]}"
do
    echo "Building for $runtime"

    # Publish CEF
    cd Grayjay.Desktop.Installer
    #dotnet publish -r $runtime -c Release -p:AssemblyVersion=1.$version.0.0
    #cd ..

    # Copy wwwroot
    #mkdir -p Grayjay.Desktop.CEF/bin/Release/net9.0/$runtime/publish/wwwroot
    #cp -r Grayjay.Desktop.Web/dist Grayjay.Desktop.CEF/bin/Release/net9.0/$runtime/publish/wwwroot/web
    
    #cd Grayjay.Desktop.CEF/bin/Release/net9.0/$runtime/publish	
	echo "Signing..."
	../sign_windows.sh "Files/FUTO.Updater.Client.exe"
	./build.sh
	../sign_windows.sh "Result/Grayjay.msi"
    
    cd ..
done
	
#Loop over each runtime for deploy
for runtime in "${runtimes[@]}"
do
	echo "Deleting existing on remote for $runtime\n"
	$SSH_CMD $server "rm $targetDir/$appName/Grayjay.msi"
	
	printf "Deploying to $targetDir/$appName:\n"
	$SCP_CMD "Grayjay.Desktop.Installer/Grayjay.msi" $server:$targetDir/$appName
	
	printf " - Done\n\n"
done

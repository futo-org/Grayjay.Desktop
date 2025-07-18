#!/bin/bash
rm Grayjay.Desktop.Installer/Output/Grayjay-1.0.0-Setup-x64.exe
echo "Building for $runtime"
cd Grayjay.Desktop.Installer
echo "Signing Input..."
../sign_windows.sh "Files/FUTO.Updater.Client.exe"
echo "Building Installer..."
./buildiss.sh
echo "Signing Input..."
../sign_windows.sh "Output/Grayjay-1.0.0-Setup-x64.exe"
cd ..
#!/bin/bash

cleanup() {
    echo "Terminating background processes..."
    kill $NPM_PID $DOTNET_PID
}

trap cleanup SIGINT

git pull origin master
git submodule update --recursive

cd Grayjay.Desktop.Web
npm install
npm run start &
NPM_PID=$!
cd ..

cd Grayjay.Desktop.Headless
dotnet run &
DOTNET_PID=$!
cd ..

echo "Open your browser at http://127.0.0.1:8080/web"
wait $NPM_PID $DOTNET_PID
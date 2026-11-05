#!/usr/bin/env bash
# Builds AlienCat.exe (needs NuGet access; CI does the same) and zips it with the README.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish src/AlienCat.App/AlienCat.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o build/publish
mkdir -p build/package
cp build/publish/AlienCat.exe README.md build/package/
(cd build/package && zip -q -r ../AlienCat.zip .)
ls -lh build/AlienCat.zip
